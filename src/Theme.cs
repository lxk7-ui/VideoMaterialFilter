using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SucaiShaixuan
{
    // Shared palette, icons and brush helpers for the main window and the folder picker.
    internal static class Theme
    {
        public static readonly Color CPrimary = Color.FromRgb(46, 108, 168);
        public static readonly Color CPrimaryDeep = Color.FromRgb(31, 78, 121);
        public static readonly Color CAccent = Color.FromRgb(255, 138, 31);
        public static readonly Color CAccentLight = Color.FromRgb(255, 160, 70);
        public static readonly Color CAccentDeep = Color.FromRgb(232, 110, 14);
        public static readonly Color CTextMain = Color.FromRgb(34, 45, 60);
        public static readonly Color CTextSub = Color.FromRgb(96, 110, 128);
        public static readonly Color CTextHint = Color.FromRgb(146, 159, 176);
        public static readonly Color CBorder = Color.FromRgb(229, 235, 243);
        public static readonly Color CInputBg = Color.FromRgb(244, 247, 251);
        public static readonly Color CPageBg = Color.FromRgb(238, 242, 247);
        public static readonly Color CSuccess = Color.FromRgb(53, 179, 126);
        public static readonly Color CRunning = Color.FromRgb(255, 170, 60);
        public static readonly Color CWinHover = Color.FromRgb(232, 237, 244);
        public static readonly Color CCloseHover = Color.FromRgb(232, 72, 72);
        // Result colours: kept / rejected / error / put back / passed before (历史通过, skipped).
        public static readonly Color CKeep = Color.FromRgb(38, 160, 108);
        public static readonly Color CReject = Color.FromRgb(226, 84, 58);
        public static readonly Color CWarn = Color.FromRgb(222, 140, 20);
        public static readonly Color CDanger = Color.FromRgb(214, 64, 50);
        public static readonly Color CMemo = Color.FromRgb(20, 140, 160);

        public const string IconMin = "M5,12 H19";
        public const string IconMax = "M5,5 H19 V19 H5 Z";
        public const string IconClose = "M7,7 L17,17 M17,7 L7,17";
        public const string IconRestore = "M7,5 H14 V12 H7 Z M10,10 H19 V18 H10 Z";
        public const string IconPlay = "M8,5 V19 L19,12 Z";
        public const string IconFolder = "M10,4 H4 C2.9,4 2,4.9 2,6 V18 C2,19.1 2.9,20 4,20 H20 C21.1,20 22,19.1 22,18 V8 C22,6.9 21.1,6 20,6 H12 L10,4 Z";
        public const string IconVideo = "M17,10.5 V7 C17,5.9 16.1,5 15,5 H5 C3.9,5 3,5.9 3,7 V17 C3,18.1 3.9,19 5,19 H15 C16.1,19 17,18.1 17,17 V13.5 L21,17 V7 L17,10.5 Z";
        public const string IconBolt = "M13,2 L4,14 H11 L10,22 L20,9 H13 L13,2 Z";
        public const string IconRefresh = "M17.65,6.35 C16.2,4.9 14.21,4 12,4 C7.58,4 4.01,7.58 4.01,12 C4.01,16.42 7.58,20 12,20 C15.73,20 18.84,17.45 19.73,14 H17.65 C16.83,16.33 14.61,18 12,18 C8.69,18 6.01,15.31 6.01,12 C6.01,8.69 8.69,6 12,6 C13.66,6 15.14,6.69 16.22,7.78 L13,11 H20 V4 L17.65,6.35 Z";
        public const string IconClock = "M12,2 C6.5,2 2,6.5 2,12 C2,17.5 6.5,22 12,22 C17.5,22 22,17.5 22,12 C22,6.5 17.5,2 12,2 Z M12.5,7 H11 V13 L16.2,16.1 L17,14.8 L12.5,12 Z";
        public const string IconAspect = "M7,14 L5,14 L5,19 L10,19 L10,17 L7,17 Z M17,14 L19,14 L19,19 L14,19 L14,17 L17,17 Z M19,5 L14,5 L14,7 L17,7 L17,10 L19,10 Z M7,7 L10,7 L10,5 L5,5 L5,10 L7,10 Z";
        public const string IconLayers = "M12,2 L2,7 L12,12 L22,7 L12,2 Z M2,17 L12,22 L22,17 M2,12 L12,17 L22,12";
        public const string IconTag = "M21.4,11.6 L12.4,2.6 C12,2.2 11.5,2 11,2 H4 C2.9,2 2,2.9 2,4 V11 C2,11.5 2.2,12 2.6,12.4 L11.6,21.4 C12,21.8 12.5,22 13,22 C13.5,22 14,21.8 14.4,21.4 L21.4,14.4 C21.8,14 22,13.5 22,13 C22,12.5 21.8,12 21.4,11.6 Z M6.5,8 C5.7,8 5,7.3 5,6.5 C5,5.7 5.7,5 6.5,5 C7.3,5 8,5.7 8,6.5 C8,7.3 7.3,8 6.5,8 Z";
        public const string IconChevronUp = "M7.4,14 L12,9.4 L16.6,14 L18,12.6 L12,6.6 L6,12.6 Z";
        public const string IconChevronDown = "M7.4,10 L12,14.6 L16.6,10 L18,11.4 L12,17.4 L6,11.4 Z";
        public const string IconTerminal = "M20,4 H4 C2.9,4 2,4.9 2,6 V18 C2,19.1 2.9,20 4,20 H20 C21.1,20 22,19.1 22,18 V6 C22,4.9 21.1,4 20,4 Z M4,18 V8 H20 V18 H4 Z M6,10 L8.5,12.5 L6,15 L7.4,16.4 L11.3,12.5 L7.4,8.6 Z";
        // Picker-only icons.
        public const string IconDrive = "M4,6 H20 C21.1,6 22,6.9 22,8 V16 C22,17.1 21.1,18 20,18 H4 C2.9,18 2,17.1 2,16 V8 C2,6.9 2.9,6 4,6 Z M17,11 A1.5,1.5 0 1 0 17,14 A1.5,1.5 0 1 0 17,11 Z";
        public const string IconFile = "M6,2 H14 L20,8 V20 C20,21.1 19.1,22 18,22 H6 C4.9,22 4,21.1 4,20 V4 C4,2.9 4.9,2 6,2 Z M13,3.5 V9 H18.5 Z";
        public const string IconSearch = "M15.5,14 H14.71 L14.43,13.73 C15.41,12.59 16,11.11 16,9.5 C16,5.91 13.09,3 9.5,3 C5.91,3 3,5.91 3,9.5 C3,13.09 5.91,16 9.5,16 C11.11,16 12.59,15.41 13.73,14.43 L14,14.71 V15.5 L19,20.49 L20.49,19 L15.5,14 Z M9.5,14 C7.01,14 5,11.99 5,9.5 C5,7.01 7.01,5 9.5,5 C11.99,5 14,7.01 14,9.5 C14,11.99 11.99,14 9.5,14 Z";
        public const string IconArrowUp = "M4,12 L5.41,13.41 L11,7.83 V20 H13 V7.83 L18.58,13.42 L20,12 L12,4 Z";
        // 素材筛选器 icons.
        public const string IconFilter = "M3,4 H21 L14,12.5 V19 L10,21 V12.5 Z";
        public const string IconStop = "M7,7 H17 V17 H7 Z";
        public const string IconUndo = "M12.5,8 C9.85,8 7.45,9 5.6,10.6 L2,7 V16 H11 L7.38,12.38 C8.77,11.22 10.54,10.5 12.5,10.5 C16.04,10.5 19.05,12.81 20.1,16 L22.47,15.22 C21.08,11.03 17.15,8 12.5,8 Z";
        public const string IconTune = "M3,17 V19 H9 V17 Z M3,5 V7 H13 V5 Z M13,21 V19 H21 V17 H13 V15 H11 V21 Z M7,9 V11 H3 V13 H7 V15 H9 V9 Z M21,13 V11 H11 V13 Z M15,9 H17 V7 H21 V5 H17 V3 H15 Z";
        public const string IconCheck = "M9,16.2 L4.8,12 L3.4,13.4 L9,19 L21,7 L19.6,5.6 Z";
        public const string IconDownload = "M5,20 H19 V18 H5 Z M19,9 H15 V3 H9 V9 H5 L12,16 Z";
        public const string IconEye = "M12,4.5 C7,4.5 2.73,7.61 1,12 C2.73,16.39 7,19.5 12,19.5 C17,19.5 21.27,16.39 23,12 C21.27,7.61 17,4.5 12,4.5 Z M12,17 C9.24,17 7,14.76 7,12 C7,9.24 9.24,7 12,7 C14.76,7 17,9.24 17,12 C17,14.76 14.76,17 12,17 Z M12,9 C10.34,9 9,10.34 9,12 C9,13.66 10.34,15 12,15 C13.66,15 15,13.66 15,12 C15,10.34 13.66,9 12,9 Z";
        public const string IconComputer ="M20,18 C21.1,18 21.99,17.1 21.99,16 L22,6 C22,4.9 21.1,4 20,4 H4 C2.9,4 2,4.9 2,6 V16 C2,17.1 2.9,18 4,18 H0 V20 H24 V18 H20 Z M4,6 H20 V16 H4 Z";

        public static SolidColorBrush SB(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush SB(byte a, byte r, byte g, byte b)
        {
            return SB(Color.FromArgb(a, r, g, b));
        }

        public static LinearGradientBrush LG(Color a, Color b, Point s, Point e)
        {
            var g = new LinearGradientBrush();
            g.StartPoint = s;
            g.EndPoint = e;
            g.GradientStops.Add(new GradientStop(a, 0.0));
            g.GradientStops.Add(new GradientStop(b, 1.0));
            g.Freeze();
            return g;
        }

        public static Geometry Geo(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        public static Path MakeIcon(string data, Brush fill, double size)
        {
            var p = new Path();
            p.Data = Geo(data);
            p.Fill = fill;
            p.Width = size;
            p.Height = size;
            p.Stretch = Stretch.Uniform;
            p.VerticalAlignment = VerticalAlignment.Center;
            p.HorizontalAlignment = HorizontalAlignment.Center;
            return p;
        }

        public static DropShadowEffect Shadow(Color c, double blur, double depth, double opacity)
        {
            var e = new DropShadowEffect();
            e.Color = c;
            e.BlurRadius = blur;
            e.ShadowDepth = depth;
            e.Opacity = opacity;
            return e;
        }
    }
}
