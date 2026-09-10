using BedtimeGuard.Windows;
using System.ServiceProcess;

namespace BedtimeGuard.Service;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                ServiceBase.Run(new GuardService());
                return 0;
            }
            Paths.RequireAdministrator();
            Directory.CreateDirectory(Paths.Data);
            if (args.SequenceEqual(new[] { "--configure-recovery" }))
            {
                ServiceRecovery.Configure();
                return 0;
            }
            if (args.SequenceEqual(new[] { "--recover-expired" }))
            {
                new TaskManagerPolicy().Restore(onlyExpired: true);
                return 0;
            }
            if (args.SequenceEqual(new[] { "--repair" }))
            {
                // Marker precedes stopping: even an unsuccessful stop cannot re-apply restrictions.
                File.WriteAllText(Paths.Paused, DateTimeOffset.UtcNow.ToString("O"));
                using var controller = new ServiceController(Paths.ServiceName);
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
                new TaskManagerPolicy().Restore();
                Console.WriteLine("已暂停限制并恢复策略。重新启用请运行 Resume.ps1。");
                return 0;
            }
            Console.Error.WriteLine("Usage: BedtimeGuard.Service.exe [--repair | --recover-expired | --configure-recovery]");
            return 2;
        }
        catch (Exception error)
        {
            Paths.Log(error.ToString());
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
