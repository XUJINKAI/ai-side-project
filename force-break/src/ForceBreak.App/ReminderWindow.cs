using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ForceBreak.App;

internal sealed class ReminderWindow : Window
{
    public ReminderWindow(DateTimeOffset bedtime, bool isBreak = false, Func<Task>? startNow = null)
    {
        Title = isBreak ? "该休息了" : "该睡觉了";
        Width = 560; Height = startNow is null ? 340 : 410; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(19, 28, 46));
        Foreground = Brushes.White;
        var content = new StackPanel { Margin = new Thickness(40), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = isBreak ? "离开屏幕，休息一下。" : "把今天留在今天。", FontSize = 30, Margin = new Thickness(0, 0, 0, 20) });
        var countdown = new TextBlock { FontSize = 48, FontWeight = FontWeights.Light };
        content.Children.Add(countdown);
        content.Children.Add(new TextBlock
        {
            Text = "到点将开始休息。请保存工作，准备休息。\n本次计划已固定，不能延期。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 20), FontSize = 16
        });
        var feedback = new TextBlock { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        if (startNow is not null)
        {
            var begin = new Button { Content = "现在开始休息", Padding = new Thickness(18, 10, 18, 10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            begin.Click += async (_, _) =>
            {
                begin.IsEnabled = false; feedback.Text = "";
                try { await startNow(); Close(); }
                catch (Exception error) { feedback.Text = "无法开始休息：" + error.Message; begin.IsEnabled = true; }
            };
            content.Children.Add(begin);
            content.Children.Add(feedback);
        }
        var close = new Button { Content = "知道了，继续收尾", Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        content.Children.Add(close);
        Content = content;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Update()
        {
            var remaining = bedtime - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                countdown.Text = isBreak ? "休息时间" : "晚安";
                Close();
                return;
            }
            countdown.Text = isBreak && remaining <= TimeSpan.FromMinutes(1)
                ? $"还有 {Math.Max(1, Math.Ceiling(remaining.TotalSeconds))} 秒"
                : $"还有 {Math.Ceiling(remaining.TotalMinutes)} 分钟";
        }
        timer.Tick += (_, _) => Update();
        Closed += (_, _) => timer.Stop();
        Loaded += (_, _) => { Update(); timer.Start(); };
    }
}
