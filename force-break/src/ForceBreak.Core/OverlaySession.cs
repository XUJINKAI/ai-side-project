namespace ForceBreak.Core;

public enum OverlayMode { Closed, Manual, Restricted, Completed }

/// <summary>Display lifetime is separate from the authority to restrict computer use.</summary>
public sealed class OverlaySession
{
    public OverlayMode Mode { get; private set; }
    public DateTimeOffset? Deadline { get; private set; }
    public bool CanClose => Mode != OverlayMode.Restricted;
    public void OpenManual() { if (Mode != OverlayMode.Restricted) { Mode = OverlayMode.Manual; Deadline = null; } }
    public void Enforce(DateTimeOffset deadline, DateTimeOffset now)
    { Deadline = deadline; Mode = deadline > now ? OverlayMode.Restricted : OverlayMode.Completed; }
    public void Release() { if (Mode == OverlayMode.Restricted) Mode = OverlayMode.Completed; }
    public void Advance(DateTimeOffset now) { if (Mode == OverlayMode.Restricted && Deadline <= now) Release(); }
    public bool TryClose(DateTimeOffset now)
    {
        Advance(now);
        if (!CanClose) return false;
        Mode = OverlayMode.Closed; Deadline = null; return true;
    }
}
