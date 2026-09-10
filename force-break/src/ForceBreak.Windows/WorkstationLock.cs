using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ForceBreak.Windows;

public sealed class WorkstationLock
{
    private long lastAttempt;
    public bool IsLocked { get; set; }
    public void Reset() { IsLocked = false; lastAttempt = 0; }
    public void Enforce()
    {
        if (IsLocked || lastAttempt != 0 && Stopwatch.GetElapsedTime(lastAttempt) < TimeSpan.FromSeconds(2)) return;
        lastAttempt = Stopwatch.GetTimestamp();
        if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error(), "系统锁屏请求失败");
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LockWorkStation();
}
