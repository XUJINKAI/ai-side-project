using System.ComponentModel;
using System.Runtime.InteropServices;
using BedtimeGuard.Windows;

namespace BedtimeGuard.Service;

internal static class ServiceRecovery
{
    public static void Configure()
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, Paths.ServiceName, 2);
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var size = Marshal.SizeOf<ActionEntry>();
                var actions = Marshal.AllocHGlobal(size * 3);
                try
                {
                    Marshal.StructureToPtr(new ActionEntry { Type = 1, Delay = 5000 }, actions, false);
                    Marshal.StructureToPtr(new ActionEntry { Type = 1, Delay = 15000 }, actions + size, false);
                    Marshal.StructureToPtr(new ActionEntry { Type = 0, Delay = 0 }, actions + size * 2, false);
                    var config = new FailureActions { ResetPeriod = 86400, Count = 3, Actions = actions };
                    if (!ChangeServiceConfig2(service, 2, ref config)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { Marshal.FreeHGlobal(actions); }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ActionEntry { public int Type; public uint Delay; }
    [StructLayout(LayoutKind.Sequential)] private struct FailureActions
    { public uint ResetPeriod; public IntPtr RebootMessage, Command; public uint Count; public IntPtr Actions; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, int level, ref FailureActions actions);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
}
