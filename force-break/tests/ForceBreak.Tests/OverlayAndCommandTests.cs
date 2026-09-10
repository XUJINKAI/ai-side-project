using ForceBreak.Core;

internal static class OverlayAndCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 1, 0, 0, TimeSpan.Zero);
    public static (string Name, Action Run)[] Cases =>
    [
        ("CLI accepts help and integer duration boundaries", () =>
        {
            Check(BreakCommand.Parse(["-h"]).Help);
            foreach (var n in new[] { 1, 30, 60, 120 }) Check(BreakCommand.Parse(["-break", n.ToString()]).Request?.RestMinutes == n);
            foreach (var args in new[] { new[] { "-break", "0" }, ["-break", "121"], ["-break", "1.5"], ["-break"], ["-h", "extra"], ["-break", "30", "extra"] })
                Throws(() => BreakCommand.Parse(args));
        }),
        ("CLI tomorrow hours are 0 through 23", () =>
        {
            foreach (var hour in new[] { 0, 6, 23 }) Check(BreakCommand.Parse(["-break-tomorrow", hour.ToString()]).Request?.TomorrowHour == hour);
            foreach (var hour in new[] { "-1", "24", "6:00", "6.5" }) Throws(() => BreakCommand.Parse(["-break-tomorrow", hour]));
        }),
        ("tomorrow always means next calendar day including before 6am", () =>
        {
            var schedule = new Schedule { TimeZoneId = "UTC", Release = new(6, 30) };
            Check(BreakPlanner.TomorrowRelease(schedule, Now) == new DateTimeOffset(2026, 9, 11, 6, 30, 0, TimeSpan.Zero));
            Check(BreakPlanner.TomorrowRelease(schedule, Now, 23) == new DateTimeOffset(2026, 9, 11, 23, 0, 0, TimeSpan.Zero));
            Check(BreakPlanner.TomorrowRelease(new() { TimeZoneId = "UTC" }, Now).Hour == 6);
            Throws(() => BreakPlanner.TomorrowRelease(schedule, Now, 24));
        }),
        ("tomorrow uses schedule timezone and resolves DST release", () =>
        {
            var s = new Schedule { TimeZoneId = "America/New_York" };
            var beforeGap = new DateTimeOffset(2026, 3, 7, 17, 0, 0, TimeSpan.Zero);
            Check(BreakPlanner.TomorrowRelease(s, beforeGap, 2) == new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
            var beforeOverlap = new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero);
            Check(BreakPlanner.TomorrowRelease(s, beforeOverlap, 1) == new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero));
        }),
        ("overnight manual rest persists through night restriction and restarts", () =>
        {
            var s = new PlannerState { Schedule = new() { TimeZoneId = "UTC" } };
            BreakPlanner.StartTomorrow(s.Break, Now, s.Schedule, Phase.Open, 10);
            s = JsonStorage.Clone(s);
            var until = s.Break.Frozen!.ReleaseAt;
            var b = BreakPlanner.Tick(s.Break, s.Schedule.Breaks, Now.AddHours(22), TimeSpan.Zero, true, true);
            Check(b.Phase == Phase.Restricted && b.ReleaseAt == until);
            b = BreakPlanner.Tick(s.Break, s.Schedule.Breaks, until, TimeSpan.Zero, false, true);
            Check(b.Phase == Phase.Disabled && s.Break.WorkSeconds == 0);
        }),
        ("overlapping manual and night restrictions retain longest display deadline", () =>
        {
            var night = new Status(new(), Phase.Restricted, Now, Now, Now.AddHours(5), false,
                EffectiveBehavior: new() { FullscreenOverlay = false, LockScreen = true });
            var rest = new BreakStatus(Phase.Restricted, 0, Now, Now, Now.AddHours(9), false,
                Behavior: new() { FullscreenOverlay = true, LockScreen = false });
            var combined = BreakPlanner.Combine(night, rest);
            Check(combined.ReleaseAt == rest.ReleaseAt && combined.EffectiveBehavior is { FullscreenOverlay: true, LockScreen: true });
        }),
        ("overlay remains after expiry and closes only on request", () =>
        {
            var session = new OverlaySession(); session.Enforce(Now.AddMinutes(1), Now);
            Check(!session.TryClose(Now)); session.OpenManual(); Check(!session.CanClose);
            session.Advance(Now.AddMinutes(1)); Check(session.Mode == OverlayMode.Completed && session.CanClose);
            Check(session.TryClose(Now.AddMinutes(1)) && session.Mode == OverlayMode.Closed);
            session.OpenManual(); Check(session.Mode == OverlayMode.Manual && session.Deadline is null && session.CanClose);
        }),
        ("service release permits close without destroying overlay", () =>
        {
            var session = new OverlaySession(); session.Enforce(Now.AddHours(1), Now); session.Release();
            Check(session.Mode == OverlayMode.Completed && session.CanClose);
            session.Enforce(Now.AddHours(2), Now); Check(!session.CanClose);
        }),
        ("visible overlays do not count typing as work", () =>
        {
            var lease = new ActivityLease(); lease.Update(new(true, 0, true), TimeSpan.Zero);
            Check(lease.IsResting(TimeSpan.FromSeconds(1)) && !lease.IsActive(TimeSpan.FromSeconds(1), 1));
            Check(!lease.IsResting(TimeSpan.FromSeconds(6)));
            lease.Update(new(true, 0, false), TimeSpan.FromSeconds(7)); Check(lease.IsActive(TimeSpan.FromSeconds(7), 1));
        }),
        ("notes survive reopen with Unicode and multiline text", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var path = Path.Combine(folder, "notes.txt"); var notes = new OverlayNotes(path); notes.Load();
                Check(notes.Text == ""); notes.Text = "第一行\n第二行 🥕"; notes.Save();
                var reopened = new OverlayNotes(path); reopened.Load(); Check(reopened.Text == notes.Text);
                reopened.Text += "\n继续记录"; reopened.Save(); notes.Load(); Check(notes.Text.EndsWith("继续记录"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        })
    ];
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws(Action run) { try { run(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
}
