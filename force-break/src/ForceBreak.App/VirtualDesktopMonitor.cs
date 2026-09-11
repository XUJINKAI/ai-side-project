using System;
using System.Runtime.InteropServices;

namespace ForceBreak.App;

/// <summary>Uses the public Windows 10+ API; reconnects after Explorer restarts.</summary>
internal sealed class VirtualDesktopMonitor : IDisposable
{
    private IVirtualDesktopManager? manager;

    public bool? IsOnCurrentDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        try
        {
            manager ??= (IVirtualDesktopManager)new VirtualDesktopManager();
            var result = manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var current);
            if (result >= 0) return current;
        }
        catch (COMException) { }
        // Unknown must not trigger repeated window destruction and loss of editing state.
        Dispose();
        return null;
    }

    public void Dispose()
    {
        if (manager is null) return;
        Marshal.ReleaseComObject(manager);
        manager = null;
    }

    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManager { }

    // Preserve the native vtable order, including unused methods.
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }
}
