using System.IO;
using System.Windows;
using System.Windows.Controls;
using ForceBreak.App;

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
                using (var overlay = new RestOverlay(path))
                {
                    overlay.OpenManual();
                    var windows = app.Windows.Cast<Window>().ToArray();
                    Check(windows.Length > 0, "manual overlay opens on available displays");
                    var panel = (Grid)windows[0].Content;
                    var editor = panel.Children.OfType<TextBox>().Single();
                    var close = panel.Children.OfType<Button>().Single();
                    Check(close.Visibility == Visibility.Visible, "manual overlay can be closed without a timer");
                    editor.Text = "第一行\n可连续保存的笔记 🥕";
                    foreach (var window in windows)
                        Check(((Grid)window.Content).Children.OfType<TextBox>().Single().Text == editor.Text, "all overlay displays share the notes");
                    overlay.ShowUntil(DateTimeOffset.UtcNow.AddMinutes(1), "测试休息");
                    Check(close.Visibility == Visibility.Collapsed, "active restriction hides close button");
                    overlay.OpenManual();
                    Check(close.Visibility == Visibility.Collapsed, "manual opening cannot unlock an enforced overlay");
                    overlay.ShowUntil(DateTimeOffset.UtcNow.AddSeconds(-1), "测试休息结束");
                    Check(overlay.IsVisible && windows[0].IsVisible && close.Visibility == Visibility.Visible, "expired overlay stays open and exposes close button");
                    Check(editor.Text.Contains("连续保存"), "expiry preserves typed text");
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!overlay.IsVisible && File.ReadAllText(path).Contains("连续保存"), "explicit close flushes notes to disk");
                    overlay.OpenManual();
                    Check(((Grid)app.Windows.Cast<Window>().First().Content).Children.OfType<TextBox>().Single().Text.Contains("连续保存"), "reopening keeps notes");
                }
                using (var reopened = new RestOverlay(path))
                {
                    reopened.OpenManual();
                    Check(((Grid)app.Windows.Cast<Window>().First().Content).Children.OfType<TextBox>().Single().Text.Contains("连续保存"), "new overlay instance reloads saved notes");
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); app.Shutdown(); }
        };
        app.Run(); return result;
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
