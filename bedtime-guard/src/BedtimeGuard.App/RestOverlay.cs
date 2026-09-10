using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BedtimeGuard.App;

/// <summary>Reusable multi-monitor overlay; scheduling and workstation locking live elsewhere.</summary>
internal sealed class RestOverlay : IDisposable
{
    private readonly List<OverlayWindow> windows = new();
    private string? layout;
    public void ShowUntil(DateTimeOffset deadline, string title)
    {
        if (deadline <= DateTimeOffset.UtcNow) { Dispose(); return; }
        var screens = Forms.Screen.AllScreens;
        var nextLayout = string.Join(";", screens.Select(s => s.Bounds.ToString()));
        if (nextLayout != layout)
        {
            Dispose(); layout = nextLayout;
            foreach (var screen in screens)
            {
                var window = new OverlayWindow(screen.Bounds);
                windows.Add(window);
                window.Update(deadline, title);
                window.Show();
            }
        }
        foreach (var window in windows) window.Update(deadline, title);
    }
    public void Dispose()
    {
        foreach (var window in windows) window.Dismiss();
        windows.Clear(); layout = null;
    }

    private sealed class OverlayWindow : Window
    {
        private readonly TextBlock heading = new() { FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock remaining = new() { FontSize = 56, FontWeight = FontWeights.Light, Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly System.Drawing.Rectangle bounds;
        private bool dismissing;
        public OverlayWindow(System.Drawing.Rectangle bounds)
        {
            this.bounds = bounds;
            Title = "Force Break · 休息";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false; Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(19, 28, 46)); Foreground = Brushes.White;
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(heading); panel.Children.Add(remaining);
            Content = panel;
            Loaded += (_, _) => CoverScreen();
            Closing += (_, e) => e.Cancel = !dismissing;
        }
        public void Update(DateTimeOffset deadline, string title)
        {
            heading.Text = title;
            remaining.Text = $"剩余 {Math.Max(1, Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalMinutes))} 分钟";
            if (IsLoaded) CoverScreen();
        }
        private void CoverScreen() => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);
        public void Dismiss() { dismissing = true; Close(); }
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
