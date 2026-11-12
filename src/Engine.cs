using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace SucaiShaixuan
{
    internal sealed class JobOptions
    {
        public string Folder;
        public bool Frame;
        public bool Audio;
        public bool Watch;
        public bool AutoStop;
        public bool Recursive;
    }

    // Runs shaixuan.py --worker as a hidden child process and turns its stdout lines
    // ("KIND<TAB>text") into events on the UI thread. If the recognition libraries crash on a
    // video, the video is recorded as 出错 and a fresh worker continues with the next one.
    internal sealed class EngineRunner
    {
        private const int MaxCrash = 5;

        public Action<string> OnLog;
        public Action OnReady;
        public Action<int> OnQueue;
        public Action<string> OnFile;
        public Action<string, int> OnProgress;
        public Action<Rec> OnResult;
        public Action<int, int> OnIdle;   // (seconds until it stops by itself, -1 = never; files downloading)
        public Action<string> OnFail;
        public Action<bool> OnFinished;   // true = stopped by the user

        private readonly Dispatcher _ui;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private JobOptions _opts;
        private Process _proc;
        private string _stopFile;
        private string _current;
        private int _crashes;
        private int _spawns;
        private bool _stopping;
        private bool _failed;
        private DateTime _lastMsg;
        private bool _loading;
        private bool _warned;
        private DispatcherTimer _watchdog;

        public bool Running { get; private set; }

        // Why the run ended with an error (null = it did not): the engine's FAIL text, or a crash.
        public string FailText { get; private set; }

        public EngineRunner(Dispatcher ui)
        {
            _ui = ui;
        }

        public void Start(JobOptions opts)
        {
            _opts = opts;
            _stopping = false;
            _crashes = 0;
            Spawn();
            _watchdog = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(10) };
            _watchdog.Tick += (s, e) => Watch();
            _watchdog.Start();
        }

        private void Spawn()
        {
            _spawns++;
            _current = null;
            _failed = false;
            _loading = true;
            _warned = false;
            _lastMsg = DateTime.Now;
            _stopFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sucai-stop-" + Process.GetCurrentProcess().Id + "-" + _spawns + ".flag");
            TryDelete(_stopFile);
            var job = new Dictionary<string, object>
            {
                { "folder", _opts.Folder },
                { "frame", _opts.Frame },
                { "audio", _opts.Audio },
                { "watch", _opts.Watch },
                { "auto_stop", _opts.AutoStop },
                { "recursive", _opts.Recursive },
                { "stop_file", _stopFile },
                { "parent_pid", Process.GetCurrentProcess().Id }
            };
            var psi = PythonStartInfo("--worker -");
            psi.EnvironmentVariables["SUCAI_JOB"] = _json.Serialize(job);

            Paths.AppendLog("\n==== " + Report.Now() + " 开始筛选：" + _opts.Folder + " ====");
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) Line(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Paths.AppendLog(e.Data); };
            p.Start();
            p.StandardInput.Close();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _proc = p;
            Running = true;
            var t = new Thread(() =>
            {
                p.WaitForExit();
                int code = p.ExitCode;
                _ui.BeginInvoke(new Action(() => Exited(p, code)));
            });
            t.IsBackground = true;
            t.Start();
        }

        // Hidden console-less run of the bundled Python on shaixuan.py with the given arguments.
        internal static ProcessStartInfo PythonStartInfo(string engineArgs)
        {
            var psi = new ProcessStartInfo(Paths.PythonExe)
            {
                Arguments = "-u -X utf8 \"" + Paths.EngineScript + "\" " + engineArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Paths.AppDir
            };
            var env = psi.EnvironmentVariables;
            // A Python installed on this PC must not leak into the bundled one.
            foreach (string k in new[] { "PYTHONHOME", "PYTHONPATH", "PYTHONSTARTUP", "PYTHONEXECUTABLE", "PYTHONUSERBASE", "VIRTUAL_ENV", "CONDA_PREFIX" })
            {
                if (env.ContainsKey(k)) env.Remove(k);
            }
            env["PYTHONNOUSERSITE"] = "1";
            env["PYTHONUTF8"] = "1";
            env["PYTHONIOENCODING"] = "utf-8";
            env["KMP_DUPLICATE_LIB_OK"] = "TRUE";
            env["HF_ENDPOINT"] = "https://hf-mirror.com";
            env["HF_HUB_DISABLE_XET"] = "1";
            env["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1";
            env["SUCAI_HOME"] = Paths.RuntimeDir;
            env["SUCAI_DATA"] = Paths.AppDir;
            return psi;
        }

        // Called on the reader thread.
        private void Line(string raw)
        {
            int tab = raw.IndexOf('\t');
            string kind = tab < 0 ? raw : raw.Substring(0, tab);
            string text = tab < 0 ? "" : raw.Substring(tab + 1).Replace('\x1f', '\n');
            _ui.BeginInvoke(new Action(() => Handle(kind, text)));
        }

        private void Handle(string kind, string text)
        {
            _lastMsg = DateTime.Now;
            switch (kind)
            {
                case "LOG":
                    _loading = text.StartsWith("正在加载");
                    if (OnLog != null) OnLog(text);
                    break;
                case "READY":
                    _loading = false;
                    if (OnReady != null) OnReady();
                    break;
                case "QUEUE":
                    try
                    {
                        var d = _json.Deserialize<Dictionary<string, object>>(text);
                        if (OnQueue != null) OnQueue(Convert.ToInt32(d["todo"]));
                    }
                    catch
                    {
                    }
                    break;
                case "FILE":
                    _current = text;
                    if (OnFile != null) OnFile(text);
                    break;
                case "PROG":
                    try
                    {
                        var d = _json.Deserialize<Dictionary<string, object>>(text);
                        if (OnProgress != null) OnProgress(Convert.ToString(d["stage"]), Convert.ToInt32(d["pct"]));
                    }
                    catch
                    {
                    }
                    break;
                case "RESULT":
                    Rec r = ParseResult(text);
                    if (r != null && OnResult != null) OnResult(r);
                    break;
                case "DONE":
                    _current = null;
                    _crashes = 0;
                    break;
                case "IDLE":
                    int stopIn = -1, downloading = 0;
                    try
                    {
                        var d = _json.Deserialize<Dictionary<string, object>>(text);
                        if (d != null && d.ContainsKey("stop_in")) stopIn = Convert.ToInt32(d["stop_in"]);
                        if (d != null && d.ContainsKey("downloading")) downloading = Convert.ToInt32(d["downloading"]);
                    }
                    catch
                    {
                    }
                    if (OnIdle != null) OnIdle(stopIn, downloading);
                    break;
                case "FAIL":
                    _failed = true;
                    FailText = text;
                    if (OnFail != null) OnFail(text);
                    break;
            }
        }

        private Rec ParseResult(string text)
        {
            try
            {
                var d = _json.Deserialize<Dictionary<string, object>>(text);
                var r = new Rec
                {
                    Rel = Convert.ToString(d["rel"]),
                    Time = d.ContainsKey("time") ? Convert.ToString(d["time"]) : Report.Now(),
                    Result = Convert.ToString(d["result"]),
                    Moved = d.ContainsKey("moved") ? Convert.ToString(d["moved"]) : "",
                    Secs = d.ContainsKey("secs") ? Convert.ToDouble(d["secs"]) : 0.0,
                    Memo = d.ContainsKey("memo") && Convert.ToBoolean(d["memo"])
                };
                var items = d.ContainsKey("items") ? d["items"] as System.Collections.IEnumerable : null;
                if (items != null)
                {
                    foreach (var o in items)
                    {
                        var h = o as Dictionary<string, object>;
                        if (h == null) continue;
                        r.Hits.Add(new Hit
                        {
                            Reason = Convert.ToString(h["reason"]),
                            Content = Convert.ToString(h["hit"]),
                            At = Convert.ToString(h["at"])
                        });
                    }
                }
                return r;
            }
            catch
            {
                return null;
            }
        }

        private void Watch()
        {
            if (!Running || _stopping || _warned || !_loading) return;
            if ((DateTime.Now - _lastMsg).TotalMinutes < 3) return;
            _warned = true;
            if (OnLog != null) OnLog("已经 3 分钟没有动静了。点「停止」再点「开始筛选」试一次；还卡住就把「运行日志」发给 Claude。");
        }

        public void Stop()
        {
            if (!Running) return;
            _stopping = true;
            try
            {
                File.WriteAllText(_stopFile, "stop");
            }
            catch
            {
                Kill();
                return;
            }
            // The engine checks the flag every half second; give the current frame a moment.
            var t = new DispatcherTimer(DispatcherPriority.Background, _ui) { Interval = TimeSpan.FromSeconds(6) };
            Process p = _proc;
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (Running && _proc == p) Kill();
            };
            t.Start();
        }

        public void Kill()
        {
            _stopping = true;
            try
            {
                if (_proc != null && !_proc.HasExited) _proc.Kill();
            }
            catch
            {
            }
        }

        private void Exited(Process p, int code)
        {
            if (p != _proc) return;
            TryDelete(_stopFile);
            Running = false;
            if (_stopping || code == 0 || code == 2 || _failed)
            {
                Finish(_stopping);
                return;
            }
            string codeText = "0x" + ((uint)code).ToString("X8");
            if (_current != null)
            {
                _crashes++;
                string rel = _current;
                if (OnLog != null) OnLog("检查「" + rel + "」时识别组件崩溃了（代码 " + codeText + "），这个视频先跳过，标成「出错」。");
                string why = "检查时识别组件崩溃（代码 " + codeText + "），请手动看一下";
                Report.Append(_opts.Folder, new[] { new[] { Report.Now(), rel, "出错", why, "", "" } });
                var r = new Rec { Rel = rel, Time = Report.Now(), Result = "出错" };
                r.Hits.Add(new Hit { Reason = why });
                if (OnResult != null) OnResult(r);
                if (_crashes <= MaxCrash)
                {
                    if (OnLog != null) OnLog("自动重新启动识别，接着查后面的视频……");
                    Spawn();
                    return;
                }
                if (OnLog != null) OnLog("连续崩溃太多次，先停下了。把「运行日志.txt」发给 Claude。");
                FailText = "识别组件连续崩溃（代码 " + codeText + "）";
                Finish(false);
                return;
            }
            FailText = "识别组件启动失败（代码 " + codeText + "）";
            if (OnLog != null)
            {
                OnLog("识别组件启动失败（代码 " + codeText + "）。可以把软件重新解压一份再试；还不行就把「运行日志.txt」发给 Claude。");
                string tail = Paths.ReadLogTail(10);
                if (tail.Length > 0) OnLog("运行日志最后几行：\n" + tail);
            }
            Finish(false);
        }

        private void Finish(bool stopped)
        {
            if (_watchdog != null) _watchdog.Stop();
            _proc = null;
            if (OnFinished != null) OnFinished(stopped);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
