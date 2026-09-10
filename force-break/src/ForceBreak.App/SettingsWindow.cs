using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForceBreak.Core;
using ForceBreak.Windows;
using ForceBreak.Service;

namespace ForceBreak.App;

internal sealed class SettingsWindow : Window
{
    private readonly TextBlock stateText = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly CheckBox enabled = new() { Content = "启用每周睡眠计划", Margin = new Thickness(0, 10, 0, 10) };
    private readonly CheckBox taskManager = new() { Content = "承诺期开始后临时禁用任务管理器", Margin = new Thickness(0, 12, 0, 6) };
    private readonly CheckBox breaksEnabled = new() { Content = "启用定时强制休息", Margin = new Thickness(0, 18, 0, 10) };
    private readonly TextBox workMinutes = new(), restMinutes = new(), breakReminder = new(), breakCommitment = new();
    private readonly TextBox commitment = new(), jitter = new(), reminder = new(), bedtime = new(), release = new();
    private readonly ComboBox zones = new() { MinWidth = 240, DisplayMemberPath = "DisplayName" };
    private readonly Dictionary<DayOfWeek, CheckBox> days = new();
    private readonly Button save = new() { Content = "保存配置", Padding = new Thickness(22, 9, 22, 9), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
    private Button? uninstall;
    private readonly TabControl tabs = new();
    private readonly CheckBox detectActivity = new() { Content = "键鼠动作检测", Margin = new Thickness(0, 12, 0, 6) };
    private readonly CheckBox fullscreen = new() { Content = "休息期全屏遮罩", Margin = new Thickness(0, 12, 0, 6) };
    private readonly CheckBox lockScreen = new() { Content = "休息期锁屏", Margin = new Thickness(0, 12, 0, 6) };
    private readonly TextBox idleMinutes = new();
    private bool initialized;
    private bool saving;
    public event Action<Status>? Saved;

    public SettingsWindow(bool agent)
    {
        Title = "Force Break · 工作与休息";
        Width = 660; Height = 640; MinWidth = 570; MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 252));
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = Page("当前状态");
        var statusPanel = panel;
        panel.Children.Add(new TextBlock { Text = "FORCE BREAK", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 16) });
        var installPath = new TextBox { Text = NativeInstaller.DefaultDirectory };
        AddRow(panel, "安装位置", installPath);
        var management = new WrapPanel { Margin = new Thickness(0, 8, 0, 18) };
        foreach (var action in new[] { MaintenanceAction.Install, MaintenanceAction.Resume, MaintenanceAction.Uninstall })
        {
            var button = new Button { Content = MaintenanceLauncher.Label(action), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 8) };
            if (action == MaintenanceAction.Uninstall) { uninstall = button; button.IsEnabled = false; button.ToolTip = "正在确认承诺状态"; }
            button.Click += async (_, _) =>
            {
                management.IsEnabled = false;
                try { await MaintenanceLauncher.Run(action, installPath.Text); }
                finally { management.IsEnabled = true; initialized = false; }
            };
            management.Children.Add(button);
        }
        panel.Children.Add(management);
        panel.Children.Add(stateText); panel.Children.Add(detail);
        panel.Children.Add(new TextBlock { Text = "关闭面板后继续在托盘运行。左键单击托盘回到本页；悬停查看距离休息的时间；右键可选择 5—120 分钟休息、直到明天或手动笔记遮罩。\n\n第一次安装后请启用需要的计划。进入承诺期后，本轮时间和限制行为固定；修改只影响后续安排，当前安排结束前不能卸载。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        panel = Page("早睡计划");
        panel.Children.Add(enabled);
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
        panel.Children.Add(new TextBlock { Text = "随机承诺时刻整晚固定，不提前公布。承诺范围必须早于首次提醒；解除时间属于次日。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 0) });
        panel = Page("定时休息");
        panel.Children.Add(breaksEnabled);
        AddRow(panel, "工作间隔（分钟）", workMinutes);
        AddRow(panel, "休息时长（分钟）", restMinutes);
        AddRow(panel, "提前提醒（分钟）", breakReminder);
        AddRow(panel, "提前承诺（分钟）", breakCommitment);
        panel.Children.Add(new TextBlock { Text = "工作间隔不包含休息时长：工作 50 分钟、休息 10 分钟组成一轮。开启动作检测后，空闲也会暂停工作累计；锁屏、注销和睡眠期间不累计。进入承诺期后按固定时间执行，本轮不能取消或延期。全部使用整数分钟；提醒提前量不超过承诺提前量，工作间隔减去承诺提前量必须至少为 11 分钟。夜间限制优先，结束后重新累计。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        panel = Page("行为配置");
        panel.Children.Add(detectActivity);
        AddRow(panel, "空闲暂停（分钟）", idleMinutes);
        panel.Children.Add(new TextBlock { Text = "通过低级键盘 / 鼠标 Hook 检测操作，仅记录距上次操作的时长，不记录按键内容。达到空闲阈值后暂停工作计时，有操作后继续；进入承诺期后不再暂停本轮倒计时。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        panel.Children.Add(taskManager);
        panel.Children.Add(fullscreen); panel.Children.Add(lockScreen);
        panel.Children.Add(new TextBlock
        {
            Text = "遮罩覆盖全部屏幕，以分钟显示倒计时，并提供自动保存的笔记。休息到期后点击关闭按钮退出。锁屏是独立选项，不勾选便不会主动锁定 Windows。\n限制行为在承诺时固定。任务管理器由服务应用系统策略，策略不关闭已打开的实例。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 18)
        });
        save.Click += async (_, _) => await Save();
        var footer = new StackPanel { Margin = new Thickness(24, 8, 24, 16) };
        footer.Children.Add(save); footer.Children.Add(feedback);
        tabs.SelectionChanged += (_, _) => save.Visibility = tabs.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        save.Visibility = Visibility.Collapsed;
        statusPanel.Children.Add(new TextBlock
        {
            Text = "安装后由后台服务常驻执行。安装、重新启用与卸载会请求管理员权限；日常配置不需要提权。已承诺的睡眠或休息结束前，卸载不可用。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 20, 0, 0)
        });
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        Content = root;
        Closing += (_, e) => { if (agent) { e.Cancel = true; Hide(); } else Application.Current.Shutdown(); };
        stateText.Text = "正在连接后台服务…";
    }

    public void ShowStatusTab() => tabs.SelectedIndex = 0;
    private StackPanel Page(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };
        tabs.Items.Add(new TabItem { Header = title, Padding = new Thickness(14, 9, 14, 9),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        return panel;
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
        stateText.Text = PhaseText(status.Phase, status.IsBreak);
        var s = status.Schedule;
        var center = s.Commitment.ToTimeSpan();
        var range = $"{center - TimeSpan.FromMinutes(s.JitterMinutes):hh\\:mm}—{center + TimeSpan.FromMinutes(s.JitterMinutes):hh\\:mm}";
        detail.Text = $"设置的承诺范围：{range}\n当前安排：提醒 {Format(status.ReminderAt)} · 开始休息 {Format(status.LockAt)}\n解除 {Format(status.ReleaseAt)}\n{status.PolicyMessage}";
        if (status.Break is { } rest)
            detail.Text += rest.SuppressedByNight ? "\n定时休息：夜间限制期间暂停" :
                $"\n定时休息：{PhaseText(rest.Phase, true)} · 距离休息约 {Math.Ceiling(rest.RemainingWorkSeconds / 60)} 分钟\n本轮休息 {Format(rest.LockAt)}—{Format(rest.ReleaseAt)}";
        detail.Text += "\n" + status.ActivityMessage;
        if (uninstall is not null)
        {
            uninstall.IsEnabled = status.Phase is Phase.Disabled or Phase.Open;
            uninstall.ToolTip = uninstall.IsEnabled ? "卸载程序与计划数据" : "已进入承诺期，当前安排结束前不能卸载";
        }
        save.IsEnabled = !saving;
        if (initialized) return;
        initialized = true;
        enabled.IsChecked = s.Enabled;
        commitment.Text = s.Commitment.ToString("HH:mm"); jitter.Text = s.JitterMinutes.ToString(CultureInfo.InvariantCulture);
        reminder.Text = s.Reminder.ToString("HH:mm"); bedtime.Text = s.Bedtime.ToString("HH:mm"); release.Text = s.Release.ToString("HH:mm");
        taskManager.IsChecked = s.DisableTaskManager;
        detectActivity.IsChecked = s.Behavior.DetectActivity;
        fullscreen.IsChecked = s.Behavior.FullscreenOverlay; lockScreen.IsChecked = s.Behavior.LockScreen;
        idleMinutes.Text = s.Behavior.IdleMinutes.ToString(CultureInfo.InvariantCulture);
        breaksEnabled.IsChecked = s.Breaks.Enabled;
        workMinutes.Text = s.Breaks.WorkMinutes.ToString(CultureInfo.InvariantCulture);
        restMinutes.Text = s.Breaks.RestMinutes.ToString(CultureInfo.InvariantCulture);
        breakReminder.Text = s.Breaks.ReminderMinutes.ToString(CultureInfo.InvariantCulture);
        breakCommitment.Text = s.Breaks.CommitmentMinutes.ToString(CultureInfo.InvariantCulture);
        foreach (var item in days) item.Value.IsChecked = s.Days.Contains(item.Key);
        zones.SelectedItem = zones.Items.Cast<TimeZoneInfo>().FirstOrDefault(z => z.Id == s.TimeZoneId);
    }

    public void SetConnectionError(string message) { stateText.Text = "服务未就绪"; detail.Text = message; save.IsEnabled = false; if (uninstall is not null) { uninstall.IsEnabled = false; uninstall.ToolTip = "无法确认承诺状态，暂不可卸载"; } }

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
                DisableTaskManager = taskManager.IsChecked == true,
                Behavior = new BehaviorOptions { DetectActivity = detectActivity.IsChecked == true,
                    IdleMinutes = int.Parse(idleMinutes.Text, CultureInfo.InvariantCulture),
                    FullscreenOverlay = fullscreen.IsChecked == true, LockScreen = lockScreen.IsChecked == true },
                Breaks = new BreakOptions
                {
                    Enabled = breaksEnabled.IsChecked == true,
                    WorkMinutes = int.Parse(workMinutes.Text, CultureInfo.InvariantCulture),
                    RestMinutes = int.Parse(restMinutes.Text, CultureInfo.InvariantCulture),
                    ReminderMinutes = int.Parse(breakReminder.Text, CultureInfo.InvariantCulture),
                    CommitmentMinutes = int.Parse(breakCommitment.Text, CultureInfo.InvariantCulture)
                }
            };
            schedule.Validate();
            var reply = await Wire.Send(new("save", schedule));
            if (!reply.Ok || reply.Status is null) throw new InvalidOperationException(reply.Error);
            UpdateStatus(reply.Status); Saved?.Invoke(reply.Status);
            feedback.Text = reply.Status.Phase is Phase.Committed or Phase.Reminder or Phase.Restricted
                ? "已保存后续计划。已经承诺的睡眠或休息安排继续执行，不能取消或延期。" : "计划已保存。";
        }
        catch (Exception error) { feedback.Text = error.Message; }
        finally { saving = false; save.IsEnabled = true; }
    }

    private static TimeOnly Parse(string value) => TimeOnly.ParseExact(value.Trim(), "HH:mm", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset? value) => value?.ToLocalTime().ToString("MM-dd HH:mm") ?? "—";
    public static string PhaseText(Phase phase, bool isBreak = false) => isBreak ? phase switch
    {
        Phase.Disabled => "定时休息未启用", Phase.Open => "正在累计工作时间", Phase.Committed => "本轮休息已承诺",
        Phase.Reminder => "请保存工作，准备休息", Phase.Restricted => "休息时间，电脑使用受限", _ => "未知状态"
    } : phase switch
    {
        Phase.Disabled => "计划未启用", Phase.Open => "可以调整计划", Phase.Committed => "今晚已承诺，计划固定",
        Phase.Reminder => "请开始收尾，准备睡觉", Phase.Restricted => "睡眠时间，电脑使用受限", _ => "未知状态"
    };
}
