using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ForceBreak.Core;
using ForceBreak.Windows;
using Forms = System.Windows.Forms;

namespace ForceBreak.App;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon tray;
    private readonly SettingsWindow settings;
    private readonly List<Forms.ToolStripItem> restItems = new();
    private readonly Forms.ToolStripItem tomorrowItem;
    private readonly DispatcherTimer timer;
    private readonly HwndSource source;
    private readonly List<ReminderWindow> reminders = new();
    private Status? status;
    private long lastSuccess;
    private readonly WorkstationLock workstation = new();
    private readonly RestOverlay overlay = new();
    private InputActivityMonitor? activity;
    private bool busy;
    private bool stopping;
    private string? noticeKey;
    private readonly bool agent;

    public TrayController(bool agent)
    {
        this.agent = agent;
        settings = new SettingsWindow(agent);
        settings.Saved += value => { status = value; lastSuccess = Stopwatch.GetTimestamp(); };
        var hwnd = new WindowInteropHelper(settings).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(SessionMessage);
        if (!WTSRegisterSessionNotification(hwnd, 0))
            settings.SetConnectionError("无法订阅会话事件，将使用定时锁屏重试。");
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "Force Break · 正在连接服务",
            Visible = agent,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        foreach (var minutes in new[] { 5, 10, 30, 60, 120 })
            restItems.Add(tray.ContextMenuStrip.Items.Add($"休息 {minutes} 分钟", null, async (_, _) => await StartRest(new("rest", RestMinutes: minutes))));
        tomorrowItem = tray.ContextMenuStrip.Items.Add("直到明天 6:00", null, async (_, _) => await StartRest(new("rest-tomorrow")));
        tomorrowItem.Visible = false;
        restItems.Add(tomorrowItem);
        tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        tray.ContextMenuStrip.Items.Add("手动开启遮罩", null, (_, _) => overlay.OpenManual());
        tray.ContextMenuStrip.Opening += (_, _) => UpdateTomorrowItem();
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowSettings(); };
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        _ = Tick();
    }

    public void ShowSettings() { settings.ShowStatusTab(); settings.Show(); settings.WindowState = WindowState.Normal; settings.Activate(); }

    private async Task Tick()
    {
        UpdateTomorrowItem();
        if (busy || stopping) return;
        busy = true;
        try
        {
            var report = (activity?.Snapshot() ?? new ActivityReport(false, 0)) with { OverlayVisible = overlay.IsVisible };
            var reply = await Wire.Send(agent ? new("activity", Activity: report) : new("status"));
            if (stopping) return;
            if (!reply.Ok || reply.Status is null) throw new InvalidOperationException(reply.Error ?? "服务返回无效状态。");
            status = reply.Status;
            lastSuccess = Stopwatch.GetTimestamp();
            settings.UpdateStatus(status);
            tray.Text = Tooltip(status);
            var manualAllowed = status.Phase != Phase.Restricted && status.Break?.Phase is not (Phase.Committed or Phase.Reminder or Phase.Restricted);
            foreach (var item in restItems) item.Enabled = manualAllowed;
            UpdateTomorrowItem();
            if (agent && status.Schedule.Behavior.DetectActivity && activity is null) activity = new();
            if (!status.Schedule.Behavior.DetectActivity && activity is not null) { activity.Dispose(); activity = null; }
            if (agent) RenderAndEnforce();
        }
        catch (Exception error)
        {
            if (stopping) return;
            tray.Text = "Force Break · 服务未连接";
            settings.SetConnectionError("服务未连接：" + error.Message + "\n请安装或启动服务；此时无法保证限制生效。");
            // Fail open on stale authority. Never trap the user after repair/service failure.
            if (lastSuccess == 0 || Stopwatch.GetElapsedTime(lastSuccess) > TimeSpan.FromSeconds(5))
            {
                status = null;
                CloseReminders(); overlay.Release();
            }
        }
        finally { busy = false; }
    }

    internal static bool IsTomorrowRestVisible(DateTimeOffset now, Schedule? schedule) =>
        schedule is not null && TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId)).Hour >= 18;

    private void UpdateTomorrowItem()
    {
        tomorrowItem.Text = $"直到明天 {(status?.Schedule.Release ?? new TimeOnly(6, 0)):H:mm}";
        tomorrowItem.Visible = IsTomorrowRestVisible(DateTimeOffset.UtcNow, status?.Schedule);
    }

    private static string Tooltip(Status value)
    {
        if (value.Phase == Phase.Restricted && value.ReleaseAt is { } end)
            return $"Force Break · 休息剩余 {Math.Max(1, Math.Ceiling((end - DateTimeOffset.UtcNow).TotalMinutes))} 分钟";
        var seconds = value.LockAt is { } start ? Math.Max(0, (start - DateTimeOffset.UtcNow).TotalSeconds) : double.PositiveInfinity;
        if (value.Break is { Phase: not Phase.Disabled } rest) seconds = Math.Min(seconds, rest.RemainingWorkSeconds);
        return double.IsFinite(seconds) ? $"Force Break · 距休息约 {Math.Ceiling(seconds / 60)} 分钟{(value.WorkTimerPaused ? "（工作计时暂停）" : "")}" : "Force Break · 计划未启用";
    }

    private async Task StartRest(Request request)
    {
        try
        {
            var reply = await Wire.Send(request);
            if (!reply.Ok || reply.Status is null) throw new InvalidOperationException(reply.Error);
            status = reply.Status; lastSuccess = Stopwatch.GetTimestamp();
            settings.UpdateStatus(status); RenderAndEnforce();
        }
        catch (Exception error) { tray.ShowBalloonTip(10000, "无法开始休息", error.Message, Forms.ToolTipIcon.Info); }
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
                tray.ShowBalloonTip(15000, status.IsBreak ? "该准备休息了" : "该准备睡觉了", $"{bedtime.ToLocalTime():HH:mm} 开始休息，请保存工作。", Forms.ToolTipIcon.Info);
                CloseReminders();
                var window = new ReminderWindow(bedtime, status.IsBreak);
                reminders.Add(window); window.Show();
            }
        }
        else CloseReminders();
        var behavior = status.EffectiveBehavior ?? new BehaviorOptions();
        if (status.Phase == Phase.Restricted && status.ReleaseAt is { } release && release > now)
        {
            if (behavior.FullscreenOverlay) overlay.ShowUntil(release, status.IsBreak ? "离开屏幕，休息一下。" : "晚安，明天再继续。");
            else overlay.Release();
            if (behavior.LockScreen) workstation.Enforce();
        }
        else overlay.Release();
    }

    private IntPtr SessionMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02B1)
        {
            if (wParam.ToInt32() == 7) workstation.IsLocked = true;
            if (wParam.ToInt32() is 8 or 5 or 1 or 3)
            {
                workstation.Reset();
                _ = Tick();
            }
        }
        return IntPtr.Zero;
    }

    private void CloseReminders()
    {
        foreach (var window in reminders) window.Close();
        reminders.Clear();
    }

    public void Dispose()
    {
        stopping = true;
        timer.Stop();
        WTSUnRegisterSessionNotification(source.Handle);
        source.RemoveHook(SessionMessage);
        CloseReminders();
        overlay.Dispose(); activity?.Dispose();
        tray.Dispose();
    }

    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
