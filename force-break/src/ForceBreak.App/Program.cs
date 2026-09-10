using System;
using System.Linq;
using System.Threading;
using System.Windows;
using ForceBreak.Service;

namespace ForceBreak.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--service" })) return ServiceHost.Run(false);
        if (args.SequenceEqual(new[] { "--recover-expired" })) return ServiceHost.Run(true);
        if (args.Length is 5 or 6 && args[0] == "--maintenance" && args[4] == "--temporary-helper"
            && Enum.TryParse<MaintenanceAction>(args[1], out var action) && Enum.IsDefined(action))
        {
            var maintenanceApp = new Application();
            try { return maintenanceApp.Run(new MaintenanceWindow(action, args[2], args[3], args.Length == 6 ? args[5] : null)); }
            finally { MaintenanceLauncher.CleanupTemporaryHelper(); }
        }
        if (args.Length == 1 && args[0] is NativeInstaller.EmergencyPause or NativeInstaller.EmergencyUninstall)
        {
            var emergencyApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            emergencyApp.Startup += async (_, _) =>
            {
                await MaintenanceLauncher.Run(args[0] == NativeInstaller.EmergencyPause ? MaintenanceAction.Repair : MaintenanceAction.Uninstall,
                    NativeInstaller.DefaultDirectory, args[0]);
                emergencyApp.Shutdown();
            };
            return emergencyApp.Run();
        }
        if (args.Length > 0 && args[0] is "-h" or "-break" or "-break-tomorrow") return CommandLine.Run(args);
        if (args.Length != 0 && !(args.Length == 1 && args[0] is "--background" or "--uninstall-ui"))
        { MessageBox.Show("不支持此参数。紧急管理命令请查阅 README。", "Force Break"); return 2; }
        var background = args.Contains("--background");
        if (args.Length == 0 && NativeInstaller.IsInstalledExecutable(Environment.ProcessPath!) && EventWaitHandle.TryOpenExisting(@"Local\ForceBreak.ShowSettings", out var existing))
        { using (existing) existing.Set(); return 0; }
        var agent = background || NativeInstaller.IsInstalledExecutable(Environment.ProcessPath!);
        var created = true;
        using var single = agent ? new Mutex(true, @"Local\ForceBreak.Agent", out created) : null;
        if (agent && !created && !args.Contains("--uninstall-ui")) return 0;
        // Recovery and uninstall entry points must still work while the agent is alive.
        if (!created) agent = false;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var controller = new TrayController(agent);
        using var show = agent ? new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ForceBreak.ShowSettings") : null;
        var waiter = show is null ? null : ThreadPool.RegisterWaitForSingleObject(show,
            (_, _) => application.Dispatcher.BeginInvoke(controller.ShowSettings), null, Timeout.Infinite, false);
        if (!background) controller.ShowSettings();
        if (args.Contains("--uninstall-ui"))
            application.Startup += async (_, _) => await MaintenanceLauncher.Run(
                MaintenanceAction.Uninstall, NativeInstaller.DefaultDirectory);
        var exit = application.Run();
        waiter?.Unregister(null);
        if (created) single?.ReleaseMutex();
        return exit;
    }
}
