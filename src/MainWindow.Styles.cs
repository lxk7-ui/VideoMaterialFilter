using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static SucaiShaixuan.Theme;

namespace SucaiShaixuan
{
    // Control styles shared by the main window, the rules dialog and the folder picker
    // (the picker comes from Axe and calls these by name, so the signatures stay as in Axe).
    public partial class MainWindow
    {
        internal static ControlTemplate ButtonTpl(Brush normal, Brush hover, Brush pressed, Brush disabled, Brush borderNormal, Brush borderHover, CornerRadius r)
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, normal);
            bd.SetValue(Border.CornerRadiusProperty, r);
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            if (borderNormal != null)
            {
                bd.SetValue(Border.BorderBrushProperty, borderNormal);
                bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            }
            else
            {
                bd.SetValue(Border.BorderThicknessProperty, new Thickness(0.0));
            }
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(Button));
            tpl.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bd"));
            if (borderHover != null) over.Setters.Add(new Setter(Border.BorderBrushProperty, borderHover, "bd"));
            tpl.Triggers.Add(over);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Border.BackgroundProperty, pressed, "bd"));
            tpl.Triggers.Add(press);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Border.BackgroundProperty, disabled, "bd"));
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            tpl.Triggers.Add(off);
            return tpl;
        }

        internal static Style MakePrimaryButtonStyle()
        {
            Brush normal = LG(CAccentLight, CAccent, new Point(0, 0), new Point(0, 1));
            Brush hover = LG(Color.FromRgb(255, 176, 96), Color.FromRgb(255, 148, 46), new Point(0, 0), new Point(0, 1));
            Brush pressed = SB(CAccentDeep);
            Brush disabled = SB(Color.FromRgb(214, 220, 228));
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(normal, hover, pressed, disabled, null, null, new CornerRadius(12.0))));
            return s;
        }

        // The start button turns into this while a run is going.
        internal static Style MakeStopButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CDanger)));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(
                Brushes.White, SB(Color.FromRgb(255, 245, 240)), SB(Color.FromRgb(255, 234, 225)), SB(Color.FromRgb(244, 246, 249)),
                SB(CDanger), SB(CDanger), new CornerRadius(12.0))));
            return s;
        }

        internal static Style MakeSecondaryButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CPrimaryDeep)));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(
                SB(Color.FromRgb(238, 243, 250)), SB(Color.FromRgb(226, 236, 248)), SB(Color.FromRgb(212, 226, 242)), SB(Color.FromRgb(244, 246, 249)),
                SB(CBorder), SB(CPrimary), new CornerRadius(10.0))));
            return s;
        }

        internal static Style MakeGhostButtonStyle(bool dark)
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            Brush hover = dark ? SB(22, 255, 255, 255) : SB(CWinHover);
            Brush press = dark ? SB(38, 255, 255, 255) : SB(CBorder);
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(Brushes.Transparent, hover, press, Brushes.Transparent, null, null, new CornerRadius(10.0))));
            return s;
        }

        internal static Style MakeTextBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, SB(CInputBg));
            bd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            host.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, new TemplateBindingExtension(ScrollViewer.VerticalScrollBarVisibilityProperty));
            bd.AppendChild(host);
            var tpl = new ControlTemplate(typeof(TextBox));
            tpl.VisualTree = bd;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "bd"));
            focus.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "bd"));
            tpl.Triggers.Add(focus);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Border.BackgroundProperty, SB(Color.FromRgb(240, 242, 246)), "bd"));
            tpl.Triggers.Add(off);
            var s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            s.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        internal static Style MakeComboBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, SB(CInputBg));
            bd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var grid = new FrameworkElementFactory(typeof(Grid));
            var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1.0, GridUnitType.Star));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c0);
            grid.AppendChild(c1);

            var toggleTpl = new ControlTemplate(typeof(ToggleButton));
            var toggleBd = new FrameworkElementFactory(typeof(Border));
            toggleBd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleTpl.VisualTree = toggleBd;
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(Grid.ColumnSpanProperty, 2);
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(Control.TemplateProperty, toggleTpl);
            var openBinding = new Binding("IsDropDownOpen");
            openBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            openBinding.Mode = BindingMode.TwoWay;
            toggle.SetBinding(ToggleButton.IsCheckedProperty, openBinding);
            grid.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 6.0, 0.0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var boxBinding = new Binding("SelectionBoxItem");
            boxBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            content.SetBinding(ContentPresenter.ContentProperty, boxBinding);
            grid.AppendChild(content);

            var arrowBox = new FrameworkElementFactory(typeof(Border));
            arrowBox.SetValue(Grid.ColumnProperty, 1);
            arrowBox.SetValue(FrameworkElement.WidthProperty, 26.0);
            arrowBox.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            arrowBox.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Path.DataProperty, Geo("M7,10 L12,15 L17,10 Z"));
            arrow.SetValue(Shape.FillProperty, SB(CTextHint));
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrowBox.AppendChild(arrow);
            grid.AppendChild(arrowBox);
            bd.AppendChild(grid);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 4.0, 0.0, 0.0));
            var popupBinding = new Binding("IsDropDownOpen");
            popupBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBinding.Mode = BindingMode.TwoWay;
            popup.SetBinding(Popup.IsOpenProperty, popupBinding);
            var minWidth = new Binding("ActualWidth");
            minWidth.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            var popupBd = new FrameworkElementFactory(typeof(Border));
            popupBd.SetValue(Border.BackgroundProperty, Brushes.White);
            popupBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            popupBd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            popupBd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            popupBd.SetValue(UIElement.EffectProperty, Shadow(Color.FromRgb(150, 165, 185), 14.0, 3.0, 0.5));
            popupBd.SetBinding(FrameworkElement.MinWidthProperty, minWidth);
            var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
            scroller.SetValue(FrameworkElement.MaxHeightProperty, 320.0);
            scroller.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            items.SetValue(FrameworkElement.MarginProperty, new Thickness(5.0));
            scroller.AppendChild(items);
            popupBd.AppendChild(scroller);
            popup.AppendChild(popupBd);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(bd);
            root.AppendChild(popup);
            var tpl = new ControlTemplate(typeof(ComboBox));
            tpl.VisualTree = root;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "bd"));
            tpl.Triggers.Add(focus);

            var ib = new FrameworkElementFactory(typeof(Border), "ib");
            ib.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ib.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
            ib.SetValue(Border.PaddingProperty, new Thickness(9.0, 7.0, 9.0, 7.0));
            ib.SetValue(FrameworkElement.MarginProperty, new Thickness(2.0, 1.0, 2.0, 1.0));
            var icp = new FrameworkElementFactory(typeof(ContentPresenter));
            icp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(icp);
            var itemTpl = new ControlTemplate(typeof(ComboBoxItem));
            itemTpl.VisualTree = ib;
            var itemOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            itemOver.Setters.Add(new Setter(Border.BackgroundProperty, SB(28, 46, 108, 168), "ib"));
            itemTpl.Triggers.Add(itemOver);
            var itemSel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            itemSel.Setters.Add(new Setter(Border.BackgroundProperty, SB(40, 46, 108, 168), "ib"));
            itemTpl.Triggers.Add(itemSel);
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTpl));

            var s = new Style(typeof(ComboBox));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32.0));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, itemStyle));
            return s;
        }

        internal static Style MakeCheckBoxStyle()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var box = new FrameworkElementFactory(typeof(Border), "box");
            box.SetValue(FrameworkElement.WidthProperty, 20.0);
            box.SetValue(FrameworkElement.HeightProperty, 20.0);
            box.SetValue(Border.BackgroundProperty, Brushes.White);
            box.SetValue(Border.BorderBrushProperty, SB(Color.FromRgb(200, 210, 222)));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5.0));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 1.0, 0.0, 0.0));
            var chk = new FrameworkElementFactory(typeof(Path), "chk");
            chk.SetValue(Path.DataProperty, Geo("M3.5,12.5 L8.5,17.5 L20.5,5.5"));
            chk.SetValue(Shape.StrokeProperty, Brushes.White);
            chk.SetValue(Shape.StrokeThicknessProperty, 2.4);
            chk.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            chk.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            chk.SetValue(Shape.StretchProperty, Stretch.Uniform);
            chk.SetValue(FrameworkElement.MarginProperty, new Thickness(3.5));
            chk.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            // Grid so the tick and the "some selected" dash can share the box.
            var marks = new FrameworkElementFactory(typeof(Grid));
            marks.AppendChild(chk);
            var dash = new FrameworkElementFactory(typeof(Border), "dash");
            dash.SetValue(FrameworkElement.WidthProperty, 9.0);
            dash.SetValue(FrameworkElement.HeightProperty, 2.4);
            dash.SetValue(Border.BackgroundProperty, Brushes.White);
            dash.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.2));
            dash.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dash.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dash.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            marks.AppendChild(dash);
            box.AppendChild(marks);
            panel.AppendChild(box);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 0.0, 0.0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(CheckBox));
            tpl.VisualTree = panel;
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, SB(CPrimary), "box"));
            on.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "box"));
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "chk"));
            tpl.Triggers.Add(on);
            var some = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = null };
            some.Setters.Add(new Setter(Border.BackgroundProperty, SB(CPrimary), "box"));
            some.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "box"));
            some.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "dash"));
            tpl.Triggers.Add(some);
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "box"));
            tpl.Triggers.Add(over);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            tpl.Triggers.Add(off);
            var s = new Style(typeof(CheckBox));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        internal static Style MakeProgressBarStyle(double height)
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, SB(Color.FromRgb(232, 237, 244)));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(height / 2));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(0.0));
            var track = new FrameworkElementFactory(typeof(Grid), "PART_Track");
            var ind = new FrameworkElementFactory(typeof(Border), "PART_Indicator");
            ind.SetValue(Border.BackgroundProperty, LG(CPrimary, CPrimaryDeep, new Point(0, 0), new Point(1, 0)));
            ind.SetValue(Border.CornerRadiusProperty, new CornerRadius(height / 2));
            ind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            track.AppendChild(ind);
            bd.AppendChild(track);
            var tpl = new ControlTemplate(typeof(ProgressBar));
            tpl.VisualTree = bd;
            var s = new Style(typeof(ProgressBar));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, height));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        // Filter tabs above the result list ("全部 12" / "筛掉 3" ...).
        internal static Style MakePillStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>" +
                "<Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='#606E80'/><Setter Property='FontSize' Value='12.5'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>" +
                "<Border x:Name='bd' Background='#EEF2F7' CornerRadius='13' Padding='12,4,12,4' Margin='0,0,6,0'>" +
                "<ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#E2EAF4'/></Trigger>" +
                "<Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='Background' Value='#222D3C'/><Setter Property='Foreground' Value='White'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        internal static Style MakeScrollBarStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='12'/><Setter Property='MinWidth' Value='12'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb MinHeight='36'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border x:Name='tb' Background='#C9D2DD' CornerRadius='3'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tb' Property='Background' Value='#97A5B7'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='tb' Property='Background' Value='#2E6CA8'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        internal static Style MakeListItemStyle()
        {
            var ib = new FrameworkElementFactory(typeof(Border), "ib");
            ib.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ib.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            ib.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            ib.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            ib.SetValue(Border.PaddingProperty, new Thickness(10.0, 7.0, 12.0, 7.0));
            ib.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 1.0, 4.0, 1.0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            tpl.VisualTree = ib;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, SB(Color.FromRgb(244, 247, 251)), "ib"));
            tpl.Triggers.Add(over);
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, SB(30, 46, 108, 168), "ib"));
            sel.Setters.Add(new Setter(Border.BorderBrushProperty, SB(110, 46, 108, 168), "ib"));
            tpl.Triggers.Add(sel);
            var s = new Style(typeof(ListBoxItem));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            s.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            return s;
        }

        // Title-bar button; the icon geometry lives in Tag so it can be swapped (maximize/restore).
        internal static Button MakeWinButton(string iconData, bool isClose)
        {
            var wbd = new FrameworkElementFactory(typeof(Border), "wbd");
            wbd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            wbd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8.0));
            var icon = new FrameworkElementFactory(typeof(Path), "wicon");
            icon.SetValue(Path.DataProperty, new TemplateBindingExtension(FrameworkElement.TagProperty));
            icon.SetValue(Shape.StrokeProperty, SB(CTextSub));
            icon.SetValue(Shape.StrokeThicknessProperty, 1.6);
            icon.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            icon.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            icon.SetValue(FrameworkElement.WidthProperty, 15.0);
            icon.SetValue(FrameworkElement.HeightProperty, 15.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(Shape.StretchProperty, Stretch.Uniform);
            wbd.AppendChild(icon);
            var tpl = new ControlTemplate(typeof(Button));
            tpl.VisualTree = wbd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            if (isClose)
            {
                over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CCloseHover), "wbd"));
                over.Setters.Add(new Setter(Shape.StrokeProperty, Brushes.White, "wicon"));
            }
            else
            {
                over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CWinHover), "wbd"));
            }
            tpl.Triggers.Add(over);
            return new Button
            {
                Width = 46.0,
                Height = 32.0,
                Template = tpl,
                Focusable = false,
                Cursor = System.Windows.Input.Cursors.Arrow,
                Tag = Geo(iconData)
            };
        }
    }
}
