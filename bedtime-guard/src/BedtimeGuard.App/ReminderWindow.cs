using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BedtimeGuard.App;

internal sealed class ReminderWindow : Window
{
    public ReminderWindow(DateTimeOffset bedtime, bool preview, System.Drawing.Rectangle? screen = null)
    {
        Title = preview ? "睡前提醒预览" : "该睡觉了";
        Width = 560; Height = 340; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(19, 28, 46));
        Foreground = Brushes.White;
        var content = new StackPanel { Margin = new Thickness(40), VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = preview ? "睡前提醒 · 预览" : "把今天留在今天。", FontSize = 30, Margin = new Thickness(0, 0, 0, 20) });
        var countdown = new TextBlock { FontSize = 48, FontWeight = FontWeights.Light };
        content.Children.Add(countdown);
        content.Children.Add(new TextBlock
        {
            Text = preview ? "这里只预览提醒，不修改计划、不锁屏。" : "到点将锁定电脑。请保存工作，准备休息。\n今晚计划已固定，不能延期。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 20), FontSize = 16
        });
        var close = new Button { Content = preview ? "关闭预览" : "知道了，继续收尾", Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        content.Children.Add(close);
        Content = content;
        if (screen is { } bounds)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Update()
        {
            var remaining = bedtime - DateTimeOffset.UtcNow;
            countdown.Text = remaining > TimeSpan.Zero ? $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}" : "晚安";
            if (remaining <= TimeSpan.Zero) Close();
        }
        timer.Tick += (_, _) => Update();
        Closed += (_, _) => timer.Stop();
        Loaded += (_, _) => { Update(); timer.Start(); };
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
