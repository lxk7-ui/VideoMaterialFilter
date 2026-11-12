using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SucaiShaixuan
{
    internal sealed class MemEntry
    {
        public string Time = "";
        public string Result = "";   // 保留 / 筛掉 / 放回
        public string Fp = "";
        public string Name = "";
        public string Folder = "";
        public string Checks = "";   // 画面+语音 / 画面 / 语音; empty when added from an old 筛选记录.csv
        public string Reason = "";
    }

    // 筛选记忆.txt next to the exe: every checked video by a content fingerprint, so a video moved to
    // another folder or renamed is still recognised. Append-only and tab separated; the last line of a
    // fingerprint wins and 忘记 drops it. The engine (shaixuan.py, class Memory) reads and writes the
    // same file and computes the same fingerprint, so both sides have to change together.
    internal static class Memory
    {
        private const int Chunk = 64 * 1024;
        private const string ClearedMark = "# 清空时间：";
        private const string Head =
            "# 素材筛选器的筛选记忆：查过的视频按内容记在这里，挪了文件夹、改了名字也认得出来，不会重复查。\r\n" +
            "# 每行：时间、结果、指纹、文件名、所在文件夹、查了什么、原因。不要手动改；要清空，在「筛选规则」里点「清空」。\r\n";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, MemEntry> ByFp = new Dictionary<string, MemEntry>();
        private static readonly Dictionary<string, Tuple<long, DateTime, string>> FpCache =
            new Dictionary<string, Tuple<long, DateTime, string>>(StringComparer.OrdinalIgnoreCase);
        private static long _loadedLen = -1;
        private static DateTime _loadedWrite;
        private static DateTime _lastLook;

        public static int Count
        {
            get
            {
                lock (Gate)
                {
                    Reload(true);
                    return ByFp.Count;
                }
            }
        }

        // Same rule as memory_action() in shaixuan.py: kept and put-back videos are not checked again.
        public static bool Skips(MemEntry e)
        {
            return e != null && (e.Result == "保留" || e.Result == "放回");
        }

        // Whether the engine will skip this video because it was checked before (for the 待检查 count).
        // A file changed in the last 5 seconds may still be downloading, so it is not looked up yet.
        public static bool WillSkip(string fullPath)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists || fi.Length == 0 || (DateTime.Now - fi.LastWriteTime).TotalSeconds < 5) return false;
                string fp = Fingerprint(fi);
                lock (Gate)
                {
                    Reload(false);
                    MemEntry e;
                    return ByFp.TryGetValue(fp, out e) && Skips(e);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Remember(string fullPath, string result, string reason)
        {
            Write(fullPath, result, reason);
        }

        // 重新检查: the engine must not skip this video any more, wherever it turns up.
        public static void Forget(string fullPath)
        {
            Write(fullPath, "忘记", "手动要求重新检查");
        }

        // Starts an empty memory that notes when it was cleared (the engine then does not refill it from
        // older 筛选记录.csv rows). The previous file is kept once as 筛选记忆_清空前.txt.
        public static void Clear()
        {
            lock (Gate)
            {
                if (File.Exists(Paths.MemoryPath))
                {
                    if (File.Exists(Paths.MemoryBackupPath)) File.Delete(Paths.MemoryBackupPath);
                    File.Move(Paths.MemoryPath, Paths.MemoryBackupPath);
                }
                File.WriteAllText(Paths.MemoryPath, Head + ClearedMark + Report.Now() + "\r\n", new UTF8Encoding(false));
                ByFp.Clear();
                _loadedLen = -1;
                Reload(true);
            }
        }

        // File size + SHA-1 of the first and last 64 KB (the whole file when it is smaller than 128 KB),
        // exactly like fingerprint() in shaixuan.py. Cached per path, size and modification time.
        public static string Fingerprint(FileInfo fi)
        {
            Tuple<long, DateTime, string> c;
            lock (Gate)
            {
                if (FpCache.TryGetValue(fi.FullName, out c) && c.Item1 == fi.Length && c.Item2 == fi.LastWriteTimeUtc) return c.Item3;
            }
            string fp = Compute(fi.FullName);
            lock (Gate)
            {
                FpCache[fi.FullName] = Tuple.Create(fi.Length, fi.LastWriteTimeUtc, fp);
            }
            return fp;
        }

        public static string Compute(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = new SHA1CryptoServiceProvider())
            {
                long size = fs.Length;
                if (size <= 2 * Chunk)
                {
                    byte[] all = ReadUpTo(fs, (int)size);
                    sha.TransformFinalBlock(all, 0, all.Length);
                }
                else
                {
                    byte[] head = ReadUpTo(fs, Chunk);
                    fs.Seek(size - Chunk, SeekOrigin.Begin);
                    byte[] tail = ReadUpTo(fs, Chunk);
                    sha.TransformBlock(head, 0, head.Length, null, 0);
                    sha.TransformFinalBlock(tail, 0, tail.Length);
                }
                return size.ToString(CultureInfo.InvariantCulture) + "-" + Hex(sha.Hash);
            }
        }

        internal static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] ReadUpTo(Stream s, int count)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) break;
                got += n;
            }
            if (got == count) return buf;
            var cut = new byte[got];
            Array.Copy(buf, cut, got);
            return cut;
        }

        private static void Write(string fullPath, string result, string reason)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists) return;
                Append(new MemEntry
                {
                    Time = Report.Now(),
                    Result = result,
                    Fp = Fingerprint(fi),
                    Name = fi.Name,
                    Folder = fi.DirectoryName ?? "",
                    Reason = reason ?? ""
                });
            }
            catch (Exception ex)
            {
                Paths.AppendLog(Report.Now() + "  筛选记忆没写进去（" + fullPath + "）：" + ex.Message);
            }
        }

        private static void Append(MemEntry e)
        {
            string reason = (e.Reason ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            string line = string.Join("\t", new[] { e.Time, e.Result, e.Fp, e.Name, e.Folder, e.Checks, reason }) + "\r\n";
            lock (Gate)
            {
                Reload(true);
                bool isNew = !File.Exists(Paths.MemoryPath);
                byte[] data = new UTF8Encoding(false).GetBytes((isNew ? Head : "") + line);
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        using (var fs = new FileStream(Paths.MemoryPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.Write(data, 0, data.Length);
                        }
                        break;
                    }
                    catch (IOException) when (attempt < 5)
                    {
                        Thread.Sleep(200);
                    }
                }
                Fold(e);
                var fi = new FileInfo(Paths.MemoryPath);
                _loadedLen = fi.Length;
                _loadedWrite = fi.LastWriteTimeUtc;
            }
        }

        // Re-reads the file when the engine (or anything else) changed it; looks at most twice a second.
        private static void Reload(bool force)
        {
            if (!force && (DateTime.UtcNow - _lastLook).TotalMilliseconds < 500) return;
            _lastLook = DateTime.UtcNow;
            var fi = new FileInfo(Paths.MemoryPath);
            long len = fi.Exists ? fi.Length : 0;
            DateTime write = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
            if (len == _loadedLen && write == _loadedWrite) return;
            ByFp.Clear();
            _loadedLen = len;
            _loadedWrite = write;
            if (!fi.Exists) return;
            string text;
            try
            {
                text = Paths.ReadText(Paths.MemoryPath);
            }
            catch
            {
                _loadedLen = -1;
                return;
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');
                if (p.Length < 3 || p[2].Length == 0) continue;
                Fold(new MemEntry
                {
                    Time = p[0],
                    Result = p[1],
                    Fp = p[2],
                    Name = p.Length > 3 ? p[3] : "",
                    Folder = p.Length > 4 ? p[4] : "",
                    Checks = p.Length > 5 ? p[5] : "",
                    Reason = p.Length > 6 ? p[6] : ""
                });
            }
        }

        private static void Fold(MemEntry e)
        {
            if (e.Result == "忘记") ByFp.Remove(e.Fp);
            else ByFp[e.Fp] = e;
        }
    }
}
