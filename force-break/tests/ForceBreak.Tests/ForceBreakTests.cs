using ForceBreak.Core;

internal static class ForceBreakTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    public static (string Name, Action Run)[] Cases =>
    [
        ("Force Break fresh defaults", () =>
        {
            var s = new Schedule(); s.Validate();
            Check(s.Commitment == new TimeOnly(20, 0) && s.JitterMinutes == 30 && s.Reminder == new TimeOnly(21, 30)
                && s.Bedtime == new TimeOnly(22, 30) && s.Release == new TimeOnly(6, 0));
            Check(s.Breaks.WorkMinutes == 50 && s.Breaks.RestMinutes == 10 && s.Breaks.ReminderMinutes == 10 && s.Breaks.CommitmentMinutes == 10);
            Check(s.DisableTaskManager && s.Behavior.DetectActivity && s.Behavior.FullscreenOverlay && !s.Behavior.LockScreen);
        }),
        ("50 work plus 10 rest resets at minute 60", () =>
        {
            var s = new Schedule { Breaks = new() { Enabled = true } }; var b = new BreakState();
            var result = BreakPlanner.Tick(b, s.Breaks, Now, TimeSpan.FromMinutes(40), false, true, s.Behavior);
            Check(result.Phase == Phase.Reminder && result.LockAt == Now.AddMinutes(10) && result.ReleaseAt == Now.AddMinutes(20));
            Check(BreakPlanner.Tick(b, s.Breaks, Now.AddMinutes(20), TimeSpan.FromMinutes(1), false, true).Phase == Phase.Open);
            Check(b.WorkSeconds == 0);
        }),
        ("activity leases pause idle, unavailable and missing agents", () =>
        {
            var lease = new ActivityLease(); var now = TimeSpan.FromHours(1);
            Check(!lease.IsActive(now, 1));
            lease.Update(new(true, 58), now);
            Check(lease.IsActive(now + TimeSpan.FromSeconds(1), 1));
            Check(!lease.IsActive(now + TimeSpan.FromSeconds(2), 1));
            lease.Update(new(true, 0), now);
            Check(!lease.IsActive(now + TimeSpan.FromSeconds(6), 1));
            lease.Update(new(false, 0), now); Check(!lease.IsActive(now, 1));
            lease.Update(new(true, 0), now + TimeSpan.FromMinutes(5));
            Check(lease.IsActive(now + TimeSpan.FromMinutes(5), 1));
            Throws(() => lease.Update(new(true, double.NaN), now));
            Throws(() => lease.Update(new(true, -1), now));
        }),
        ("behavior settings cannot change a frozen night or break", () =>
        {
            var state = new PlannerState { Schedule = new() { Enabled = true, TimeZoneId = "UTC" } };
            var planner = new Planner(state, () => 0); var at = Now.AddHours(8);
            planner.Tick(at);
            var changed = state.Schedule with { Behavior = new() { FullscreenOverlay = false, LockScreen = true } };
            var result = planner.Update(changed, at.AddMinutes(1));
            Check(result.EffectiveBehavior is { FullscreenOverlay: true, LockScreen: false });
            var b = new BreakState(); var options = new BreakOptions { Enabled = true };
            BreakPlanner.Tick(b, options, Now, TimeSpan.FromMinutes(40), false, true, new());
            var rest = BreakPlanner.Tick(b, options, Now.AddMinutes(11), TimeSpan.Zero, false, false, changed.Behavior);
            Check(rest.Phase == Phase.Restricted && rest.Behavior is { FullscreenOverlay: true, LockScreen: false } && rest.TaskManagerRequested);
        }),
        ("manual rest persists, works when disabled, and resets work", () =>
        {
            foreach (var minutes in new[] { 5, 10, 30 })
            {
                var state = new PlannerState(); state.Break.WorkSeconds = 200;
                BreakPlanner.StartManual(state.Break, Now, minutes, state.Schedule, Phase.Disabled);
                state = JsonStorage.Clone(state);
                Check(state.Break.WorkSeconds == 0);
                var result = BreakPlanner.Tick(state.Break, state.Schedule.Breaks, Now, TimeSpan.Zero, false, true);
                Check(result.Phase == Phase.Restricted && result.ReleaseAt == Now.AddMinutes(minutes));
                Check(result.Behavior is { FullscreenOverlay: true, LockScreen: false });
                Check(BreakPlanner.Tick(state.Break, state.Schedule.Breaks, Now.AddMinutes(minutes), TimeSpan.FromMinutes(1), false, true).Phase == Phase.Disabled);
            }
        }),
        ("manual rest cannot replace commitments or night restrictions", () =>
        {
            var s = new Schedule(); var b = new BreakState();
            Throws(() => BreakPlanner.StartManual(b, Now, 3, s, Phase.Open));
            Throws(() => BreakPlanner.StartManual(b, Now, 5, s, Phase.Restricted));
            BreakPlanner.Tick(b, new() { Enabled = true }, Now, TimeSpan.FromMinutes(40), false, true, s.Behavior);
            var before = b.Frozen;
            Throws(() => BreakPlanner.StartManual(b, Now, 5, s, Phase.Open)); Check(b.Frozen == before);
        }),
        ("manual rest and committed night retain both protection deadlines", () =>
        {
            var state = new PlannerState { Schedule = new() { Enabled = true, TimeZoneId = "UTC" } };
            var now = Now.AddHours(8); var night = new Planner(state, () => 0).Tick(now);
            BreakPlanner.StartManual(state.Break, now, 5, state.Schedule, night.Phase);
            var rest = BreakPlanner.Tick(state.Break, state.Schedule.Breaks, now, TimeSpan.Zero, false, true);
            var combined = BreakPlanner.Combine(night, rest);
            Check(combined.IsBreak && combined.Phase == Phase.Restricted && combined.PolicyUntil == night.ReleaseAt);
            Check(state.Frozen is not null);
        }),
        ("idle threshold validation", () =>
        {
            Throws(() => (new Schedule { Behavior = new() { IdleMinutes = 0 } }).Validate());
            Throws(() => (new Schedule { Behavior = new() { IdleMinutes = 61 } }).Validate());
        })
    ];
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }
}
