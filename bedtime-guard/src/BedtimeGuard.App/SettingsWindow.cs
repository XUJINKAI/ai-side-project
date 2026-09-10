using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BedtimeGuard.Core;
using BedtimeGuard.Windows;

namespace BedtimeGuard.App;

internal sealed class SettingsWindow : Window
{
    private readonly TextBlock stateText = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly CheckBox enabled = new() { Content = "启用每周睡眠计划", Margin = new Thickness(0, 10, 0, 10) };
    private readonly CheckBox taskManager = new() { Content = "承诺期开始后临时禁用任务管理器（可选）", Margin = new Thickness(0, 12, 0, 6) };
    private readonly TextBox commitment = new(), jitter = new(), reminder = new(), bedtime = new(), release = new();
    private readonly ComboBox zones = new() { MinWidth = 240, DisplayMemberPath = "DisplayName" };
    private readonly Dictionary<DayOfWeek, CheckBox> days = new();
    private readonly Button save = new() { Content = "保存计划", Padding = new Thickness(22, 9, 22, 9), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
    private bool initialized;
    private bool saving;
    public event Action<Status>? Saved;

    public SettingsWindow()
    {
        Title = "Bedtime Guard · 睡眠计划";
        Width = 660; Height = 820; MinWidth = 570; MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 252));
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "BEDTIME GUARD", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(stateText); panel.Children.Add(detail); panel.Children.Add(enabled);
        AddRow(panel, "承诺时间中心", commitment); AddRow(panel, "前后随机（分钟）", jitter);
        AddRow(panel, "首次提醒", reminder); AddRow(panel, "开始限制", bedtime); AddRow(panel, "次日解除", release);
        var week = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var names = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var i = 0; i < order.Length; i++)
        {
            var box = new CheckBox { Content = names[i], Margin = new Thickness(0, 0, 12, 6) };
            days.Add(order[i], box); week.Children.Add(box);
        }
        panel.Children.Add(week);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones()) zones.Items.Add(zone);
        AddRow(panel, "计划时区", zones);
        panel.Children.Add(taskManager);
        panel.Children.Add(new TextBlock
        {
            Text = "随机承诺时间整晚固定，不提前公布。进入承诺期后，修改仅影响后续夜晚；今晚不能取消或延期。\n任务管理器限制只适用于个人未受管理的电脑，不阻止管理员恢复。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 18)
        });
        save.Click += async (_, _) => await Save();
        panel.Children.Add(save); panel.Children.Add(feedback);
        panel.Children.Add(new TextBlock
        {
            Text = "关闭窗口后继续在托盘运行。紧急恢复：从开始菜单运行“Bedtime Guard - Repair（恢复）”，需要管理员权限。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 20, 0, 0)
        });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        stateText.Text = "正在连接后台服务…";
    }

    private static void AddRow(Panel panel, string label, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        control.Padding = new Thickness(8, 5, 8, 5);
        Grid.SetColumn(control, 1); grid.Children.Add(control); panel.Children.Add(grid);
    }

    public void UpdateStatus(Status status)
    {
        stateText.Text = PhaseText(status.Phase);
        var s = status.Schedule;
        var center = s.Commitment.ToTimeSpan();
        var range = $"{center - TimeSpan.FromMinutes(s.JitterMinutes):hh\\:mm}—{center + TimeSpan.FromMinutes(s.JitterMinutes):hh\\:mm}";
        detail.Text = $"设置的承诺范围：{range}\n当前安排：提醒 {Format(status.ReminderAt)} · 锁屏 {Format(status.LockAt)}\n解除 {Format(status.ReleaseAt)}\n{status.PolicyMessage}";
        save.IsEnabled = !saving;
        if (initialized) return;
        initialized = true;
        enabled.IsChecked = s.Enabled;
        commitment.Text = s.Commitment.ToString("HH:mm"); jitter.Text = s.JitterMinutes.ToString(CultureInfo.InvariantCulture);
        reminder.Text = s.Reminder.ToString("HH:mm"); bedtime.Text = s.Bedtime.ToString("HH:mm"); release.Text = s.Release.ToString("HH:mm");
        taskManager.IsChecked = s.DisableTaskManager;
        foreach (var item in days) item.Value.IsChecked = s.Days.Contains(item.Key);
        zones.SelectedItem = zones.Items.Cast<TimeZoneInfo>().FirstOrDefault(z => z.Id == s.TimeZoneId);
    }

    public void SetConnectionError(string message) { stateText.Text = "服务未就绪"; detail.Text = message; save.IsEnabled = false; }

    private async Task Save()
    {
        saving = true; save.IsEnabled = false;
        try
        {
            var schedule = new Schedule
            {
                Enabled = enabled.IsChecked == true,
                Commitment = Parse(commitment.Text), JitterMinutes = int.Parse(jitter.Text, CultureInfo.InvariantCulture),
                Reminder = Parse(reminder.Text), Bedtime = Parse(bedtime.Text), Release = Parse(release.Text),
                Days = days.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray(),
                TimeZoneId = (zones.SelectedItem as TimeZoneInfo)?.Id ?? throw new ArgumentException("请选择时区。"),
                DisableTaskManager = taskManager.IsChecked == true
            };
            schedule.Validate();
            var reply = await Wire.Send(new("save", schedule));
            if (!reply.Ok || reply.Status is null) throw new InvalidOperationException(reply.Error);
            UpdateStatus(reply.Status); Saved?.Invoke(reply.Status);
            feedback.Text = reply.Status.Phase is Phase.Committed or Phase.Reminder or Phase.Restricted
                ? "已保存后续计划。今晚已进入承诺期，原计划继续执行，不能取消或延期。" : "计划已保存。";
        }
        catch (Exception error) { feedback.Text = error.Message; }
        finally { saving = false; save.IsEnabled = true; }
    }

    private static TimeOnly Parse(string value) => TimeOnly.ParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset? value) => value?.ToLocalTime().ToString("MM-dd HH:mm") ?? "—";
    public static string PhaseText(Phase phase) => phase switch
    {
        Phase.Disabled => "计划未启用", Phase.Open => "可以调整计划", Phase.Committed => "今晚已承诺，计划固定",
        Phase.Reminder => "请开始收尾，准备睡觉", Phase.Restricted => "睡眠时间，电脑使用受限", _ => "未知状态"
    };
}
