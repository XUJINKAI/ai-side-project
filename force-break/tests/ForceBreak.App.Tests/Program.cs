using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using ForceBreak.App;
using ForceBreak.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var path = Path.Combine(Path.GetTempPath(), "ForceBreak-NotesTest-" + Guid.NewGuid(), "notes.txt");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 0;
        app.Startup += (_, _) =>
        {
            try
            {
                CheckTomorrowMenu();
                using (var overlay = new RestOverlay(path))
                {
                    overlay.OpenManual();
                    var windows = app.Windows.Cast<Window>().ToArray();
                    Check(windows.Length > 0, "manual overlay opens on available displays");
                    var panel = (Grid)windows[0].Content;
                    var editor = Find<TextBox>(panel).Single();
                    var close = Find<Button>(panel).Single();
                    Check(close.Visibility == Visibility.Visible, "manual overlay can be closed without a timer");
                    editor.Text = "第一行\n可连续保存的笔记 🥕";
                    foreach (var window in windows)
                        Check(Find<TextBox>((Grid)window.Content).Single().Text == editor.Text, "all overlay displays share the notes");
                    overlay.ShowUntil(DateTimeOffset.UtcNow.AddMinutes(1), "测试休息");
                    Check(close.Visibility == Visibility.Collapsed, "active restriction hides close button");
                    Capture(windows[0], 1280, 720, "rest-1280.png");
                    Capture(windows[0], 900, 600, "rest-900.png");
                    overlay.OpenManual();
                    Check(close.Visibility == Visibility.Collapsed, "manual opening cannot unlock an enforced overlay");
                    overlay.ShowUntil(DateTimeOffset.UtcNow.AddSeconds(-1), "测试休息结束");
                    Check(overlay.IsVisible && windows[0].IsVisible && close.Visibility == Visibility.Visible, "expired overlay stays open and exposes close button");
                    Capture(windows[0], 1280, 720, "finished-1280.png");
                    Check(editor.Text.Contains("连续保存"), "expiry preserves typed text");
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!overlay.IsVisible && File.ReadAllText(path).Contains("连续保存"), "explicit close flushes notes to disk");
                    overlay.OpenManual();
                    Check(Find<TextBox>((Grid)app.Windows.Cast<Window>().First().Content).Single().Text.Contains("连续保存"), "reopening keeps notes");
                }
                using (var reopened = new RestOverlay(path))
                {
                    reopened.OpenManual();
                    Check(Find<TextBox>((Grid)app.Windows.Cast<Window>().First().Content).Single().Text.Contains("连续保存"), "new overlay instance reloads saved notes");
                }
                CheckPresence(app, path);
            }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static void CheckTomorrowMenu()
    {
        var schedule = new Schedule { TimeZoneId = "China Standard Time" };
        var evening = new DateTimeOffset(2026, 9, 11, 18, 0, 0, TimeSpan.FromHours(8));
        Check(!TrayController.IsTomorrowRestVisible(evening.AddTicks(-1), schedule), "tomorrow menu hidden before 18:00");
        Check(TrayController.IsTomorrowRestVisible(evening.ToUniversalTime(), schedule), "tomorrow menu appears at 18:00 in schedule timezone");
        Check(TrayController.IsTomorrowRestVisible(evening.AddHours(6).AddTicks(-1), schedule), "tomorrow menu remains visible before midnight");
        Check(!TrayController.IsTomorrowRestVisible(evening.AddHours(6), schedule), "tomorrow menu disappears at midnight");
        Check(!TrayController.IsTomorrowRestVisible(evening, null), "tomorrow menu hidden before schedule is known");
    }
    private static void CheckPresence(Application app, string path)
    {
        bool? currentDesktop = true;
        using var overlay = new RestOverlay(path, _ => currentDesktop);
        overlay.ShowUntil(DateTimeOffset.UtcNow.AddMinutes(1), "保持休息");
        var original = app.Windows.Cast<Window>().ToArray();
        var editor = Find<TextBox>((Grid)original[0].Content).Single();
        editor.Text = "切换桌面仍保留未保存的笔记";
        editor.Select(2, 4);
        original[0].Topmost = false;
        original[0].WindowState = WindowState.Minimized;
        overlay.RefreshPresence();
        Check(original[0].Topmost && original[0].WindowState == WindowState.Normal, "presence restores topmost and minimized windows");
        Check(editor.SelectionStart == 2 && editor.SelectionLength == 4, "ordinary refresh preserves selection");

        currentDesktop = false;
        overlay.RefreshPresence();
        var replacement = app.Windows.Cast<Window>().ToArray();
        Check(replacement.Length == original.Length && original.All(w => !w.IsVisible), "desktop change replaces all display windows");
        Check(replacement.All(w => !original.Contains(w)), "desktop change creates new windows");
        var restored = Find<TextBox>((Grid)replacement[0].Content).Single();
        Check(restored.Text == editor.Text && restored.SelectionStart == 2 && restored.SelectionLength == 4, "desktop change preserves unsaved notes and selection");
        Check(replacement.All(w => Find<Button>((Grid)w.Content).Single().Visibility == Visibility.Collapsed), "desktop change cannot unlock restriction");

        currentDesktop = true;
        overlay.RefreshPresence();
        Check(app.Windows.Cast<Window>().SequenceEqual(replacement), "current desktop does not recreate windows");
        currentDesktop = null;
        overlay.RefreshPresence();
        Check(app.Windows.Cast<Window>().SequenceEqual(replacement), "unavailable desktop API does not recreate windows repeatedly");
        overlay.ShowUntil(DateTimeOffset.UtcNow.AddSeconds(-1), "结束");
        var close = Find<Button>((Grid)replacement[0].Content).Single();
        Check(Equals(close.Content, "关闭"), "close button uses plain language");
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.RefreshPresence();
        Check(!overlay.IsVisible && app.Windows.Count == 0, "presence timer cannot reopen a closed overlay");
    }
    private static void Capture(Window window, int width, int height, string name)
    {
        window.Width = width; window.Height = height; window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.GetFullPath("out/overlay-previews"); Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var match in Find<T>(child)) yield return match;
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
