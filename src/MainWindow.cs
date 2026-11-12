using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    public partial class MainWindow : Window
    {
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".webm", ".m4v", ".mkv", ".flv" };
        private const int MaxLogChars = 120000;

        private Border _windowFrame;
        private Border _titleBar;
        private Button _maxBtn;
        private Ellipse _statusDot;
        private TextBlock _headerStatus;
        private TextBox _folderBox;
        private TextBlock _folderInfo;
        private CheckBox _frameCheck;
        private CheckBox _audioCheck;
        private CheckBox _watchCheck;
        private CheckBox _autoStopCheck;
        private CheckBox _recCheck;
        private Button _rulesBtn;
        private Button _modelBtn;
        private Button _selfTestBtn;
        private Button _startBtn;
        private Path _startIcon;
        private TextBlock _startText;
        private TextBlock _stateTitle;
        private TextBlock _stateSub;
        private TextBlock _stageText;
        private ProgressBar _fileBar;
        private TextBlock _statChecked;
        private TextBlock _statKept;
        private TextBlock _statRejected;
        private TextBlock _statError;
        private TextBlock _statSkipped;
        private TextBlock _statLeft;
        private RadioButton _pillAll;
        private RadioButton _pillReject;
        private RadioButton _pillKeep;
        private RadioButton _pillMemo;
        private RadioButton _pillError;
        private CheckBox _selectAll;
        private TextBlock _selCount;
        private Button _playBtn;
        private Button _locateBtn;
        private Button _restoreBtn;
        private Button _recheckBtn;
        private ListBox _list;
        private TextBlock _emptyText;
        private TextBox _logBox;
        private TextBlock _logLast;
        private Path _logArrow;

        private readonly ObservableCollection<Rec> _view = new ObservableCollection<Rec>();
        private List<Rec> _all = new List<Rec>();   // newest first
        private string _filter = "";
        private readonly AppState _state;
        private Rules _rules;
        private EngineRunner _engine;
        private string _runFolder = "";
        private int _sessChecked, _sessKept, _sessRejected, _sessError;
        private int _sessSkipped;   // kept without checking because 筛选记忆 knew them
        private int _left;
        private int _infoGen;       // newest folder count; older background counts are dropped
        private int _infoRunning;   // background folder counts still going (UI thread only)
        private bool _busy;
        private bool _stopping;
        private bool _selfTesting;
        // How the last run ended (已完成 / 已停止 / 出错停止 + summary). Stays on the status card while
        // there is nothing new to check; cleared by another folder or the next run.
        private string _endTitle, _endSub;
        private Color _endColor;
        private bool _endIsError;
        private bool _incomplete;   // runtime\ missing: stays on the status card, start stays disabled
        private string _currentRel;
        private readonly DispatcherTimer _folderTimer;
        private readonly DispatcherTimer _idleTimer;
        // 查完自动停止: counts down on the status card while the engine waits for new videos.
        private readonly DispatcherTimer _countdown;
        private DateTime _stopAt;
        private bool _waitingNew;

        public MainWindow()
        {
            Paths.TrimLog();
            // Before anything reads 筛选记忆 / 设置.ini / 上次设置.json: a fresh folder takes them from the old version.
            string migrated = Migrate.Run();
            _state = AppState.Load();
            if (!File.Exists(Paths.ConfigPath))
            {
                try { new Rules().Save(); } catch { }
            }
            _rules = Rules.Load();
            BuildStyles();
            BuildUi();
            WindowResizer.Enable(this, 10.0);

            _frameCheck.IsChecked = _state.Frame;
            _audioCheck.IsChecked = _state.Audio;
            _watchCheck.IsChecked = _state.Watch;
            _autoStopCheck.IsChecked = _state.AutoStop;
            _recCheck.IsChecked = _state.Recursive;
            _watchCheck.Checked += (s, e) => UpdateAutoStopEnabled();
            _watchCheck.Unchecked += (s, e) => UpdateAutoStopEnabled();
            _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdown.Tick += (s, e) => TickCountdown();
            string folder = _state.Folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = DefaultFolder();
            _folderBox.Text = folder;

            _folderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _folderTimer.Tick += (s, e) =>
            {
                _folderTimer.Stop();
                FolderChanged();
            };
            _folderBox.TextChanged += (s, e) =>
            {
                _folderTimer.Stop();
                _folderTimer.Start();
            };
            _folderBox.LostKeyboardFocus += (s, e) => ShowPathEnd();
            _recCheck.Checked += (s, e) => RefreshFolderInfo();
            _recCheck.Unchecked += (s, e) => RefreshFolderInfo();
            // While idle, keep the "还没查" count fresh as the browser keeps downloading.
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _idleTimer.Tick += (s, e) => { if (!_busy && IsActive && _infoRunning == 0) RefreshFolderInfo(); };
            _idleTimer.Start();

            ShowRules();
            FolderChanged();
            SetBusy(false);
            Closing += OnClosing;
            Loaded += (s, e) =>
            {
                string missing = Paths.MissingPartsMessage();
                if (missing == null) return;
                _incomplete = true;
                _startBtn.IsEnabled = false;
                _selfTestBtn.IsEnabled = false;
                SetIdleState();
                MessageBox.Show(this, missing, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            Log("素材筛选器 " + AppInfo.Version + " 已打开。");
            if (migrated != null) Log(migrated);
        }

        private static string DefaultFolder()
        {
            try
            {
                string dl = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads\\视频素材");
                if (Directory.Exists(dl)) return dl;
            }
            catch
            {
            }
            return "";
        }

        private string Folder
        {
            get
            {
                string f = (_folderBox.Text ?? "").Trim().Trim('"').Trim().TrimEnd('\\', '/');
                // "E:" alone means the current folder on drive E, not the drive itself.
                return f.Length == 2 && f[1] == ':' ? f + "\\" : f;
            }
        }

        // ---------------------------------------------------------------- folder and records
        private void FolderChanged()
        {
            if (!_busy) _endTitle = null;
            _folderBox.ToolTip = Folder.Length > 0 ? Folder : "插件把视频下载到哪个文件夹，就选哪个";
            if (!_folderBox.IsKeyboardFocused) ShowPathEnd();
            if (_busy) return;
            LoadRecords();
            RefreshFolderInfo();
        }

        // The end of a long path (the actual folder name) matters more than the drive part.
        private void ShowPathEnd()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _folderBox.CaretIndex = _folderBox.Text.Length;
                _folderBox.ScrollToHorizontalOffset(double.MaxValue);
            }));
        }

        private void LoadRecords()
        {
            string folder = Folder;
            _all = folder.Length > 0 && Directory.Exists(folder) ? Report.Load(folder) : new List<Rec>();
            _all.Reverse();
            RebuildView();
        }

        private static List<string> ListVideos(string folder, bool recursive)
        {
            var list = new List<string>();
            try
            {
                var opt = new EnumerationOptionsLite(recursive);
                foreach (string f in opt.Files(folder))
                {
                    if (Array.IndexOf(VideoExt, System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0)
                    {
                        list.Add(Paths.Rel(folder, f));
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private void RefreshFolderInfo()
        {
            string folder = Folder;
            int gen = ++_infoGen;
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                _folderInfo.Text = folder.Length == 0 ? "" : "文件夹不存在";
                _left = 0;
                UpdateStats();
                if (!_busy) SetIdleState();
                return;
            }
            bool recursive = _recCheck.IsChecked == true;
            var done = new HashSet<string>(_all.Where(r => r.Result != "重查" && r.Result != "检查中").Select(r => r.Rel), StringComparer.OrdinalIgnoreCase);
            int rejected = _all.Count(r => r.Result == "筛掉");
            // Listing a big folder (with subfolders, or on a network drive) and reading fingerprints can take
            // a moment, so it runs off the UI thread. Videos this folder's record does not list may still be
            // known to 筛选记忆 (checked in another folder, or renamed); those are not 待检查.
            _infoRunning++;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var videos = ListVideos(folder, recursive);
                var unknown = videos.Where(v => !done.Contains(v)).ToList();
                int total = videos.Count;
                if (unknown.Count > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (gen == _infoGen && !_folderInfo.Text.StartsWith(total + " 个视频")) _folderInfo.Text = total + " 个视频";
                    }));
                }
                int left = unknown.Count;
                try
                {
                    left = unknown.Count(v => !Memory.WillSkip(System.IO.Path.Combine(folder, v)));
                }
                catch
                {
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _infoRunning--;
                    if (gen == _infoGen) ShowFolderInfo(total, left, rejected);
                }));
            });
        }

        private void ShowFolderInfo(int total, int left, int rejected)
        {
            var parts = new List<string> { total + " 个视频" };
            if (left > 0) parts.Add(left + " 个待检查");
            if (rejected > 0) parts.Add("已筛掉 " + rejected);
            _folderInfo.Text = string.Join(" · ", parts);
            if (!_busy)
            {
                _left = left;
                UpdateStats();
                SetIdleState();
            }
        }

        private bool Matches(Rec r)
        {
            switch (_filter)
            {
                case "筛掉": return r.Result == "筛掉";
                case "保留": return (r.Result == "保留" && !r.IsMemo) || r.Result == "放回";
                case Rec.MemoReason: return r.IsMemo;
                case "出错": return r.Result == "出错";
                default: return true;
            }
        }

        private void RebuildView()
        {
            if (_emptyText == null) return; // a filter pill fires Checked while the window is still being built
            _view.Clear();
            foreach (var r in _all)
            {
                if (Matches(r)) _view.Add(r);
            }
            UpdateCounts();
        }

        // Puts a record at the top (replacing the older entry of the same video).
        private void Upsert(Rec r)
        {
            int i = _all.FindIndex(x => string.Equals(x.Rel, r.Rel, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                Rec old = _all[i];
                _all.RemoveAt(i);
                _view.Remove(old);
            }
            _all.Insert(0, r);
            if (Matches(r)) _view.Insert(0, r);
            UpdateCounts();
        }

        private void Replace(Rec old, Rec now)
        {
            int i = _all.IndexOf(old);
            if (i >= 0) _all[i] = now;
            int v = _view.IndexOf(old);
            if (v >= 0)
            {
                if (Matches(now)) _view[v] = now;
                else _view.RemoveAt(v);
            }
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            SetPill(_pillAll, _all.Count(r => r.Result != "检查中"));
            SetPill(_pillReject, _all.Count(r => r.Result == "筛掉"));
            SetPill(_pillKeep, _all.Count(r => (r.Result == "保留" && !r.IsMemo) || r.Result == "放回"));
            SetPill(_pillMemo, _all.Count(r => r.IsMemo));
            SetPill(_pillError, _all.Count(r => r.Result == "出错"));
            if (_view.Count > 0)
            {
                _emptyText.Visibility = Visibility.Collapsed;
            }
            else
            {
                _emptyText.Visibility = Visibility.Visible;
                _emptyText.Text = _all.Count == 0
                    ? "暂无记录"
                    : "无";
            }
            UpdateActions();
        }

        private static void SetPill(RadioButton rb, int n)
        {
            ((TextBlock)rb.Tag).Text = n.ToString();
        }

        private void UpdateStats()
        {
            _statChecked.Text = _sessChecked.ToString();
            _statKept.Text = _sessKept.ToString();
            _statRejected.Text = _sessRejected.ToString();
            _statError.Text = _sessError.ToString();
            _statSkipped.Text = _sessSkipped.ToString();
            _statLeft.Text = Math.Max(0, _left).ToString();
        }

        private List<Rec> Selected()
        {
            return _list.SelectedItems.Cast<Rec>().Where(r => r.Result != "检查中").ToList();
        }

        private void UpdateActions()
        {
            if (_list == null) return;
            var sel = Selected();
            _playBtn.IsEnabled = sel.Count > 0;
            _locateBtn.IsEnabled = sel.Count > 0;
            _restoreBtn.IsEnabled = !_busy && sel.Any(r => r.Result == "筛掉");
            _recheckBtn.IsEnabled = !_busy && sel.Count > 0;
            string busyTip = "先停止筛选";
            _restoreBtn.ToolTip = _busy ? busyTip : null;
            _recheckBtn.ToolTip = _busy ? busyTip : null;
            int pickable = _view.Count(r => r.Result != "检查中");
            _selectAll.IsEnabled = pickable > 0;
            _selectAll.IsChecked = sel.Count == 0 ? false : sel.Count >= pickable ? true : (bool?)null;
            _selCount.Text = sel.Count > 0 ? "已选 " + sel.Count : "全选";
        }

        // 全选 box: picks every row in the current view (全部 / 筛掉 / ...), or none when all are picked.
        private void ToggleSelectAll()
        {
            int pickable = _view.Count(r => r.Result != "检查中");
            if (pickable > 0 && Selected().Count >= pickable) _list.UnselectAll();
            else _list.SelectAll();
            UpdateActions();
        }

        private static bool IsInTickBox(DependencyObject d)
        {
            while (d != null && !(d is ListBoxItem))
            {
                if (d is CheckBox) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        private void UpdateAutoStopEnabled()
        {
            _autoStopCheck.IsEnabled = !_busy && !_selfTesting && _watchCheck.IsChecked == true;
        }

        // ---------------------------------------------------------------- state display
        private void SetState(string title, string sub, Color dot)
        {
            _stateTitle.Text = title;
            _stateSub.Text = sub ?? "";
            _statusDot.Fill = SB(dot);
            ((DropShadowEffect)_statusDot.Effect).Color = dot;
        }

        private void SetIdleState()
        {
            if (_busy) return;
            _headerStatus.Text = "就绪";
            _fileBar.Value = 0;
            _stageText.Text = "";
            if (_incomplete)
            {
                SetState("软件文件不完整", "请重新完整解压一次压缩包", CDanger);
            }
            else if (Folder.Length == 0 || !Directory.Exists(Folder))
            {
                SetState("选择视频文件夹", "", CSuccess);
            }
            else if (_endTitle != null && (_left == 0 || _endIsError))
            {
                SetState(_endTitle, _endSub, _endColor);   // an error stays up even with videos left
            }
            else if (_left > 0)
            {
                SetState("就绪", _left + " 个待检查", CSuccess);
            }
            else
            {
                SetState("就绪", "都查过了", CSuccess);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _startBtn.Style = (Style)Resources[busy ? "StopButton" : "PrimaryButton"];
            _startIcon.Data = Geo(busy ? IconStop : IconPlay);
            _startIcon.Fill = busy ? SB(CDanger) : Brushes.White;
            _startText.Text = busy ? "停止筛选" : "开始筛选";
            _startBtn.IsEnabled = true;
            foreach (UIElement c in new UIElement[] { _folderBox, _frameCheck, _audioCheck, _watchCheck, _recCheck, _rulesBtn, _modelBtn, _selfTestBtn })
            {
                c.IsEnabled = !busy && !_selfTesting;
            }
            UpdateAutoStopEnabled();
            if (!busy) StopCountdown();
            _headerStatus.Text = busy ? "正在筛选" : "就绪";
            if (!busy) SetIdleState();
            UpdateActions();
        }

        private void Log(string text)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss") + "  ";
            string line = stamp + (text ?? "").Replace("\n", "\n" + new string(' ', stamp.Length));
            Paths.AppendLog(DateTime.Now.ToString("yyyy-MM-dd ") + line);
            if (_logBox.Text.Length > MaxLogChars) _logBox.Text = _logBox.Text.Substring(_logBox.Text.Length - MaxLogChars / 2);
            _logBox.AppendText((_logBox.Text.Length > 0 ? "\n" : "") + line);
            if (_logBox.Visibility == Visibility.Visible) _logBox.ScrollToEnd();
            string last = (text ?? "").Split('\n')[0].Trim();
            if (last.Length > 0) _logLast.Text = last;
        }

        // ---------------------------------------------------------------- start / stop
        private void StartOrStop()
        {
            if (_busy)
            {
                if (_engine == null || _stopping) return;
                _stopping = true;
                _engine.Stop();
                _startBtn.IsEnabled = false;
                _startText.Text = "正在停止…";
                SetState("正在停止", "", CRunning);
                Log("正在停止，当前这个视频查到一半会直接放弃……");
                return;
            }
            string folder = Folder;
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "先选一个存视频的文件夹。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_frameCheck.IsChecked != true && _audioCheck.IsChecked != true)
            {
                MessageBox.Show(this, "「查画面」和「查语音」至少勾一个。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string missing = Paths.MissingPartsMessage();
            if (missing != null)
            {
                MessageBox.Show(this, missing, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _rules = Rules.Load();
            ShowRules();
            if (_audioCheck.IsChecked == true && !ModelCatalog.IsInstalled(_rules.Model))
            {
                if (ModelCatalog.Get(_rules.Model) != null)
                {
                    var r = MessageBox.Show(this, "语音模型「" + _rules.Model + "」还没下载（" + ModelCatalog.SizeText(_rules.Model) + "）。\n\n现在下载吗？下载完接着开始筛选。",
                        AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return;
                    new ModelWindow(_rules.Model, _rules.Model, false) { Owner = this }.ShowDialog();
                    if (!ModelCatalog.IsInstalled(_rules.Model)) return;
                }
                else
                {
                    // A model name typed into 设置.ini by hand: the engine downloads it on its own.
                    var r = MessageBox.Show(this, "规则里写的语音模型「" + _rules.Model + "」软件里没有，开始后会联网下载。\n\n要继续吗？",
                        AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return;
                }
            }
            _state.Folder = folder;
            _state.Frame = _frameCheck.IsChecked == true;
            _state.Audio = _audioCheck.IsChecked == true;
            _state.Watch = _watchCheck.IsChecked == true;
            _state.AutoStop = _autoStopCheck.IsChecked == true;
            _state.Recursive = _recCheck.IsChecked == true;
            _state.Save();

            _runFolder = folder;
            _sessChecked = _sessKept = _sessRejected = _sessError = _sessSkipped = 0;
            UpdateStats();
            _stopping = false;
            _engine = new EngineRunner(Dispatcher)
            {
                OnLog = Log,
                OnReady = () => SetState("正在检查", "", CRunning),
                OnQueue = n =>
                {
                    _left = n;
                    UpdateStats();
                    // Waiting for new videos and one turned up that is still being written.
                    if (_waitingNew && n > 0)
                    {
                        StopCountdown();
                        _waitingNew = true;
                        SetState("等待新视频", "新视频正在下载", CPrimary);
                    }
                },
                OnFile = FileStarted,
                OnProgress = (stage, pct) =>
                {
                    _fileBar.Value = pct;
                    _stageText.Text = (stage == "语音" ? "听语音 " : "看画面 ") + pct + "%";
                },
                OnResult = GotResult,
                OnIdle = (stopIn, downloading) =>
                {
                    DropChecking();
                    _currentRel = null;
                    _fileBar.Value = 0;
                    _stageText.Text = "";
                    _left = 0;
                    UpdateStats();
                    _headerStatus.Text = "等新视频";
                    ShowWaiting(stopIn, downloading);
                },
                OnFail = text => SetState("出错停止", text, CDanger),
                OnFinished = Finished
            };
            SetBusy(true);
            SetState("正在加载识别组件", "", CRunning);
            Log("开始筛选：" + folder);
            try
            {
                _engine.Start(new JobOptions
                {
                    Folder = folder,
                    Frame = _state.Frame,
                    Audio = _state.Audio,
                    Watch = _state.Watch,
                    AutoStop = _state.Watch && _state.AutoStop,
                    Recursive = _state.Recursive
                });
            }
            catch (Exception ex)
            {
                Log("启动识别失败：" + ex.Message);
                _engine = null;
                SetBusy(false);
                SetState("启动识别失败", ex.Message, CDanger);
            }
        }

        // Status card while the engine waits for new videos: downloads in progress, or how long until it
        // stops by itself (stopIn < 0: 查完自动停止 is off, it waits until 停止 is pressed).
        private void ShowWaiting(int stopIn, int downloading)
        {
            StopCountdown();
            _waitingNew = true;
            if (downloading > 0)
            {
                SetState("等待新视频", "正在下载 " + downloading + " 个", CPrimary);
            }
            else if (stopIn < 0)
            {
                SetState("等待新视频", "", CPrimary);
            }
            else
            {
                _stopAt = DateTime.Now.AddSeconds(stopIn);
                TickCountdown();
                _countdown.Start();
            }
        }

        private void TickCountdown()
        {
            if (!_busy || _stopping)
            {
                StopCountdown();
                return;
            }
            int s = (int)Math.Ceiling((_stopAt - DateTime.Now).TotalSeconds);
            if (s <= 0) _countdown.Stop();
            SetState("等待新视频", s > 0 ? string.Format("{0}:{1:00} 后自动停止", s / 60, s % 60) : "马上停止", CPrimary);
        }

        private void StopCountdown()
        {
            if (_countdown != null) _countdown.Stop();
            _waitingNew = false;
        }

        private void FileStarted(string rel)
        {
            StopCountdown();
            DropChecking();
            _currentRel = rel;
            _fileBar.Value = 0;
            _stageText.Text = "";
            _headerStatus.Text = "正在筛选";
            SetState("正在检查", System.IO.Path.GetFileName(rel), CRunning);
            Upsert(new Rec { Rel = rel, Time = Report.Now(), Result = "检查中" });
        }

        private void DropChecking()
        {
            var placeholder = _all.FirstOrDefault(r => r.Result == "检查中");
            if (placeholder == null) return;
            _all.Remove(placeholder);
            _view.Remove(placeholder);
            UpdateCounts();
        }

        private void GotResult(Rec r)
        {
            DropChecking();
            Upsert(r);
            if (r.Memo)
            {
                // Settled from 筛选记忆 without checking: not counted as 已查.
                if (r.Result == "保留") _sessSkipped++;
                else if (r.Result == "筛掉") _sessRejected++;
                else _sessError++;
            }
            else
            {
                _sessChecked++;
                if (r.Result == "保留") _sessKept++;
                else if (r.Result == "筛掉") _sessRejected++;
                else _sessError++;
                _fileBar.Value = 100;
            }
            _left = Math.Max(0, _left - 1);
            UpdateStats();
        }

        private void Finished(bool stopped)
        {
            // Decided before SetBusy(false), which redraws the status card.
            string fail = _engine != null ? _engine.FailText : null;
            _engine = null;
            _stopping = false;
            _currentRel = null;
            StopCountdown();
            string summary = "保留 " + _sessKept + " · 筛掉 " + _sessRejected + (_sessError > 0 ? " · 出错 " + _sessError : "") +
                             (_sessSkipped > 0 ? " · " + Rec.MemoReason + " " + _sessSkipped : "");
            bool any = _sessChecked + _sessRejected + _sessError + _sessSkipped > 0;
            _endColor = CSuccess;
            _endIsError = !stopped && fail != null;
            if (stopped)
            {
                _endTitle = "已停止";
                _endSub = summary;
            }
            else if (fail != null)
            {
                _endTitle = "出错停止";
                _endSub = fail;
                _endColor = CDanger;
            }
            else
            {
                _endTitle = any ? "已完成" : "没有要查的视频";
                _endSub = any ? summary : "";
            }
            _left = 0;
            SetBusy(false);
            LoadRecords();
            RefreshFolderInfo();
            if (!stopped) FlashTaskbar();   // finished (or stopped by itself) while another window was in front
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Hwnd;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        private void FlashTaskbar()
        {
            if (IsActive) return;
            try
            {
                var fi = new FlashInfo
                {
                    Size = (uint)Marshal.SizeOf(typeof(FlashInfo)),
                    Hwnd = new WindowInteropHelper(this).Handle,
                    Flags = 2 | 12,   // FLASHW_TRAY | FLASHW_TIMERNOFG: the taskbar button, until the window comes to the front
                    Count = uint.MaxValue,
                    Timeout = 0
                };
                FlashWindowEx(ref fi);
            }
            catch
            {
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_busy && _engine != null)
            {
                var r = MessageBox.Show(this, "正在筛选，确定要退出吗？\n当前这个视频查到一半会放弃，下次打开会接着查没查完的。",
                    AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _engine.Kill();
            }
            _state.Folder = Folder;
            _state.Frame = _frameCheck.IsChecked == true;
            _state.Audio = _audioCheck.IsChecked == true;
            _state.Watch = _watchCheck.IsChecked == true;
            _state.AutoStop = _autoStopCheck.IsChecked == true;
            _state.Recursive = _recCheck.IsChecked == true;
            _state.Save();
        }

        // ---------------------------------------------------------------- rules
        private void ShowRules()
        {
            _rulesBtn.ToolTip = _rules.Summary();
        }

        private void EditRules()
        {
            var dlg = new RulesWindow(Rules.Load()) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            try
            {
                dlg.Result.Save();
                _rules = dlg.Result;
                ShowRules();
                Log("筛选规则已保存，下次点「开始筛选」生效。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "规则保存不了：" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ManageModels()
        {
            new ModelWindow(Rules.Load().Model, null, true) { Owner = this }.ShowDialog();
            _rules = Rules.Load();   // 使用 may have switched the model
            ShowRules();
        }

        // Runs each engine self-check in its own process, like the old installer did.
        private void SelfTest()
        {
            if (_busy || _selfTesting) return;
            string missing = Paths.MissingPartsMessage();
            if (missing != null)
            {
                MessageBox.Show(this, missing, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selfTesting = true;
            _selfTestBtn.IsEnabled = false;
            _startBtn.IsEnabled = false;
            if (_logBox.Visibility != Visibility.Visible) ToggleLog();
            Log("开始检查识别组件……");
            var steps = new[] { new[] { "onnx", "运算组件" }, new[] { "cv2", "图像组件" }, new[] { "av", "视频读取组件" }, new[] { "ocr", "文字识别" }, new[] { "asr", "语音识别" } };
            var t = new Thread(() =>
            {
                var failed = new List<string>();
                foreach (var st in steps)
                {
                    Dispatcher.BeginInvoke(new Action(() => SetState("正在自检", st[1], CRunning)));
                    string output;
                    int code = RunCheck(st[0], out output);
                    string line = code == 0 ? "　　" + st[1] + "：正常" : "　　" + st[1] + "：没通过（代码 " + code + "）\n" + output;
                    if (code != 0) failed.Add(st[1]);
                    Dispatcher.BeginInvoke(new Action(() => Log(line)));
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _selfTesting = false;
                    SetBusy(false);
                    if (failed.Count == 0)
                    {
                        Log("识别组件全部正常。");
                        SetState("组件正常", "", CSuccess);
                    }
                    else
                    {
                        Log("没通过：" + string.Join("、", failed) + "。把软件重新解压一份再试；还不行就把「运行日志.txt」发给 Claude。");
                        SetState("组件异常", string.Join("、", failed), CDanger);
                    }
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        private static int RunCheck(string name, out string output)
        {
            try
            {
                var psi = EngineRunner.PythonStartInfo("--check " + name);
                using (var p = Process.Start(psi))
                {
                    p.StandardInput.Close();
                    var err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(180000))
                    {
                        try { p.Kill(); } catch { }
                        output = "超过 3 分钟没反应";
                        return -1;
                    }
                    string all = (o + "\n" + err.Result).Trim();
                    var lines = all.Split('\n');
                    output = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 8)).Select(x => "　　　　" + x.TrimEnd()));
                    if (p.ExitCode != 0) Paths.AppendLog("---- 检查 " + name + " 的输出 ----\n" + all);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        // ---------------------------------------------------------------- list actions
        private void PickFolder()
        {
            var dlg = new FolderPicker(Folder) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedPath))
            {
                _folderBox.Text = dlg.SelectedPath;
            }
        }

        private void OpenFolder(bool rejected)
        {
            string folder = Folder;
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "先选一个存视频的文件夹。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string dir = rejected ? System.IO.Path.Combine(folder, Report.RejectDir) : folder;
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "还没有被筛掉的视频。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShellOpen(dir, false);
        }

        private void PlaySelected()
        {
            var sel = Selected();
            if (sel.Count > 0) Play(sel[0]);
        }

        private void Play(Rec r)
        {
            string path = Report.FullPath(BusyFolder(), r);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "找不到这个视频了，可能已经被挪走或删掉：\n" + path, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShellOpen(path, false);
        }

        // Opens the folder with the picked videos selected. Picked videos in several folders (kept ones and
        // the 已筛掉 subfolder, say): one window per folder, at most 5.
        private void LocateSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            string folder = BusyFolder();
            var paths = sel.Select(r => Report.FullPath(folder, r)).ToList();
            var there = paths.Where(File.Exists).ToList();
            if (there.Count == 0)
            {
                ShellOpen(System.IO.Path.GetDirectoryName(paths[0]), false);
                return;
            }
            if (there.Count == 1)
            {
                ShellOpen(there[0], true);
                return;
            }
            foreach (var g in there.GroupBy(System.IO.Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).Take(5))
            {
                if (!ShellSelect(g.Key, g.ToList())) ShellOpen(g.First(), true);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPathW(string path);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);

        private static bool ShellSelect(string folder, List<string> files)
        {
            IntPtr dir = IntPtr.Zero;
            var items = new List<IntPtr>();
            try
            {
                dir = ILCreateFromPathW(folder);
                if (dir == IntPtr.Zero) return false;
                foreach (string f in files)
                {
                    IntPtr p = ILCreateFromPathW(f);
                    if (p != IntPtr.Zero) items.Add(p);
                }
                return items.Count > 0 && SHOpenFolderAndSelectItems(dir, (uint)items.Count, items.ToArray(), 0) >= 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (IntPtr p in items) ILFree(p);
                if (dir != IntPtr.Zero) ILFree(dir);
            }
        }

        private string BusyFolder()
        {
            return _busy && _runFolder.Length > 0 ? _runFolder : Folder;
        }

        private void RestoreSelected()
        {
            var sel = Selected().Where(r => r.Result == "筛掉").ToList();
            if (sel.Count == 0 || _busy) return;
            string folder = Folder;
            var errors = new List<string>();
            int ok = 0;
            foreach (var r in sel)
            {
                try
                {
                    string rel = Report.Restore(folder, r);
                    var now = new Rec { Rel = rel, Time = Report.Now(), Result = "放回" };
                    Replace(r, now);
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add(r.Name + "：" + ex.Message);
                }
            }
            if (ok > 0) Log("放回了 " + ok + " 个视频，以后不会再查它们。");
            RefreshFolderInfo();
            if (errors.Count > 0)
            {
                MessageBox.Show(this, "有 " + errors.Count + " 个没放回去：\n\n" + string.Join("\n", errors.Take(8)), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RecheckSelected()
        {
            var sel = Selected();
            if (sel.Count == 0 || _busy) return;
            int rejected = sel.Count(r => r.Result == "筛掉");
            string msg = "选中的 " + sel.Count + " 个视频，下次点「开始筛选」时会重新检查。" + (rejected > 0 ? "\n其中 " + rejected + " 个筛掉的会先放回原文件夹。" : "") + "\n\n确定吗？";
            if (MessageBox.Show(this, msg, AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string folder = Folder;
            var errors = new List<string>();
            foreach (var r in sel)
            {
                try
                {
                    Report.MarkRecheck(folder, r);
                }
                catch (Exception ex)
                {
                    errors.Add(r.Name + "：" + ex.Message);
                }
            }
            LoadRecords();
            RefreshFolderInfo();
            Log("标记了 " + (sel.Count - errors.Count) + " 个视频要重新检查。");
            if (errors.Count > 0)
            {
                MessageBox.Show(this, "有 " + errors.Count + " 个没标上：\n\n" + string.Join("\n", errors.Take(8)), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShellOpen(string path, bool select)
        {
            try
            {
                if (select)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                }
                else
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        MessageBox.Show(this, "还没有这个文件：\n" + path, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log("打不开：" + path + "（" + ex.Message + "）");
            }
        }
    }

    // Directory walk that skips the 已筛掉 folders and hidden folders, like the engine does.
    internal sealed class EnumerationOptionsLite
    {
        private readonly bool _recursive;

        public EnumerationOptionsLite(bool recursive)
        {
            _recursive = recursive;
        }

        public IEnumerable<string> Files(string folder)
        {
            var stack = new Stack<string>();
            stack.Push(folder);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] files;
                try { files = Directory.GetFiles(dir); } catch { continue; }
                foreach (string f in files) yield return f;
                if (!_recursive) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(dir); } catch { continue; }
                foreach (string s in subs)
                {
                    string name = System.IO.Path.GetFileName(s);
                    if (name == Report.RejectDir || name.StartsWith(".")) continue;
                    stack.Push(s);
                }
            }
        }
    }
}
