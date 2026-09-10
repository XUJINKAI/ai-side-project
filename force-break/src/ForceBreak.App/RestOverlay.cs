using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ForceBreak.Core;
using Forms = System.Windows.Forms;

namespace ForceBreak.App;

/// <summary>Multi-monitor overlay with a persistent shared notebook and independent display lifetime.</summary>
internal sealed class RestOverlay : IDisposable
{
    private readonly List<OverlayWindow> windows = new();
    private readonly OverlaySession session = new();
    private readonly OverlayNotes notes;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string? layout;
    private string title = "休息一下，记下想法。";
    private string? saveError;
    private bool dirty;
    private bool loadFailed;
    public bool IsVisible => session.Mode != OverlayMode.Closed;

    public RestOverlay(string? notesPath = null)
    {
        notes = new(notesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForceBreak", "overlay-notes.txt"));
        try { notes.Load(); }
        catch (Exception error) { loadFailed = true; saveError = "笔记读取失败，暂不允许编辑：" + error.Message; }
        timer.Tick += (_, _) => { if (dirty) Save(); Refresh(); };
        timer.Start();
    }
    public void ShowUntil(DateTimeOffset deadline, string heading)
    { title = heading; session.Enforce(deadline, DateTimeOffset.UtcNow); Refresh(); }
    public void OpenManual()
    {
        session.OpenManual();
        if (session.Mode == OverlayMode.Manual) title = "休息一下，记下想法。";
        Refresh();
    }
    // Restriction ends, but notes remain visible until the user closes the overlay.
    public void Release() { session.Release(); Refresh(); }
    private void Refresh()
    {
        session.Advance(DateTimeOffset.UtcNow);
        if (!IsVisible) return;
        var screens = Forms.Screen.AllScreens;
        var nextLayout = string.Join(";", screens.Select(s => s.Bounds.ToString()));
        if (nextLayout != layout)
        {
            CloseWindows(); layout = nextLayout;
            foreach (var screen in screens)
            {
                var window = new OverlayWindow(screen.Bounds, Edit, RequestClose);
                windows.Add(window);
                window.SetText(notes.Text); window.Show();
            }
        }
        var caption = session.Mode switch
        {
            OverlayMode.Restricted => $"剩余 {Math.Max(1, Math.Ceiling((session.Deadline!.Value - DateTimeOffset.UtcNow).TotalMinutes))} 分钟",
            OverlayMode.Completed => "休息已结束，可以关闭",
            _ => "自由记录 · 不计时"
        };
        foreach (var window in windows)
            window.Update(title, caption, session.CanClose, loadFailed,
                saveError ?? (dirty ? "正在保存…" : "笔记自动保存在本机"));
    }
    private void Edit(OverlayWindow source, string text)
    {
        notes.Text = text; dirty = true;
        foreach (var window in windows) if (window != source) window.SetText(text);
    }
    private bool Save()
    {
        if (!dirty) return true;
        try { notes.Save(); dirty = false; saveError = null; return true; }
        catch (Exception error) { saveError = "保存失败，请复制笔记后重试：" + error.Message; return false; }
    }
    private void RequestClose()
    {
        session.Advance(DateTimeOffset.UtcNow);
        if (!session.CanClose || !Save()) { Refresh(); return; }
        if (session.TryClose(DateTimeOffset.UtcNow)) CloseWindows();
    }
    private void CloseWindows()
    { foreach (var window in windows) window.Dismiss(); windows.Clear(); layout = null; }
    public void Dispose()
    { timer.Stop(); Save(); CloseWindows(); }

    private sealed class OverlayWindow : Window
    {
        private readonly TextBlock heading = new() { FontSize = 26, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock remaining = new() { FontSize = 36, FontWeight = FontWeights.Light, Margin = new Thickness(0, 12, 0, 20) };
        private readonly TextBox editor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, FontSize = 18, Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(29, 40, 59)), Foreground = Brushes.White, BorderBrush = Brushes.SlateGray };
        private readonly TextBlock saveStatus = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        private readonly Button close = new() { Content = "关闭遮罩", Padding = new Thickness(24, 10, 24, 10), HorizontalAlignment = HorizontalAlignment.Right };
        private readonly System.Drawing.Rectangle bounds;
        private bool dismissing;
        private bool syncing;
        public OverlayWindow(System.Drawing.Rectangle bounds, Action<OverlayWindow, string> edit, Action requestClose)
        {
            this.bounds = bounds;
            Title = "Force Break · 笔记遮罩";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false; Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(19, 28, 46)); Foreground = Brushes.White;
            var panel = new Grid { Margin = new Thickness(40), MaxWidth = 900 };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                panel.RowDefinitions.Add(new RowDefinition { Height = height });
            UIElement[] controls = [heading, remaining, editor, saveStatus, close];
            for (var i = 0; i < controls.Length; i++) { Grid.SetRow(controls[i], i); panel.Children.Add(controls[i]); }
            Content = panel;
            System.Windows.Automation.AutomationProperties.SetName(editor, "休息笔记");
            editor.TextChanged += (_, _) => { if (!syncing) edit(this, editor.Text); };
            close.Click += (_, _) => requestClose();
            Loaded += (_, _) => CoverScreen();
            Closing += (_, e) => { if (!dismissing) { e.Cancel = true; Dispatcher.BeginInvoke(requestClose); } };
        }
        public void SetText(string text)
        {
            if (editor.Text == text) return;
            var caret = editor.CaretIndex;
            syncing = true;
            try { editor.Text = text; editor.CaretIndex = Math.Min(caret, text.Length); }
            finally { syncing = false; }
        }
        public void Update(string title, string caption, bool canClose, bool readOnly, string message)
        {
            heading.Text = title; remaining.Text = caption;
            close.Visibility = canClose ? Visibility.Visible : Visibility.Collapsed;
            editor.IsReadOnly = readOnly; saveStatus.Text = message;
            if (IsLoaded) CoverScreen();
        }
        private void CoverScreen()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);
        }
        public void Dismiss() { dismissing = true; Close(); }
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
