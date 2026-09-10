using BedtimeGuard.Core;

internal static class BreakTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly BreakOptions Options = new() { Enabled = true };
    public static (string Name, Action Run)[] Cases =>
    [
        ("legacy hours migrate to minutes without changing the interval", () =>
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<BreakOptions>("{\"WorkHours\":1.5}")!;
            Equal(90d, legacy.WorkMinutes);
            var json = System.Text.Json.JsonSerializer.Serialize(legacy);
            Equal(false, json.Contains("WorkHours"));
            Equal(true, json.Contains("WorkMinutes"));
            (Options with { WorkMinutes = 11, ReminderMinutes = 0, CommitmentMinutes = 0 }).Validate();
        }),
        ("break defaults: 50 minute reminder, 60 minute lock, 70 minute release", () =>
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
                Options with { ReminderMinutes = -1 }, Options with { CommitmentMinutes = 9 }, Options with { CommitmentMinutes = 60 } })
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
        ("legacy short intervals extend future cycles without changing frozen deadlines", () =>
        {
            var frozen = new FrozenBreak(Start, Start.AddMinutes(1), Start.AddMinutes(6), false);
            var state = new PlannerState { Version = 1, Schedule = new Schedule { Breaks = Options with { WorkMinutes = 6, CommitmentMinutes = 5, ReminderMinutes = 5 } },
                Break = new BreakState { WorkSeconds = 60, Frozen = frozen } };
            StateMigration.Upgrade(state);
            Equal(16d, state.Schedule.Breaks.WorkMinutes); Equal(2, state.Version);
            Equal(frozen, state.Break.Frozen); Equal(60d, state.Break.WorkSeconds);
        }),
        ("editing before commitment keeps accumulated work", () =>
        {
            var state = new BreakState(); Tick(state, 20, 20);
            Equal(6000d, Tick(state, 20, 0, Options with { WorkMinutes = 120 }).RemainingWorkSeconds);
            Tick(state, 20, 0, Options with { Enabled = false }); Equal(0d, state.WorkSeconds);
        })
    ];
    private static BreakStatus Tick(BreakState state, double minutes, double elapsed = 0, BreakOptions? options = null) =>
        BreakPlanner.Tick(state, options ?? Options, Start.AddMinutes(minutes), TimeSpan.FromMinutes(elapsed), false, false);
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid configuration"); }
}
