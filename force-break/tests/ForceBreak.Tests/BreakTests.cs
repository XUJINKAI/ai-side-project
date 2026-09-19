using ForceBreak.Core;

internal static class BreakTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly BreakOptions Options = new() { Enabled = true, WorkMinutes = 60 };
    public static (string Name, Action Run)[] Cases =>
    [
        ("60 minute work interval: 50 minute reminder, 60 minute lock, 70 minute release", () =>
        {
            var state = new BreakState();
            Equal(Phase.Open, Tick(state, 49, 49).Phase);
            Equal(Phase.Reminder, Tick(state, 50, 1).Phase);
            Equal(Start.AddMinutes(60), state.Frozen!.LockAt);
            Equal(Start.AddMinutes(70), state.Frozen.ReleaseAt);
            Equal(Phase.Restricted, Tick(state, 60).Phase);
            Equal(Phase.Restricted, Tick(state, 69.99).Phase);
            Equal(Phase.Open, Tick(state, 70, 1).Phase);
            Equal(0d, state.WorkSeconds);
        }),
        ("separate break commitment and reminder boundaries", () =>
        {
            var state = new BreakState(); var options = Options with { CommitmentMinutes = 20, ReminderMinutes = 5 };
            Equal(Phase.Committed, Tick(state, 40, 40, options).Phase);
            Equal(Phase.Committed, Tick(state, 54, 0, options).Phase);
            Equal(Phase.Reminder, Tick(state, 55, 0, options).Phase);
        }),
        ("break freeze survives disable and all timing/policy edits", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50); var frozen = state.Frozen;
            var edited = Options with { Enabled = false, WorkMinutes = 120, RestMinutes = 1, ReminderMinutes = 0, CommitmentMinutes = 0 };
            Equal(Phase.Reminder, Tick(state, 51, 0, edited).Phase);
            Equal(frozen, state.Frozen);
            Equal(Phase.Restricted, Tick(state, 61, 0, edited).Phase);
            Equal(Phase.Disabled, Tick(state, 70, 0, edited).Phase);
        }),
        ("unlocked counter pauses before commitment", () =>
        {
            var state = new BreakState(); Tick(state, 20, 20);
            Equal(Phase.Open, Tick(state, 180).Phase);
            Equal(1200d, state.WorkSeconds);
            Equal(Phase.Reminder, Tick(state, 210, 30).Phase);
            Equal(Start.AddMinutes(220), state.Frozen!.LockAt);
        }),
        ("sleep after commitment keeps absolute deadlines, no overdue break", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50);
            Equal(Phase.Restricted, Tick(state, 65).Phase);
            Equal(Phase.Open, Tick(state, 180).Phase);
            Equal(0d, state.WorkSeconds);
        }),
        ("restart preserves break progress and frozen snapshot", () =>
        {
            var saved = new PlannerState(); Tick(saved.Break, 25, 25);
            var restart = JsonStorage.Clone(saved); Equal(1500d, restart.Break.WorkSeconds);
            Tick(restart.Break, 50, 25);
            var again = JsonStorage.Clone(restart);
            Equal(restart.Break.Frozen, again.Break.Frozen);
            Equal(Phase.Restricted, Tick(again.Break, 60).Phase);
        }),
        ("night restriction supersedes and resets periodic breaks", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50);
            var result = BreakPlanner.Tick(state, Options, Start.AddMinutes(51), TimeSpan.Zero, true, true);
            Equal(true, result.SuppressedByNight); Equal<FrozenBreak?>(null, state.Frozen); Equal(0d, state.WorkSeconds);
            Equal(3600d, Tick(state, 600).RemainingWorkSeconds);
        }),
        ("combined status selects break lock but preserves later night policy deadline", () =>
        {
            var night = new Status(new(), Phase.Committed, Start.AddHours(13), Start.AddHours(14), Start.AddHours(21), true);
            var rest = new BreakStatus(Phase.Restricted, 0, Start.AddMinutes(50), Start.AddHours(1), Start.AddMinutes(70), true);
            var result = BreakPlanner.Combine(night, rest);
            Equal(true, result.IsBreak); Equal(Phase.Restricted, result.Phase);
            Equal(rest.ReleaseAt, result.ReleaseAt); Equal(night.ReleaseAt, result.PolicyUntil);
            Equal(false, BreakPlanner.Combine(night with { Phase = Phase.Restricted }, rest).IsBreak);
        }),
        ("combined reminders select nearest lock deadline", () =>
        {
            var night = new Status(new(), Phase.Reminder, Start, Start.AddMinutes(30), Start.AddHours(8), false);
            var rest = new BreakStatus(Phase.Reminder, 600, Start, Start.AddMinutes(10), Start.AddMinutes(20));
            Equal(true, BreakPlanner.Combine(night, rest).IsBreak);
            Equal(false, BreakPlanner.Combine(night with { LockAt = Start.AddMinutes(5) }, rest).IsBreak);
        }),
        ("zero lead at the minimum open interval", () =>
        {
            var options = Options with { WorkMinutes = 11, ReminderMinutes = 0, CommitmentMinutes = 0, RestMinutes = 1 };
            var state = new BreakState(); Equal(Phase.Open, Tick(state, 10, 10, options).Phase);
            Equal(Phase.Restricted, Tick(state, 11, 1, options).Phase);
            Equal(Phase.Open, Tick(state, 12, 0, options).Phase);
        }),
        ("break validation rejects unsafe or inconsistent boundaries", () =>
        {
            foreach (var bad in new[] { Options with { WorkMinutes = double.NaN }, Options with { WorkMinutes = double.PositiveInfinity },
                Options with { WorkMinutes = 0 }, Options with { RestMinutes = 0 }, Options with { RestMinutes = 181 },
                Options with { ReminderMinutes = -1 }, Options with { CommitmentMinutes = 9 }, Options with { CommitmentMinutes = 60 },
                Options with { NaturalRestMinutes = 0 }, Options with { MinimumPostBreakMinutes = 181 } })
                Throws(bad.Validate);
        }),
        ("open interval must exceed ten whole minutes", () =>
        {
            Throws(() => (Options with { WorkMinutes = 3, RestMinutes = 5, ReminderMinutes = 0, CommitmentMinutes = 0 }).Validate());
            Throws(() => (Options with { WorkMinutes = 20 }).Validate());
            Throws(() => (Options with { WorkMinutes = 21.5 }).Validate());
            (Options with { WorkMinutes = 21 }).Validate();
        }),
        ("long rest cannot trigger another cycle until it finishes", () =>
        {
            var options = Options with { WorkMinutes = 11, RestMinutes = 15, ReminderMinutes = 0, CommitmentMinutes = 0 };
            var state = new BreakState(); Tick(state, 11, 11, options);
            var frozen = state.Frozen;
            Equal(Phase.Restricted, Tick(state, 22, 11, options).Phase);
            Equal(frozen, state.Frozen);
            Equal(Phase.Open, Tick(state, 26, 4, options).Phase);
            Equal(Phase.Open, Tick(state, 36, 10, options).Phase);
            Equal(Phase.Restricted, Tick(state, 37, 1, options).Phase);
        }),
        ("editing before commitment keeps accumulated work", () =>
        {
            var state = new BreakState(); Tick(state, 20, 20);
            Equal(6000d, Tick(state, 20, 0, Options with { WorkMinutes = 120 }).RemainingWorkSeconds);
            Tick(state, 20, 0, Options with { Enabled = false }); Equal(0d, state.WorkSeconds);
        }),
        ("continuous natural rest resets uncommitted work", () =>
        {
            var state = new BreakState(); Tick(state, 30, 30);
            var result = Observe(state, 40, WorkObservation.Recovering(Start.AddMinutes(30)));
            Equal(Phase.Open, result.Phase); Equal(0d, state.WorkSeconds); Equal(3600d, result.RemainingWorkSeconds);
        }),
        ("paused or short idle does not reset work", () =>
        {
            var state = new BreakState(); Tick(state, 30, 30);
            Observe(state, 39, WorkObservation.Recovering(Start.AddMinutes(30)));
            Equal(1800d, state.WorkSeconds);
            Observe(state, 40, WorkObservation.Paused);
            Equal(1800d, state.WorkSeconds); Equal<DateTimeOffset?>(null, state.RestStartedAt);
        }),
        ("natural rest fulfills a committed break before its deadline", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50);
            Equal(Phase.Open, Observe(state, 60, WorkObservation.Recovering(Start.AddMinutes(50))).Phase);
            Equal(0d, state.WorkSeconds); Equal<FrozenBreak?>(null, state.Frozen);
        }),
        ("rest spanning the deadline shortens enforced remainder", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50);
            var result = Observe(state, 62, WorkObservation.Recovering(Start.AddMinutes(55)));
            Equal(Phase.Restricted, result.Phase); Equal(Start.AddMinutes(65), result.ReleaseAt);
        }),
        ("start cycle now moves the committed break and is idempotent", () =>
        {
            var state = new BreakState(); Tick(state, 50, 50);
            var schedule = new Schedule { Breaks = Options };
            BreakPlanner.StartCycleNow(state, Options, Start.AddMinutes(50), schedule, Phase.Open);
            var release = state.Frozen!.ReleaseAt;
            BreakPlanner.StartCycleNow(state, Options, Start.AddMinutes(51), schedule, Phase.Open);
            Equal(release, state.Frozen!.ReleaseAt);
            Equal(Phase.Restricted, Observe(state, 51, WorkObservation.Paused).Phase);
        }),
        ("nearby committed bedtime covers a periodic break", () =>
        {
            var state = new BreakState();
            var result = BreakPlanner.Tick(state, Options, Start.AddMinutes(50), TimeSpan.FromMinutes(50),
                WorkObservation.Active, false, false, guaranteedNightLockAt: Start.AddMinutes(65));
            Equal(Start.AddMinutes(65), result.CoveredByNightAt);
            var night = new Status(new(), Phase.Reminder, Start, Start.AddMinutes(65), Start.AddHours(9), false,
                EffectiveBehavior: new());
            Equal(false, BreakPlanner.Combine(night, result).IsBreak);
            result = BreakPlanner.Tick(state, Options, Start.AddMinutes(65), TimeSpan.Zero,
                WorkObservation.Paused, true, false);
            Equal(true, result.SuppressedByNight); Equal(0d, state.WorkSeconds);
        }),
        ("distant bedtime does not cover a periodic break", () =>
        {
            var state = new BreakState();
            var result = BreakPlanner.Tick(state, Options, Start.AddMinutes(50), TimeSpan.FromMinutes(50),
                WorkObservation.Active, false, false, guaranteedNightLockAt: Start.AddMinutes(121));
            Equal<DateTimeOffset?>(null, result.CoveredByNightAt); Equal(Phase.Reminder, result.Phase);
        }),
        ("fresh activity hook cannot fabricate a long natural rest", () =>
        {
            var lease = new ActivityLease(); var mono = TimeSpan.FromHours(1); var wall = Start;
            lease.Update(new(true, TimeSpan.FromDays(365).TotalSeconds, HasObservedInput: false), mono);
            Equal(WorkActivityKind.Paused, lease.Observe(mono, wall, 1, true).Kind);
            lease.Update(new(true, 600, HasObservedInput: true), mono);
            var observed = lease.Observe(mono, wall, 1, true);
            Equal(WorkActivityKind.Recovering, observed.Kind); Equal(wall.AddMinutes(-10), observed.Since);
        })
    ];

    private static BreakStatus Tick(BreakState state, double minutes, double elapsed = 0, BreakOptions? options = null) =>
        BreakPlanner.Tick(state, options ?? Options, Start.AddMinutes(minutes), TimeSpan.FromMinutes(elapsed), false, false);
    private static BreakStatus Observe(BreakState state, double minutes, WorkObservation observation, BreakOptions? options = null) =>
        BreakPlanner.Tick(state, options ?? Options, Start.AddMinutes(minutes), TimeSpan.Zero,
            observation, false, false);
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid configuration"); }
}
