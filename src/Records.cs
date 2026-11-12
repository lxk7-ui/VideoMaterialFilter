using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SucaiShaixuan
{
    internal sealed class Hit
    {
        public string Reason = "";
        public string Content = "";
        public string At = "";
    }

    // One video in the result list: the latest verdict found in 筛选记录.csv, or live from the engine.
    internal sealed class Rec
    {
        public string Time = "";
        public string Rel = "";
        public string Result = "";   // 保留 / 筛掉 / 出错 / 放回 / 重查 / 检查中
        public readonly List<Hit> Hits = new List<Hit>();
        public string Moved = "";    // where a rejected video is now, when this session knows it
        public double Secs;
        public bool Memo;            // handled from 筛选记忆 (checked before), not checked this time

        // Kept without checking because it passed before (历史通过). Rows from 筛选记录.csv carry it in the
        // reason: 历史通过 since v2.3, 之前查过 in v2.1 / v2.2.
        public bool IsMemo
        {
            get
            {
                return Result == "保留" && (Memo || Hits.Any(h => h.Reason == MemoReason || h.Reason == "之前查过"));
            }
        }

        public const string MemoReason = "历史通过";

        // Where and when it passed before, as the engine wrote it ("2026-11-07 21:30 查过（在「密封毛条」）").
        public string MemoNote
        {
            get
            {
                var h = Hits.FirstOrDefault(x => x.Reason == MemoReason || x.Reason == "之前查过");
                return h != null ? h.Content : "";
            }
        }

        public string Name
        {
            get { return Path.GetFileName(Rel); }
        }

        public string SubDir
        {
            get { return Path.GetDirectoryName(Rel) ?? ""; }
        }

        public string ReasonText
        {
            get { return string.Join("、", Hits.Select(h => h.Reason).Where(s => s.Length > 0).Distinct()); }
        }

        public string DetailText
        {
            get
            {
                return string.Join("；", Hits.Where(h => h.Content.Length > 0)
                    .Select(h => (h.At.Length > 0 ? h.At + " " : "") + "「" + h.Content + "」"));
            }
        }
    }

    // 筛选记录.csv in the checked folder. The engine appends to it while it runs; this side
    // appends 放回 / 重查 / crash rows. Columns: 检查时间, 文件, 结果, 原因, 命中内容, 出现位置.
    internal static class Report
    {
        public const string FileName = "筛选记录.csv";
        public const string RejectDir = "已筛掉";
        private static readonly string[] Head = { "检查时间", "文件", "结果", "原因", "命中内容", "出现位置" };

        public static string PathOf(string folder)
        {
            return Path.Combine(folder, FileName);
        }

        public static string Now()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        // Latest record per video, oldest first. Several rows with the same time, file and result
        // are one check that found several problems.
        public static List<Rec> Load(string folder)
        {
            var order = new List<Rec>();
            var latest = new Dictionary<string, Rec>(StringComparer.OrdinalIgnoreCase);
            string p = PathOf(folder);
            if (!File.Exists(p)) return order;
            List<string[]> rows;
            try
            {
                rows = ParseCsv(Paths.ReadText(p));
            }
            catch
            {
                return order;
            }
            Rec last = null;
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                if (r.Length < 3 || r[1].Length == 0) continue;
                string time = r[0], rel = r[1], result = r[2];
                Rec rec;
                if (last != null && last.Time == time && last.Rel == rel && last.Result == result)
                {
                    rec = last;
                }
                else
                {
                    rec = new Rec { Time = time, Rel = rel, Result = result };
                    Rec old;
                    if (latest.TryGetValue(rel, out old)) order.Remove(old);
                    latest[rel] = rec;
                    order.Add(rec);
                    last = rec;
                }
                string reason = r.Length > 3 ? r[3] : "", hit = r.Length > 4 ? r[4] : "", at = r.Length > 5 ? r[5] : "";
                if (reason.Length > 0 || hit.Length > 0) rec.Hits.Add(new Hit { Reason = reason, Content = hit, At = at });
            }
            return order;
        }

        public static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(sb.ToString());
                    sb.Clear();
                    if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row.ToArray());
                    row = new List<string>();
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0 || row.Count > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }

        private static string Field(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // Appends rows; retries a few times because Excel or the engine may hold the file briefly.
        // A new file is UTF-8 with BOM (Excel opens it right); one that Excel saved as ANSI (GBK)
        // gets GBK lines, so it stays readable there.
        public static bool Append(string folder, IEnumerable<string[]> rows)
        {
            string p = PathOf(folder);
            var sb = new StringBuilder();
            bool isNew = !File.Exists(p);
            if (isNew) sb.Append(string.Join(",", Head.Select(Field))).Append("\r\n");
            foreach (var r in rows) sb.Append(string.Join(",", r.Select(Field))).Append("\r\n");
            Encoding enc = isNew ? new UTF8Encoding(false) : Paths.EncodingOf(p);
            byte[] data = (isNew ? new UTF8Encoding(true).GetPreamble() : new byte[0]).Concat(enc.GetBytes(sb.ToString())).ToArray();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(300);
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
            return false;
        }

        public static string FullPath(string folder, Rec r)
        {
            if (r.Result == "筛掉")
            {
                string moved = FindRejected(folder, r);
                if (moved != null) return moved;
            }
            return Path.Combine(folder, r.Rel);
        }

        public static string FindRejected(string folder, Rec r)
        {
            if (!string.IsNullOrEmpty(r.Moved) && File.Exists(r.Moved)) return r.Moved;
            string dir = Path.Combine(Path.Combine(folder, r.SubDir), RejectDir);
            string p = Path.Combine(dir, r.Name);
            if (File.Exists(p)) return p;
            string b = Path.GetFileNameWithoutExtension(r.Name), ext = Path.GetExtension(r.Name);
            for (int i = 1; i < 200; i++)
            {
                p = Path.Combine(dir, b + " (" + i + ")" + ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), b = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, b + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }

        // Moves a rejected video back next to the others. Returns the new relative name, or throws
        // with a message the user can read.
        public static string Restore(string folder, Rec r)
        {
            string src = FindRejected(folder, r);
            if (src == null) throw new IOException("在「" + RejectDir + "」文件夹里找不到它，可能已经被挪走或删掉了。");
            string dst = Unique(Path.Combine(folder, r.Rel));
            try
            {
                File.Move(src, dst);
            }
            catch (IOException ex)
            {
                throw new IOException("挪不动，是不是正在播放？关掉播放器再试。（" + ex.Message + "）");
            }
            string rel = Paths.Rel(folder, dst);
            var rows = new List<string[]> { new[] { Now(), r.Rel, "放回", "手动放回", "", "" } };
            // Renamed because a file with the same name came back meanwhile: remember the new name
            // too, otherwise the next run would check (and maybe reject) it again.
            if (!string.Equals(rel, r.Rel, StringComparison.OrdinalIgnoreCase)) rows.Add(new[] { Now(), rel, "放回", "手动放回", "", "" });
            Append(folder, rows);
            // Put back by hand = this video is fine, also when it turns up in another folder later.
            Memory.Remember(dst, "放回", "手动放回");
            r.Moved = "";
            return rel;
        }

        // The engine skips every file the report already lists, unless its latest row is 重查, and every
        // file 筛选记忆 knows; so both have to forget it.
        public static void MarkRecheck(string folder, Rec r)
        {
            string rel = r.Rel;
            if (r.Result == "筛掉") rel = Restore(folder, r);
            Append(folder, new[] { new[] { Now(), rel, "重查", "手动要求重新检查", "", "" } });
            Memory.Forget(Path.Combine(folder, rel));
        }
    }
}
