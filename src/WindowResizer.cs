using System;
using System.Windows;
using System.Windows.Interop;

namespace SucaiShaixuan
{
    // Borderless (WindowStyle=None + AllowsTransparency) windows have no native resize frame.
    // Answering WM_NCHITTEST with edge codes lets Windows run its normal sizing loop. The window
    // needs ResizeMode=CanResize and a non-fully-transparent background in the edge area.
    internal static class WindowResizer
    {
        private const int WM_NCHITTEST = 0x0084;

        public static void Enable(Window w, double grip)
        {
            w.SourceInitialized += (s, e) =>
            {
                var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
                if (src == null) return;
                src.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg != WM_NCHITTEST || w.WindowState != WindowState.Normal) return IntPtr.Zero;
                    long lp = lParam.ToInt64();
                    int x = unchecked((short)(lp & 0xFFFF));
                    int y = unchecked((short)((lp >> 16) & 0xFFFF));
                    Point p;
                    try { p = w.PointFromScreen(new Point(x, y)); } catch { return IntPtr.Zero; }
                    double wd = w.ActualWidth;
                    double ht = w.ActualHeight;
                    bool l = p.X < grip, r = p.X >= wd - grip, t = p.Y < grip, b = p.Y >= ht - grip;
                    int code = 0;
                    if (t && l) code = 13;
                    else if (t && r) code = 14;
                    else if (b && l) code = 16;
                    else if (b && r) code = 17;
                    else if (l) code = 10;
                    else if (r) code = 11;
                    else if (t) code = 12;
                    else if (b) code = 15;
                    if (code == 0) return IntPtr.Zero;
                    handled = true;
                    return new IntPtr(code);
                });
            };
        }
    }
}
