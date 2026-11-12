using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    // One line of the result list: tick box (bound to the row's selection, so several rows can be picked
    // without holding Ctrl), coloured verdict chip, file name, why it was rejected, time.
    internal sealed class ResultRow : Grid
    {
        public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
            "Record", typeof(Rec), typeof(ResultRow), new PropertyMetadata(null, (d, e) => ((ResultRow)d).Apply(e.NewValue as Rec)));

        private static readonly Brush MainBrush = SB(CTextMain);
        private static readonly Brush SubBrush = SB(CTextSub);
        private static readonly Brush HintBrush = SB(CTextHint);

        private readonly CheckBox _check = new CheckBox();
        private readonly Border _chip = new Border();
        private readonly TextBlock _chipText = new TextBlock();
        private readonly TextBlock _name = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly TextBlock _time = new TextBlock();

        public ResultRow()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30.0) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72.0) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _check.SetResourceReference(StyleProperty, "Check");
            _check.VerticalAlignment = VerticalAlignment.Center;
            _check.Focusable = false;
            _check.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsSelected")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
                Mode = BindingMode.TwoWay
            });

            _chip.CornerRadius = new CornerRadius(7.0);
            _chip.Padding = new Thickness(0.0, 3.0, 0.0, 3.0);
            _chip.Width = 46.0;
            _chip.HorizontalAlignment = HorizontalAlignment.Left;
            _chip.VerticalAlignment = VerticalAlignment.Center;
            _chipText.FontSize = 12.0;
            _chipText.FontWeight = FontWeights.SemiBold;
            _chipText.HorizontalAlignment = HorizontalAlignment.Center;
            _chip.Child = _chipText;

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _name.FontSize = 13.0;
            _name.Foreground = MainBrush;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _detail.FontSize = 12.0;
            _detail.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
            _detail.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(_name);
            texts.Children.Add(_detail);

            _time.FontSize = 11.5;
            _time.Foreground = HintBrush;
            _time.VerticalAlignment = VerticalAlignment.Center;
            _time.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);

            SetColumn(_check, 0);
            SetColumn(_chip, 1);
            SetColumn(texts, 2);
            SetColumn(_time, 3);
            Children.Add(_check);
            Children.Add(_chip);
            Children.Add(texts);
            Children.Add(_time);
        }

        public static Color ColorOf(Rec r)
        {
            if (r.IsMemo) return CMemo;
            switch (r.Result)
            {
                case "保留": return CKeep;
                case "筛掉": return CReject;
                case "出错": return CWarn;
                case "放回": return CPrimary;
                case "检查中": return CAccent;
                default: return CTextHint;
            }
        }

        private static string ChipText(Rec r)
        {
            if (r.IsMemo) return Rec.MemoReason;
            switch (r.Result)
            {
                case "放回": return "已放回";
                case "重查": return "待重查";
                default: return r.Result;
            }
        }

        private void Apply(Rec r)
        {
            if (r == null) return;
            Color c = ColorOf(r);
            _chip.Background = SB(Color.FromArgb(34, c.R, c.G, c.B));
            _chipText.Foreground = SB(c);
            _chipText.Text = ChipText(r);
            _chip.Width = r.IsMemo ? 66.0 : r.Result == "检查中" || r.Result == "放回" || r.Result == "重查" ? 52.0 : 46.0;
            // The placeholder of the video being checked right now cannot be picked.
            _check.Visibility = r.Result == "检查中" ? Visibility.Hidden : Visibility.Visible;
            _name.Text = r.Name;

            string detail;
            Brush detailBrush = SubBrush;
            if (r.IsMemo)
            {
                detail = r.MemoNote.Length > 0 ? r.MemoNote : "以前查过，没有问题";
                detailBrush = HintBrush;
            }
            else
            {
                switch (r.Result)
                {
                    case "筛掉":
                        detail = r.ReasonText + (r.DetailText.Length > 0 ? "　" + r.DetailText : "");
                        detailBrush = SB(Color.FromRgb(184, 70, 48));
                        break;
                    case "出错":
                        detail = r.ReasonText;
                        detailBrush = SB(Color.FromRgb(168, 108, 20));
                        break;
                    case "保留":
                        detail = r.ReasonText.Length > 0 ? r.ReasonText : r.Secs > 0 ? "用时 " + Math.Round(r.Secs) + " 秒" : "";
                        detailBrush = HintBrush;
                        break;
                    case "放回":
                    case "重查":
                    case "检查中":
                        detail = "";
                        break;
                    default:
                        detail = r.ReasonText;
                        break;
                }
            }
            if (r.SubDir.Length > 0) detail = (r.SubDir + "　" + detail).Trim();
            _detail.Text = detail;
            _detail.Foreground = detailBrush;
            _detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = detail.Length > 0 ? r.Rel + "\n" + detail : r.Rel;
            _time.Text = ShortTime(r.Time);
        }

        private static string ShortTime(string t)
        {
            DateTime d;
            if (!DateTime.TryParse(t, out d)) return t ?? "";
            return d.Date == DateTime.Today ? d.ToString("HH:mm") : d.ToString("MM-dd HH:mm");
        }
    }
}
