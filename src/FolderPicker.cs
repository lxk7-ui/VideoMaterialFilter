using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    public enum PickerKind { Drive, Folder, File, Info }

    // One row of the picker list (a drive, folder, file or an informational line).
    public sealed class PickerEntry
    {
        public string FullPath;
        public string Name;
        public PickerKind Kind;
        public string Detail;
        public string Right;
        public bool Enabled = true;
        public int Score;
        public int Depth;
        public List<int> Highlight;

        public bool Selectable
        {
            get { return Enabled && Kind != PickerKind.Info; }
        }
    }

    // Decides whether a folder/file name matches the search keyword, how high it ranks and which
    // characters to highlight. The helpers below do the character-level matching.
    internal sealed class NameMatcher
    {
        public readonly string Keyword;
        public readonly string[] Terms;
        public readonly bool Exact;

        private const string Separators = " 　_-.·・、，,()（）[]【】{}《》<>#&+~";

        public NameMatcher(string keyword, bool exact)
        {
            Keyword = (keyword ?? "").Trim();
            Exact = exact;
            Terms = Keyword.Split(new[] { ' ', '　', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Returns a score >= 0 when the name matches (higher is listed first) or -1 for no match.
        public int Match(string name, bool isFile, List<int> hl)
        {
            if (Keyword.Length == 0 || string.IsNullOrEmpty(name)) return -1;
            string stem = isFile ? System.IO.Path.GetFileNameWithoutExtension(name) : name;
            hl.Clear();
            int score = Score(name, stem, hl);
            if (score < 0) hl.Clear();
            return score;
        }

        // name: full name as shown; stem: name without extension (same as name for folders).
        // Fill hl with the indices of matched characters in name.
        // 精确 = the whole name (or a file's name without extension) equals the keyword.
        // 模糊 = every space-separated term is found, trying contiguous, then ignoring
        //        spaces/punctuation, then characters in order with limited gaps; looser ranks lower.
        private int Score(string name, string stem, List<int> hl)
        {
            bool whole = string.Equals(name.Trim(), Keyword, StringComparison.OrdinalIgnoreCase);
            bool wholeStem = !whole && string.Equals(stem.Trim(), Keyword, StringComparison.OrdinalIgnoreCase);
            if (Exact)
            {
                if (!whole && !wholeStem) return -1;
                if (whole) MarkAll(name, hl);
                else for (int i = 0; i < stem.Length; i++) hl.Add(i);
                return 1000;
            }
            int score = 0;
            foreach (string term in Terms)
            {
                int pos = FindContiguous(name, term, hl);
                if (pos >= 0)
                {
                    score += 100 - Math.Min(pos, 30) + (pos == 0 ? 20 : 0);
                    continue;
                }
                if (FindLoose(name, term, hl) >= 0)
                {
                    score += 60;
                    continue;
                }
                if (term.Length < 2) return -1;
                var sub = new List<int>();
                int gaps = FindSubsequence(name, term, sub);
                if (gaps < 0 || gaps > Math.Max(3, term.Length * 2)) return -1;
                hl.AddRange(sub);
                score += Math.Max(5, 30 - gaps);
            }
            if (whole || wholeStem) score += 500;
            else if (name.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase)) score += 50;
            return score;
        }

        // Case-insensitive substring search. Returns the start index or -1.
        public static int FindContiguous(string name, string term, List<int> hl)
        {
            int i = name.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            for (int k = i; k < i + term.Length; k++) hl.Add(k);
            return i;
        }

        // Like FindContiguous but ignores spaces and punctuation on both sides, so "密封 毛条" finds
        // "密封_毛条". Returns the start index in name or -1.
        public static int FindLoose(string name, string term, List<int> hl)
        {
            string t = StripSeparators(term);
            if (t.Length == 0) return -1;
            for (int s = 0; s < name.Length; s++)
            {
                if (IsSeparator(name[s]) || !SameChar(name[s], t[0])) continue;
                var found = new List<int>();
                int k = 0;
                for (int i = s; i < name.Length && k < t.Length; i++)
                {
                    if (IsSeparator(name[i])) continue;
                    if (!SameChar(name[i], t[k])) break;
                    found.Add(i);
                    k++;
                }
                if (k == t.Length)
                {
                    hl.AddRange(found);
                    return s;
                }
            }
            return -1;
        }

        // Characters of term appear in name in order, not necessarily adjacent ("密条" finds
        // "密封毛条"). Returns the number of skipped characters between matches, or -1.
        public static int FindSubsequence(string name, string term, List<int> hl)
        {
            string t = StripSeparators(term);
            if (t.Length == 0) return -1;
            var found = new List<int>();
            int gaps = 0;
            int pos = 0;
            foreach (char c in t)
            {
                int i = pos;
                while (i < name.Length && !SameChar(name[i], c)) i++;
                if (i >= name.Length) return -1;
                if (found.Count > 0) gaps += i - pos;
                found.Add(i);
                pos = i + 1;
            }
            hl.AddRange(found);
            return gaps;
        }

        public static void MarkAll(string name, List<int> hl)
        {
            for (int i = 0; i < name.Length; i++) hl.Add(i);
        }

        private static bool IsSeparator(char c)
        {
            return Separators.IndexOf(c) >= 0;
        }

        private static bool SameChar(char a, char b)
        {
            return a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }

        private static string StripSeparators(string s)
        {
            var chars = new List<char>(s.Length);
            foreach (char c in s) if (!IsSeparator(c)) chars.Add(c);
            return new string(chars.ToArray());
        }
    }

    // Visual for one PickerEntry; created per realized (virtualized) list container.
    internal sealed class PickerRow : Grid
    {
        public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
            "Entry", typeof(PickerEntry), typeof(PickerRow), new PropertyMetadata(null, OnEntryChanged));

        private static readonly Geometry FolderGeo = Geo(IconFolder);
        private static readonly Geometry DriveGeo = Geo(IconDrive);
        private static readonly Geometry FileGeo = Geo(IconFile);
        private static readonly Geometry VideoGeo = Geo(IconVideo);
        private static readonly Brush FolderBrush = SB(Color.FromRgb(240, 176, 60));
        private static readonly Brush DriveBrush = SB(CPrimary);
        private static readonly Brush FileBrush = SB(CTextHint);
        private static readonly Brush VideoBrush = SB(Color.FromRgb(120, 150, 190));
        private static readonly Brush MainBrush = SB(CTextMain);
        private static readonly Brush SubBrush = SB(CTextSub);
        private static readonly Brush HintBrush = SB(CTextHint);
        private static readonly Brush MarkBrush = SB(CAccentDeep);
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".flv", ".wmv", ".ts", ".webm", ".mpg", ".mpeg" };

        private readonly Path _icon = new Path();
        private readonly TextBlock _name = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly TextBlock _right = new TextBlock();

        public PickerRow()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _icon.Width = 18.0;
            _icon.Height = 18.0;
            _icon.Stretch = Stretch.Uniform;
            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _name.FontSize = 13.0;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _detail.FontSize = 11.5;
            _detail.Foreground = HintBrush;
            _detail.TextTrimming = TextTrimming.CharacterEllipsis;
            _detail.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
            texts.Children.Add(_name);
            texts.Children.Add(_detail);
            _right.FontSize = 11.5;
            _right.Foreground = HintBrush;
            _right.VerticalAlignment = VerticalAlignment.Center;
            _right.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
            SetColumn(_icon, 0);
            SetColumn(texts, 1);
            SetColumn(_right, 2);
            Children.Add(_icon);
            Children.Add(texts);
            Children.Add(_right);
        }

        private static void OnEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PickerRow)d).Apply(e.NewValue as PickerEntry);
        }

        private void Apply(PickerEntry e)
        {
            if (e == null) return;
            switch (e.Kind)
            {
                case PickerKind.Drive:
                    _icon.Data = DriveGeo;
                    _icon.Fill = DriveBrush;
                    break;
                case PickerKind.Folder:
                    _icon.Data = FolderGeo;
                    _icon.Fill = FolderBrush;
                    break;
                case PickerKind.File:
                    bool video = Array.IndexOf(VideoExt, (System.IO.Path.GetExtension(e.Name) ?? "").ToLowerInvariant()) >= 0;
                    _icon.Data = video ? VideoGeo : FileGeo;
                    _icon.Fill = video ? VideoBrush : FileBrush;
                    break;
                default:
                    _icon.Data = null;
                    break;
            }
            _icon.Visibility = e.Kind == PickerKind.Info ? Visibility.Collapsed : Visibility.Visible;
            _name.Foreground = e.Kind == PickerKind.Info ? HintBrush : (e.Kind == PickerKind.File ? SubBrush : MainBrush);
            _name.Inlines.Clear();
            if (e.Highlight == null || e.Highlight.Count == 0)
            {
                _name.Inlines.Add(new Run(e.Name ?? ""));
            }
            else
            {
                var mark = new bool[e.Name.Length];
                foreach (int i in e.Highlight) if (i >= 0 && i < mark.Length) mark[i] = true;
                int start = 0;
                while (start < mark.Length)
                {
                    int end = start;
                    while (end < mark.Length && mark[end] == mark[start]) end++;
                    var run = new Run(e.Name.Substring(start, end - start));
                    if (mark[start])
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.Foreground = MarkBrush;
                    }
                    _name.Inlines.Add(run);
                    start = end;
                }
            }
            _detail.Text = e.Detail ?? "";
            _detail.Visibility = string.IsNullOrEmpty(e.Detail) ? Visibility.Collapsed : Visibility.Visible;
            _right.Text = e.Right ?? "";
            ToolTip = e.Kind == PickerKind.Info ? null : e.FullPath;
        }
    }

    // Folder chooser used by every "浏览" button for folders.
    internal sealed class FolderPicker : Window
    {
        private const string ThisPC = "ThisPC";
        private const int MaxHits = 3000;

        private static bool _lastExact;
        private static bool _lastRecursive = true;
        private static bool _lastShowFiles = true;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        private sealed class SearchRun
        {
            public string Keyword;
            public string Scope;
            public NameMatcher Matcher;
            public bool Recursive;
            public bool IncludeFiles;
            public CancellationToken Token;
            public readonly ConcurrentQueue<PickerEntry> Hits = new ConcurrentQueue<PickerEntry>();
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public int ScannedDirs;
            public int Found;
            public volatile bool Finished;
            public volatile bool Truncated;
            public volatile bool Stopped;
        }

        private sealed class Listing
        {
            public readonly List<PickerEntry> Entries = new List<PickerEntry>();
            public int Folders;
            public int Files;
            public string Error;
        }

        private string _currentDir;
        private string _selectedPath;
        private string _selectAfterLoad;
        private bool _suppressSearch;
        private TextBox _addr;
        private TextBox _search;
        private TextBlock _hint;
        private Button _clearBtn;
        private ToggleButton _fuzzyBtn;
        private ToggleButton _exactBtn;
        private CheckBox _subCheck;
        private CheckBox _filesCheck;
        private TextBlock _status;
        private Button _stopBtn;
        private ListBox _list;
        private TextBlock _selText;
        private ObservableCollection<PickerEntry> _items = new ObservableCollection<PickerEntry>();
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _pump;
        private CancellationTokenSource _cts;
        private SearchRun _run;

        public string SelectedPath
        {
            get { return _selectedPath; }
        }

        public FolderPicker(string initialPath)
        {
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(700.0, work.Width - 40.0);
            Height = Math.Min(740.0, work.Height - 40.0);
            MinWidth = Math.Min(540.0, Width);
            MinHeight = Math.Min(480.0, Height);
            Title = "选择文件夹";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
            _currentDir = ResolveInitial(initialPath);
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                StartSearch();
            };
            _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pump.Tick += (s, e) => PumpResults();
            BuildUI();
            WindowResizer.Enable(this, 10.0);
            PreviewKeyDown += OnPreviewKey;
            Loaded += (s, e) => _search.Focus();
            Closed += (s, e) => CancelWork();
            UpdateHint();
            LoadDir();
        }

        // Starts from the given folder; if it no longer exists, from its nearest existing parent.
        private static string ResolveInitial(string p)
        {
            try
            {
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = System.IO.Path.GetFullPath(p.Trim().Trim('"'));
                    while (!string.IsNullOrEmpty(dir))
                    {
                        if (Directory.Exists(dir)) return dir;
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }
                }
            }
            catch
            {
            }
            return ThisPC;
        }

        private void BuildUI()
        {
            var root = new Grid { Background = SB(1, 255, 255, 255) };
            var frame = new Border
            {
                Margin = new Thickness(12.0),
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                Effect = Shadow(Color.FromRgb(110, 126, 150), 24.0, 0.0, 0.35)
            };
            root.Children.Add(frame);
            var g = new Grid();
            for (int i = 0; i < 6; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions[4].Height = new GridLength(1.0, GridUnitType.Star);
            frame.Child = g;
            Content = root;

            Style secondary = MainWindow.MakeSecondaryButtonStyle();
            Style input = MainWindow.MakeTextBoxStyle();
            Style check = MainWindow.MakeCheckBoxStyle();

            // Row 0: title bar (drag to move).
            var titleBar = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0, 18.0, 0.0, 0.0),
                Padding = new Thickness(18.0, 10.0, 10.0, 6.0)
            };
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(MakeIcon(IconFolder, SB(Color.FromRgb(240, 176, 60)), 18.0));
            titleStack.Children.Add(new TextBlock
            {
                Text = "选择文件夹",
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = SB(CTextMain),
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Button close = MainWindow.MakeWinButton(IconClose, true);
            close.Click += (s, e) => DialogResult = false;
            Grid.SetColumn(close, 1);
            tg.Children.Add(titleStack);
            tg.Children.Add(close);
            titleBar.Child = tg;
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                try { DragMove(); } catch { }
            };
            Grid.SetRow(titleBar, 0);
            g.Children.Add(titleBar);

            // Row 1: navigation (up, this PC, editable address).
            var nav = new Grid { Margin = new Thickness(16.0, 4.0, 16.0, 10.0) };
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Button up = ToolButton(IconArrowUp, "上一级", secondary);
            up.ToolTip = "返回上一级（Backspace）";
            up.Click += (s, e) => GoUp();
            Button pc = ToolButton(IconComputer, "此电脑", secondary);
            pc.Click += (s, e) => Navigate(ThisPC, null);
            // TextBox padding is applied twice by this template (host margin + text view), so values are halved.
            _addr = new TextBox
            {
                Height = 32.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5.0, 0.0, 5.0, 0.0),
                Style = input,
                FontSize = 12.5,
                ToolTip = "可直接输入或粘贴路径，按回车跳转"
            };
            _addr.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    GoToTyped();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(up, 0);
            Grid.SetColumn(pc, 1);
            Grid.SetColumn(_addr, 2);
            nav.Children.Add(up);
            nav.Children.Add(pc);
            nav.Children.Add(_addr);
            Grid.SetRow(nav, 1);
            g.Children.Add(nav);

            // Row 2: search box + fuzzy/exact switch.
            var sr = new Grid { Margin = new Thickness(16.0, 0.0, 16.0, 8.0) };
            sr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            sr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var host = new Grid();
            _search = new TextBox
            {
                Height = 34.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(17.0, 0.0, 15.0, 0.0),
                Style = input,
                FontSize = 13.0
            };
            _search.TextChanged += OnSearchTextChanged;
            _search.PreviewKeyDown += OnSearchKey;
            Path searchIcon = MakeIcon(IconSearch, SB(CTextHint), 16.0);
            searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
            searchIcon.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
            searchIcon.IsHitTestVisible = false;
            _hint = new TextBlock
            {
                Foreground = SB(CTextHint),
                FontSize = 13.0,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(36.0, 0.0, 30.0, 0.0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _clearBtn = new Button
            {
                Width = 24.0,
                Height = 24.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
                Focusable = false,
                Visibility = Visibility.Collapsed,
                ToolTip = "清除搜索（Esc）",
                Cursor = Cursors.Hand,
                Template = MainWindow.ButtonTpl(Brushes.Transparent, SB(CWinHover), SB(CBorder), Brushes.Transparent, null, null, new CornerRadius(12.0)),
                Content = new TextBlock { Text = "×", FontSize = 15.0, Foreground = SB(CTextSub), Margin = new Thickness(0.0, -2.0, 0.0, 0.0) }
            };
            _clearBtn.Click += (s, e) =>
            {
                _search.Text = "";
                _search.Focus();
            };
            host.Children.Add(_search);
            host.Children.Add(searchIcon);
            host.Children.Add(_hint);
            host.Children.Add(_clearBtn);
            Grid.SetColumn(host, 0);
            sr.Children.Add(host);

            var seg = new Border
            {
                CornerRadius = new CornerRadius(10.0),
                Background = SB(CInputBg),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                Padding = new Thickness(3.0),
                Height = 34.0
            };
            var segPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Style segStyle = SegmentStyle();
            _fuzzyBtn = new ToggleButton
            {
                Content = "模糊",
                Style = segStyle,
                ToolTip = "模糊搜索：名称中含有关键词即可；可用空格分隔多个关键词，忽略空格与符号，字符不连续也能找到"
            };
            _exactBtn = new ToggleButton
            {
                Content = "精确",
                Style = segStyle,
                ToolTip = "精确搜索：名称与关键词完全一致（不区分大小写，文件可省略扩展名）"
            };
            _fuzzyBtn.IsChecked = !_lastExact;
            _exactBtn.IsChecked = _lastExact;
            _fuzzyBtn.Click += (s, e) => SetMode(false);
            _exactBtn.Click += (s, e) => SetMode(true);
            segPanel.Children.Add(_fuzzyBtn);
            segPanel.Children.Add(_exactBtn);
            seg.Child = segPanel;
            Grid.SetColumn(seg, 1);
            sr.Children.Add(seg);
            Grid.SetRow(sr, 2);
            g.Children.Add(sr);

            // Row 3: options and live status.
            var opt = new Grid { Margin = new Thickness(18.0, 0.0, 16.0, 8.0) };
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _subCheck = new CheckBox
            {
                Style = check,
                IsChecked = _lastRecursive,
                Margin = new Thickness(0.0, 0.0, 18.0, 0.0),
                Content = new TextBlock { Text = "包含子文件夹", FontSize = 12.5, Foreground = SB(CTextMain) },
                ToolTip = "勾选：搜索当前位置下的所有层级；取消：只筛选当前这一层"
            };
            _filesCheck = new CheckBox
            {
                Style = check,
                IsChecked = _lastShowFiles,
                Content = new TextBlock { Text = "显示文件", FontSize = 12.5, Foreground = SB(CTextMain) },
                ToolTip = "在列表和搜索结果中同时显示文件（只用于查看，选择的仍是文件夹）"
            };
            _subCheck.Click += (s, e) => OptionsChanged();
            _filesCheck.Click += (s, e) => OptionsChanged();
            _status = new TextBlock
            {
                FontSize = 12.0,
                Foreground = SB(CTextHint),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12.0, 0.0, 6.0, 0.0)
            };
            _stopBtn = new Button
            {
                Content = "停止",
                Height = 26.0,
                Width = 50.0,
                FontSize = 12.0,
                Style = secondary,
                Visibility = Visibility.Collapsed
            };
            _stopBtn.Click += (s, e) => StopSearch();
            Grid.SetColumn(_subCheck, 0);
            Grid.SetColumn(_filesCheck, 1);
            Grid.SetColumn(_status, 2);
            Grid.SetColumn(_stopBtn, 3);
            opt.Children.Add(_subCheck);
            opt.Children.Add(_filesCheck);
            opt.Children.Add(_status);
            opt.Children.Add(_stopBtn);
            Grid.SetRow(opt, 3);
            g.Children.Add(opt);

            // Row 4: the list. It sits in a star row, so it gets a bounded height and scrolls.
            var listBorder = new Border
            {
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(12.0),
                Margin = new Thickness(16.0, 0.0, 16.0, 0.0),
                Padding = new Thickness(0.0, 4.0, 2.0, 4.0),
                Background = Brushes.White
            };
            _list = new ListBox
            {
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                ItemsSource = _items,
                ItemContainerStyle = ItemStyle(),
                ItemTemplate = RowTemplate(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(_list, true);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
            _list.Resources.Add(typeof(ScrollBar), ScrollBarStyle());
            _list.MouseDoubleClick += OnListDoubleClick;
            _list.SelectionChanged += (s, e) => UpdateSelText();
            _list.PreviewKeyDown += OnListKey;
            listBorder.Child = _list;
            Grid.SetRow(listBorder, 4);
            g.Children.Add(listBorder);

            // Row 5: footer with the chosen path and the buttons.
            var foot = new Border
            {
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
                Padding = new Thickness(18.0, 12.0, 16.0, 14.0),
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
            };
            var fg = new Grid();
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _selText = new TextBlock
            {
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
            };
            var cancel = new Button { Content = "取消", Width = 84.0, Height = 36.0, FontSize = 13.0, Margin = new Thickness(0.0, 0.0, 10.0, 0.0), Style = secondary };
            cancel.Click += (s, e) => DialogResult = false;
            var ok = new Button { Width = 96.0, Height = 36.0, Style = ConfirmStyle() };
            ok.Content = new TextBlock { Text = "确定", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            ok.ToolTip = "选择当前选中的文件夹（Ctrl+Enter）";
            ok.Click += (s, e) => Confirm();
            Grid.SetColumn(_selText, 0);
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(ok, 2);
            fg.Children.Add(_selText);
            fg.Children.Add(cancel);
            fg.Children.Add(ok);
            foot.Child = fg;
            Grid.SetRow(foot, 5);
            g.Children.Add(foot);
        }

        private static Button ToolButton(string icon, string text, Style style)
        {
            var b = new Button { Height = 32.0, Padding = new Thickness(10.0, 0.0, 12.0, 0.0), Margin = new Thickness(0.0, 0.0, 8.0, 0.0), Style = style, FontSize = 12.5 };
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(MakeIcon(icon, SB(CPrimary), 14.0));
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = p;
            return b;
        }

        private static Style ConfirmStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.TemplateProperty, MainWindow.ButtonTpl(
                SB(CPrimary), SB(Color.FromRgb(38, 94, 148)), SB(CPrimaryDeep), SB(Color.FromRgb(214, 220, 228)), null, null, new CornerRadius(10.0))));
            return s;
        }

        private static Style SegmentStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "sb");
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(7.0));
            bd.SetValue(Border.PaddingProperty, new Thickness(14.0, 0.0, 14.0, 0.0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(ToggleButton));
            tpl.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CWinHover), "sb"));
            tpl.Triggers.Add(over);
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, SB(CPrimary), "sb"));
            on.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            tpl.Triggers.Add(on);
            var s = new Style(typeof(ToggleButton));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextSub)));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style ItemStyle()
        {
            var ib = new FrameworkElementFactory(typeof(Border), "ib");
            ib.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ib.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            ib.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            ib.SetValue(Border.CornerRadiusProperty, new CornerRadius(7.0));
            ib.SetValue(Border.PaddingProperty, new Thickness(10.0, 3.0, 12.0, 3.0));
            ib.SetValue(FrameworkElement.MarginProperty, new Thickness(6.0, 0.5, 4.0, 0.5));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            tpl.VisualTree = ib;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CWinHover), "ib"));
            tpl.Triggers.Add(over);
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, SB(36, 46, 108, 168), "ib"));
            sel.Setters.Add(new Setter(Border.BorderBrushProperty, SB(110, 46, 108, 168), "ib"));
            tpl.Triggers.Add(sel);
            var s = new Style(typeof(ListBoxItem));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            s.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            var disabled = new DataTrigger { Binding = new Binding("Selectable"), Value = false };
            disabled.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
            s.Triggers.Add(disabled);
            return s;
        }

        private static DataTemplate RowTemplate()
        {
            var row = new FrameworkElementFactory(typeof(PickerRow));
            row.SetBinding(PickerRow.EntryProperty, new Binding());
            return new DataTemplate { VisualTree = row };
        }

        // A wider, rounded scrollbar that is easy to grab; the thumb turns blue while dragging.
        private static Style ScrollBarStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='14'/><Setter Property='MinWidth' Value='14'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Border Background='#EEF2F7' CornerRadius='6' Margin='3,2'/>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb MinHeight='36'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border x:Name='tb' Background='#C2CBD7' CornerRadius='4'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tb' Property='Background' Value='#97A5B7'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='tb' Property='Background' Value='#2E6CA8'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private string ScopeName()
        {
            if (_currentDir == ThisPC) return "此电脑（所有磁盘）";
            string trimmed = _currentDir.TrimEnd('\\');
            string name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }

        private void UpdateHint()
        {
            bool empty = _search.Text.Length == 0;
            _hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _clearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            _hint.Text = "在「" + ScopeName() + "」中搜索" + (_filesCheck.IsChecked == true ? "文件夹或文件" : "文件夹") + "…";
            _search.ToolTip = "搜索范围：" + (_currentDir == ThisPC ? "此电脑（所有磁盘）" : _currentDir);
        }

        private void SetStatus(string text, bool warn)
        {
            _status.Text = text ?? "";
            _status.Foreground = SB(warn ? CAccentDeep : CTextHint);
            _status.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        private void SetMode(bool exact)
        {
            _lastExact = exact;
            _fuzzyBtn.IsChecked = !exact;
            _exactBtn.IsChecked = exact;
            if (_search.Text.Trim().Length > 0) StartSearch();
        }

        private void OptionsChanged()
        {
            _lastRecursive = _subCheck.IsChecked == true;
            _lastShowFiles = _filesCheck.IsChecked == true;
            UpdateHint();
            if (_search.Text.Trim().Length > 0) StartSearch();
            else LoadDir();
        }

        private void CancelWork()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }
            _run = null;
            _pump.Stop();
            _stopBtn.Visibility = Visibility.Collapsed;
        }

        private void Navigate(string dir, string selectAfter)
        {
            _debounce.Stop();
            CancelWork();
            _currentDir = dir;
            _selectAfterLoad = selectAfter;
            if (_search.Text.Length > 0)
            {
                _suppressSearch = true;
                _search.Text = "";
                _suppressSearch = false;
            }
            UpdateHint();
            LoadDir();
        }

        private void GoUp()
        {
            if (_currentDir == ThisPC) return;
            string trimmed = _currentDir.TrimEnd('\\');
            string parent = System.IO.Path.GetDirectoryName(trimmed);
            Navigate(string.IsNullOrEmpty(parent) ? ThisPC : parent, _currentDir);
        }

        private void GoToTyped()
        {
            string text = _addr.Text.Trim().Trim('"');
            if (text.Length == 0 || text == "此电脑")
            {
                Navigate(ThisPC, null);
                return;
            }
            try
            {
                if (text.Length == 2 && text[1] == ':') text += "\\";
                string full = System.IO.Path.GetFullPath(text);
                if (Directory.Exists(full))
                {
                    Navigate(full, null);
                    return;
                }
                if (File.Exists(full))
                {
                    Navigate(System.IO.Path.GetDirectoryName(full), full);
                    return;
                }
            }
            catch
            {
            }
            SetStatus("路径不存在：" + text, true);
        }

        private void LoadDir()
        {
            CancelWork();
            var cts = new CancellationTokenSource();
            _cts = cts;
            string dir = _currentDir;
            bool files = _filesCheck.IsChecked == true;
            _addr.Text = dir == ThisPC ? "此电脑" : dir;
            SetStatus("正在读取…", false);
            Task.Factory.StartNew(() =>
            {
                Listing r = ReadDir(dir, files);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _items = new ObservableCollection<PickerEntry>(r.Entries);
                    _list.ItemsSource = _items;
                    if (r.Error != null) SetStatus("无法读取：" + r.Error, true);
                    else if (dir == ThisPC) SetStatus(r.Folders + " 个驱动器", false);
                    else SetStatus(r.Folders + " 个文件夹" + (files ? "，" + r.Files + " 个文件" : ""), false);
                    SelectPath(_selectAfterLoad);
                    _selectAfterLoad = null;
                    UpdateSelText();
                }));
            }, cts.Token, TaskCreationOptions.None, TaskScheduler.Default);
        }

        private void SelectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string want = path.TrimEnd('\\');
            foreach (var e in _items)
            {
                if (e.FullPath != null && string.Equals(e.FullPath.TrimEnd('\\'), want, StringComparison.OrdinalIgnoreCase))
                {
                    _list.SelectedItem = e;
                    _list.ScrollIntoView(e);
                    return;
                }
            }
        }

        private static Listing ReadDir(string dir, bool includeFiles)
        {
            var r = new Listing();
            if (dir == ThisPC)
            {
                DriveInfo[] drives;
                try { drives = DriveInfo.GetDrives(); }
                catch (Exception ex) { r.Error = ex.Message; return r; }
                Array.Sort(drives, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var d in drives)
                {
                    bool ready;
                    try { ready = d.IsReady; } catch { ready = false; }
                    string letter = d.Name.TrimEnd('\\');
                    var e = new PickerEntry { Kind = PickerKind.Drive, FullPath = d.Name, Depth = 0 };
                    if (!ready)
                    {
                        e.Name = DriveKindName(d.DriveType) + " (" + letter + ")";
                        e.Right = "未就绪";
                        e.Enabled = false;
                    }
                    else
                    {
                        string label = null;
                        try { label = d.VolumeLabel; } catch { }
                        e.Name = (string.IsNullOrEmpty(label) ? DriveKindName(d.DriveType) : label) + " (" + letter + ")";
                        try { e.Right = "可用 " + FormatSize(d.AvailableFreeSpace) + " / 共 " + FormatSize(d.TotalSize); } catch { }
                    }
                    r.Entries.Add(e);
                    r.Folders++;
                }
                return r;
            }
            try
            {
                var di = new DirectoryInfo(dir);
                var dirs = new List<DirectoryInfo>();
                foreach (var d in di.EnumerateDirectories())
                {
                    FileAttributes a;
                    try { a = d.Attributes; } catch { continue; }
                    if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;
                    dirs.Add(d);
                }
                dirs.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
                foreach (var d in dirs) r.Entries.Add(new PickerEntry { Kind = PickerKind.Folder, FullPath = d.FullName, Name = d.Name, Depth = 0 });
                r.Folders = dirs.Count;
                if (includeFiles)
                {
                    var files = new List<FileInfo>();
                    foreach (var f in di.EnumerateFiles())
                    {
                        FileAttributes a;
                        try { a = f.Attributes; } catch { continue; }
                        if ((a & FileAttributes.Hidden) != 0) continue;
                        files.Add(f);
                    }
                    files.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
                    foreach (var f in files)
                    {
                        long len = 0;
                        try { len = f.Length; } catch { }
                        r.Entries.Add(new PickerEntry { Kind = PickerKind.File, FullPath = f.FullName, Name = f.Name, Right = FormatSize(len) });
                    }
                    r.Files = files.Count;
                }
                if (r.Entries.Count == 0) r.Entries.Add(new PickerEntry { Kind = PickerKind.Info, Name = includeFiles ? "（此文件夹是空的）" : "（此文件夹下没有子文件夹）" });
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                r.Entries.Add(new PickerEntry { Kind = PickerKind.Info, Name = "无法读取目录：" + ex.Message });
            }
            return r;
        }

        private static string DriveKindName(DriveType t)
        {
            switch (t)
            {
                case DriveType.Fixed: return "本地磁盘";
                case DriveType.Removable: return "可移动磁盘";
                case DriveType.Network: return "网络驱动器";
                case DriveType.CDRom: return "DVD 驱动器";
                case DriveType.Ram: return "RAM 磁盘";
                default: return "磁盘";
            }
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.#") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.#") + " MB";
            if (bytes >= 1L << 10) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;
            UpdateHint();
            _debounce.Stop();
            if (_search.Text.Trim().Length == 0)
            {
                LoadDir();
                return;
            }
            // Drop the previous search at once so its results stop arriving while the user types.
            CancelWork();
            SetStatus("准备搜索…", false);
            _debounce.Start();
        }

        private void StartSearch()
        {
            _debounce.Stop();
            string kw = _search.Text.Trim();
            if (kw.Length == 0) return;
            CancelWork();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var run = new SearchRun
            {
                Keyword = kw,
                Scope = _currentDir,
                Matcher = new NameMatcher(kw, _exactBtn.IsChecked == true),
                Recursive = _subCheck.IsChecked == true,
                IncludeFiles = _filesCheck.IsChecked == true,
                Token = cts.Token
            };
            _run = run;
            _items = new ObservableCollection<PickerEntry>();
            _list.ItemsSource = _items;
            _stopBtn.Visibility = Visibility.Visible;
            SetStatus("正在搜索…", false);
            UpdateSelText();
            Task.Factory.StartNew(() => SearchWorker(run), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _pump.Start();
        }

        private void StopSearch()
        {
            var run = _run;
            if (run == null) return;
            run.Stopped = true;
            if (_cts != null) _cts.Cancel();
        }

        // Breadth-first walk of the scope: shallow matches show up first and results stream in
        // while deeper folders are still being read.
        private static void SearchWorker(SearchRun run)
        {
            try
            {
                var queue = new Queue<KeyValuePair<string, int>>();
                if (run.Scope == ThisPC)
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                                queue.Enqueue(new KeyValuePair<string, int>(d.RootDirectory.FullName, 0));
                        }
                        catch
                        {
                        }
                    }
                }
                else
                {
                    queue.Enqueue(new KeyValuePair<string, int>(run.Scope, 0));
                }
                while (queue.Count > 0)
                {
                    if (run.Token.IsCancellationRequested) return;
                    var cur = queue.Dequeue();
                    try
                    {
                        foreach (var fsi in new DirectoryInfo(cur.Key).EnumerateFileSystemInfos())
                        {
                            if (run.Token.IsCancellationRequested) return;
                            FileAttributes a;
                            try { a = fsi.Attributes; } catch { continue; }
                            if ((a & FileAttributes.Directory) != 0)
                            {
                                if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;
                                Interlocked.Increment(ref run.ScannedDirs);
                                TryHit(run, fsi.Name, fsi.FullName, PickerKind.Folder, cur.Value + 1, 0);
                                // Junctions/symlinks are skipped so loops like "Application Data" cannot recurse forever.
                                if (run.Recursive && (a & FileAttributes.ReparsePoint) == 0)
                                    queue.Enqueue(new KeyValuePair<string, int>(fsi.FullName, cur.Value + 1));
                            }
                            else if (run.IncludeFiles)
                            {
                                if ((a & FileAttributes.Hidden) != 0) continue;
                                long size = 0;
                                var fi = fsi as FileInfo;
                                if (fi != null) { try { size = fi.Length; } catch { } }
                                TryHit(run, fsi.Name, fsi.FullName, PickerKind.File, cur.Value + 1, size);
                            }
                            if (run.Found >= MaxHits)
                            {
                                run.Truncated = true;
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // Unreadable folder (access denied, vanished, path too long): skip it.
                    }
                }
            }
            finally
            {
                run.Finished = true;
            }
        }

        private static void TryHit(SearchRun run, string name, string fullPath, PickerKind kind, int depth, long size)
        {
            var hl = new List<int>();
            int score = run.Matcher.Match(name, kind == PickerKind.File, hl);
            if (score < 0) return;
            Interlocked.Increment(ref run.Found);
            string parent = System.IO.Path.GetDirectoryName(fullPath) ?? "";
            string detail = parent;
            if (run.Scope != ThisPC)
            {
                string scope = run.Scope.TrimEnd('\\');
                if (string.Equals(parent.TrimEnd('\\'), scope, StringComparison.OrdinalIgnoreCase)) detail = "位于：当前文件夹";
                else if (parent.StartsWith(scope + "\\", StringComparison.OrdinalIgnoreCase)) detail = "位于：…\\" + parent.Substring(scope.Length + 1);
            }
            else
            {
                detail = "位于：" + parent;
            }
            run.Hits.Enqueue(new PickerEntry
            {
                Kind = kind,
                FullPath = fullPath,
                Name = name,
                Detail = detail,
                Right = kind == PickerKind.File ? FormatSize(size) : null,
                Score = score,
                Depth = depth,
                Highlight = hl
            });
        }

        // Moves streamed hits into the list in ranked order and keeps the status line current.
        private void PumpResults()
        {
            SearchRun run = _run;
            if (run == null)
            {
                _pump.Stop();
                return;
            }
            PickerEntry e;
            int n = 0;
            while (n < 600 && run.Hits.TryDequeue(out e))
            {
                InsertRanked(e);
                n++;
            }
            bool done = run.Finished && run.Hits.IsEmpty;
            string scope = run.Scope == ThisPC ? "此电脑" : ScopeName();
            if (!done)
            {
                SetStatus(string.Format("正在「{0}」中搜索… 已扫描 {1:N0} 个文件夹，找到 {2:N0} 个", scope, run.ScannedDirs, _items.Count), false);
                return;
            }
            _pump.Stop();
            _stopBtn.Visibility = Visibility.Collapsed;
            string secs = run.Clock.Elapsed.TotalSeconds.ToString("0.0");
            if (_items.Count == 0)
            {
                _items.Add(new PickerEntry { Kind = PickerKind.Info, Name = "没有找到与「" + run.Keyword + "」匹配的" + (run.IncludeFiles ? "文件夹或文件" : "文件夹") });
                SetStatus(string.Format("{0}未找到（扫描 {1:N0} 个文件夹，用时 {2} 秒）", run.Stopped ? "已停止，" : "", run.ScannedDirs, secs), true);
            }
            else if (run.Truncated)
            {
                SetStatus(string.Format("结果过多，仅显示前 {0:N0} 个，请输入更精确的关键词", MaxHits), true);
            }
            else
            {
                SetStatus(string.Format("{0}找到 {1:N0} 个（扫描 {2:N0} 个文件夹，用时 {3} 秒）", run.Stopped ? "已停止，" : "", _items.Count, run.ScannedDirs, secs), false);
            }
            _run = null;
        }

        private void InsertRanked(PickerEntry e)
        {
            int lo = 0;
            int hi = _items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Compare(_items[mid], e) <= 0) lo = mid + 1;
                else hi = mid;
            }
            _items.Insert(lo, e);
        }

        private static int Compare(PickerEntry a, PickerEntry b)
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = a.Depth.CompareTo(b.Depth);
            if (c != 0) return c;
            c = (a.Kind == PickerKind.File ? 1 : 0).CompareTo(b.Kind == PickerKind.File ? 1 : 0);
            if (c != 0) return c;
            return StrCmpLogicalW(a.Name, b.Name);
        }

        private PickerEntry SelectedEntry()
        {
            var e = _list.SelectedItem as PickerEntry;
            return e != null && e.Selectable ? e : null;
        }

        private static string TargetOf(PickerEntry e)
        {
            return e.Kind == PickerKind.File ? System.IO.Path.GetDirectoryName(e.FullPath) : e.FullPath;
        }

        private void UpdateSelText()
        {
            PickerEntry e = SelectedEntry();
            if (e != null)
            {
                _selText.Text = (e.Kind == PickerKind.File ? "将选择该文件所在文件夹：" : "将选择：") + TargetOf(e);
                _selText.Foreground = SB(CTextMain);
            }
            else if (_currentDir != ThisPC)
            {
                _selText.Text = "未选中（确定将使用当前位置：" + _currentDir + "）";
                _selText.Foreground = SB(CTextHint);
            }
            else
            {
                _selText.Text = "请先选择一个驱动器或文件夹";
                _selText.Foreground = SB(CTextHint);
            }
            _selText.ToolTip = _selText.Text;
        }

        private void Confirm()
        {
            PickerEntry e = SelectedEntry();
            string result = e != null ? TargetOf(e) : (_currentDir != ThisPC ? _currentDir : null);
            if (string.IsNullOrEmpty(result) || !Directory.Exists(result))
            {
                _selText.Text = "请先选择一个文件夹或驱动器";
                _selText.Foreground = SB(CAccentDeep);
                return;
            }
            _selectedPath = result.Length > 3 ? result.TrimEnd('\\') : result;
            DialogResult = true;
        }

        private void Open(PickerEntry e)
        {
            if (e == null) return;
            if (e.Kind == PickerKind.File) Navigate(System.IO.Path.GetDirectoryName(e.FullPath), e.FullPath);
            else if (Directory.Exists(e.FullPath)) Navigate(e.FullPath, null);
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
            {
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
            }
            if (dep == null) return;
            Open(SelectedEntry());
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Confirm();
                else Open(SelectedEntry());
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                GoUp();
                e.Handled = true;
            }
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Confirm();
                else if (_search.Text.Trim().Length > 0) StartSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _items.Count > 0)
            {
                foreach (var it in _items)
                {
                    if (!it.Selectable) continue;
                    _list.SelectedItem = it;
                    _list.UpdateLayout();
                    var c = _list.ItemContainerGenerator.ContainerFromItem(it) as ListBoxItem;
                    if (c != null) c.Focus();
                    break;
                }
                e.Handled = true;
            }
        }

        private void OnPreviewKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_search.Text.Length > 0)
                {
                    _search.Text = "";
                    _search.Focus();
                }
                else
                {
                    DialogResult = false;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
        }
    }
}
