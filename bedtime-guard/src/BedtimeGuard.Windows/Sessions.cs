using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BedtimeGuard.Windows;

public sealed class Sessions(Installation installation)
{
    private readonly Dictionary<int, Queue<DateTimeOffset>> attempts = [];

    public void EnsureApp()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (var i = 0; i < count; i++)
            {
                var s = Marshal.PtrToStructure<WtsSession>(list + i * Marshal.SizeOf<WtsSession>());
                if (s.Id == 0 || s.State is not (0 or 4) || !WTSQueryUserToken(s.Id, out var token)) continue;
                try
                {
                    using var identity = new WindowsIdentity(token);
                    if (identity.User?.Value != installation.UserSid || IsRunning(s.Id)) continue;
                    if (!attempts.TryGetValue(s.Id, out var history)) attempts[s.Id] = history = new();
                    var now = DateTimeOffset.UtcNow;
                    while (history.TryPeek(out var old) && now - old > TimeSpan.FromMinutes(5)) history.Dequeue();
                    if (history.Count >= 3) continue;
                    history.Enqueue(now);
                    Start(token);
                    Paths.Log($"Started user agent in session {s.Id}.");
                }
                finally { CloseHandle(token); }
            }
        }
        finally { WTSFreeMemory(list); }
    }

    private bool IsRunning(int session)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(installation.AppPath)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == session && string.Equals(process.MainModule?.FileName,
                        installation.AppPath, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return false;
    }

    private void Start(IntPtr token)
    {
        if (!CreateEnvironmentBlock(out var environment, token, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
            var command = new StringBuilder($"\"{installation.AppPath}\" --background");
            if (!CreateProcessAsUser(token, installation.AppPath, command, IntPtr.Zero, IntPtr.Zero, false,
                0x400, environment, Path.GetDirectoryName(installation.AppPath)!, ref startup, out var process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(process.Thread);
            CloseHandle(process.Process);
        }
        finally { DestroyEnvironmentBlock(environment); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct WtsSession { public int Id; public IntPtr Name; public int State; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved; public string? Desktop; public string? Title;
        public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public short Show, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(int session, out IntPtr token);
    [DllImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);
    [DllImport("userenv.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, int flags,
        IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
