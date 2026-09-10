namespace BedtimeGuard.Core;

public sealed record BreakOptions
{
    public bool Enabled { get; init; }
    public double WorkHours { get; init; } = 1;
    public int RestMinutes { get; init; } = 10;
    public int ReminderMinutes { get; init; } = 10;
    public int CommitmentMinutes { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(WorkHours) || WorkHours < 0.1 || WorkHours > 24)
            throw new ArgumentException("休息间隔必须为 0.1—24 小时，可使用小数。");
        if (RestMinutes is < 1 or > 180) throw new ArgumentException("休息时长必须为 1—180 分钟。");
        if (ReminderMinutes < 0 || CommitmentMinutes < ReminderMinutes || CommitmentMinutes >= WorkHours * 60)
            throw new ArgumentException("承诺提前量必须不少于提醒提前量，且两者均需小于工作间隔。提前量可以为 0。");
    }
}

public sealed record FrozenBreak(DateTimeOffset RemindAt, DateTimeOffset LockAt,
    DateTimeOffset ReleaseAt, bool DisableTaskManager);

public sealed class BreakState
{
    public double WorkSeconds { get; set; }
    public FrozenBreak? Frozen { get; set; }
}

public sealed record BreakStatus(Phase Phase, double RemainingWorkSeconds,
    DateTimeOffset? RemindAt = null, DateTimeOffset? LockAt = null, DateTimeOffset? ReleaseAt = null,
    bool TaskManagerRequested = false, bool SuppressedByNight = false);

public static class BreakPlanner
{
    public static BreakStatus Tick(BreakState state, BreakOptions options, DateTimeOffset now,
        TimeSpan unlockedElapsed, bool nightRestricted, bool disableTaskManager)
    {
        options.Validate();
        if (!double.IsFinite(state.WorkSeconds) || state.WorkSeconds < 0) throw new InvalidDataException("Invalid break timer.");
        if (nightRestricted)
        {
            state.WorkSeconds = 0;
            state.Frozen = null;
            return new(Phase.Disabled, options.WorkHours * 3600, SuppressedByNight: true);
        }
        if (state.Frozen is { } existing)
        {
            if (existing.RemindAt > existing.LockAt || existing.LockAt >= existing.ReleaseAt ||
                existing.ReleaseAt - existing.LockAt > TimeSpan.FromHours(3))
                throw new InvalidDataException("Invalid frozen break.");
            if (now < existing.ReleaseAt) return Describe(existing, now);
            // A completed break starts a fresh work interval. Do not count the final rest tick.
            state.WorkSeconds = 0;
            state.Frozen = null;
            unlockedElapsed = TimeSpan.Zero;
        }
        if (!options.Enabled)
        {
            state.WorkSeconds = 0;
            return new(Phase.Disabled, options.WorkHours * 3600);
        }
        state.WorkSeconds = Math.Min(options.WorkHours * 3600,
            state.WorkSeconds + Math.Max(0, unlockedElapsed.TotalSeconds));
        var remaining = options.WorkHours * 3600 - state.WorkSeconds;
        if (remaining <= options.CommitmentMinutes * 60)
        {
            var start = now.AddSeconds(remaining);
            state.Frozen = new(start.AddMinutes(-options.ReminderMinutes), start,
                start.AddMinutes(options.RestMinutes), disableTaskManager);
            return Describe(state.Frozen, now);
        }
        return new(Phase.Open, remaining);
    }

    private static BreakStatus Describe(FrozenBreak frozen, DateTimeOffset now) => new(
        now >= frozen.LockAt ? Phase.Restricted : now >= frozen.RemindAt ? Phase.Reminder : Phase.Committed,
        Math.Max(0, (frozen.LockAt - now).TotalSeconds), frozen.RemindAt, frozen.LockAt,
        frozen.ReleaseAt, frozen.DisableTaskManager);

    public static Status Combine(Status night, BreakStatus rest)
    {
        var protect = night.TaskManagerRequested || rest.TaskManagerRequested;
        var deadlines = new[] { night.TaskManagerRequested ? night.ReleaseAt : null,
            rest.TaskManagerRequested ? rest.ReleaseAt : null };
        var until = deadlines.Max();
        var chooseBreak = night.Phase != Phase.Restricted && (
            rest.Phase == Phase.Restricted ||
            rest.Phase == Phase.Reminder && (night.Phase != Phase.Reminder || rest.LockAt <= night.LockAt) ||
            rest.Phase == Phase.Committed && night.Phase is Phase.Disabled or Phase.Open ||
            rest.Phase == Phase.Open && night.Phase == Phase.Disabled);
        return night with
        {
            Phase = chooseBreak ? rest.Phase : night.Phase,
            ReminderAt = chooseBreak ? rest.RemindAt : night.ReminderAt,
            LockAt = chooseBreak ? rest.LockAt : night.LockAt,
            ReleaseAt = chooseBreak ? rest.ReleaseAt : night.ReleaseAt,
            TaskManagerRequested = protect, PolicyUntil = until, Break = rest, IsBreak = chooseBreak
        };
    }
}
