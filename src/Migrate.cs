using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SucaiShaixuan
{
    // A new version is usually unzipped into a new folder, which would start with an empty 筛选记忆 (every
    // video that passed before would be checked again) and default rules. On the first start in a fresh
    // folder this finds the previous install and copies its 筛选记忆.txt, 设置.ini (when ours is still
    // the default) and 上次设置.json. Where the app last ran is kept in Paths.LastHomePath; v2.2 and older
    // did not write that, so their folders are looked for next to this one and on the desktop.
    internal static class Migrate
    {
        // Returns what was brought over (a line for the log), or null.
        public static string Run()
        {
            // Only as the real 素材筛选器.exe: test tools that load this assembly must not copy anything
            // or move the pointer to their own folder.
            if (Assembly.GetEntryAssembly() != typeof(Migrate).Assembly) return null;
            string msg = null;
            try
            {
                if (!File.Exists(Paths.MemoryPath) && !File.Exists(Paths.StatePath))
                {
                    string old = FindOld();
                    if (old != null) msg = CopyFrom(old);
                }
            }
            catch (Exception ex)
            {
                Paths.AppendLog(Report.Now() + "  从旧版本带设置时出错：" + ex.Message);
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.LastHomePath));
                File.WriteAllText(Paths.LastHomePath, Paths.AppDir, new UTF8Encoding(false));
            }
            catch
            {
            }
            return msg;
        }

        private static string CopyFrom(string old)
        {
            var got = new List<string>();
            string mem = Path.Combine(old, "筛选记忆.txt");
            if (File.Exists(mem))
            {
                File.Copy(mem, Paths.MemoryPath, false);
                got.Add("筛选记忆（" + Memory.Count + " 个视频）");
            }
            string ini = Path.Combine(old, "设置.ini");
            if (File.Exists(ini) && Rules.Load().IsDefault())
            {
                byte[] mine = File.Exists(Paths.ConfigPath) ? File.ReadAllBytes(Paths.ConfigPath) : null;
                File.Copy(ini, Paths.ConfigPath, true);
                if (Rules.Load().IsDefault())
                {
                    if (mine != null) File.WriteAllBytes(Paths.ConfigPath, mine);   // theirs was the default too: keep ours
                }
                else
                {
                    got.Add("筛选规则");
                }
            }
            string state = Path.Combine(old, "上次设置.json");
            if (File.Exists(state))
            {
                File.Copy(state, Paths.StatePath, false);
                got.Add("上次选的文件夹");
            }
            if (got.Count == 0) return null;
            return "从旧版本（" + old + "）带过来了：" + string.Join("、", got) + "。";
        }

        // The previous install with the newest 筛选记忆.txt.
        private static string FindOld()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Paths.AppDir.TrimEnd('\\') };
            var found = new List<string>();
            Action<string> consider = dir =>
            {
                if (string.IsNullOrEmpty(dir)) return;
                dir = dir.Trim().TrimEnd('\\');
                if (!seen.Add(dir)) return;
                try
                {
                    if (File.Exists(Path.Combine(dir, "素材筛选器.exe")) && File.Exists(Path.Combine(dir, "筛选记忆.txt"))) found.Add(dir);
                }
                catch
                {
                }
            };
            try
            {
                if (File.Exists(Paths.LastHomePath)) consider(File.ReadAllText(Paths.LastHomePath));
            }
            catch
            {
            }
            var roots = new List<string>();
            string p = Path.GetDirectoryName(Paths.AppDir);
            for (int i = 0; i < 3 && !string.IsNullOrEmpty(p); i++)
            {
                roots.Add(p);
                p = Path.GetDirectoryName(p);
            }
            foreach (var sf in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile })
            {
                try
                {
                    string r = Environment.GetFolderPath(sf);
                    if (r.Length > 0) roots.Add(r);
                }
                catch
                {
                }
            }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            // Only folders that look like ours, two levels down: 素材筛选器_v2.2_便携版\素材筛选器, 视频素材工具_v2\素材筛选器
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                consider(root);
                foreach (string d1 in Dirs(root).Where(Ours))
                {
                    consider(d1);
                    foreach (string d2 in Dirs(d1).Where(Ours)) consider(d2);
                }
            }
            return found.OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "筛选记忆.txt"))).FirstOrDefault();
        }

        private static bool Ours(string dir)
        {
            string n = Path.GetFileName(dir);
            return n.Contains("素材筛选器") || n.Contains("视频素材");
        }

        private static IEnumerable<string> Dirs(string dir)
        {
            try
            {
                return Directory.GetDirectories(dir);
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
