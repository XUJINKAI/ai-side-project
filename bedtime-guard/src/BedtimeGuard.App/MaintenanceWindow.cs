using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BedtimeGuard.Service;

namespace BedtimeGuard.App;

internal static class MaintenanceLauncher
{
    public static string Label(MaintenanceAction action) => action switch
    {
        MaintenanceAction.Install => "安装", MaintenanceAction.Repair => "恢复（暂停限制）",
        MaintenanceAction.Resume => "重新启用", MaintenanceAction.Uninstall => "卸载", _ => "维护"
    };

    public static async Task Run(MaintenanceAction action, string directory, string? emergencyConfirmation = null)
    {
        var description = action switch
        {
            MaintenanceAction.Install => $"安装到：{directory}\n安装完成后，请配置并启用睡眠计划。",
            MaintenanceAction.Uninstall => "将停止限制、恢复任务管理器，并删除已安装的程序和计划数据。",
            MaintenanceAction.Repair => "立即暂停限制并恢复任务管理器。保存的睡眠计划会保留。",
            _ => "恢复已保存的计划。如果今晚仍在限制期，会继续限制使用。"
        };
        if (MessageBox.Show(description, Label(action), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        string? temporary = null;
        try
        {
            if (action == MaintenanceAction.Install) NativeInstaller.ValidateDirectory(directory);
            var source = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "BedtimeGuard.dll")))
                throw new InvalidOperationException("安装管理请使用发布的单文件 BedtimeGuard.exe。");
            temporary = Path.Combine(Path.GetTempPath(), "BedtimeGuard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var helper = Path.Combine(temporary, "BedtimeGuard.exe");
            File.Copy(source, helper);
            using var identity = WindowsIdentity.GetCurrent();
            var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas" };
            foreach (var argument in new[] { "--maintenance", action.ToString(), identity.User!.Value, directory, "--temporary-helper" })
                start.ArgumentList.Add(argument);
            if (emergencyConfirmation is not null) start.ArgumentList.Add(emergencyConfirmation);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动维护界面。");
            await process.WaitForExitAsync();
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { /* User cancelled UAC. */ }
        catch (Exception e) { MessageBox.Show(e.Message, Label(action), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            if (temporary is not null)
                try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static void CleanupTemporaryHelper()
    {
        var executable = Environment.ProcessPath;
        if (executable is null) return;
        var directory = Path.GetDirectoryName(executable)!;
        // If uninstall terminated the parent UI, Windows removes the running helper at next boot.
        if (Path.GetFileName(directory).StartsWith("BedtimeGuard-", StringComparison.Ordinal)
            && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(directory)!),
                Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        {
            MoveFileEx(executable, null, 4);
            MoveFileEx(directory, null, 4);
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string? destination, uint flags);
}

internal sealed class MaintenanceWindow : Window
{
    public MaintenanceWindow(MaintenanceAction action, string sid, string directory, string? emergencyConfirmation = null)
    {
        Title = "Bedtime Guard · " + MaintenanceLauncher.Label(action);
        Width = 540; Height = 300; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = MaintenanceLauncher.Label(action), FontSize = 24 });
        var result = new TextBlock { Text = "正在处理，请稍候…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 24, 0, 20) };
        panel.Children.Add(result);
        var close = new Button { Content = "关闭", IsEnabled = false, Padding = new Thickness(20, 8, 20, 8), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(close); Content = panel;
        var busy = true; var code = 1;
        Closing += (_, e) => e.Cancel = busy;
        close.Click += (_, _) => Application.Current.Shutdown(code);
        Loaded += async (_, _) =>
        {
            try
            {
                await Task.Run(() => NativeInstaller.Execute(action, sid, directory, Environment.ProcessPath!, emergencyConfirmation));
                result.Text = action == MaintenanceAction.Install ? "安装完成。请关闭此窗口，返回主界面配置并启用计划。" : MaintenanceLauncher.Label(action) + "已完成。";
                code = 0;
            }
            catch (Exception e) { result.Text = "操作未完成：" + e.Message; }
            finally { busy = false; close.IsEnabled = true; }
        };
    }
}
