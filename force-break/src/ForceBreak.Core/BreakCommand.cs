using System.Globalization;

namespace ForceBreak.Core;

public sealed record BreakCommand(bool Help, Request? Request)
{
    public const string Usage = "Force Break\n\n-h                     显示帮助\n-break 30              立即休息 30 分钟（整数 1—120）\n-break-tomorrow 6      休息至明天 6:00（整数小时 0—23）\n\n解除时刻按早睡计划的时区计算；命令需要已安装并运行后台服务。\n有效承诺或休息不能被新命令替换。";
    public static BreakCommand Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "-h") return new(true, null);
        if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(Usage);
        return args[0] switch
        {
            "-break" when value is >= 1 and <= 120 => new(false, new("rest", RestMinutes: value)),
            "-break-tomorrow" when value is >= 0 and <= 23 => new(false, new("rest-tomorrow", TomorrowHour: value)),
            _ => throw new ArgumentException(Usage)
        };
    }
}
