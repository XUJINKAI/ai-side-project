using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BedtimeGuard.Core;
using BedtimeGuard.Windows;
using Forms = System.Windows.Forms;

namespace BedtimeGuard.App;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon tray;
    private readonly SettingsWindow settings;
    private readonly DispatcherTimer timer;
    private readonly HwndSource source;
    private readonly List<ReminderWindow> reminders = new();
    private readonly List<ReminderWindow> previews = new();
    private Status? status;
    private long lastSuccess;
    private long lastLock;
    private bool busy;
    private bool locked;
    private bool stopping;
    private string? noticeKey;
    private string? overlayKey;

    public TrayController()
    {
        settings = new SettingsWindow();
        settings.Saved += value => { status = value; lastSuccess = Stopwatch.GetTimestamp(); };
        var hwnd = new WindowInteropHelper(settings).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(SessionMessage);
        if (!WTSRegisterSessionNotification(hwnd, 0))
            settings.SetConnectionError("无法订阅会话事件，将使用定时锁屏重试。");
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Bedtime Guard · 正在连接服务",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("设置与状态", null, (_, _) => ShowSettings());
        tray.ContextMenuStrip.Items.Add("预览睡前提醒", null, (_, _) => Preview());
        tray.DoubleClick += (_, _) => ShowSettings();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        _ = Tick();
    }

    public void ShowSettings() { settings.Show(); settings.WindowState = WindowState.Normal; settings.Activate(); }

    private async Task Tick()
    {
        if (busy || stopping) return;
        busy = true;
        try
        {
            var reply = await Wire.Send(new("status"));
            if (stopping) return;
            if (!reply.Ok || reply.Status is null) throw new InvalidOperationException(reply.Error ?? "服务返回无效状态。");
            status = reply.Status;
            lastSuccess = Stopwatch.GetTimestamp();
            settings.UpdateStatus(status);
            tray.Text = "Bedtime Guard · " + SettingsWindow.PhaseText(status.Phase);
            RenderAndEnforce();
        }
        catch (Exception error)
        {
            if (stopping) return;
            tray.Text = "Bedtime Guard · 服务未连接";
            settings.SetConnectionError("服务未连接：" + error.Message + "\n请安装或启动服务；此时无法保证限制生效。");
            // Fail open on stale authority. Never trap the user after repair/service failure.
            if (lastSuccess == 0 || Stopwatch.GetElapsedTime(lastSuccess) > TimeSpan.FromSeconds(5))
            {
                status = null;
                CloseReminders();
            }
        }
        finally { busy = false; }
    }

    private void RenderAndEnforce()
    {
        if (status is null) return;
        var now = DateTimeOffset.UtcNow;
        if (status.Phase == Phase.Reminder && status.LockAt is { } bedtime)
        {
            var left = bedtime - now;
            var stage = left <= TimeSpan.FromMinutes(1) ? "last" : left <= TimeSpan.FromMinutes(5) ? "five" : "first";
            var key = $"{bedtime:O}/{stage}";
            if (noticeKey != key)
            {
                noticeKey = key;
                tray.ShowBalloonTip(15000, "该准备睡觉了", $"{bedtime.ToLocalTime():HH:mm} 将锁定电脑，请保存工作。", Forms.ToolTipIcon.Info);
                if (stage != "last")
                {
                    CloseReminders();
                    var window = new ReminderWindow(bedtime, false);
                    reminders.Add(window);
                    window.Show();
                }
            }
            if (stage == "last" && overlayKey != key)
            {
                CloseReminders();
                overlayKey = key;
                foreach (var screen in Forms.Screen.AllScreens)
                {
                    var window = new ReminderWindow(bedtime, false, screen.Bounds);
                    reminders.Add(window);
                    window.Show();
                }
            }
        }
        else
        {
            CloseReminders();
            if (status.Phase == Phase.Restricted && status.ReleaseAt > now && !locked &&
                (lastLock == 0 || Stopwatch.GetElapsedTime(lastLock) >= TimeSpan.FromSeconds(2)))
            {
                lastLock = Stopwatch.GetTimestamp();
                if (!LockWorkStation()) settings.SetConnectionError($"系统锁屏请求失败：{Marshal.GetLastWin32Error()}");
            }
        }
    }

    private IntPtr SessionMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02B1)
        {
            if (wParam.ToInt32() == 7) locked = true;
            if (wParam.ToInt32() is 8 or 5 or 1 or 3)
            {
                locked = false;
                lastLock = 0;
                _ = Tick();
            }
        }
        return IntPtr.Zero;
    }

    private void Preview()
    {
        var window = new ReminderWindow(DateTimeOffset.Now.AddSeconds(30), true);
        previews.Add(window);
        window.Closed += (_, _) => previews.Remove(window);
        window.Show();
    }

    private void CloseReminders()
    {
        foreach (var window in reminders) window.Close();
        reminders.Clear();
        overlayKey = null;
    }

    public void Dispose()
    {
        stopping = true;
        timer.Stop();
        WTSUnRegisterSessionNotification(source.Handle);
        source.RemoveHook(SessionMessage);
        CloseReminders();
        foreach (var window in previews.ToArray()) window.Close();
        tray.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LockWorkStation();
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
