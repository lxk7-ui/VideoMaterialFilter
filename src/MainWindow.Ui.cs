using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    public partial class MainWindow
    {
        private void BuildStyles()
        {
            Resources["PrimaryButton"] = MakePrimaryButtonStyle();
            Resources["StopButton"] = MakeStopButtonStyle();
            Resources["SecondaryButton"] = MakeSecondaryButtonStyle();
            Resources["GhostButton"] = MakeGhostButtonStyle(false);
            Resources["DarkGhostButton"] = MakeGhostButtonStyle(true);
            Resources["InputBox"] = MakeTextBoxStyle();
            Resources["Check"] = MakeCheckBoxStyle();
            Resources["Bar"] = MakeProgressBarStyle(8.0);
            Resources["Pill"] = MakePillStyle();
            Resources[typeof(ScrollBar)] = MakeScrollBarStyle();
        }

        private void BuildUi()
        {
            Title = AppInfo.Name + " " + AppInfo.Version;
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(1120.0, work.Width);
            Height = Math.Min(760.0, work.Height);
            MinWidth = Math.Min(920.0, work.Width);
            MinHeight = Math.Min(620.0, work.Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
            StateChanged += WindowStateChanged;
            if (File.Exists(Paths.IconPath))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(Paths.IconPath);
                    bi.EndInit();
                    bi.Freeze();
                    Icon = bi;
                }
                catch
                {
                }
            }

            // Alpha 1 instead of transparent: the 10px margin must receive mouse input for resizing.
            var root = new Grid { Background = SB(1, 255, 255, 255) };
            _windowFrame = new Border
            {
                Background = SB(CPageBg),
                CornerRadius = new CornerRadius(18.0),
                Margin = new Thickness(10.0),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                Effect = Shadow(Color.FromRgb(110, 126, 150), 24.0, 0.0, 0.35)
            };
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52.0) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            _windowFrame.Child = shell;
            root.Children.Add(_windowFrame);
            Content = root;

            BuildTitleBar(shell);

            var body = new Grid { Margin = new Thickness(16.0, 14.0, 16.0, 16.0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(352.0) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16.0) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            UIElement left = BuildLeft();
            Grid.SetColumn(left, 0);
            body.Children.Add(left);
            UIElement right = BuildRight();
            Grid.SetColumn(right, 2);
            body.Children.Add(right);
        }

        private void BuildTitleBar(Grid shell)
        {
            _titleBar = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18.0, 18.0, 0.0, 0.0) };
            var title = new Grid();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            var logo = new Border
            {
                Width = 34.0,
                Height = 34.0,
                CornerRadius = new CornerRadius(9.0),
                Background = LG(Color.FromRgb(64, 132, 196), CPrimaryDeep, new Point(0, 0), new Point(1, 1)),
                VerticalAlignment = VerticalAlignment.Center
            };
            logo.Child = MakeIcon(IconFilter, Brushes.White, 17.0);
            brand.Children.Add(logo);
            brand.Children.Add(new TextBlock
            {
                Text = AppInfo.Name,
                Foreground = SB(CTextMain),
                FontSize = 17.0,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(11.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var badge = new Border
            {
                Background = SB(28, 46, 108, 168),
                CornerRadius = new CornerRadius(6.0),
                Padding = new Thickness(7.0, 2.0, 7.0, 2.0),
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock { Text = AppInfo.Version, Foreground = SB(CPrimary), FontSize = 11.0 };
            brand.Children.Add(badge);
            title.Children.Add(brand);

            var status = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 162.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusDot = new Ellipse
            {
                Width = 9.0,
                Height = 9.0,
                Fill = SB(CSuccess),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = CSuccess, BlurRadius = 8.0, ShadowDepth = 0.0 }
            };
            _headerStatus = new TextBlock
            {
                Text = "就绪",
                Foreground = SB(CTextSub),
                FontSize = 12.5,
                Margin = new Thickness(7.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            status.Children.Add(_statusDot);
            status.Children.Add(_headerStatus);
            title.Children.Add(status);

            var winButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Button minBtn = MakeWinButton(IconMin, false);
            minBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            _maxBtn = MakeWinButton(IconMax, false);
            _maxBtn.Click += (s, e) => ToggleMaximize();
            Button closeBtn = MakeWinButton(IconClose, true);
            closeBtn.Click += (s, e) => Close();
            winButtons.Children.Add(minBtn);
            winButtons.Children.Add(_maxBtn);
            winButtons.Children.Add(closeBtn);
            title.Children.Add(winButtons);
            _titleBar.Child = title;
            _titleBar.MouseLeftButtonDown += TitleBarDown;
            Grid.SetRow(_titleBar, 0);
            shell.Children.Add(_titleBar);
        }

        // ---------------------------------------------------------------- left: folder, options, start
        private UIElement BuildLeft()
        {
            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = Card();
            var inner = new Grid { Margin = new Thickness(18.0, 16.0, 16.0, 16.0) };
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var form = new StackPanel();
            scroll.Content = form;
            Grid.SetRow(scroll, 0);
            inner.Children.Add(scroll);
            card.Child = inner;
            Grid.SetRow(card, 0);
            left.Children.Add(card);

            form.Children.Add(GroupTitle("视频文件夹", IconFolder, 0.0));
            _folderBox = new TextBox
            {
                Height = 34.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(9.0, 0.0, 9.0, 0.0),
                Style = (Style)Resources["InputBox"]
            };
            form.Children.Add(WithBrowse(_folderBox));
            _folderInfo = new TextBlock
            {
                FontSize = 12.0,
                Foreground = SB(CTextSub),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2.0, 7.0, 0.0, 0.0)
            };
            form.Children.Add(_folderInfo);
            Button openDir = SmallButton("打开文件夹", IconFolder);
            openDir.Click += (s, e) => OpenFolder(false);
            Button openRej = SmallButton("已筛掉", IconFilter);
            openRej.Click += (s, e) => OpenFolder(true);
            form.Children.Add(ButtonRow(new Thickness(0.0, 10.0, 0.0, 0.0), openDir, openRej));

            form.Children.Add(GroupTitle("检查", IconSearch, 20.0));
            _frameCheck = Check("画面：抖音号、价格");
            _audioCheck = Check("语音：价格");
            form.Children.Add(_frameCheck);
            form.Children.Add(_audioCheck);

            form.Children.Add(GroupTitle("选项", IconClock, 12.0));
            _watchCheck = Check("自动检查新下载的视频");
            _autoStopCheck = Check("查完自动停止");
            _autoStopCheck.Margin = new Thickness(30.0, 0.0, 0.0, 9.0);   // belongs to the option above
            _recCheck = Check("包含子文件夹");
            form.Children.Add(_watchCheck);
            form.Children.Add(_autoStopCheck);
            form.Children.Add(_recCheck);

            _rulesBtn = SmallButton("筛选规则", IconTune);
            _rulesBtn.Click += (s, e) => EditRules();
            _modelBtn = SmallButton("模型下载", IconDownload);
            _modelBtn.Click += (s, e) => ManageModels();
            _selfTestBtn = SmallButton("组件自检", IconCheck);
            _selfTestBtn.Click += (s, e) => SelfTest();
            Grid g = ButtonRow(new Thickness(0.0, 12.0, 0.0, 0.0), _rulesBtn, _modelBtn, _selfTestBtn);
            Grid.SetRow(g, 1);
            inner.Children.Add(g);

            _startBtn = new Button { Height = 52.0, Margin = new Thickness(0.0, 14.0, 0.0, 0.0), Style = (Style)Resources["PrimaryButton"] };
            var startContent = new StackPanel { Orientation = Orientation.Horizontal };
            _startIcon = MakeIcon(IconPlay, Brushes.White, 18.0);
            _startText = new TextBlock
            {
                Text = "开始筛选",
                FontWeight = FontWeights.Bold,
                FontSize = 15.5,
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _startText.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = _startBtn });
            startContent.Children.Add(_startIcon);
            startContent.Children.Add(_startText);
            _startBtn.Content = startContent;
            _startBtn.Click += (s, e) => StartOrStop();
            Grid.SetRow(_startBtn, 1);
            left.Children.Add(_startBtn);
            return left;
        }

        // ---------------------------------------------------------------- right: status, results, log
        private UIElement BuildRight()
        {
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Status card.
            var status = Card();
            var sp = new StackPanel { Margin = new Thickness(20.0, 16.0, 20.0, 16.0) };
            _stateTitle = new TextBlock { FontSize = 16.5, FontWeight = FontWeights.Bold, Foreground = SB(CTextMain), TextTrimming = TextTrimming.CharacterEllipsis };
            _stateSub = new TextBlock { FontSize = 12.5, Foreground = SB(CTextSub), Margin = new Thickness(0.0, 4.0, 0.0, 0.0), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(_stateTitle);
            sp.Children.Add(_stateSub);
            var barRow = new Grid { Margin = new Thickness(0.0, 12.0, 0.0, 0.0) };
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92.0) });
            _fileBar = new ProgressBar { Minimum = 0.0, Maximum = 100.0, Style = (Style)Resources["Bar"], VerticalAlignment = VerticalAlignment.Center };
            _stageText = new TextBlock { FontSize = 12.0, Foreground = SB(CTextHint), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_fileBar, 0);
            Grid.SetColumn(_stageText, 1);
            barRow.Children.Add(_fileBar);
            barRow.Children.Add(_stageText);
            sp.Children.Add(barRow);
            var stats = new UniformGrid { Columns = 6, Margin = new Thickness(0.0, 14.0, 0.0, 0.0) };
            _statChecked = StatTile(stats, "已查", CTextMain);
            _statKept = StatTile(stats, "保留", CKeep);
            _statRejected = StatTile(stats, "筛掉", CReject);
            _statError = StatTile(stats, "出错", CWarn);
            _statSkipped = StatTile(stats, Rec.MemoReason, CMemo);
            _statLeft = StatTile(stats, "待检查", CTextSub);
            sp.Children.Add(stats);
            status.Child = sp;
            Grid.SetRow(status, 0);
            right.Children.Add(status);

            // Result list card.
            var listCard = Card();
            listCard.Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
            var lg = new Grid { Margin = new Thickness(16.0, 14.0, 12.0, 10.0) };
            lg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            var head = new Grid { Margin = new Thickness(4.0, 0.0, 4.0, 0.0) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var headTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headTitle.Children.Add(MakeIcon(IconVideo, SB(CPrimary), 16.0));
            headTitle.Children.Add(new TextBlock
            {
                Text = "检查记录",
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = SB(CTextMain),
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(headTitle, 0);
            head.Children.Add(headTitle);
            var pills = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _pillAll = Pill(pills, "全部", "");
            _pillReject = Pill(pills, "筛掉", "筛掉");
            _pillKeep = Pill(pills, "保留", "保留");
            _pillMemo = Pill(pills, Rec.MemoReason, Rec.MemoReason);
            _pillError = Pill(pills, "出错", "出错");
            _pillAll.IsChecked = true;
            Grid.SetColumn(pills, 1);
            head.Children.Add(pills);
            Grid.SetRow(head, 0);
            lg.Children.Add(head);

            var actionRow = new Grid { Margin = new Thickness(4.0, 12.0, 0.0, 8.0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            // Ticks every row in the current view; a dash when only some are picked. Its label reads 全选, or
            // 已选 N while rows are picked (wide enough for that, so the buttons do not move).
            _selCount = new TextBlock { Text = "全选", FontSize = 12.5, Foreground = SB(CTextMain), MinWidth = 46.0 };
            _selectAll = new CheckBox
            {
                Style = (Style)Resources["Check"],
                Margin = new Thickness(6.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                Content = _selCount
            };
            _selectAll.Click += (s, e) => ToggleSelectAll();
            Grid.SetColumn(_selectAll, 0);
            actionRow.Children.Add(_selectAll);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 1);
            actionRow.Children.Add(actions);
            _playBtn = ActionButton(actions, "播放", IconPlay);
            _playBtn.Click += (s, e) => PlaySelected();
            _locateBtn = ActionButton(actions, "打开所在位置", IconFolder);
            _locateBtn.Click += (s, e) => LocateSelected();
            _restoreBtn = ActionButton(actions, "放回原处", IconUndo);
            _restoreBtn.ToolTip = "筛错了？把选中的视频挪回原来的文件夹，以后不再查它";
            _restoreBtn.Click += (s, e) => RestoreSelected();
            _recheckBtn = ActionButton(actions, "重新检查", IconRefresh);
            _recheckBtn.ToolTip = "改了规则以后想再查一遍：选中的视频下次点「开始筛选」时重新检查";
            _recheckBtn.Click += (s, e) => RecheckSelected();

            Grid.SetRow(actionRow, 1);
            lg.Children.Add(actionRow);

            var listHost = new Grid();
            _list = new ListBox
            {
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                SelectionMode = SelectionMode.Extended,
                ItemContainerStyle = MakeListItemStyle(),
                ItemTemplate = new DataTemplate { VisualTree = RowFactory() },
                ItemsSource = _view
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            _list.SelectionChanged += (s, e) => UpdateActions();
            _list.MouseDoubleClick += (s, e) =>
            {
                // Plays the row that was double-clicked (with several rows ticked, not just the first of them);
                // a double click on a row's tick box only ticks.
                var item = ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item == null || IsInTickBox(e.OriginalSource as DependencyObject)) return;
                var rec = item.DataContext as Rec;
                if (rec != null && rec.Result != "检查中") Play(rec);
            };
            _list.KeyDown += (s, e) => { if (e.Key == Key.Enter) PlaySelected(); };
            listHost.Children.Add(_list);
            _emptyText = new TextBlock
            {
                FontSize = 13.0,
                Foreground = SB(CTextHint),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LineHeight = 22.0,
                Margin = new Thickness(30.0),
                IsHitTestVisible = false
            };
            listHost.Children.Add(_emptyText);
            Grid.SetRow(listHost, 2);
            lg.Children.Add(listHost);
            listCard.Child = lg;
            Grid.SetRow(listCard, 1);
            right.Children.Add(listCard);

            // Log card (collapsed: shows only the latest line).
            var logCard = new Border
            {
                Background = SB(Color.FromRgb(28, 32, 40)),
                CornerRadius = new CornerRadius(14.0),
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
                Effect = Shadow(Color.FromRgb(180, 192, 208), 12.0, 2.0, 0.45)
            };
            var logGrid = new Grid();
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var logHeader = new Border { Height = 40.0, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            hl.Children.Add(MakeIcon(IconTerminal, SB(Color.FromRgb(120, 190, 255)), 16.0));
            hl.Children.Add(new TextBlock
            {
                Text = "运行日志",
                Foreground = SB(Color.FromRgb(205, 214, 228)),
                FontSize = 13.0,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(hl, 0);
            hg.Children.Add(hl);
            _logLast = new TextBlock
            {
                Foreground = SB(Color.FromRgb(150, 162, 178)),
                FontSize = 12.0,
                Margin = new Thickness(14.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_logLast, 1);
            hg.Children.Add(_logLast);
            var openLog = new Button
            {
                Style = (Style)Resources["DarkGhostButton"],
                Content = new TextBlock { Text = "打开日志文件", FontSize = 12.0, Foreground = SB(Color.FromRgb(120, 190, 255)) },
                Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            openLog.Click += (s, e) =>
            {
                e.Handled = true;
                ShellOpen(Paths.LogPath, false);
            };
            Grid.SetColumn(openLog, 2);
            hg.Children.Add(openLog);
            _logArrow = MakeIcon(IconChevronUp, SB(Color.FromRgb(170, 182, 198)), 16.0);
            _logArrow.Margin = new Thickness(6.0, 0.0, 16.0, 0.0);
            Grid.SetColumn(_logArrow, 3);
            hg.Children.Add(_logArrow);
            logHeader.Child = hg;
            logHeader.MouseLeftButtonUp += (s, e) => ToggleLog();
            Grid.SetRow(logHeader, 0);
            logGrid.Children.Add(logHeader);
            _logBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                Foreground = SB(Color.FromRgb(220, 226, 234)),
                FontFamily = new FontFamily("Consolas, Microsoft YaHei"),
                FontSize = 12.0,
                Margin = new Thickness(14.0, 0.0, 10.0, 12.0),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 170.0,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CaretBrush = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_logBox, 1);
            logGrid.Children.Add(_logBox);
            logCard.Child = logGrid;
            Grid.SetRow(logCard, 2);
            right.Children.Add(logCard);
            _logArrow.Data = Geo(IconChevronUp);
            return right;
        }

        // ---------------------------------------------------------------- small builders
        private static Border Card()
        {
            return new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0),
                Effect = Shadow(Color.FromRgb(185, 198, 214), 16.0, 3.0, 0.5)
            };
        }

        private static StackPanel GroupTitle(string text, string iconData, double top)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, top, 0.0, 9.0) };
            p.Children.Add(MakeIcon(iconData, SB(CPrimary), 14.0));
            p.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = SB(CPrimaryDeep),
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return p;
        }

        private CheckBox Check(string text)
        {
            return new CheckBox
            {
                Style = (Style)Resources["Check"],
                Margin = new Thickness(2.0, 0.0, 0.0, 9.0),
                Content = new TextBlock { Text = text, FontSize = 12.5, Foreground = SB(CTextMain), TextWrapping = TextWrapping.Wrap }
            };
        }

        private Grid WithBrowse(TextBox box)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(box, 0);
            g.Children.Add(box);
            var browse = new Button
            {
                Content = "选择",
                Width = 58.0,
                Height = 34.0,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                FontSize = 12.5,
                Style = (Style)Resources["SecondaryButton"]
            };
            browse.Click += (s, e) => PickFolder();
            Grid.SetColumn(browse, 1);
            g.Children.Add(browse);
            return g;
        }

        private Button SmallButton(string text, string iconData)
        {
            var btn = new Button { Height = 32.0, FontSize = 12.5, Style = (Style)Resources["SecondaryButton"] };
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(MakeIcon(iconData, SB(CPrimary), 13.0));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6.0, 0.0, 0.0, 0.0), Foreground = SB(CPrimaryDeep), VerticalAlignment = VerticalAlignment.Center });
            btn.Content = content;
            return btn;
        }

        // Equal-width buttons side by side, 8px apart.
        private static Grid ButtonRow(Thickness margin, params Button[] buttons)
        {
            var g = new Grid { Margin = margin };
            for (int i = 0; i < buttons.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                buttons[i].Margin = new Thickness(i == 0 ? 0.0 : 4.0, 0.0, i == buttons.Length - 1 ? 0.0 : 4.0, 0.0);
                Grid.SetColumn(buttons[i], i);
                g.Children.Add(buttons[i]);
            }
            return g;
        }

        private Button ActionButton(Panel host, string text, string iconData)
        {
            var btn = new Button
            {
                Height = 30.0,
                Padding = new Thickness(9.0, 0.0, 10.0, 0.0),
                Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
                FontSize = 12.5,
                Style = (Style)Resources["SecondaryButton"],
                IsEnabled = false
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(MakeIcon(iconData, SB(CPrimary), 12.0));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6.0, 0.0, 0.0, 0.0), Foreground = SB(CPrimaryDeep), VerticalAlignment = VerticalAlignment.Center });
            btn.Content = content;
            host.Children.Add(btn);
            return btn;
        }

        private TextBlock StatTile(Panel host, string label, Color color)
        {
            var b = new Border
            {
                Background = SB(Color.FromRgb(246, 248, 251)),
                CornerRadius = new CornerRadius(12.0),
                Padding = new Thickness(11.0, 8.0, 6.0, 8.0),
                Margin = new Thickness(0.0, 0.0, 7.0, 0.0)
            };
            var sp = new StackPanel();
            var num = new TextBlock { Text = "0", FontSize = 20.0, FontWeight = FontWeights.Bold, Foreground = SB(color) };
            sp.Children.Add(num);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = SB(CTextHint), Margin = new Thickness(0.0, 1.0, 0.0, 0.0) });
            b.Child = sp;
            host.Children.Add(b);
            return num;
        }

        private RadioButton Pill(Panel host, string label, string filter)
        {
            var count = new TextBlock { Text = "0", FontWeight = FontWeights.Bold, Margin = new Thickness(5.0, 0.0, 0.0, 0.0) };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = label });
            content.Children.Add(count);
            var rb = new RadioButton { Style = (Style)Resources["Pill"], Content = content, GroupName = "filter", Tag = count };
            rb.Checked += (s, e) =>
            {
                _filter = filter;
                RebuildView();
            };
            host.Children.Add(rb);
            return rb;
        }

        private static FrameworkElementFactory RowFactory()
        {
            var row = new FrameworkElementFactory(typeof(ResultRow));
            row.SetBinding(ResultRow.RecordProperty, new Binding());
            return row;
        }

        // ---------------------------------------------------------------- window chrome
        private void TitleBarDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); } catch { }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (_windowFrame == null) return;
            bool max = WindowState == WindowState.Maximized;
            _windowFrame.Margin = new Thickness(max ? 8.0 : 10.0);
            _windowFrame.CornerRadius = new CornerRadius(max ? 10.0 : 18.0);
            _titleBar.CornerRadius = max ? new CornerRadius(10.0, 10.0, 0.0, 0.0) : new CornerRadius(18.0, 18.0, 0.0, 0.0);
            if (_maxBtn != null) _maxBtn.Tag = Geo(max ? IconRestore : IconMax);
        }

        private void ToggleLog()
        {
            bool open = _logBox.Visibility != Visibility.Visible;
            _logBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _logArrow.Data = Geo(open ? IconChevronDown : IconChevronUp);
            if (open) _logBox.ScrollToEnd();
        }
    }
}
