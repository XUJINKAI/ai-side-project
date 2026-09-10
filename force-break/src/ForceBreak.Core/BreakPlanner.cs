namespace ForceBreak.Core;

public sealed record BreakOptions
{
    public bool Enabled { get; init; }
    public double WorkMinutes { get; init; } = 50;
    public int RestMinutes { get; init; } = 10;
    public int ReminderMinutes { get; init; } = 10;
    public int CommitmentMinutes { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(WorkMinutes) || WorkMinutes < 11 || WorkMinutes > 1440 || WorkMinutes != Math.Truncate(WorkMinutes))
            throw new ArgumentException("休息间隔必须为 11—1440 分钟，必须是整数。");
        if (RestMinutes is < 1 or > 180) throw new ArgumentException("休息时长必须为 1—180 分钟。");
        if (ReminderMinutes < 0 || CommitmentMinutes < ReminderMinutes || WorkMinutes - CommitmentMinutes < 11)
            throw new ArgumentException("提前量必须满足 0 ≤ 提醒 ≤ 承诺，且工作间隔 − 承诺提前量至少为 11 分钟。");
    }
}

public sealed record FrozenBreak(DateTimeOffset RemindAt, DateTimeOffset LockAt,
    DateTimeOffset ReleaseAt, bool DisableTaskManager, BehaviorOptions Behavior, bool IsManual = false, bool UntilTomorrow = false);

public sealed class BreakState
{
    public double WorkSeconds { get; set; }
    public FrozenBreak? Frozen { get; set; }
}

public sealed record BreakStatus(Phase Phase, double RemainingWorkSeconds,
    DateTimeOffset? RemindAt = null, DateTimeOffset? LockAt = null, DateTimeOffset? ReleaseAt = null,
    bool TaskManagerRequested = false, bool SuppressedByNight = false, BehaviorOptions? Behavior = null);

public static class BreakPlanner
{
    public static BreakStatus Tick(BreakState state, BreakOptions options, DateTimeOffset now,
        TimeSpan unlockedElapsed, bool nightRestricted, bool disableTaskManager, BehaviorOptions? behavior = null)
    {
        options.Validate();
        if (!double.IsFinite(state.WorkSeconds) || state.WorkSeconds < 0) throw new InvalidDataException("Invalid break timer.");
        if (nightRestricted && state.Frozen is not { IsManual: true })
        {
            state.WorkSeconds = 0;
            state.Frozen = null;
            return new(Phase.Disabled, options.WorkMinutes * 60, SuppressedByNight: true);
        }
        if (state.Frozen is { } existing)
        {
            if (existing.RemindAt > existing.LockAt || existing.LockAt >= existing.ReleaseAt ||
                existing.ReleaseAt - existing.LockAt > TimeSpan.FromHours(existing.UntilTomorrow ? 50 : 3)
                || existing.UntilTomorrow && !existing.IsManual)
                throw new InvalidDataException("Invalid frozen break.");
            if (now < existing.ReleaseAt) return Describe(existing, now);
            // A completed break starts a fresh work interval. Do not count the final rest tick.
            state.WorkSeconds = 0;
            state.Frozen = null;
            unlockedElapsed = TimeSpan.Zero;
        }
        if (nightRestricted)
        {
            state.WorkSeconds = 0;
            return new(Phase.Disabled, options.WorkMinutes * 60, SuppressedByNight: true);
        }
        if (!options.Enabled)
        {
            state.WorkSeconds = 0;
            return new(Phase.Disabled, options.WorkMinutes * 60);
        }
        state.WorkSeconds = Math.Min(options.WorkMinutes * 60,
            state.WorkSeconds + Math.Max(0, unlockedElapsed.TotalSeconds));
        var remaining = options.WorkMinutes * 60 - state.WorkSeconds;
        if (remaining <= options.CommitmentMinutes * 60)
        {
            var start = now.AddSeconds(remaining);
            state.Frozen = new(start.AddMinutes(-options.ReminderMinutes), start,
                start.AddMinutes(options.RestMinutes), disableTaskManager, behavior ?? new BehaviorOptions());
            return Describe(state.Frozen, now);
        }
        return new(Phase.Open, remaining);
    }

    public static void StartManual(BreakState state, DateTimeOffset now, int minutes, Schedule schedule, Phase nightPhase)
    {
        if (minutes is < 1 or > 120) throw new ArgumentException("手动休息时长必须为 1—120 整数分钟。");
        StartManualUntil(state, now, now.AddMinutes(minutes), schedule, nightPhase, false);
    }

    public static DateTimeOffset TomorrowRelease(Schedule schedule, DateTimeOffset now, int? hour = null)
    {
        if (hour is < 0 or > 23) throw new ArgumentException("明天解除的小时必须为 0—23 的整数。");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).AddDays(1);
        return Planner.Resolve(date.ToDateTime(hour is { } h ? new TimeOnly(h, 0) : schedule.Release), zone, true);
    }

    public static void StartTomorrow(BreakState state, DateTimeOffset now, Schedule schedule, Phase nightPhase, int? hour = null) =>
        StartManualUntil(state, now, TomorrowRelease(schedule, now, hour), schedule, nightPhase, true);

    private static void StartManualUntil(BreakState state, DateTimeOffset now, DateTimeOffset end, Schedule schedule, Phase nightPhase, bool tomorrow)
    {
        if (nightPhase == Phase.Restricted) throw new InvalidOperationException("正在执行早睡限制，无需另开休息。");
        if (state.Frozen is { } frozen && now < frozen.ReleaseAt)
            throw new InvalidOperationException("本轮休息已经承诺或正在执行，不能替换；结束后可开始新的休息。");
        state.WorkSeconds = 0;
        state.Frozen = new(now, now, end, schedule.DisableTaskManager, schedule.Behavior, true, tomorrow);
    }

    private static BreakStatus Describe(FrozenBreak frozen, DateTimeOffset now) => new(
        now >= frozen.LockAt ? Phase.Restricted : now >= frozen.RemindAt ? Phase.Reminder : Phase.Committed,
        Math.Max(0, (frozen.LockAt - now).TotalSeconds), frozen.RemindAt, frozen.LockAt,
        frozen.ReleaseAt, frozen.DisableTaskManager, Behavior: frozen.Behavior);

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
        var behavior = chooseBreak ? rest.Behavior : night.EffectiveBehavior;
        if (night.Phase == Phase.Restricted && rest.Phase == Phase.Restricted && rest.Behavior is { } restBehavior && night.EffectiveBehavior is { } nightBehavior)
            behavior = nightBehavior with { FullscreenOverlay = nightBehavior.FullscreenOverlay || restBehavior.FullscreenOverlay,
                LockScreen = nightBehavior.LockScreen || restBehavior.LockScreen };
        return night with
        {
            Phase = chooseBreak ? rest.Phase : night.Phase,
            ReminderAt = chooseBreak ? rest.RemindAt : night.ReminderAt,
            LockAt = chooseBreak ? rest.LockAt : night.LockAt,
            ReleaseAt = night.Phase == Phase.Restricted && rest.Phase == Phase.Restricted
                ? new[] { night.ReleaseAt, rest.ReleaseAt }.Max() : chooseBreak ? rest.ReleaseAt : night.ReleaseAt,
            TaskManagerRequested = protect, PolicyUntil = until, Break = rest, IsBreak = chooseBreak,
            EffectiveBehavior = behavior
        };
    }
}
