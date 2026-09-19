namespace ForceBreak.Core;

public sealed record BehaviorOptions
{
    public bool DetectActivity { get; init; } = true;
    public int IdleMinutes { get; init; } = 1;
    public bool FullscreenOverlay { get; init; } = true;
    public bool LockScreen { get; init; }
    public void Validate()
    {
        if (IdleMinutes is < 1 or > 60) throw new ArgumentException("空闲暂停阈值必须为 1—60 整数分钟。");
    }
}

// Only elapsed inactivity is reported: no key codes, text or cursor coordinates.
public sealed record ActivityReport(bool Available, double IdleSeconds, bool OverlayVisible = false,
    bool HasObservedInput = false);

public enum WorkActivityKind { Active, Recovering, Paused }

/// <summary>A service-authoritative classification consumed by the work-cycle state machine.</summary>
public sealed record WorkObservation(WorkActivityKind Kind, DateTimeOffset? Since = null)
{
    public static readonly WorkObservation Active = new(WorkActivityKind.Active);
    public static readonly WorkObservation Paused = new(WorkActivityKind.Paused);
    public static WorkObservation Recovering(DateTimeOffset? since = null) => new(WorkActivityKind.Recovering, since);
}

public sealed class ActivityLease
{
    private TimeSpan received;
    private ActivityReport? report;
    public void Update(ActivityReport value, TimeSpan now)
    {
        if (!double.IsFinite(value.IdleSeconds) || value.IdleSeconds < 0)
            throw new ArgumentException("Invalid activity report.");
        report = value; received = now;
    }

    public WorkObservation Observe(TimeSpan monotonicNow, DateTimeOffset wallNow, int idleMinutes, bool idleCountsAsRest)
    {
        if (report is null || monotonicNow < received || monotonicNow - received > TimeSpan.FromSeconds(5))
            return WorkObservation.Paused;
        if (report.OverlayVisible || !report.Available) return WorkObservation.Paused;
        var idleSeconds = report.IdleSeconds + (monotonicNow - received).TotalSeconds;
        if (idleSeconds < idleMinutes * 60) return WorkObservation.Active;
        // A fresh hook has no trustworthy pre-start input timestamp. Never turn that sentinel into days of rest.
        return idleCountsAsRest && report.HasObservedInput
            ? WorkObservation.Recovering(wallNow.AddSeconds(-idleSeconds))
            : WorkObservation.Paused;
    }

    public bool IsResting(TimeSpan now) => report is { OverlayVisible: true } && now >= received && now - received <= TimeSpan.FromSeconds(5);
    public bool IsActive(TimeSpan now, int idleMinutes) => report is { Available: true, OverlayVisible: false } &&
        now >= received && now - received <= TimeSpan.FromSeconds(5) &&
        report.IdleSeconds + (now - received).TotalSeconds < idleMinutes * 60;
}
