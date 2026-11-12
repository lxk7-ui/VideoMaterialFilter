using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SucaiShaixuan
{
    // 设置.ini, shared with the Python engine (configparser: case-sensitive keys, "=" or ":",
    // no inline comments). Saving rewrites the whole file from the template below so the
    // comments stay helpful for anyone who opens it in Notepad.
    internal sealed class Rules
    {
        public const string DefaultDouyin = "抖音号,抖音ID,抖音,douyin";
        public const string DefaultPrice = "价格,价钱,到手价,券后,原价,现价,售价,特价,秒杀价,活动价,优惠价,多少钱,几块钱";
        public static readonly string[] Models = { "tiny", "base", "small", "medium" };

        public string DouyinWords = DefaultDouyin;
        public bool CheckAt = true;
        public double Interval = 1.0;
        public double MinScore = 0.6;
        public string PriceWords = DefaultPrice;
        public string Model = "small";
        public bool AudioDouyin;
        public bool StopEarly = true;
        public double IdleMinutes = 2.0;   // 查完自动停止: how long to wait for new videos first

        public static Rules Load()
        {
            var r = new Rules();
            if (!File.Exists(Paths.ConfigPath)) return r;
            string section = "";
            string text;
            try
            {
                text = Paths.ReadText(Paths.ConfigPath);
            }
            catch
            {
                return r;
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOfAny(new[] { '=', ':' });
                if (eq <= 0) continue;
                r.Set(section, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return r;
        }

        private void Set(string section, string key, string val)
        {
            switch (section + "/" + key)
            {
                case "画面/抖音关键词": DouyinWords = val; break;
                case "画面/查@用户名": CheckAt = Yes(val); break;
                case "画面/间隔秒数": Interval = Num(val, 1.0); break;
                case "画面/可信度": MinScore = Num(val, 0.6); break;
                case "价格/价格关键词": PriceWords = val; break;
                case "语音/模型": if (val.Length > 0) Model = val; break;
                case "语音/也查抖音关键词": AudioDouyin = Yes(val); break;
                case "其他/找到就停": StopEarly = Yes(val); break;
                case "其他/查完等几分钟": IdleMinutes = Math.Min(240.0, Math.Max(0.0, Num(val, 2.0))); break;
            }
        }

        // Same rules as a fresh install (an untouched 设置.ini); used when bringing rules over from an older version.
        public bool IsDefault()
        {
            var d = new Rules();
            return SplitWords(DouyinWords).SequenceEqual(SplitWords(d.DouyinWords)) && CheckAt == d.CheckAt &&
                   Interval == d.Interval && MinScore == d.MinScore &&
                   SplitWords(PriceWords).SequenceEqual(SplitWords(d.PriceWords)) && Model == d.Model &&
                   AudioDouyin == d.AudioDouyin && StopEarly == d.StopEarly && IdleMinutes == d.IdleMinutes;
        }

        private static bool Yes(string s)
        {
            s = (s ?? "").Trim();
            return s == "是" || s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s == "开";
        }

        private static double Num(string s, double def)
        {
            double d;
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        private static string Fmt(double d)
        {
            return d.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // Same separators the engine accepts; duplicates and blanks are dropped.
        public static List<string> SplitWords(string s)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (string w in (s ?? "").Split(new[] { ',', '，', '、', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = w.Trim();
                if (t.Length > 0 && seen.Add(t)) list.Add(t);
            }
            return list;
        }

        public string Summary()
        {
            var parts = new List<string>
            {
                "抖音关键词 " + SplitWords(DouyinWords).Count + " 个" + (CheckAt ? "，查 @用户名" : ""),
                "价格关键词 " + SplitWords(PriceWords).Count + " 个，另外自动认 ¥19.9、九块九、只要39.9 这类说法",
                "每 " + Fmt(Interval) + " 秒看一帧画面，语音模型 " + Model + (StopEarly ? "，找到一个问题就停" : "")
            };
            return string.Join("\n", parts);
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; 素材筛选器设置。一般在软件里点「筛选规则」改就行，也可以直接改这个文件。");
            sb.AppendLine("; 多个词用英文逗号隔开。");
            sb.AppendLine();
            sb.AppendLine("[画面]");
            sb.AppendLine("; 画面里出现这些字就筛掉（抖音水印、抖音号）");
            sb.AppendLine("抖音关键词 = " + string.Join(",", SplitWords(DouyinWords)));
            sb.AppendLine("; 画面里出现「@用户名」也算抖音号：是 / 否");
            sb.AppendLine("查@用户名 = " + (CheckAt ? "是" : "否"));
            sb.AppendLine("; 每隔几秒看一帧画面。越小越仔细，也越慢");
            sb.AppendLine("间隔秒数 = " + Fmt(Math.Max(0.2, Interval)));
            sb.AppendLine("; 文字识别的可信度门槛，0~1。越高越不容易误判，但也可能漏掉");
            sb.AppendLine("可信度 = " + Fmt(Math.Min(0.99, Math.Max(0.0, MinScore))));
            sb.AppendLine();
            sb.AppendLine("[价格]");
            sb.AppendLine("; 画面字幕和语音里出现这些词，就算说了价格");
            sb.AppendLine("; 「¥19.9」「9.9元」「九块九」「十九块钱」「只要39.9」这类说法程序会自动识别，不用写在这里");
            sb.AppendLine("价格关键词 = " + string.Join(",", SplitWords(PriceWords)));
            sb.AppendLine();
            sb.AppendLine("[语音]");
            sb.AppendLine("; 语音识别模型：tiny / base / small / medium。越大越准，也越慢");
            sb.AppendLine("; 软件里已经带了 small；换成别的，先在软件里点「模型下载」下载好");
            sb.AppendLine("模型 = " + Model);
            sb.AppendLine("; 语音里说到上面的抖音关键词也筛掉：是 / 否");
            sb.AppendLine("也查抖音关键词 = " + (AudioDouyin ? "是" : "否"));
            sb.AppendLine();
            sb.AppendLine("[其他]");
            sb.AppendLine("; 找到一个问题就不再往下查（快很多）：是 / 否");
            sb.AppendLine("找到就停 = " + (StopEarly ? "是" : "否"));
            sb.AppendLine("; 勾了「自动检查新下载的视频」和「查完自动停止」时：查完以后等几分钟，");
            sb.AppendLine("; 这段时间里没有新视频、也没有正在下载的，就自动停止");
            sb.AppendLine("查完等几分钟 = " + Fmt(Math.Min(240.0, Math.Max(0.0, IdleMinutes))));
            File.WriteAllText(Paths.ConfigPath, sb.ToString(), new UTF8Encoding(true));
        }

        public Rules Clone()
        {
            return (Rules)MemberwiseClone();
        }
    }
}
