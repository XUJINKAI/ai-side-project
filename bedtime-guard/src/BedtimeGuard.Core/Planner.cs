namespace BedtimeGuard.Core;

public sealed class Planner(PlannerState state, Func<int> draw)
{
    public PlannerState State { get; } = state;

    public Status Tick(DateTimeOffset now)
    {
        if (State.Version != 3 || State.Draws is null) throw new InvalidDataException("Invalid planner state.");
        State.Schedule.Validate();
        if (State.Frozen is { } frozen)
        {
            if (!(frozen.CommitAt < frozen.RemindAt && frozen.RemindAt < frozen.LockAt && frozen.LockAt < frozen.ReleaseAt)
                || frozen.ReleaseAt - frozen.CommitAt > TimeSpan.FromHours(26))
                throw new InvalidDataException("Invalid frozen night; use administrator recovery.");
            if (now < frozen.ReleaseAt) return Describe(frozen, now);
            State.CompletedThrough = frozen.Date;
            State.Frozen = null;
        }

        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById(State.Schedule.TimeZoneId)).DateTime);
        // Persist the draw before the commitment window starts, even if temporarily disabled.
        GetDraw(date);
        foreach (var d in new[] { date.AddDays(-1), date })
        {
            if (!State.Schedule.Enabled || !State.Schedule.Days.Contains(d.DayOfWeek) ||
                State.CompletedThrough is { } completed && d <= completed) continue;
            var night = Build(d);
            if (now >= night.CommitAt && now < night.ReleaseAt)
            {
                State.Frozen = night;
                return Describe(night, now);
            }
        }
        foreach (var old in State.Draws.Keys.Where(d => d < date.AddDays(-2)).ToArray()) State.Draws.Remove(old);
        if (!State.Schedule.Enabled) return new(State.Schedule, Phase.Disabled, null, null, null, false);
        for (var i = 0; i < 8; i++)
        {
            var d = date.AddDays(i);
            if (!State.Schedule.Days.Contains(d.DayOfWeek) || State.CompletedThrough is { } done && d <= done) continue;
            var next = Build(d);
            if (now < next.CommitAt) return new(State.Schedule, Phase.Open, next.RemindAt, next.LockAt, next.ReleaseAt, false);
        }
        return new(State.Schedule, Phase.Open, null, null, null, false);
    }

    public Status Update(Schedule schedule, DateTimeOffset now)
    {
        // Freeze using OLD rules before accepting any changes. No race at the deadline.
        Tick(now);
        schedule.Validate();
        State.Schedule = schedule;
        return Tick(now); // Frozen night survives; new settings apply to later nights.
    }

    private Status Describe(Night n, DateTimeOffset now) => new(State.Schedule,
        now >= n.LockAt ? Phase.Restricted : now >= n.RemindAt ? Phase.Reminder : Phase.Committed,
        n.RemindAt, n.LockAt, n.ReleaseAt, n.DisableTaskManager, EffectiveBehavior: n.Behavior);

    private int GetDraw(DateOnly date)
    {
        if (!State.Draws.TryGetValue(date, out var value))
        {
            value = draw();
            if (value < 0 || value >= 1_000_000) throw new InvalidOperationException("Invalid random sample.");
            State.Draws[date] = value;
        }
        return value;
    }

    private Night Build(DateOnly date)
    {
        var s = State.Schedule;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
        var rangeMinutes = s.JitterMinutes;
        var minutes = (long)GetDraw(date) * (2 * rangeMinutes + 1) / 1_000_000 - rangeMinutes;
        return new(date,
            Resolve(date.ToDateTime(s.Commitment).AddMinutes(minutes), zone, false),
            Resolve(date.ToDateTime(s.Reminder), zone, false),
            Resolve(date.ToDateTime(s.Bedtime), zone, false),
            Resolve(date.AddDays(1).ToDateTime(s.Release), zone, true), s.DisableTaskManager, s.Behavior);
    }

    internal static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone, bool end)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Spring gap: use first valid local instant. Autumn overlap: earlier start/later end.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local)
            ? (end ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetAmbiguousTimeOffsets(local).Max())
            : zone.GetUtcOffset(local);
        return new(local, offset);
    }
}
