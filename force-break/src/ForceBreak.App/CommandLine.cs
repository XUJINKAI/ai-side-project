using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ForceBreak.Core;
using ForceBreak.Windows;

namespace ForceBreak.App;

internal static class CommandLine
{
    public static int Run(string[] args)
    {
        var console = GetFileType(GetStdHandle(-11)) is 1 or 3 || AttachConsole(uint.MaxValue) || GetFileType(GetStdHandle(-11)) != 0;
        if (console) Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        void Output(string text)
        {
            if (console) Console.WriteLine(text);
            else MessageBox.Show(text, "Force Break");
        }
        BreakCommand command;
        try { command = BreakCommand.Parse(args); }
        catch (ArgumentException error) { Output(error.Message); return 2; }
        if (command.Help) { Output(BreakCommand.Usage); return 0; }
        try
        {
            var result = Wire.Send(command.Request!).GetAwaiter().GetResult();
            if (!result.Ok || result.Status is null) throw new InvalidOperationException(result.Error);
            if (console) Console.WriteLine($"休息已开始，解除时间：{result.Status.ReleaseAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
            return 0;
        }
        catch (Exception error) { Output("无法开始休息。请确认已安装并启动后台服务。\n" + error.Message); return 1; }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachConsole(uint processId);
}
