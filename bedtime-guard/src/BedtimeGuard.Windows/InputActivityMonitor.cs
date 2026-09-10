using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BedtimeGuard.Core;

namespace BedtimeGuard.Windows;

/// <summary>Session-local low-level hooks. Stores timestamps only, never input content.</summary>
public sealed class InputActivityMonitor : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly HookProc keyboardCallback;
    private readonly HookProc mouseCallback;
    private long lastInput;
    private long lastPump;
    private volatile bool installed;
    private volatile uint threadId;
    private int disposed;

    public InputActivityMonitor()
    {
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        thread = new Thread(Run) { IsBackground = true, Name = "Force Break input hooks" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5))) { Dispose(); throw new TimeoutException("输入检测启动超时。"); }
    }

    public ActivityReport Snapshot()
    {
        var stamp = Interlocked.Read(ref lastInput);
        var pump = Interlocked.Read(ref lastPump);
        return new(installed && pump != 0 && Stopwatch.GetElapsedTime(pump) < TimeSpan.FromSeconds(5),
            stamp == 0 ? TimeSpan.FromDays(365).TotalSeconds : Stopwatch.GetElapsedTime(stamp).TotalSeconds);
    }

    private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        // KBDLLHOOKSTRUCT.flags: ignore injected events, but always pass all input through.
        if (code == 0 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
            Interlocked.Exchange(ref lastInput, Stopwatch.GetTimestamp());
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    private IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        // MSLLHOOKSTRUCT.flags follows POINT and mouseData.
        if (code == 0 && (Marshal.ReadInt32(data, 12) & 1) == 0)
            Interlocked.Exchange(ref lastInput, Stopwatch.GetTimestamp());
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void Run()
    {
        IntPtr keyboard = IntPtr.Zero, mouse = IntPtr.Zero;
        UIntPtr timer = UIntPtr.Zero;
        try
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Create queue before exposing its thread id.
            threadId = GetCurrentThreadId();
            void Install()
            {
                var nextKeyboard = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
                var nextMouse = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
                installed = nextKeyboard != IntPtr.Zero && nextMouse != IntPtr.Zero;
                if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
                if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
                keyboard = nextKeyboard; mouse = nextMouse;
            }
            Install();
            timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, 1000, IntPtr.Zero);
            if (timer == UIntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Interlocked.Exchange(ref lastPump, Stopwatch.GetTimestamp());
            ready.Set();
            var ticks = 0;
            while (Volatile.Read(ref disposed) == 0 && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Id == 0x0113)
                {
                    Interlocked.Exchange(ref lastPump, Stopwatch.GetTimestamp());
                    // Windows can silently remove a timed-out hook. Periodically re-arm on its own thread.
                    if (++ticks % 60 == 0 || !installed) Install();
                }
            }
        }
        catch (Exception error) { Paths.Log("Input hooks: " + error.Message); }
        finally
        {
            installed = false;
            if (timer != UIntPtr.Zero) KillTimer(IntPtr.Zero, timer);
            if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
            if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
            ready.Set();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (threadId != 0) PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        // Keep delegates rooted until the hook thread has unregistered both hooks.
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam;
        public uint Time; public int X, Y; public uint Private;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint interval, IntPtr callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(IntPtr window, UIntPtr id);
}
