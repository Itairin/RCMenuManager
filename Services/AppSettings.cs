namespace RCMenuManager.Services;

/// <summary>
/// User preferences that must survive a restart. Deliberately tiny: window
/// geometry, the pane split, the last scope and the "显示 Shift 项" toggle.
/// Anything that can be re-derived from the registry is not stored here, so the
/// file cannot go stale and lie about the system state.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Saved bounds in device-independent pixels; null when never saved.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 720;
    public bool IsMaximized { get; set; }

    /// <summary>Share of the content width taken by the list pane (clamped to 0.2 - 0.85).</summary>
    public double ListColumnRatio { get; set; } = 0.606;

    /// <summary>Scope id to reopen, e.g. <c>Folder</c> or <c>FileExt:.txt</c>.</summary>
    public string? LastScopeId { get; set; }

    /// <summary>Preview tab: show Shift-only (Extended) entries.</summary>
    public bool ShowExtended { get; set; }
}
