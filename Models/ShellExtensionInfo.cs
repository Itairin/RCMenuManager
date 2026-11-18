namespace RCMenuManager.Models;

/// <summary>
/// Read-only view of a <c>shellex\ContextMenuHandlers</c> entry. These are
/// DLL-based menu providers registered by the system or by 3rd-party apps; we
/// surface them in the preview popup so the user sees the same options that
/// Explorer would, but they cannot be edited or deleted from this app.
/// </summary>
public sealed class ShellExtensionInfo
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Name registered on the CLSID itself (for example "Shell extensions for
    /// sharing"). The menu row prints a localized text instead, so this is only
    /// used for matching and for explaining what the row actually is.
    /// </summary>
    public string FriendlyName { get; set; } = string.Empty;
    public string Clsid { get; set; } = string.Empty;
    public string DllPath { get; set; } = string.Empty;
    public string ScopeId { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>CLSID + DLL, shown in the details pane for reference.</summary>
    public string HandlerDetail => string.IsNullOrEmpty(DllPath)
        ? $"CLSID {Clsid}"
        : $"CLSID {Clsid} · {DllPath}";
}
