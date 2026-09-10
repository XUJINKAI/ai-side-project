namespace ForceBreak.Core;

public enum Phase { Disabled, Open, Committed, Reminder, Restricted }

public sealed record Schedule
{
    public bool Enabled { get; init; }
    public TimeOnly Commitment { get; init; } = new(20, 0);
    public int JitterMinutes { get; init; } = 30;
    public TimeOnly Reminder { get; init; } = new(21, 30);
    public TimeOnly Bedtime { get; init; } = new(22, 30);
    public TimeOnly Release { get; init; } = new(6, 0);
    public DayOfWeek[] Days { get; init; } = Enum.GetValues<DayOfWeek>();
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public bool DisableTaskManager { get; init; } = true;
    public BehaviorOptions Behavior { get; init; } = new();
    public BreakOptions Breaks { get; init; } = new();

    public void Validate()
    {
        Breaks.Validate();
        Behavior.Validate();
        if (new[] { Commitment, Reminder, Bedtime, Release }.Any(t => t.Ticks % TimeSpan.TicksPerMinute != 0))
            throw new ArgumentException("计划时间最小单位为分钟，不能包含秒。");
        if (JitterMinutes is < 0 or > 120) throw new ArgumentException("随机范围必须为 0—120 分钟。");
        var earliest = Commitment.ToTimeSpan() - TimeSpan.FromMinutes(JitterMinutes);
        var latest = Commitment.ToTimeSpan() + TimeSpan.FromMinutes(JitterMinutes);
        if (earliest < TimeSpan.Zero || latest >= Reminder.ToTimeSpan())
            throw new ArgumentException("整个随机承诺时间范围必须在当天内，且严格早于首次提醒。");
        if (Reminder >= Bedtime) throw new ArgumentException("首次提醒必须早于限制开始时间。");
        if (Release.ToTimeSpan() >= earliest)
            throw new ArgumentException("次日解除时间必须早于最早承诺时间，避免两晚重叠。");
        if (Days is null || Days.Length == 0 || Days.Any(d => !Enum.IsDefined(d)) || Days.Distinct().Count() != Days.Length)
            throw new ArgumentException("请选择有效且不重复的星期。");
        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    }
}

// A night belongs to the calendar date on which commitment/reminder/bedtime occur.
public sealed record Night(DateOnly Date, DateTimeOffset CommitAt, DateTimeOffset RemindAt,
    DateTimeOffset LockAt, DateTimeOffset ReleaseAt, bool DisableTaskManager, BehaviorOptions Behavior);

public sealed class PlannerState
{
    public int Version { get; set; } = 1;
    public Schedule Schedule { get; set; } = new();
    public Dictionary<DateOnly, int> Draws { get; set; } = [];
    public Night? Frozen { get; set; }
    public DateOnly? CompletedThrough { get; set; }
    public BreakState Break { get; set; } = new();
}

// Exact random commitment timestamps and random draws never leave the service.
public sealed record Status(Schedule Schedule, Phase Phase, DateTimeOffset? ReminderAt,
    DateTimeOffset? LockAt, DateTimeOffset? ReleaseAt, bool TaskManagerRequested,
    string? PolicyMessage = null, string? Error = null, BreakStatus? Break = null,
    bool IsBreak = false, DateTimeOffset? PolicyUntil = null, BehaviorOptions? EffectiveBehavior = null,
    bool WorkTimerPaused = false, string? ActivityMessage = null);

public sealed record Request(string Command, Schedule? Schedule = null, int? RestMinutes = null, ActivityReport? Activity = null, int? TomorrowHour = null);
public sealed record Response(bool Ok, Status? Status = null, string? Error = null);
