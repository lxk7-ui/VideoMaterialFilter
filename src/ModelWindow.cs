using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    // 模型下载: every speech model with its state; one download at a time, with progress.
    internal sealed class ModelWindow : Window
    {
        private sealed class Row
        {
            public ModelInfo Model;
            public TextBlock Status;
            public Button Action;
        }

        private readonly string _autoStart;   // download this one right away and close when it is done
        private readonly bool _allowUse;      // 使用 buttons (not when opened from the rules window, which has its own list)
        private string _current;              // the model in 设置.ini
        private readonly List<Row> _rows = new List<Row>();
        private readonly DispatcherTimer _timer;
        private Border _progress;
        private ProgressBar _bar;
        private TextBlock _barText;
        private TextBlock _error;
        private ModelDownloader _dl;
        private ModelInfo _dlModel;
        private string _failedId;
        private long _lastBytes;
        private DateTime _lastTick;
        private double _speed;
        private bool _closeWhenStopped;

        public ModelWindow(string current, string autoStart, bool allowUse)
        {
            _current = current ?? "";
            _autoStart = autoStart;
            _allowUse = allowUse;
            Title = "模型下载";
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(600.0, work.Width - 40.0);
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
            BuildUi();
            Refresh();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, e) => Tick();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closing += OnClosing;
            Loaded += (s, e) => { if (_autoStart != null) Start(ModelCatalog.Get(_autoStart)); };
        }

        private void BuildUi()
        {
            Style secondary = MainWindow.MakeSecondaryButtonStyle();
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
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52.0) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            frame.Child = g;
            Content = root;

            var bar = new Grid { Background = Brushes.Transparent };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(MakeIcon(IconDownload, SB(CPrimary), 17.0));
            head.Children.Add(new TextBlock { Text = "模型下载", FontSize = 16.0, FontWeight = FontWeights.Bold, Foreground = SB(CTextMain), Margin = new Thickness(9.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(head);
            Button close = MainWindow.MakeWinButton(IconClose, true);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
            close.Click += (s, e) => Close();
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            Grid.SetRow(bar, 0);
            g.Children.Add(bar);

            var form = new StackPanel { Margin = new Thickness(22.0, 0.0, 22.0, 20.0) };
            form.Children.Add(new TextBlock { Text = "语音识别模型", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = SB(CPrimaryDeep), Margin = new Thickness(0.0, 4.0, 0.0, 8.0) });
            foreach (ModelInfo m in ModelCatalog.All)
            {
                var row = new Row { Model = m };
                var box = new Border { Background = SB(CInputBg), CornerRadius = new CornerRadius(10.0), Padding = new Thickness(14.0, 7.0, 8.0, 7.0), Margin = new Thickness(0.0, 0.0, 0.0, 6.0) };
                var rg = new Grid();
                foreach (double w in new[] { 76.0, 76.0, 76.0 }) rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Cell(rg, 0, new TextBlock { Text = m.Id, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = SB(CTextMain) });
                Cell(rg, 1, new TextBlock { Text = m.Note, FontSize = 12.0, Foreground = SB(CTextHint) });
                Cell(rg, 2, new TextBlock { Text = ModelCatalog.Mb(m.TotalSize), FontSize = 12.5, Foreground = SB(CTextSub) });
                row.Status = new TextBlock { FontSize = 12.5 };
                Cell(rg, 3, row.Status);
                row.Action = new Button { Height = 30.0, Width = 76.0, FontSize = 12.5, Style = secondary };
                row.Action.Click += (s, e) => RowAction(row);
                Cell(rg, 4, row.Action);
                box.Child = rg;
                form.Children.Add(box);
                _rows.Add(row);
            }

            var pp = new StackPanel();
            _bar = new ProgressBar { Minimum = 0.0, Maximum = 1000.0, Style = MainWindow.MakeProgressBarStyle(8.0) };
            _barText = new TextBlock { FontSize = 12.0, Foreground = SB(CTextSub), Margin = new Thickness(2.0, 7.0, 0.0, 0.0) };
            pp.Children.Add(_bar);
            pp.Children.Add(_barText);
            _progress = new Border { Margin = new Thickness(0.0, 10.0, 0.0, 0.0), Child = pp, Visibility = Visibility.Collapsed };
            form.Children.Add(_progress);
            _error = new TextBlock { FontSize = 12.0, Foreground = SB(CDanger), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2.0, 8.0, 0.0, 0.0), Visibility = Visibility.Collapsed };
            form.Children.Add(_error);
            Grid.SetRow(form, 1);
            g.Children.Add(form);
        }

        private static void Cell(Grid g, int col, FrameworkElement e)
        {
            e.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(e, col);
            g.Children.Add(e);
        }

        private Row RowOf(ModelInfo m)
        {
            return _rows.Find(r => r.Model == m);
        }

        private void Refresh()
        {
            bool busy = _dl != null;
            foreach (Row r in _rows)
            {
                string id = r.Model.Id;
                if (busy && r.Model == _dlModel)
                {
                    SetAction(r, "取消", true);
                    continue;
                }
                if (ModelCatalog.IsInstalled(id))
                {
                    bool cur = id == _current;
                    SetStatus(r, cur ? "正在使用" : "已下载", cur ? CKeep : CTextSub);
                    SetAction(r, !cur && _allowUse ? "使用" : null, !busy);
                }
                else if (id == _failedId)
                {
                    SetStatus(r, "下载失败", CDanger);
                    SetAction(r, "重试", !busy);
                }
                else
                {
                    bool partial = Directory.Exists(r.Model.DownloadDir);
                    SetStatus(r, partial ? "没下完" : "未下载", CTextHint);
                    SetAction(r, partial ? "继续" : "下载", !busy);
                }
            }
        }

        private static void SetStatus(Row r, string text, Color c)
        {
            r.Status.Text = text;
            r.Status.Foreground = SB(c);
        }

        private static void SetAction(Row r, string text, bool enabled)
        {
            r.Action.Visibility = text == null ? Visibility.Hidden : Visibility.Visible;
            r.Action.Content = text ?? "";
            r.Action.IsEnabled = enabled;
        }

        private void RowAction(Row r)
        {
            switch (r.Action.Content as string)
            {
                case "取消":
                    Stop();
                    break;
                case "使用":
                    Use(r.Model.Id);
                    break;
                default:
                    Start(r.Model);
                    break;
            }
        }

        private void Start(ModelInfo m)
        {
            if (m == null || _dl != null) return;
            if (ModelCatalog.IsInstalled(m.Id))
            {
                Refresh();
                return;
            }
            _failedId = null;
            _error.Visibility = Visibility.Collapsed;
            _dlModel = m;
            _dl = new ModelDownloader(m);
            _dl.Finished += () => Dispatcher.BeginInvoke(new Action(DownloadFinished));
            _speed = 0.0;
            _lastBytes = -1;
            _bar.Value = 0.0;
            _progress.Visibility = Visibility.Visible;
            Refresh();
            Tick();
            _dl.Start();
            _timer.Start();
        }

        private void Stop()
        {
            if (_dl == null) return;
            _dl.Cancel();
            _barText.Text = "正在停止…";
            Row r = RowOf(_dlModel);
            if (r != null) SetAction(r, "取消", false);
        }

        private void Tick()
        {
            if (_dl == null) return;
            long done = _dl.Done, total = Math.Max(1L, _dl.Total);
            DateTime now = DateTime.UtcNow;
            if (_lastBytes >= 0)
            {
                double dt = (now - _lastTick).TotalSeconds;
                if (dt > 0.2)
                {
                    double inst = Math.Max(0L, done - _lastBytes) / dt;
                    _speed = _speed <= 0.0 ? inst : _speed * 0.75 + inst * 0.25;
                }
            }
            _lastBytes = done;
            _lastTick = now;
            int pct = (int)Math.Min(100L, done * 100L / total);
            _bar.Value = done * 1000.0 / total;
            Row row = RowOf(_dlModel);
            switch (_dl.Phase)
            {
                case "下载":
                    _barText.Text = pct + "%" + (_speed > 1024.0 ? " · " + Speed(_speed) + " · 还剩 " + Eta((total - done) / _speed) : "");
                    SetStatus(row, "下载中 " + pct + "%", CPrimary);
                    break;
                case "校验":
                    _barText.Text = "正在校验…";
                    SetStatus(row, "校验中", CPrimary);
                    break;
                default:
                    _barText.Text = "正在连接…";
                    SetStatus(row, "连接中", CPrimary);
                    break;
            }
        }

        private void DownloadFinished()
        {
            _timer.Stop();
            ModelDownloader dl = _dl;
            ModelInfo m = _dlModel;
            _dl = null;
            _dlModel = null;
            _progress.Visibility = Visibility.Collapsed;
            if (dl.Succeeded)
            {
                Downloaded = true;
            }
            else if (dl.Error != null)
            {
                _failedId = m.Id;
                _error.Text = "下载失败：" + dl.Error;
                _error.Visibility = Visibility.Visible;
            }
            Refresh();
            if (_closeWhenStopped || (dl.Succeeded && _autoStart == m.Id))
            {
                try
                {
                    DialogResult = dl.Succeeded;
                }
                catch (InvalidOperationException)
                {
                    Close();   // shown with Show() (the screenshot harness)
                }
            }
        }

        public bool Downloaded { get; private set; }

        private void Use(string id)
        {
            try
            {
                Rules r = Rules.Load();
                r.Model = id;
                r.Save();
                _current = id;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "规则保存不了：" + ex.Message, "模型下载", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Refresh();
        }

        // Closing in the middle of a download stops it first; the window goes away once the download
        // threads have let go of the part files, so a new download cannot collide with them.
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_dl == null) return;
            e.Cancel = true;
            if (_closeWhenStopped) return;
            var r = MessageBox.Show(this, "正在下载。关掉就停下了，已经下好的部分会留着，下次点「继续」接着下。\n\n要停下吗？", "模型下载", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _closeWhenStopped = true;
            Stop();
        }

        private static string Speed(double bytesPerSec)
        {
            if (bytesPerSec >= 1048576.0) return (bytesPerSec / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB/s";
            return Math.Max(1, (int)(bytesPerSec / 1024.0)) + "KB/s";
        }

        private static string Eta(double secs)
        {
            if (double.IsNaN(secs) || double.IsInfinity(secs) || secs < 0) return "…";
            if (secs < 60.0) return "不到 1 分钟";
            if (secs < 3600.0) return Math.Ceiling(secs / 60.0) + " 分钟";
            return (int)(secs / 3600.0) + " 小时 " + (int)(secs % 3600.0 / 60.0) + " 分钟";
        }
    }
}
