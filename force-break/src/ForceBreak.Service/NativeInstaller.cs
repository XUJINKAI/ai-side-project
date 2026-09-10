using ForceBreak.Core;
using ForceBreak.Windows;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace ForceBreak.Service;

public enum MaintenanceAction { Install, Repair, Resume, Uninstall }

/// <summary>Shared by GUI and integration tests. No shell, PowerShell, sc.exe or schtasks.exe.</summary>
public static class NativeInstaller
{
    public const string RecoveryTask = "ForceBreak-PolicyRecovery";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ForceBreak";
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ForceBreak");
    public static string Shortcuts => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Force Break");

    public const string EmergencyPause = "--i-confirm-i-am-breaking-my-commitment-and-want-to-emergency-pause-force-break-until-i-manually-enable-it-again";
    public const string EmergencyUninstall = "--i-confirm-i-am-breaking-my-commitment-and-want-to-permanently-uninstall-force-break-and-delete-all-my-saved-plans";

    public static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + Paths.ServiceName);
        return key is not null;
    }

    public static bool IsInstalledExecutable(string executable)
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") is string directory &&
            string.Equals(Path.Combine(directory, "ForceBreak.exe"), executable, StringComparison.OrdinalIgnoreCase);
    }

    public static void Execute(MaintenanceAction action, string userSid, string installDirectory, string sourceExe, string? emergencyConfirmation = null, bool overwrite = false)
    {
        Paths.RequireAdministrator();
        // One machine-wide installation transaction; users cannot mutate this admin-owned mutex.
        using var transaction = new Mutex(false, @"Global\ForceBreak.Maintenance");
        bool acquired;
        try { acquired = transaction.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("另一个安装或维护操作正在执行，请稍后重试。");
        try
        {
            switch (action)
            {
                case MaintenanceAction.Install: Install(userSid, installDirectory, sourceExe, overwrite); break;
                case MaintenanceAction.Repair:
                    if (emergencyConfirmation != EmergencyPause) throw new InvalidOperationException("紧急暂停需要完整的管理员确认参数，见 README。");
                    Repair(); break;
                case MaintenanceAction.Resume: Resume(); break;
                case MaintenanceAction.Uninstall: Uninstall(emergencyConfirmation == EmergencyUninstall); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        finally { transaction.ReleaseMutex(); }
    }

    private static void Install(string sid, string directory, string source, bool overwrite)
    {
        var identity = new SecurityIdentifier(sid);
        _ = identity.Translate(typeof(NTAccount));
        directory = ValidateDirectory(directory);
        RejectReparseAncestors(Paths.Data);
        if (!File.Exists(source) || !string.Equals(Path.GetExtension(source), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用发布的单文件 EXE 安装。");
        if (HasExistingInstallation(directory))
        {
            if (!overwrite) throw new InvalidOperationException("发现已有安装或数据，需要确认覆盖安装。");
            Overwrite(sid, directory, source);
            return;
        }
        ScheduledRecovery.EnsureAbsent();
        bool serviceCreated = false, taskCreated = false, shortcutCreated = false, registrationCreated = false;
        try
        {
            CreateProtectedDirectory(directory, true);
            CreateProtectedDirectory(Paths.Data, false);
            var executable = Path.Combine(directory, "ForceBreak.exe");
            File.Copy(source, executable, false);
            JsonStorage.Write(Paths.Install, new Installation(sid, executable));
            JsonStorage.Write(Paths.State, new PlannerState());
            CreateService(executable);
            serviceCreated = true;
            ServiceRecovery.Configure();
            ScheduledRecovery.Register(executable);
            taskCreated = true;
            Directory.CreateDirectory(Shortcuts);
            shortcutCreated = true;
            CreateShortcut("Force Break.lnk", executable, "");

            using (var registration = Registry.LocalMachine.CreateSubKey(UninstallKey, true))
            {
                registrationCreated = true;
                registration.SetValue("DisplayName", "Force Break");
                registration.SetValue("DisplayVersion", "0.4.0");
                registration.SetValue("InstallLocation", directory);
                registration.SetValue("DisplayIcon", executable);
                registration.SetValue("UninstallString", $"\"{executable}\" --uninstall-ui");
                registration.SetValue("NoModify", 1, RegistryValueKind.DWord);
            }
            Start();
        }
        catch
        {
            // Roll back only resources created by this transaction. Keep recovery resources on failure.
            if (serviceCreated) { Stop(); new TaskManagerPolicy().Restore(); }
            if (File.Exists(Paths.Lease)) throw new InvalidOperationException("恢复记录尚未清除，已保留安装文件，请点击恢复后重试。");
            if (taskCreated) ScheduledRecovery.Remove();
            if (serviceCreated) DeleteService();
            if (shortcutCreated) Directory.Delete(Shortcuts, true);
            if (registrationCreated) Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (Directory.Exists(Paths.Data)) Directory.Delete(Paths.Data, true);
            throw;
        }
    }

    public static bool HasExistingInstallation(string directory)
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
        return IsInstalled() || Directory.Exists(directory) || Directory.Exists(Paths.Data) ||
            Directory.Exists(Shortcuts) || key is not null;
    }

    private static void Overwrite(string sid, string directory, string source)
    {
        var executable = Path.Combine(directory, "ForceBreak.exe");
        if (File.Exists(Paths.Install))
        {
            var old = JsonStorage.Read<Installation>(Paths.Install);
            if (!string.Equals(old.AppPath, executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"已有安装位于 {Path.GetDirectoryName(old.AppPath)}，请将安装位置改为该目录后覆盖。");
            sid = old.UserSid; // Never change the constrained account during an upgrade.
        }
        RejectReparseAncestors(executable);
        RejectReparseAncestors(Paths.State);
        RejectReparseAncestors(Paths.Install);
        RejectReparseAncestors(Shortcuts);
        // Validate before stopping a working installation. Existing valid state is never reset.
        if (File.Exists(Paths.State)) JsonStorage.Read<PlannerState>(Paths.State).Schedule.Validate();
        else if (IsInstalled() || File.Exists(Paths.Lease))
            throw new InvalidOperationException("现有状态文件缺失，不能自动清空承诺。请先通过紧急管理命令处理。");
        var paused = File.Exists(Paths.Paused);
        Stop();
        try
        {
            CreateProtectedDirectory(Paths.Data, false);
            File.WriteAllText(Paths.Paused, DateTimeOffset.UtcNow.ToString("O"));
            new TaskManagerPolicy().Restore();
            var state = File.Exists(Paths.State) ? JsonStorage.Read<PlannerState>(Paths.State) : new PlannerState();
            if (state.Version != 1) throw new InvalidDataException("Unknown state version.");
            state.Schedule.Validate();
            // Keep the original JSON as an administrator-only recovery copy.
            if (File.Exists(Paths.State)) File.Copy(Paths.State, Path.Combine(Paths.Data, "state.before-install.json"), true);
            CreateProtectedDirectory(directory, true);
            ScheduledRecovery.Remove();
            StopUserProcesses(executable);
            File.Copy(source, executable, true);
            JsonStorage.Write(Paths.State, state);
            JsonStorage.Write(Paths.Install, new Installation(sid, executable));
            DeleteService();
            CreateService(executable);
            ServiceRecovery.Configure();
            ScheduledRecovery.Register(executable);
            Directory.CreateDirectory(Shortcuts);
            CreateShortcut("Force Break.lnk", executable, "");
            using var registration = Registry.LocalMachine.CreateSubKey(UninstallKey, true);
            registration.SetValue("DisplayName", "Force Break");
            registration.SetValue("DisplayVersion", "0.4.0");
            registration.SetValue("InstallLocation", directory);
            registration.SetValue("DisplayIcon", executable);
            registration.SetValue("UninstallString", $"\"{executable}\" --uninstall-ui");
            registration.SetValue("NoModify", 1, RegistryValueKind.DWord);
            if (!paused) { File.Delete(Paths.Paused); Start(); }
        }
        catch
        {
            // Retain data and recovery records for a retry; never roll back by deleting user state.
            File.WriteAllText(Paths.Paused, DateTimeOffset.UtcNow.ToString("O"));
            throw;
        }
    }

    private static void Repair()
    {
        if (!Directory.Exists(Paths.Data)) throw new InvalidOperationException("未找到安装数据。");
        File.WriteAllText(Paths.Paused, DateTimeOffset.UtcNow.ToString("O"));
        Stop();
        new TaskManagerPolicy().Restore();
    }

    private static void Resume()
    {
        _ = Paths.ReadInstallation();
        File.Delete(Paths.Paused);
        Start();
    }

    private static void Uninstall(bool emergency)
    {
        var installation = JsonStorage.Read<Installation>(Paths.Install);
        var parent = Path.GetDirectoryName(installation.AppPath)!;
        var directory = ValidateDirectory(parent);
        if (!string.Equals(Path.GetFileName(installation.AppPath), "ForceBreak.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装路径不符合预期，未删除文件。");
        if (Environment.ProcessPath?.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("卸载应从临时维护副本运行。");
        if (!emergency) CheckUninstallCommitment();
        Repair();
        if (File.Exists(Paths.Lease)) throw new InvalidOperationException("策略恢复尚未完成，请让目标用户登录后重试。");
        ScheduledRecovery.Remove();
        StopUserProcesses(installation.AppPath);
        // Delete files before removing service registration so a file-in-use failure is retryable.
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        DeleteService();
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
        if (Directory.Exists(Shortcuts)) Directory.Delete(Shortcuts, true);
        Directory.Delete(Paths.Data, true);
    }

    private static void CheckUninstallCommitment()
    {
        // Stop and flush the authority before checking: UI state and disk checkpoints may be stale.
        var running = false;
        if (IsInstalled())
        {
            using var service = new ServiceController(Paths.ServiceName);
            running = service.Status != ServiceControllerStatus.Stopped;
        }
        Stop();
        try
        {
            var state = JsonStorage.Read<PlannerState>(Paths.State);
            if (state.Version != 1) throw new InvalidDataException("Unknown state version.");
            var now = DateTimeOffset.UtcNow;
            var planner = new Planner(state, () => System.Security.Cryptography.RandomNumberGenerator.GetInt32(1_000_000));
            var night = planner.Tick(now);
            var rest = BreakPlanner.Tick(state.Break, state.Schedule.Breaks, now, TimeSpan.Zero,
                night.Phase == Phase.Restricted, state.Schedule.DisableTaskManager);
            // Do not rewrite migrated state before restarting a possibly older service.
            if (BreakPlanner.Combine(night, rest).Phase is Phase.Committed or Phase.Reminder or Phase.Restricted)
                throw new InvalidOperationException("已进入承诺期，当前安排结束前不能卸载。");
        }
        catch
        {
            // A rejected uninstall must not become a way to pause the service.
            if (running && !File.Exists(Paths.Paused)) Start();
            throw;
        }
    }

    private static void StopUserProcesses(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        if (!process.WaitForExit(10000)) throw new IOException("界面进程未退出，请稍后重试卸载。");
                    }
                }
                catch (InvalidOperationException) { /* Already exited. */ }
            }
        }
    }

    private static void Start()
    {
        using var service = new ServiceController(Paths.ServiceName);
        if (service.Status == ServiceControllerStatus.Running) return;
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    }

    private static void Stop()
    {
        if (!IsInstalled()) return;
        using var service = new ServiceController(Paths.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }

    public static string ValidateDirectory(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var programFiles = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (!string.Equals(Path.GetDirectoryName(full), programFiles, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("安装位置必须是 Program Files 下的直属新文件夹。");
        RejectReparseAncestors(full);
        return full;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("安装路径不能经过符号链接或目录联接。");
    }

    private static void CreateProtectedDirectory(string path, bool userRead)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (userRead) security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        directory.Create(security);
        directory.SetAccessControl(security);
    }

    private static void CreateShortcut(string name, string executable, string arguments)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(Path.Combine(Shortcuts, name));
            try { shortcut.TargetPath = executable; shortcut.Arguments = arguments; shortcut.Save(); }
            finally { Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static void CreateService(string executable)
    {
        var manager = OpenSCManager(null, null, 2);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = CreateServiceNative(manager, Paths.ServiceName, "Force Break", 0x12,
                0x10, 2, 1, $"\"{executable}\" --service", null, IntPtr.Zero, null, null, null);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseServiceHandle(service);
        }
        finally { CloseServiceHandle(manager); }
    }

    private static void DeleteService()
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, Paths.ServiceName, 0x10000);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1060) return;
                throw new Win32Exception(error);
            }
            try { if (!DeleteServiceNative(service)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateServiceNative(IntPtr manager, string name, string display, uint access, uint type, uint start, uint error,
        string binary, string? group, IntPtr tag, string? dependencies, string? user, string? password);
    [DllImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteServiceNative(IntPtr service);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
}
