using System.Runtime.InteropServices;

namespace BedtimeGuard.Service;

internal static class ScheduledRecovery
{
    private static dynamic Connect()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        scheduler.Connect();
        return scheduler;
    }

    public static void EnsureAbsent()
    {
        dynamic scheduler = Connect();
        try
        {
            dynamic folder = scheduler.GetFolder(@"\");
            try
            {
                try
                {
                    dynamic existing = folder.GetTask(NativeInstaller.RecoveryTask);
                    Marshal.FinalReleaseComObject(existing);
                }
                catch (Exception e) when ((uint)e.HResult == 0x80070002) { return; }
                throw new InvalidOperationException("恢复任务已经存在，未覆盖。");
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }

    public static void Register(string executable)
    {
        dynamic scheduler = Connect();
        try
        {
            dynamic definition = scheduler.NewTask(0);
            definition.RegistrationInfo.Description = "Restore Bedtime Guard's temporary Task Manager policy after expiry or service failure.";
            definition.Principal.UserId = "SYSTEM";
            definition.Principal.LogonType = 5;
            definition.Principal.RunLevel = 1;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT1M";
            definition.Settings.MultipleInstances = 2;
            dynamic timer = definition.Triggers.Create(1);
            timer.StartBoundary = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss");
            timer.Repetition.Interval = "PT1M";
            definition.Triggers.Create(8);
            definition.Triggers.Create(9);
            dynamic action = definition.Actions.Create(0);
            action.Path = executable;
            action.Arguments = "--recover-expired";
            dynamic folder = scheduler.GetFolder(@"\");
            try
            {
                dynamic registered = folder.RegisterTaskDefinition(NativeInstaller.RecoveryTask, definition, 2, "SYSTEM", null, 5);
                Marshal.FinalReleaseComObject(registered);
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }

    public static void Remove()
    {
        dynamic scheduler = Connect();
        try
        {
            dynamic folder = scheduler.GetFolder(@"\");
            try
            {
                dynamic task;
                try { task = folder.GetTask(NativeInstaller.RecoveryTask); }
                catch (Exception e) when ((uint)e.HResult == 0x80070002) { return; }
                try
                {
                    try { task.Stop(0); } catch (Exception e) when ((uint)e.HResult == 0x8004130B) { }
                }
                finally { Marshal.FinalReleaseComObject(task); }
                folder.DeleteTask(NativeInstaller.RecoveryTask, 0);
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(scheduler); }
    }
}
