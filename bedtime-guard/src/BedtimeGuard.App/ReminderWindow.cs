using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BedtimeGuard.App;

internal sealed class ReminderWindow : Window
{
    public ReminderWindow(DateTimeOffset bedtime, bool isBreak = false)
    {
        Title = isBreak ? "该休息了" : "该睡觉了";
        Width = 560; Height = 340; Topmost = true;
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
        var close = new Button { Content = "知道了，继续收尾", Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        content.Children.Add(close);
        Content = content;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Update()
        {
            var remaining = bedtime - DateTimeOffset.UtcNow;
            countdown.Text = remaining > TimeSpan.Zero ? $"还有 {Math.Ceiling(remaining.TotalMinutes)} 分钟" : isBreak ? "休息时间" : "晚安";
            if (remaining <= TimeSpan.Zero) Close();
        }
        timer.Tick += (_, _) => Update();
        Closed += (_, _) => timer.Stop();
        Loaded += (_, _) => { Update(); timer.Start(); };
    }

}
