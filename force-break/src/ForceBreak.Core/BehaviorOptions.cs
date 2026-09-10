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
public sealed record ActivityReport(bool Available, double IdleSeconds, bool OverlayVisible = false);

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
    public bool IsResting(TimeSpan now) => report is { OverlayVisible: true } && now >= received && now - received <= TimeSpan.FromSeconds(5);
    public bool IsActive(TimeSpan now, int idleMinutes) => report is { Available: true, OverlayVisible: false } &&
        now >= received && now - received <= TimeSpan.FromSeconds(5) &&
        report.IdleSeconds + (now - received).TotalSeconds < idleMinutes * 60;
}
