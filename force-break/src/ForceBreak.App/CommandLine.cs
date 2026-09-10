using System;
using System.Windows;
using ForceBreak.Core;
using ForceBreak.Windows;

namespace ForceBreak.App;

internal static class CommandLine
{
    public static int Run(string[] args)
    {
        using var output = CommandOutput.Open();
        void Output(string text)
        {
            if (output is not null) output.WriteLine(text);
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
            if (output is not null) output.WriteLine($"休息已开始，解除时间：{result.Status.ReleaseAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
            return 0;
        }
        catch (Exception error) { Output("无法开始休息。请确认已安装并启动后台服务。\n" + error.Message); return 1; }
    }
}
