using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    // Editor for 设置.ini, so nobody has to open the ini file in Notepad.
    internal sealed class RulesWindow : Window
    {
        private static readonly string[] ModelLabels =
        {
            "tiny（最快）",
            "base（快）",
            "small（推荐）",
            "medium（最准，慢）"
        };

        private readonly Rules _r;
        private TextBox _douyin;
        private TextBox _price;
        private TextBox _interval;
        private TextBox _score;
        private TextBox _idle;
        private CheckBox _at;
        private CheckBox _audioDouyin;
        private CheckBox _stopEarly;
        private ComboBox _model;
        private Grid _modelRow;
        private TextBlock _modelNote;
        private TextBlock _memText;
        private Button _memClear;

        public Rules Result { get; private set; }

        public RulesWindow(Rules r)
        {
            _r = r;
            Title = "筛选规则";
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(600.0, work.Width - 40.0);
            SizeToContent = SizeToContent.Height;
            MaxHeight = work.Height - 40.0;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = MainWindow.MakeScrollBarStyle();
            BuildUi();
            Load(r);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void BuildUi()
        {
            Style input = MainWindow.MakeTextBoxStyle();
            Style check = MainWindow.MakeCheckBoxStyle();
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
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            frame.Child = g;
            Content = root;

            var bar = new Grid { Background = Brushes.Transparent };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(MakeIcon(IconTune, SB(CPrimary), 17.0));
            head.Children.Add(new TextBlock { Text = "筛选规则", FontSize = 16.0, FontWeight = FontWeights.Bold, Foreground = SB(CTextMain), Margin = new Thickness(9.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });

            bar.Children.Add(head);
            Button close = MainWindow.MakeWinButton(IconClose, true);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
            close.Click += (s, e) => Close();
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            Grid.SetRow(bar, 0);
            g.Children.Add(bar);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var form = new StackPanel { Margin = new Thickness(22.0, 4.0, 22.0, 10.0) };
            scroll.Content = form;
            Grid.SetRow(scroll, 1);
            g.Children.Add(scroll);

            form.Children.Add(Section("抖音号关键词"));

            _douyin = Multi(input, 50.0);
            form.Children.Add(_douyin);
            _at = new CheckBox { Style = check, Margin = new Thickness(2.0, 10.0, 0.0, 0.0), Content = Text("@用户名 也算") };
            form.Children.Add(_at);

            form.Children.Add(Section("价格关键词"));

            _price = Multi(input, 64.0);
            form.Children.Add(_price);


            form.Children.Add(Section("画面"));
            var nums = new Grid();
            nums.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            nums.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            nums.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            _interval = Single(input);
            _score = Single(input);
            var left = new StackPanel();
            left.Children.Add(Label("间隔秒数"));
            left.Children.Add(_interval);

            var right = new StackPanel();
            right.Children.Add(Label("可信度（0～1）"));
            right.Children.Add(_score);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            nums.Children.Add(left);
            nums.Children.Add(right);
            form.Children.Add(nums);

            form.Children.Add(Section("语音"));
            form.Children.Add(Label("模型"));
            _model = new ComboBox { Style = MainWindow.MakeComboBoxStyle() };
            foreach (string l in ModelLabels) _model.Items.Add(l);
            _model.SelectionChanged += (s, e) => UpdateModelNote();
            form.Children.Add(_model);
            _modelNote = new TextBlock { FontSize = 12.0, Foreground = SB(CWarn), VerticalAlignment = VerticalAlignment.Center };
            var download = new Button { Content = "下载", Height = 28.0, Width = 72.0, FontSize = 12.5, Style = secondary };
            download.Click += (s, e) => DownloadModel();
            _modelRow = TextAndButton(_modelNote, download);
            _modelRow.Margin = new Thickness(2.0, 8.0, 0.0, 0.0);
            form.Children.Add(_modelRow);
            _audioDouyin = new CheckBox { Style = check, Margin = new Thickness(2.0, 10.0, 0.0, 0.0), Content = Text("也查抖音号关键词") };
            form.Children.Add(_audioDouyin);

            form.Children.Add(Section("其他"));
            _stopEarly = new CheckBox { Style = check, Margin = new Thickness(2.0, 0.0, 0.0, 0.0), Content = Text("找到一个问题就停") };
            form.Children.Add(_stopEarly);
            var idleRow = new Grid { Margin = new Thickness(0.0, 12.0, 0.0, 0.0) };
            idleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            idleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            idleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var idleCol = new StackPanel();
            idleCol.Children.Add(Label("查完自动停止前等几分钟"));
            _idle = Single(input);
            idleCol.Children.Add(_idle);
            idleRow.Children.Add(idleCol);
            form.Children.Add(idleRow);

            form.Children.Add(Section("筛选记忆"));
            _memText = Text("");
            _memText.VerticalAlignment = VerticalAlignment.Center;
            _memClear = new Button { Content = "清空", Height = 28.0, Width = 72.0, FontSize = 12.5, Style = secondary };
            _memClear.Click += (s, e) => ClearMemory();
            Grid memRow = TextAndButton(_memText, _memClear);
            memRow.Margin = new Thickness(2.0, 0.0, 0.0, 0.0);
            form.Children.Add(memRow);
            UpdateMemory();

            var foot = new Grid { Margin = new Thickness(22.0, 10.0, 22.0, 18.0) };
            var reset = new Button { Content = "恢复默认", Height = 36.0, Width = 96.0, Style = secondary, HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += (s, e) => Load(new Rules());
            foot.Children.Add(reset);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Height = 36.0, Width = 88.0, Style = secondary, Margin = new Thickness(0.0, 0.0, 10.0, 0.0) };
            cancel.Click += (s, e) => Close();
            var save = new Button { Height = 36.0, Width = 110.0, Style = MainWindow.MakePrimaryButtonStyle() };
            save.Content = new TextBlock { Text = "保存", FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontSize = 14.0 };
            save.Click += (s, e) => Save();
            btns.Children.Add(cancel);
            btns.Children.Add(save);
            foot.Children.Add(btns);
            Grid.SetRow(foot, 2);
            g.Children.Add(foot);
        }

        private static TextBlock Section(string text)
        {
            return new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = SB(CPrimaryDeep), Margin = new Thickness(0.0, 12.0, 0.0, 6.0) };
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, FontSize = 12.5, Foreground = SB(CTextSub), Margin = new Thickness(0.0, 0.0, 0.0, 6.0), TextWrapping = TextWrapping.Wrap };
        }

        private static TextBlock Text(string text)
        {
            return new TextBlock { Text = text, FontSize = 12.5, Foreground = SB(CTextMain), TextWrapping = TextWrapping.Wrap };
        }

        private static TextBox Multi(Style style, double height)
        {
            var t = new TextBox
            {
                Style = style,
                Height = height,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(8.0, 6.0, 8.0, 6.0),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            ScrollViewer.SetVerticalScrollBarVisibility(t, ScrollBarVisibility.Auto);
            return t;
        }

        private static TextBox Single(Style style)
        {
            return new TextBox { Style = style, Height = 32.0, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(9.0, 0.0, 9.0, 0.0) };
        }

        private static Grid TextAndButton(TextBlock text, Button button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(button, 1);
            g.Children.Add(text);
            g.Children.Add(button);
            return g;
        }

        private void Load(Rules r)
        {
            _douyin.Text = string.Join("，", Rules.SplitWords(r.DouyinWords));
            _price.Text = string.Join("，", Rules.SplitWords(r.PriceWords));
            _at.IsChecked = r.CheckAt;
            _interval.Text = r.Interval.ToString("0.##", CultureInfo.InvariantCulture);
            _score.Text = r.MinScore.ToString("0.##", CultureInfo.InvariantCulture);
            int idx = Array.IndexOf(Rules.Models, r.Model);
            _model.SelectedIndex = idx >= 0 ? idx : 2;
            _audioDouyin.IsChecked = r.AudioDouyin;
            _stopEarly.IsChecked = r.StopEarly;
            _idle.Text = r.IdleMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            UpdateModelNote();
        }

        private void UpdateModelNote()
        {
            if (_model.SelectedIndex < 0) return;
            string m = Rules.Models[_model.SelectedIndex];
            _modelRow.Visibility = ModelCatalog.IsInstalled(m) ? Visibility.Collapsed : Visibility.Visible;
            _modelNote.Text = "未下载（" + ModelCatalog.SizeText(m) + "）";
        }

        private void DownloadModel()
        {
            if (_model.SelectedIndex < 0) return;
            string m = Rules.Models[_model.SelectedIndex];
            new ModelWindow(_r.Model, m, false) { Owner = this }.ShowDialog();
            UpdateModelNote();
        }

        private void UpdateMemory()
        {
            int n = Memory.Count;
            _memText.Text = "已记住 " + n + " 个视频";
            _memClear.IsEnabled = n > 0;
        }

        private void ClearMemory()
        {
            int n = Memory.Count;
            if (n == 0) return;
            var r = MessageBox.Show(this, "清空以后，历史通过的 " + n + " 个视频换个文件夹或改了名字再出现，会重新检查。\n各个文件夹里「筛选记录.csv」记着的照样不重查。\n\n确定清空吗？",
                "筛选记忆", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            try
            {
                Memory.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "清空不了：" + ex.Message, "筛选记忆", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateMemory();
        }

        private static bool ParseNum(string s, out double d)
        {
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        private void Save()
        {
            double interval, score;
            if (!ParseNum(_interval.Text, out interval) || interval < 0.2 || interval > 30)
            {
                MessageBox.Show(this, "「每隔几秒看一帧」填 0.2 到 30 之间的数，比如 1。", "筛选规则", MessageBoxButton.OK, MessageBoxImage.Information);
                _interval.Focus();
                return;
            }
            if (!ParseNum(_score.Text, out score) || score < 0 || score > 0.99)
            {
                MessageBox.Show(this, "「可信度」填 0 到 0.99 之间的数，比如 0.6。", "筛选规则", MessageBoxButton.OK, MessageBoxImage.Information);
                _score.Focus();
                return;
            }
            double idle;
            if (!ParseNum(_idle.Text, out idle) || idle < 0 || idle > 240)
            {
                MessageBox.Show(this, "「查完自动停止前等几分钟」填 0 到 240 之间的数，比如 2。", "筛选规则", MessageBoxButton.OK, MessageBoxImage.Information);
                _idle.Focus();
                return;
            }
            var r = _r.Clone();
            r.DouyinWords = string.Join(",", Rules.SplitWords(_douyin.Text));
            r.PriceWords = string.Join(",", Rules.SplitWords(_price.Text));
            r.CheckAt = _at.IsChecked == true;
            r.Interval = interval;
            r.MinScore = score;
            r.Model = Rules.Models[Math.Max(0, _model.SelectedIndex)];
            r.AudioDouyin = _audioDouyin.IsChecked == true;
            r.StopEarly = _stopEarly.IsChecked == true;
            r.IdleMinutes = idle;
            Result = r;
            DialogResult = true;
        }
    }
}
