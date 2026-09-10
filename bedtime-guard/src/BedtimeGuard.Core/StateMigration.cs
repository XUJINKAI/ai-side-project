namespace BedtimeGuard.Core;

public static class StateMigration
{
    public static void Upgrade(PlannerState state)
    {
        if (state.Version == 2) return;
        if (state.Version != 1) throw new InvalidDataException("Unknown state version.");
        var b = state.Schedule.Breaks;
        if (!double.IsFinite(b.WorkMinutes) || b.WorkMinutes < 1 || b.WorkMinutes > 1440 ||
            b.RestMinutes is < 1 or > 180 || b.ReminderMinutes < 0 || b.CommitmentMinutes < b.ReminderMinutes || b.CommitmentMinutes >= b.WorkMinutes)
            throw new InvalidDataException("Invalid legacy break configuration.");
        var commitment = Math.Min(b.CommitmentMinutes, 1429);
        state.Schedule = state.Schedule with { Breaks = b with
        {
            WorkMinutes = Math.Max(Math.Ceiling(b.WorkMinutes), commitment + 11),
            CommitmentMinutes = commitment,
            ReminderMinutes = Math.Min(b.ReminderMinutes, commitment)
        } };
        // Frozen deadlines, accumulated work and nightly random draws remain untouched.
        state.Version = 2;
    }
}
