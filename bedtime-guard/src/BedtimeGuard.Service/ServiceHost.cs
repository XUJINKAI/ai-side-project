using BedtimeGuard.Windows;
using System.ServiceProcess;

namespace BedtimeGuard.Service;

public static class ServiceHost
{
    public static int Run(bool recovery)
    {
        try
        {
            if (recovery)
            {
                Paths.RequireAdministrator();
                if (Directory.Exists(Paths.Data)) new TaskManagerPolicy().Restore(onlyExpired: true);
            }
            else ServiceBase.Run(new GuardService());
            return 0;
        }
        catch (Exception error) { Paths.Log(error.ToString()); return 1; }
    }
}
