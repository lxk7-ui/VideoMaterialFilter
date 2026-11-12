using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace SucaiShaixuan
{
    internal sealed class ModelFile
    {
        public readonly string Name;
        public readonly long Size;
        public readonly string Sha256;

        public ModelFile(string name, long size, string sha256)
        {
            Name = name;
            Size = size;
            Sha256 = sha256;
        }
    }

    internal sealed class ModelInfo
    {
        public readonly string Id;
        public readonly string Note;
        public readonly ModelFile[] Files;

        public ModelInfo(string id, string note, params ModelFile[] files)
        {
            Id = id;
            Note = note;
            Files = files;
        }

        public long TotalSize
        {
            get { return Files.Sum(f => f.Size); }
        }

        public string Dir
        {
            get { return Path.Combine(Paths.ModelDir, "faster-whisper-" + Id); }
        }

        // Where an unfinished download waits (and resumes from).
        public string DownloadDir
        {
            get { return Dir + ".download"; }
        }
    }

    // The faster-whisper speech models the engine can use. Sizes and SHA-256 are those of the
    // Systran/faster-whisper-* repositories; ModelScope, hf-mirror and Hugging Face serve identical files.
    internal static class ModelCatalog
    {
        private static readonly ModelFile Tokenizer = new ModelFile("tokenizer.json", 2203239, "fb7b63191e9bb045082c79fd742a3106a12c99513ab30df4a0d47fa6cb6fd0ab");
        private static readonly ModelFile Vocabulary = new ModelFile("vocabulary.txt", 459861, "34ce3fe1c5041027b3f8d42912270993f986dbc4bb34cf27f951e34a1e453913");

        public static readonly ModelInfo[] All =
        {
            new ModelInfo("tiny", "最快",
                new ModelFile("config.json", 2249, "a73a28cdfe1c43ccc7202fa333d1f89c202477271407ae9a7f19afa52039cac8"),
                new ModelFile("model.bin", 75538270, "dcb76c6586fc06cbdac6dd21f14cfd129cc4cdd9dce19bf4ffa62e59cbe6e6d1"),
                Tokenizer, Vocabulary),
            new ModelInfo("base", "快",
                new ModelFile("config.json", 2309, "56a6d8110d311f19c8f0471e562832c7527f146b567275bfca59fcf7c184da9a"),
                new ModelFile("model.bin", 145217532, "d01c3014881c9c6f3133c182f3d2887eb6ca1c789a7538c5c007196857a0a6a9"),
                Tokenizer, Vocabulary),
            new ModelInfo("small", "推荐",
                new ModelFile("config.json", 2370, "b55496ac7940a7ae47d2c01eab40edfd8701feec1229d9cce3b40014383fb828"),
                new ModelFile("model.bin", 483546902, "3e305921506d8872816023e4c273e75d2419fb89b24da97b4fe7bce14170d671"),
                Tokenizer, Vocabulary),
            new ModelInfo("medium", "最准，慢",
                new ModelFile("config.json", 2257, "3622a2ddc41ec0e0fd4e68c13c6830f03b90c38d89aaad184de02c8c642cf807"),
                new ModelFile("model.bin", 1527906378, "9b45e1009dcc4ab601eff815b61d80e60ce3fd8c74c1a14f4a282258286b51ae"),
                Tokenizer, Vocabulary)
        };

        // Tried in this order ({0} model, {1} file). Whatever server a file comes from, it has to match
        // the SHA-256 above. ModelScope is in China and needs no proxy; the other two are the fallback.
        public static readonly string[] Sources =
        {
            "https://www.modelscope.cn/models/Systran/faster-whisper-{0}/resolve/master/{1}",
            "https://hf-mirror.com/Systran/faster-whisper-{0}/resolve/main/{1}",
            "https://huggingface.co/Systran/faster-whisper-{0}/resolve/main/{1}"
        };

        public static ModelInfo Get(string id)
        {
            return All.FirstOrDefault(m => m.Id == id);
        }

        // All files present with the right sizes. The SHA-256 check happens once, right after downloading.
        public static bool IsInstalled(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try
            {
                if (Path.IsPathRooted(id)) return File.Exists(Path.Combine(id, "model.bin"));   // a model folder typed into 设置.ini
                ModelInfo m = Get(id);
                string dir = Path.Combine(Paths.ModelDir, "faster-whisper-" + id);
                if (m == null) return File.Exists(Path.Combine(dir, "model.bin"));
                foreach (ModelFile f in m.Files)
                {
                    var fi = new FileInfo(Path.Combine(dir, f.Name));
                    if (!fi.Exists || fi.Length != f.Size) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string SizeText(string id)
        {
            ModelInfo m = Get(id);
            return m == null ? "大小未知" : "约 " + Mb(m.TotalSize);
        }

        public static string Mb(long bytes)
        {
            if (bytes >= 1000L * 1024 * 1024) return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + "GB";
            return Math.Max(1L, (long)Math.Round(bytes / 1048576.0)) + "MB";
        }
    }

    // Downloads one model into runtime\models\faster-whisper-<id>.download and moves it into place
    // when every file has passed its SHA-256 check. A single connection to these servers can be slow,
    // so big files come in up to 8 ranges at once; each range is its own .part file, which is what
    // makes stopping and starting again continue where it was. Before downloading, every server is
    // tried both directly and through the Windows proxy, and the combination that answers first is
    // used; a range that keeps failing moves on to the next combination.
    internal sealed class ModelDownloader
    {
        private const int ProbeBytes = 256 * 1024;
        private const long PartBytes = 64L << 20;
        private const int MaxParts = 8;

        private sealed class Route
        {
            public string Source;
            public IWebProxy Proxy;   // null = direct
        }

        private sealed class Part
        {
            public string File;
            public long Start;
            public long Length;
            public long Have;
        }

        private sealed class Job
        {
            public ModelFile File;
            public string Target;
            public bool Ready;   // already downloaded and checked by an earlier run
            public readonly List<Part> Parts = new List<Part>();
        }

        // One open response; disposing it also forgets the request, so Cancel() no longer aborts it.
        private sealed class Conn : IDisposable
        {
            private readonly ModelDownloader _owner;
            public readonly HttpWebRequest Req;
            public readonly HttpWebResponse Resp;

            public Conn(ModelDownloader owner, HttpWebRequest req, HttpWebResponse resp)
            {
                _owner = owner;
                Req = req;
                Resp = resp;
            }

            public void Dispose()
            {
                try { Resp.Close(); } catch { }
                _owner.Untrack(Req);
            }
        }

        private readonly ModelInfo _m;
        private readonly List<HttpWebRequest> _live = new List<HttpWebRequest>();
        private readonly ManualResetEvent _stopped = new ManualResetEvent(false);
        private volatile bool _cancel;
        private volatile bool _probesOver;
        private volatile string _fatal;
        private long _done;
        private List<Route> _routes;

        public readonly long Total;
        public volatile string Phase = "连接";   // 连接 / 下载 / 校验
        public string Error { get; private set; }
        public bool Succeeded { get; private set; }
        public event Action Finished;              // raised on a worker thread

        static ModelDownloader()
        {
            // An exe built without a target-framework attribute would offer TLS 1.0 only and open just
            // 2 connections per server.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 64);
            ServicePointManager.Expect100Continue = false;
        }

        public ModelDownloader(ModelInfo m)
        {
            _m = m;
            Total = m.TotalSize;
        }

        public long Done
        {
            get { return Interlocked.Read(ref _done); }
        }

        public void Start()
        {
            new Thread(Run) { IsBackground = true, Name = "模型下载" }.Start();
        }

        public void Cancel()
        {
            _cancel = true;
            _stopped.Set();
            lock (_live)
            {
                foreach (HttpWebRequest r in _live)
                {
                    try { r.Abort(); } catch { }
                }
            }
        }

        private void Run()
        {
            try
            {
                Directory.CreateDirectory(_m.DownloadDir);
                List<Job> jobs = Plan();
                Interlocked.Exchange(ref _done, jobs.Sum(j => j.Ready ? j.File.Size : j.Parts.Sum(p => p.Have)));
                CheckDisk(jobs);
                _routes = PickRoutes();
                if (_cancel) return;
                Route best = _routes[0];
                Paths.AppendLog(Report.Now() + "  下载模型 " + _m.Id + "：用 " + new Uri(string.Format(best.Source, _m.Id, "")).Host + (best.Proxy == null ? "（直连）" : "（系统代理）"));
                Phase = "下载";
                var threads = new List<Thread>();
                foreach (Job job in jobs)
                {
                    if (job.Ready) continue;
                    foreach (Part part in job.Parts)
                    {
                        if (part.Have >= part.Length) continue;
                        ModelFile f = job.File;
                        Part p = part;
                        var t = new Thread(() => RunPart(f, p)) { IsBackground = true };
                        threads.Add(t);
                        t.Start();
                    }
                }
                foreach (Thread t in threads) t.Join();
                if (_cancel) return;
                if (_fatal != null) throw new IOException(_fatal);
                Phase = "校验";
                foreach (Job job in jobs) Merge(job);
                Install();
                Succeeded = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!_cancel) Error = "没有权限往软件文件夹里写文件（" + ex.Message + "）。把软件文件夹放到桌面或 D 盘再试。";
            }
            catch (Exception ex)
            {
                if (!_cancel) Error = ex.Message;
            }
            finally
            {
                Paths.AppendLog(Report.Now() + "  下载模型 " + _m.Id + "：" + (Succeeded ? "完成" : _cancel ? "停止" : "失败 " + Error));
                Action h = Finished;
                if (h != null) h();
            }
        }

        // What still has to come down. Parts of an earlier, unfinished run are kept and continued.
        private List<Job> Plan()
        {
            var jobs = new List<Job>();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelFile f in _m.Files)
            {
                var job = new Job { File = f, Target = Path.Combine(_m.DownloadDir, f.Name) };
                jobs.Add(job);
                keep.Add(job.Target);
                var done = new FileInfo(job.Target);
                if (done.Exists && done.Length == f.Size)
                {
                    job.Ready = true;
                    continue;
                }
                int n = (int)Math.Max(1L, Math.Min(MaxParts, (f.Size + PartBytes - 1) / PartBytes));
                long step = (f.Size + n - 1) / n;
                for (int i = 0; i < n; i++)
                {
                    long start = i * step;
                    var p = new Part { File = job.Target + "." + i + "-" + n + ".part", Start = start, Length = Math.Min(step, f.Size - start) };
                    var fi = new FileInfo(p.File);
                    p.Have = fi.Exists ? fi.Length : 0;
                    job.Parts.Add(p);
                    keep.Add(p.File);
                }
                // Longer than its range: stopped half way through joining the parts. Start that file over.
                if (job.Parts.Any(p => p.Have > p.Length))
                {
                    foreach (Part p in job.Parts)
                    {
                        TryDelete(p.File);
                        p.Have = 0;
                    }
                }
            }
            foreach (string stale in Directory.GetFiles(_m.DownloadDir))
            {
                if (!keep.Contains(stale)) TryDelete(stale);
            }
            return jobs;
        }

        private void CheckDisk(List<Job> jobs)
        {
            // Joining the parts briefly needs room for one more part.
            long join = jobs.Where(j => !j.Ready && j.Parts.Count > 1).Select(j => j.Parts[1].Length).DefaultIfEmpty(0L).Max();
            long need = Total - Done + join + (20L << 20);
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_m.DownloadDir)));
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    throw new IOException("磁盘空间不够：还要 " + ModelCatalog.Mb(need) + "，" + drive.Name.TrimEnd('\\') + " 盘只剩 " + ModelCatalog.Mb(drive.AvailableFreeSpace) + "。");
                }
            }
            catch (ArgumentException)
            {
                // A network folder has no drive letter to look at; just try.
            }
        }

        // Fetches the first 256 KB of model.bin over every server, directly and through the Windows
        // proxy, all at once. Routes that answered come first, fastest first; the rest are kept as
        // fallbacks in the default order.
        private List<Route> PickRoutes()
        {
            Phase = "连接";
            IWebProxy sys = null;
            try
            {
                sys = WebRequest.GetSystemWebProxy();
                sys.Credentials = CredentialCache.DefaultCredentials;
            }
            catch
            {
            }
            var all = new List<Route>();
            foreach (string src in ModelCatalog.Sources)
            {
                all.Add(new Route { Source = src });
                if (sys != null) all.Add(new Route { Source = src, Proxy = sys });
            }
            var answered = new List<Route>();
            var noProxy = new List<Route>();
            int pending = all.Count;
            foreach (Route r in all)
            {
                Route route = r;
                new Thread(() =>
                {
                    try
                    {
                        // The first question to the Windows proxy can take seconds (proxy auto-detection),
                        // so it is asked here, where it does not hold up the direct routes.
                        if (route.Proxy != null && route.Proxy.IsBypassed(new Uri(string.Format(route.Source, _m.Id, "model.bin"))))
                        {
                            lock (answered) noProxy.Add(route);
                            return;
                        }
                        if (Probe(route)) lock (answered) answered.Add(route);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pending);
                    }
                }) { IsBackground = true }.Start();
            }
            var sw = Stopwatch.StartNew();
            long firstAt = -1;
            while (!_cancel && Volatile.Read(ref pending) > 0 && sw.ElapsedMilliseconds < 30000)
            {
                lock (answered)
                {
                    if (answered.Count > 0 && firstAt < 0) firstAt = sw.ElapsedMilliseconds;
                }
                // Once one answered, give the others a second to line up behind it.
                if (firstAt >= 0 && sw.ElapsedMilliseconds - firstAt > 1000) break;
                Thread.Sleep(50);
            }
            List<Route> order;
            int working;
            lock (answered)
            {
                working = answered.Count;
                order = new List<Route>(answered);
                order.AddRange(all.Where(r => !answered.Contains(r) && !noProxy.Contains(r)));
            }
            _probesOver = true;
            AbortAll();
            if (_cancel) return order;
            if (working == 0) throw new IOException("连不上下载服务器。检查一下网络，过一会儿再点下载。");
            return order;
        }

        private bool Probe(Route r)
        {
            using (Conn c = Open(string.Format(r.Source, _m.Id, "model.bin"), r.Proxy, 0, ProbeBytes - 1))
            {
                CheckRange(c.Resp, 0);
                var buf = new byte[64 * 1024];
                long got = 0;
                using (Stream s = c.Resp.GetResponseStream())
                {
                    while (got < ProbeBytes && !_cancel && !_probesOver)
                    {
                        int n = s.Read(buf, 0, (int)Math.Min(buf.Length, ProbeBytes - got));
                        if (n <= 0) break;
                        got += n;
                    }
                }
                return got >= ProbeBytes;
            }
        }

        // Keeps one range going until it is complete. After two failed tries in a row it moves to the
        // next route; when every route failed several times over, the whole download gives up.
        private void RunPart(ModelFile f, Part p)
        {
            try
            {
                int route = 0, fails = 0;
                string last = "";
                while (!_cancel && _fatal == null && p.Have < p.Length)
                {
                    long before = p.Have;
                    try
                    {
                        Fetch(_routes[route], f.Name, p);
                    }
                    catch (Exception ex)
                    {
                        if (_cancel) return;
                        last = ex.Message;
                    }
                    if (p.Have >= p.Length) return;
                    if (p.Have > before)
                    {
                        fails = 0;
                        continue;
                    }
                    fails++;
                    if (fails % 2 == 0) route = (route + 1) % _routes.Count;
                    if (fails >= 3 * _routes.Count + 2)
                    {
                        _fatal = "网络不好，一直下载不下来（" + last + "）。过一会儿再点「继续」，已经下好的部分不用重下。";
                        return;
                    }
                    _stopped.WaitOne(Math.Min(10000, 1000 * fails));
                }
            }
            catch (Exception ex)
            {
                if (!_cancel && _fatal == null) _fatal = ex.Message;
            }
        }

        private void Fetch(Route r, string name, Part p)
        {
            long from = p.Start + p.Have, to = p.Start + p.Length - 1;
            using (Conn c = Open(string.Format(r.Source, _m.Id, name), r.Proxy, from, to))
            {
                CheckRange(c.Resp, from);
                using (Stream s = c.Resp.GetResponseStream())
                using (var fs = new FileStream(p.File, FileMode.Append, FileAccess.Write, FileShare.Read, 1))
                {
                    var buf = new byte[64 * 1024];
                    long need = to - from + 1;
                    while (need > 0 && !_cancel)
                    {
                        int n = s.Read(buf, 0, (int)Math.Min(buf.Length, need));
                        if (n <= 0) break;
                        fs.Write(buf, 0, n);
                        need -= n;
                        p.Have += n;
                        Interlocked.Add(ref _done, n);
                    }
                }
            }
        }

        // GET with a byte range. Redirects are followed here rather than by HttpWebRequest, which does
        // not know 308 (hf-mirror answers with it) and must keep the Range header on every hop.
        private Conn Open(string url, IWebProxy proxy, long from, long to)
        {
            var uri = new Uri(url);
            for (int hop = 0; hop < 10; hop++)
            {
                if (_cancel) throw new IOException("已停止");
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Proxy = proxy;
                req.AllowAutoRedirect = false;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SucaiShaixuan/" + AppInfo.Version;
                req.AddRange(from, to);
                lock (_live) _live.Add(req);
                HttpWebResponse resp;
                try
                {
                    resp = (HttpWebResponse)req.GetResponse();
                }
                catch (WebException ex)
                {
                    resp = ex.Response as HttpWebResponse;
                    if (resp == null)
                    {
                        Untrack(req);
                        throw;
                    }
                }
                int code = (int)resp.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string loc = resp.Headers[HttpResponseHeader.Location];
                    resp.Close();
                    Untrack(req);
                    if (string.IsNullOrEmpty(loc)) throw new IOException("服务器跳转地址是空的");
                    uri = new Uri(uri, loc);
                    continue;
                }
                if (code >= 400)
                {
                    resp.Close();
                    Untrack(req);
                    throw new IOException("服务器返回 " + code + "（" + uri.Host + "）");
                }
                return new Conn(this, req, resp);
            }
            throw new IOException("跳转次数太多");
        }

        private static void CheckRange(HttpWebResponse resp, long from)
        {
            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                string cr = resp.Headers["Content-Range"] ?? "";   // bytes 100-199/1000
                int sp = cr.IndexOf(' '), dash = cr.IndexOf('-');
                long start;
                if (sp >= 0 && dash > sp && long.TryParse(cr.Substring(sp + 1, dash - sp - 1), out start) && start == from) return;
                throw new IOException("服务器给的分段不对（" + cr + "）");
            }
            // The whole file instead of a range is fine only when the range starts at 0.
            if (resp.StatusCode == HttpStatusCode.OK && from == 0) return;
            throw new IOException("服务器不支持分段下载（" + (int)resp.StatusCode + "）");
        }

        // Joins the parts (appending each to the first and deleting it, so it needs little extra disk
        // space) while hashing them, and keeps the file only if the SHA-256 matches.
        private void Merge(Job job)
        {
            if (job.Ready) return;
            string first = job.Parts[0].File;
            var buf = new byte[1 << 20];
            using (var sha = new SHA256CryptoServiceProvider())
            {
                using (var fs = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0) sha.TransformBlock(buf, 0, n, null, 0);
                }
                if (job.Parts.Count > 1)
                {
                    using (var outFs = new FileStream(first, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        for (int i = 1; i < job.Parts.Count; i++)
                        {
                            using (var inFs = new FileStream(job.Parts[i].File, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                            {
                                int n;
                                while ((n = inFs.Read(buf, 0, buf.Length)) > 0)
                                {
                                    sha.TransformBlock(buf, 0, n, null, 0);
                                    outFs.Write(buf, 0, n);
                                }
                            }
                            File.Delete(job.Parts[i].File);
                        }
                    }
                }
                sha.TransformFinalBlock(buf, 0, 0);
                if (!string.Equals(Memory.Hex(sha.Hash), job.File.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(first);
                    Interlocked.Add(ref _done, -job.File.Size);
                    throw new IOException(job.File.Name + " 下载下来的内容不对（校验没通过），已经删掉了。再点一次「下载」重下。");
                }
            }
            File.Move(first, job.Target);
        }

        // model.bin goes in last: the engine takes a folder with model.bin as a complete model.
        private void Install()
        {
            Directory.CreateDirectory(_m.Dir);
            foreach (ModelFile f in _m.Files.OrderBy(x => x.Name == "model.bin" ? 1 : 0))
            {
                string src = Path.Combine(_m.DownloadDir, f.Name), dst = Path.Combine(_m.Dir, f.Name);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
            try
            {
                Directory.Delete(_m.DownloadDir, true);
            }
            catch
            {
            }
        }

        private void Untrack(HttpWebRequest req)
        {
            lock (_live) _live.Remove(req);
        }

        private void AbortAll()
        {
            lock (_live)
            {
                foreach (HttpWebRequest r in _live)
                {
                    try { r.Abort(); } catch { }
                }
                _live.Clear();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
