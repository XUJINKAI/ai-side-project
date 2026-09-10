using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace BedtimeGuard.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\BedtimeGuard.App", out var created);
        if (!created)
        {
            MessageBox.Show("Bedtime Guard 已在运行，请双击任务栏托盘图标打开设置。", "Bedtime Guard");
            return;
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var controller = new TrayController();
        if (!args.Contains("--background")) controller.ShowSettings();
        application.Run();
        single.ReleaseMutex();
    }
}
