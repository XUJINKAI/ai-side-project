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
        private readonly TextBlock heading = new() { FontSize = 28, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock remaining = new() { FontSize = 56, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Light, Margin = new Thickness(0, 18, 0, 0) };
        private readonly TextBox editor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, Height = 200, FontSize = 16, Padding = new Thickness(0, 12, 0, 0),
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(224, 232, 243)), BorderThickness = new Thickness(0), CaretBrush = Brushes.White };
        private readonly TextBlock saveStatus = new() { FontSize = 12, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        private readonly Button close = new() { Content = "关闭遮罩", Padding = new Thickness(24, 10, 24, 10), HorizontalAlignment = HorizontalAlignment.Left };
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
            var panel = new Grid { Margin = new Thickness(64), MaxWidth = 1100, VerticalAlignment = VerticalAlignment.Center };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            var gap = new ColumnDefinition { Width = new GridLength(72) };
            panel.ColumnDefinitions.Add(gap);
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            var message = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            message.Children.Add(heading); message.Children.Add(remaining);
            close.Background = new SolidColorBrush(Color.FromRgb(36, 50, 72));
            close.Foreground = new SolidColorBrush(Color.FromRgb(228, 235, 245));
            close.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 83, 109));
            close.FontSize = 14;
            close.Template = ButtonTemplate();
            // Reserve the button's space so the message does not jump when rest ends.
            message.Children.Add(new Border { Height = 80, Padding = new Thickness(0, 30, 0, 0), Child = close });
            panel.Children.Add(message);
            var notebook = new StackPanel();
            notebook.Children.Add(new TextBlock { Text = "随手记", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(151, 169, 194)) });
            notebook.Children.Add(editor); notebook.Children.Add(saveStatus);
            var card = new Border { MaxWidth = 360, Padding = new Thickness(22, 18, 22, 12), CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(23, 33, 52)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 77)), BorderThickness = new Thickness(1), Child = notebook };
            Grid.SetColumn(card, 2); panel.Children.Add(card);
            Content = panel;
            SizeChanged += (_, _) =>
            {
                panel.Margin = new Thickness(ActualWidth < 900 ? 28 : 64);
                gap.Width = new GridLength(Math.Clamp(ActualWidth * 0.04, 24, 72));
                remaining.FontSize = remaining.Text.StartsWith("剩余 ", StringComparison.Ordinal) ? Math.Clamp(ActualWidth / 25, 28, 56) : 28;
                heading.FontSize = Math.Clamp(ActualWidth / 48, 20, 28);
                editor.Height = Math.Clamp(ActualHeight * 0.2, 110, 200);
            };
            System.Windows.Automation.AutomationProperties.SetName(editor, "休息笔记");
            editor.TextChanged += (_, _) => { if (!syncing) edit(this, editor.Text); };
            close.Click += (_, _) => requestClose();
            Loaded += (_, _) => CoverScreen();
            Closing += (_, e) => { if (!dismissing) { e.Cancel = true; Dispatcher.BeginInvoke(requestClose); } };
        }
        private static ControlTemplate ButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.PaddingProperty })
                border.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(48, 66, 92))));
            template.Triggers.Add(hover);
            return template;
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
            remaining.FontSize = caption.StartsWith("剩余 ", StringComparison.Ordinal) ? Math.Clamp(ActualWidth / 25, 28, 56) : 28;
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
