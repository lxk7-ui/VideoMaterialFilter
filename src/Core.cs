using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SucaiShaixuan
{
    public static class AppInfo
    {
        public const string Name = "素材筛选器";
        public const string Version = "v2.3";
    }

    // Package layout (everything is relative to the exe, so the folder can be unzipped anywhere):
    //   素材筛选器.exe  设置.ini  运行日志.txt  上次设置.json  筛选记忆.txt
    //   runtime\python\python.exe       private Python 3.11 with the recognition libraries
    //   runtime\app\shaixuan.py         recognition engine (runs as a child process)
    //   runtime\models\faster-whisper-* speech models (small comes with the package, the rest via 模型下载)
    internal static class Paths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static readonly string RuntimeDir = Path.Combine(AppDir, "runtime");
        public static readonly string PythonExe = Path.Combine(RuntimeDir, "python\\python.exe");
        public static readonly string EngineScript = Path.Combine(RuntimeDir, "app\\shaixuan.py");
        public static readonly string ModelDir = Path.Combine(RuntimeDir, "models");
        public static readonly string ConfigPath = Path.Combine(AppDir, "设置.ini");
        public static readonly string StatePath = Path.Combine(AppDir, "上次设置.json");
        public static readonly string LogPath = Path.Combine(AppDir, "运行日志.txt");
        public static readonly string IconPath = Path.Combine(AppDir, "素材筛选器.ico");
        public static readonly string MemoryPath = Path.Combine(AppDir, "筛选记忆.txt");
        public static readonly string MemoryBackupPath = Path.Combine(AppDir, "筛选记忆_清空前.txt");
        // Which folder the app last ran from (outside the app folder, so a newer version unzipped somewhere
        // else can find the old 筛选记忆 and rules; see Migrate).
        public static readonly string LastHomePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "素材筛选器", "上次位置.txt");

        private static readonly object LogLock = new object();

        // Explorer can run an exe straight out of a zip; it then sits alone in a temp folder.
        public static string MissingPartsMessage()
        {
            if (File.Exists(PythonExe) && File.Exists(EngineScript)) return null;
            string dir = AppDir.ToLowerInvariant();
            if (dir.Contains("\\temp") && (dir.Contains(".zip") || dir.Contains("rar$") || dir.Contains("7z")))
            {
                return "现在是在压缩包里直接打开的，软件找不到它的识别组件。\n\n请先把压缩包「全部解压」出来，再双击解压出来的文件夹里的「素材筛选器.exe」。";
            }
            return "软件文件夹不完整：找不到 runtime 文件夹里的识别组件。\n\n请把压缩包重新完整解压一次，不要只复制「素材筛选器.exe」。";
        }

        public static void AppendLog(string text)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(LogPath, text.EndsWith("\n") ? text : text + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        // 运行日志 over 2 MB keeps only its second half.
        public static void TrimLog()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (!fi.Exists || fi.Length <= 2 * 1024 * 1024) return;
                byte[] all = File.ReadAllBytes(LogPath);
                int start = all.Length / 2;
                while (start < all.Length && all[start] != (byte)'\n') start++;
                var tail = new byte[all.Length - start - 1 > 0 ? all.Length - start - 1 : 0];
                Array.Copy(all, Math.Min(all.Length, start + 1), tail, 0, tail.Length);
                File.WriteAllBytes(LogPath, tail);
            }
            catch
            {
            }
        }

        public static string ReadLogTail(int lines)
        {
            try
            {
                string[] all;
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    all = sr.ReadToEnd().Split('\n');
                }
                var sb = new StringBuilder();
                int n = 0;
                for (int i = all.Length - 1; i >= 0 && n < lines; i--)
                {
                    string l = all[i].TrimEnd('\r');
                    if (l.Trim().Length == 0) continue;
                    sb.Insert(0, l + "\n");
                    n++;
                }
                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "";
            }
        }

        // Reads text written by Python (UTF-8, maybe with BOM) or saved as ANSI by Notepad or Excel (GBK).
        // Decided line by line, so a file that Excel saved as GBK and that got UTF-8 lines appended
        // afterwards (by an older version) still reads correctly.
        public static string ReadText(string path)
        {
            byte[] b = ReadBytes(path);
            var strict = new UTF8Encoding(false, true);
            try
            {
                return StripBom(strict.GetString(b));
            }
            catch (DecoderFallbackException)
            {
            }
            Encoding gbk = Encoding.GetEncoding(936);
            var sb = new StringBuilder(b.Length);
            int start = 0;
            while (start < b.Length)
            {
                int nl = Array.IndexOf(b, (byte)'\n', start);   // never part of a GBK or UTF-8 multi-byte character
                int end = nl < 0 ? b.Length : nl + 1;
                try
                {
                    sb.Append(strict.GetString(b, start, end - start));
                }
                catch (DecoderFallbackException)
                {
                    sb.Append(gbk.GetString(b, start, end - start));
                }
                start = end;
            }
            return StripBom(sb.ToString());
        }

        // GBK when the file is there and is not UTF-8 (saved as ANSI by Excel or Notepad), so lines added
        // to it stay readable in Excel; otherwise UTF-8.
        public static Encoding EncodingOf(string path)
        {
            try
            {
                if (!File.Exists(path)) return new UTF8Encoding(false);
                new UTF8Encoding(false, true).GetString(ReadBytes(path));
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(936);
            }
            catch
            {
                return new UTF8Encoding(false);
            }
        }

        private static byte[] ReadBytes(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var b = new byte[fs.Length];
                int got = 0;
                while (got < b.Length)
                {
                    int n = fs.Read(b, got, b.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got == b.Length) return b;
                var cut = new byte[got];
                Array.Copy(b, cut, got);
                return cut;
            }
        }

        private static string StripBom(string s)
        {
            return s.Length > 0 && s[0] == '\uFEFF' ? s.Substring(1) : s;
        }

        // Path of a file inside folder, relative to it. Works for a drive root ("E:\") too.
        public static string Rel(string folder, string full)
        {
            string root = (folder ?? "").TrimEnd('\\', '/');
            return full.Length > root.Length ? full.Substring(root.Length).TrimStart('\\', '/') : "";
        }
    }

    // Window choices remembered between runs (上次设置.json).
    public class AppState
    {
        public string Folder = "";
        public bool Frame = true;
        public bool Audio = true;
        public bool Watch = true;
        public bool AutoStop = true;   // with Watch: stop by itself when nothing new turns up for a while
        public bool Recursive;

        public static AppState Load()
        {
            try
            {
                if (File.Exists(Paths.StatePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<AppState>(Paths.ReadText(Paths.StatePath));
                    if (s != null)
                    {
                        if (s.Folder == null) s.Folder = "";
                        return s;
                    }
                }
            }
            catch
            {
            }
            return new AppState();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(Paths.StatePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
