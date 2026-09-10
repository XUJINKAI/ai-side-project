using BedtimeGuard.Core;
using BedtimeGuard.Service;
using BedtimeGuard.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;

// Run elevated on an expendable Windows host; the real locking policy stays disabled.
if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
Paths.RequireAdministrator();
var executable = Path.GetFullPath(args[0]);
var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BedtimeGuard-SmokeTest");
var sid = WindowsIdentity.GetCurrent().User!.Value;
var installed = false;
Process? ui = null;
try
{
    Check(File.Exists(executable), "published executable exists");
    Check(Directory.GetFiles(Path.GetDirectoryName(executable)!).Length == 1, "distribution contains exactly one EXE");
    ui = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })!;
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (!ui.HasExited && ui.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
    { await Task.Delay(200); ui.Refresh(); }
    Check(!ui.HasExited && ui.MainWindowHandle != IntPtr.Zero, "first launch opens a GUI without installation or scripts");
    ui.Kill(); await ui.WaitForExitAsync(); ui.Dispose(); ui = null;

    NativeInstaller.Execute(MaintenanceAction.Install, sid, directory, executable);
    installed = true;
    Check(Directory.GetFiles(directory).Select(Path.GetFileName).SequenceEqual(new[] { "BedtimeGuard.exe" }), "installed application is also a single EXE");
    var reply = await Wire.Send(new Request("status"));
    Check(reply.Ok && reply.Status?.Phase == Phase.Disabled, "same EXE runs as real service with authenticated IPC");
    using (var registration = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BedtimeGuard"))
        Check(registration?.GetValue("UninstallString")?.ToString()?.Contains("--uninstall-ui") == true, "Windows uninstall entry opens the GUI");
    var duplicateRejected = false;
    try { NativeInstaller.Execute(MaintenanceAction.Install, sid, directory, executable); }
    catch (InvalidOperationException) { duplicateRejected = true; }
    Check(duplicateRejected && File.Exists(Path.Combine(directory, "BedtimeGuard.exe")), "existing installation is not overwritten");
    NativeInstaller.Execute(MaintenanceAction.Repair, sid, directory, executable);
    Check(File.Exists(Paths.Paused), "native repair persists pause marker");
    var state = JsonStorage.Read<PlannerState>(Paths.State);
    state.Schedule = state.Schedule with { Breaks = new BreakOptions { Enabled = true, WorkHours = 0.1, RestMinutes = 1, ReminderMinutes = 5, CommitmentMinutes = 5 } };
    state.Break.WorkSeconds = 60; // Exactly at commitment; never enter the lock period in this test.
    JsonStorage.Write(Paths.State, state);
    NativeInstaller.Execute(MaintenanceAction.Resume, sid, directory, executable);
    reply = await Wire.Send(new Request("status"));
    Check(reply.Ok && !File.Exists(Paths.Paused), "native resume restarts the service");
    Check(reply.Status?.Break?.Phase == Phase.Reminder, "service freezes periodic break at its commitment boundary");
    var frozenRelease = reply.Status!.Break!.ReleaseAt;
    reply = await Wire.Send(new Request("save", state.Schedule with { Breaks = new BreakOptions() }));
    Check(reply.Ok && reply.Status?.Break?.Phase == Phase.Reminder && reply.Status.Break.ReleaseAt == frozenRelease,
        "real IPC cannot cancel or postpone an already committed break");
    using (var recover = Process.Start(new ProcessStartInfo(Path.Combine(directory, "BedtimeGuard.exe"))
           { UseShellExecute = false, ArgumentList = { "--recover-expired" } })!)
    {
        await recover.WaitForExitAsync();
        Check(recover.ExitCode == 0, "same EXE supports independent recovery task");
    }
    NativeInstaller.Execute(MaintenanceAction.Uninstall, sid, directory, executable);
    installed = false;
    Check(!Directory.Exists(directory) && !Directory.Exists(Paths.Data) && !Directory.Exists(NativeInstaller.Shortcuts), "native uninstall removes application, state and shortcuts");
    Check(!NativeInstaller.IsInstalled(), "native uninstall removes service registration");
    Console.WriteLine("PASS single EXE GUI launch, native install, IPC, recovery, resume and uninstall");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally
{
    if (ui is not null) { if (!ui.HasExited) ui.Kill(); ui.Dispose(); }
    if (installed) NativeInstaller.Execute(MaintenanceAction.Uninstall, sid, directory, executable);
}

static void Check(bool condition, string name)
{ if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
