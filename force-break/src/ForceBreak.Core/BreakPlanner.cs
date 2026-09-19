namespace ForceBreak.Core;

public sealed record BreakOptions
{
    public bool Enabled { get; init; }
    public double WorkMinutes { get; init; } = 50;
    public int RestMinutes { get; init; } = 10;
    public int ReminderMinutes { get; init; } = 10;
    public int CommitmentMinutes { get; init; } = 10;
    public bool RecognizeNaturalRest { get; init; } = true;
    public int NaturalRestMinutes { get; init; } = 10;
    public bool IdleCountsAsRest { get; init; } = true;
    public bool MergeWithBedtime { get; init; } = true;
    public int MinimumPostBreakMinutes { get; init; } = 10;

    public void Validate()
    {
        if (!double.IsFinite(WorkMinutes) || WorkMinutes < 11 || WorkMinutes > 1440 || WorkMinutes != Math.Truncate(WorkMinutes))
            throw new ArgumentException("休息间隔必须为 11—1440 分钟，必须是整数。");
        if (RestMinutes is < 1 or > 180) throw new ArgumentException("休息时长必须为 1—180 分钟。");
        if (ReminderMinutes < 0 || CommitmentMinutes < ReminderMinutes || WorkMinutes - CommitmentMinutes < 11)
            throw new ArgumentException("提前量必须满足 0 ≤ 提醒 ≤ 承诺，且工作间隔 − 承诺提前量至少为 11 分钟。");
        if (NaturalRestMinutes is < 1 or > 180) throw new ArgumentException("自然休息时长必须为 1—180 分钟。");
        if (MinimumPostBreakMinutes is < 0 or > 180) throw new ArgumentException("休息后最短可用时间必须为 0—180 分钟。");
    }
}

public sealed record FrozenBreak(DateTimeOffset RemindAt, DateTimeOffset LockAt,
    DateTimeOffset ReleaseAt, bool DisableTaskManager, BehaviorOptions Behavior, bool IsManual = false,
    bool UntilTomorrow = false, bool ResetsWork = true, DateTimeOffset? CoveredByNightAt = null);

public sealed class BreakState
{
    public double WorkSeconds { get; set; }
    public DateTimeOffset? RestStartedAt { get; set; }
    public FrozenBreak? Frozen { get; set; }
}

public sealed record BreakStatus(Phase Phase, double RemainingWorkSeconds,
    DateTimeOffset? RemindAt = null, DateTimeOffset? LockAt = null, DateTimeOffset? ReleaseAt = null,
    bool TaskManagerRequested = false, bool SuppressedByNight = false, BehaviorOptions? Behavior = null,
    double NaturalRestSeconds = 0, double NaturalRestRequiredSeconds = 0,
    DateTimeOffset? CoveredByNightAt = null, bool IsManual = false);

public static class BreakPlanner
{
    public static BreakStatus Tick(BreakState state, BreakOptions options, DateTimeOffset now,
        TimeSpan activeElapsed, WorkObservation activity, bool nightRestricted, bool disableTaskManager,
        BehaviorOptions? behavior = null, DateTimeOffset? guaranteedNightLockAt = null)
    {
        options.Validate();
        if (!double.IsFinite(state.WorkSeconds) || state.WorkSeconds < 0) throw new InvalidDataException("Invalid break timer.");
        if (state.RestStartedAt > now.AddMinutes(1)) throw new InvalidDataException("Invalid natural rest start.");

        if (nightRestricted && state.Frozen is not { IsManual: true })
        {
            Reset(state);
            return new(Phase.Disabled, options.WorkMinutes * 60, SuppressedByNight: true);
        }

        if (state.Frozen is { IsManual: true } manual)
        {
            ValidateFrozen(manual);
            if (now < manual.ReleaseAt) return Describe(manual, now, options, state);
            if (manual.ResetsWork) state.WorkSeconds = 0;
            state.Frozen = null;
            state.RestStartedAt = null;
            activeElapsed = TimeSpan.Zero;
        }

        if (state.Frozen is { } existing)
        {
            ValidateFrozen(existing);
            ObserveNaturalRest(state, options, now, activity);
            if (NaturalRestCompleted(state, options, now))
            {
                Reset(state);
                activeElapsed = TimeSpan.Zero;
            }
            else if (state.Frozen is { } pending)
            {
                if (pending.CoveredByNightAt is { } coveredAt)
                {
                    if (now < coveredAt)
                        return Covered(pending, coveredAt, now, options, state);
                    // The guaranteed night should normally have entered Restricted above. Fail safe if it did not.
                    pending = pending with
                    {
                        CoveredByNightAt = null,
                        RemindAt = now,
                        LockAt = now,
                        ReleaseAt = now.AddMinutes(options.RestMinutes)
                    };
                    state.Frozen = pending;
                    state.RestStartedAt = null;
                }

                if (now >= pending.LockAt && state.RestStartedAt is { } earlyStart)
                {
                    var naturalEnd = earlyStart.AddMinutes(options.NaturalRestMinutes);
                    if (naturalEnd < pending.ReleaseAt)
                    {
                        if (naturalEnd <= pending.LockAt)
                        {
                            Reset(state);
                            activeElapsed = TimeSpan.Zero;
                        }
                        else
                        {
                            pending = pending with { ReleaseAt = naturalEnd };
                            state.Frozen = pending;
                        }
                    }
                }
                if (state.Frozen is { } running)
                {
                    if (now < running.ReleaseAt) return Describe(running, now, options, state);
                    Reset(state);
                    activeElapsed = TimeSpan.Zero;
                }
            }
        }

        if (nightRestricted)
        {
            Reset(state);
            return new(Phase.Disabled, options.WorkMinutes * 60, SuppressedByNight: true);
        }
        if (!options.Enabled)
        {
            Reset(state);
            return new(Phase.Disabled, options.WorkMinutes * 60);
        }

        ObserveNaturalRest(state, options, now, activity);
        if (NaturalRestCompleted(state, options, now)) Reset(state);
        if (activity.Kind == WorkActivityKind.Active)
            state.WorkSeconds = Math.Min(options.WorkMinutes * 60,
                state.WorkSeconds + Math.Max(0, activeElapsed.TotalSeconds));

        var remaining = options.WorkMinutes * 60 - state.WorkSeconds;
        if (remaining <= options.CommitmentMinutes * 60)
        {
            var start = now.AddSeconds(remaining);
            var coveredAt = options.MergeWithBedtime && guaranteedNightLockAt is { } nightLock && nightLock > now &&
                nightLock <= start.AddMinutes(options.RestMinutes + options.MinimumPostBreakMinutes)
                ? nightLock : (DateTimeOffset?)null;
            state.Frozen = new(start.AddMinutes(-options.ReminderMinutes), start,
                start.AddMinutes(options.RestMinutes), disableTaskManager, behavior ?? new BehaviorOptions(),
                CoveredByNightAt: coveredAt);
            return coveredAt is { } at
                ? Covered(state.Frozen, at, now, options, state)
                : Describe(state.Frozen, now, options, state);
        }
        return Open(state, options, remaining, now);
    }

    // Compatibility overload for callers that do not classify activity themselves.
    public static BreakStatus Tick(BreakState state, BreakOptions options, DateTimeOffset now,
        TimeSpan unlockedElapsed, bool nightRestricted, bool disableTaskManager, BehaviorOptions? behavior = null) =>
        Tick(state, options, now, unlockedElapsed,
            unlockedElapsed > TimeSpan.Zero ? WorkObservation.Active : WorkObservation.Paused,
            nightRestricted, disableTaskManager, behavior);

    public static void StartCycleNow(BreakState state, BreakOptions options, DateTimeOffset now,
        Schedule schedule, Phase nightPhase)
    {
        options.Validate();
        if (nightPhase == Phase.Restricted) throw new InvalidOperationException("正在执行早睡限制，无需另开休息。");
        if (state.Frozen is { IsManual: true } manual && now < manual.ReleaseAt)
            throw new InvalidOperationException("手动休息正在执行，不能替换。");
        if (state.Frozen is { IsManual: false } running && now >= running.LockAt && running.CoveredByNightAt is null)
            return; // Idempotent at and after the deadline.
        if (!options.Enabled && state.Frozen is null)
            throw new InvalidOperationException("定时休息未启用，没有可提前开始的本轮休息。");
        var source = state.Frozen;
        var duration = source is null ? TimeSpan.FromMinutes(options.RestMinutes) : source.ReleaseAt - source.LockAt;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(3)) duration = TimeSpan.FromMinutes(options.RestMinutes);
        state.Frozen = new(now, now, now + duration,
            source?.DisableTaskManager ?? schedule.DisableTaskManager,
            source?.Behavior ?? schedule.Behavior);
        state.RestStartedAt = null;
    }

    public static void StartManual(BreakState state, DateTimeOffset now, int minutes, Schedule schedule, Phase nightPhase)
    {
        if (minutes is < 1 or > 120) throw new ArgumentException("手动休息时长必须为 1—120 整数分钟。");
        StartManualUntil(state, now, now.AddMinutes(minutes), schedule, nightPhase, false, true);
    }

    public static DateTimeOffset TomorrowRelease(Schedule schedule, DateTimeOffset now, int? hour = null)
    {
        if (hour is < 0 or > 23) throw new ArgumentException("明天解除的小时必须为 0—23 的整数。");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).AddDays(1);
        return Planner.Resolve(date.ToDateTime(hour is { } h ? new TimeOnly(h, 0) : schedule.Release), zone, true);
    }

    public static void StartTomorrow(BreakState state, DateTimeOffset now, Schedule schedule, Phase nightPhase, int? hour = null) =>
        StartManualUntil(state, now, TomorrowRelease(schedule, now, hour), schedule, nightPhase, true, true);

    private static void StartManualUntil(BreakState state, DateTimeOffset now, DateTimeOffset end,
        Schedule schedule, Phase nightPhase, bool tomorrow, bool resetsWork)
    {
        if (nightPhase == Phase.Restricted) throw new InvalidOperationException("正在执行早睡限制，无需另开休息。");
        if (state.Frozen is { } frozen && now < frozen.ReleaseAt)
            throw new InvalidOperationException("本轮休息已经承诺或正在执行，不能替换；结束后可开始新的休息。");
        state.WorkSeconds = 0;
        state.RestStartedAt = null;
        state.Frozen = new(now, now, end, schedule.DisableTaskManager, schedule.Behavior,
            true, tomorrow, resetsWork);
    }

    private static void ObserveNaturalRest(BreakState state, BreakOptions options, DateTimeOffset now, WorkObservation activity)
    {
        if (!options.RecognizeNaturalRest || activity.Kind != WorkActivityKind.Recovering)
        {
            state.RestStartedAt = null;
            return;
        }
        var observed = activity.Since is { } since && since <= now ? since : now;
        if (state.RestStartedAt is null || observed < state.RestStartedAt) state.RestStartedAt = observed;
    }

    private static bool NaturalRestCompleted(BreakState state, BreakOptions options, DateTimeOffset now) =>
        options.RecognizeNaturalRest && state.RestStartedAt is { } start &&
        now - start >= TimeSpan.FromMinutes(options.NaturalRestMinutes);

    private static BreakStatus Open(BreakState state, BreakOptions options, double remaining, DateTimeOffset now)
    {
        var natural = state.RestStartedAt is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0;
        return new(Phase.Open, remaining, NaturalRestSeconds: natural,
            NaturalRestRequiredSeconds: options.NaturalRestMinutes * 60);
    }

    private static BreakStatus Covered(FrozenBreak frozen, DateTimeOffset coveredAt, DateTimeOffset now,
        BreakOptions options, BreakState state) => new(Phase.Committed,
        Math.Max(0, (frozen.LockAt - now).TotalSeconds), frozen.RemindAt, frozen.LockAt,
        frozen.ReleaseAt, frozen.DisableTaskManager, Behavior: frozen.Behavior,
        NaturalRestSeconds: state.RestStartedAt is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0,
        NaturalRestRequiredSeconds: options.NaturalRestMinutes * 60, CoveredByNightAt: coveredAt);

    private static BreakStatus Describe(FrozenBreak frozen, DateTimeOffset now, BreakOptions options, BreakState state) => new(
        now >= frozen.LockAt ? Phase.Restricted : now >= frozen.RemindAt ? Phase.Reminder : Phase.Committed,
        Math.Max(0, (frozen.LockAt - now).TotalSeconds), frozen.RemindAt, frozen.LockAt,
        frozen.ReleaseAt, frozen.DisableTaskManager, Behavior: frozen.Behavior,
        NaturalRestSeconds: state.RestStartedAt is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0,
        NaturalRestRequiredSeconds: options.NaturalRestMinutes * 60, IsManual: frozen.IsManual);

    private static void ValidateFrozen(FrozenBreak frozen)
    {
        if (frozen.RemindAt > frozen.LockAt || frozen.LockAt >= frozen.ReleaseAt ||
            frozen.ReleaseAt - frozen.LockAt > TimeSpan.FromHours(frozen.UntilTomorrow ? 50 : 3) ||
            frozen.UntilTomorrow && !frozen.IsManual)
            throw new InvalidDataException("Invalid frozen break.");
    }

    private static void Reset(BreakState state)
    {
        state.WorkSeconds = 0;
        state.RestStartedAt = null;
        state.Frozen = null;
    }

    public static Status Combine(Status night, BreakStatus rest)
    {
        var protect = night.TaskManagerRequested || rest.TaskManagerRequested;
        var deadlines = new[] { night.TaskManagerRequested ? night.ReleaseAt : null,
            rest.TaskManagerRequested ? rest.ReleaseAt : null };
        var until = deadlines.Max();
        var chooseBreak = rest.CoveredByNightAt is null && night.Phase != Phase.Restricted && (
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
