using ForceBreak.Core;

var tests = new (string Name, Action Run)[]
{
    ("default schedule and disabled startup", () =>
    {
        var s = new Schedule(); s.Validate();
        Equal(new TimeOnly(20, 0), s.Commitment);
        Equal(Phase.Disabled, New(s).Tick(At(10, 23, 30)).Phase);
    }),
    ("commitment precedes first reminder, inclusive lower boundary", () =>
    {
        var p = New(Enabled());
        Equal(Phase.Open, p.Tick(At(10, 19, 59, 59)).Phase);
        Equal(Phase.Committed, p.Tick(At(10, 20, 0)).Phase);
        Equal(Phase.Committed, p.Tick(At(10, 21, 59, 59)).Phase);
        Equal(Phase.Reminder, p.Tick(At(10, 22, 0)).Phase);
        Equal(Phase.Restricted, p.Tick(At(10, 23, 0)).Phase);
        Equal(Phase.Restricted, p.Tick(At(11, 5, 59, 59)).Phase);
        Equal(Phase.Open, p.Tick(At(11, 6, 0)).Phase);
    }),
    ("frozen night survives disable, time, day, policy and timezone edits", () =>
    {
        var p = New(Enabled() with { DisableTaskManager = true });
        p.Tick(At(10, 21, 0)); var frozen = p.State.Frozen;
        var changed = Enabled() with { Enabled = false, Commitment = new(21, 0),
            Bedtime = new(23, 30), Release = new(7, 0), Days = [DayOfWeek.Monday], DisableTaskManager = false };
        Equal(Phase.Committed, p.Update(changed, At(10, 21, 1)).Phase);
        Equal(frozen, p.State.Frozen); Equal(true, p.Tick(At(10, 23, 1)).TaskManagerRequested);
        Equal(Phase.Disabled, p.Tick(At(11, 6, 0)).Phase);
    }),
    ("save at exact deadline freezes old rules first", () =>
    {
        var p = New(Enabled());
        p.Tick(At(10, 12, 0));
        Equal(Phase.Committed, p.Update(Enabled() with { Enabled = false }, At(10, 20, 0)).Phase);
    }),
    ("restart preserves the fixed commitment and frozen night", () =>
    {
        var p = New(Enabled()); p.Tick(At(10, 12, 0));
        var restarted = new Planner(JsonStorage.Clone(p.State));
        restarted.Tick(At(10, 20, 45));
        Equal(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero), restarted.State.Frozen!.CommitAt);
        var again = new Planner(JsonStorage.Clone(restarted.State));
        Equal(Phase.Restricted, again.Tick(At(11, 1, 0)).Phase);
    }),
    ("editing a future fixed commitment applies immediately", () =>
    {
        var p = New(Enabled()); p.Tick(At(10, 10, 0));
        p.Update(Enabled() with { Commitment = new(21, 0) }, At(10, 11, 0));
        Equal(Phase.Open, p.Tick(At(10, 20, 59, 59)).Phase);
        Equal(Phase.Committed, p.Tick(At(10, 21, 0)).Phase);
    }),
    ("wake or login during restriction catches yesterday's night", () =>
    {
        var p = New(Enabled());
        Equal(Phase.Restricted, p.Tick(At(11, 2, 0)).Phase);
        Equal(new DateOnly(2026, 9, 10), p.State.Frozen!.Date);
    }),
    ("wake after release never retroactively locks", () => Equal(Phase.Open, New(Enabled()).Tick(At(11, 8, 0)).Phase)),
    ("weekday refers to evening, including next morning", () =>
    {
        var p = New(Enabled() with { Days = [DayOfWeek.Thursday] });
        Equal(Phase.Restricted, p.Tick(At(11, 3, 0)).Phase);
        Equal(Phase.Open, p.Tick(At(11, 23, 0)).Phase);
        Equal(Phase.Open, p.Tick(At(12, 3, 0)).Phase);
    }),
    ("rollback does not unfreeze an already committed night", () =>
    {
        var p = New(Enabled()); p.Tick(At(10, 21, 0));
        Equal(Phase.Committed, p.Update(Enabled() with { Enabled = false }, At(10, 18, 0)).Phase);
    }),
    ("completed nights cannot replay after clock rollback", () =>
    {
        var p = New(Enabled()); p.Tick(At(10, 23, 0)); p.Tick(At(11, 6, 0));
        Equal(Phase.Open, p.Tick(At(10, 23, 0)).Phase);
    }),
    ("range validation rejects crossing reminder or previous date", () =>
    {
        Throws(() => (Enabled() with { Commitment = new(22, 0) }).Validate());
        Throws(() => (Enabled() with { Commitment = new(0, 15) }).Validate());
        Throws(() => (Enabled() with { Release = new(20, 0) }).Validate());
        Throws(() => (Enabled() with { Reminder = new(23, 0) }).Validate());
        Throws(() => (Enabled() with { Days = [] }).Validate());
        Throws(() => (Enabled() with { Reminder = new(22, 0, 1) }).Validate());
    }),
    ("DST spring gap release moves to first valid instant", () =>
    {
        var s = Enabled() with { TimeZoneId = "America/New_York", Release = new(2, 30) };
        var p = New(s); var result = p.Tick(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero));
        Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), result.ReleaseAt!.Value.ToUniversalTime());
    }),
    ("DST autumn overlap release uses later occurrence", () =>
    {
        var p = New(Enabled() with { TimeZoneId = "America/New_York", Release = new(1, 30) });
        var result = p.Tick(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
        Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), result.ReleaseAt!.Value.ToUniversalTime());
    }),
    ("status excludes the internal commitment timestamp", () =>
    {
        var p = New(Enabled());
        var json = System.Text.Json.JsonSerializer.Serialize(p.Tick(At(10, 21, 0)));
        if (json.Contains("CommitAt")) throw new Exception("Commitment timestamp leaked");
    }),
    ("removed v1 random fields are ignored without migration", () =>
    {
        var json = """{"Version":1,"Schedule":{"JitterMinutes":30},"Draws":{},"Break":{}}""";
        var state = System.Text.Json.JsonSerializer.Deserialize<PlannerState>(json, JsonStorage.Options)!;
        state.Validate();
        Equal(new TimeOnly(20, 0), state.Schedule.Commitment);
        Equal(50, state.Schedule.Breaks.MinimumPostBreakMinutes);
    }),
    ("state roundtrip and atomic replacement", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "bedtime-test-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(folder, "state.json"); var p = New(Enabled()); p.Tick(At(10, 21, 0));
            JsonStorage.Write(path, p.State);
            var read = JsonStorage.Read<PlannerState>(path); Equal(p.State.Frozen, read.Frozen);
            p.Tick(At(11, 6, 0)); JsonStorage.Write(path, p.State);
            Equal(new DateOnly(2026, 9, 10), JsonStorage.Read<PlannerState>(path).CompletedThrough!.Value);
            Equal(false, File.Exists(path + ".tmp"));
            File.WriteAllText(path, "{broken"); Throws(() => JsonStorage.Read<PlannerState>(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    })
};

tests = tests.Concat(BreakTests.Cases).Concat(ForceBreakTests.Cases).Concat(OverlayAndCommandTests.Cases).ToArray();
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static Schedule Enabled() => new() { Enabled = true, TimeZoneId = "UTC", Reminder = new(22, 0), Bedtime = new(23, 0), DisableTaskManager = false };
static Planner New(Schedule schedule) => new(new PlannerState { Schedule = schedule });
static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 9, day, hour, minute, second, TimeSpan.Zero);
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}");
}
static void Throws(Action action)
{
    try { action(); } catch { return; }
    throw new Exception("Expected exception");
}
