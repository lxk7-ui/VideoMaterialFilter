using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("素材筛选器")]
[assembly: AssemblyProduct("素材筛选器")]
[assembly: AssemblyDescription("自动筛掉带抖音号、写了价格、说了价格的视频素材")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]

namespace SucaiShaixuan
{
    public class App
    {
        private static Mutex _single;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [STAThread]
        public static void Main()
        {
            // Two windows would move the same files at the same time; bring the open one forward instead.
            bool created;
            _single = new Mutex(true, "SucaiShaixuan.SingleInstance", out created);
            if (!created)
            {
                IntPtr h = FindWindow(null, AppInfo.Name + " " + AppInfo.Version);
                if (h != IntPtr.Zero)
                {
                    ShowWindow(h, 9);
                    SetForegroundWindow(h);
                }
                else
                {
                    MessageBox.Show("素材筛选器已经开着了，在任务栏里找一下它的窗口。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Paths.AppendLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  界面出错：" + e.Exception);
                MessageBox.Show("出错了：" + e.Exception.Message + "\n\n详细信息已写进「运行日志.txt」。", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            app.Run(new MainWindow());
            GC.KeepAlive(_single);
        }
    }
}
