using System.Collections.Generic;

namespace RCMenuManager.Models;

/// <summary>
/// Represents a single context-menu verb parsed from the registry. Children
/// are populated when the verb declares <c>SubCommands</c> or hosts a nested
/// <c>shell</c> sub-key (legacy cascading).
/// </summary>
public sealed class ContextMenuItem
{
    /// <summary>Sub-key name in the registry (the "verb"), e.g. "open", "vscode".</summary>
    public string VerbName { get; init; } = string.Empty;

    /// <summary>Resolved display name. Falls back to <see cref="VerbName"/> when blank.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Raw command line (HKCR\...\&lt;verb&gt;\command\(Default)). May be empty for cascading parents.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Command line with environment variables expanded. Empty when the raw command is empty.</summary>
    public string ExpandedCommand { get; init; } = string.Empty;

    /// <summary>Icon expression as found in the registry, e.g. "imageres.dll,-64".</summary>
    public string? IconExpression { get; init; }

    /// <summary>Comma-separated MUIVerb / Localized resource (raw value, before resolving).</summary>
    public string? MuiVerb { get; init; }

    /// <summary>Position hint: "Top", "Bottom" or null.</summary>
    public string? Position { get; init; }

    /// <summary>True when the entry has the <c>Extended</c> value (Shift+right-click only).</summary>
    public bool IsExtended { get; init; }

    /// <summary>True when the entry has the <c>ProgrammaticAccessOnly</c> value.</summary>
    public bool IsProgrammaticOnly { get; init; }

    /// <summary>True when the entry has the <c>NeverDefault</c> value.</summary>
    public bool IsNeverDefault { get; init; }

    /// <summary>True when the entry has the <c>LegacyDisable</c> value.</summary>
    public bool IsLegacyDisabled { get; init; }

    /// <summary>True for verbs known to be system-critical (open/edit/print/...).</summary>
    public bool IsSystemVerb { get; init; }

    /// <summary>True when the entry uses cascading children (SubCommands / nested shell / ExtendedSubCommandsKey).</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>How the cascading children were declared on this verb.</summary>
    public CascadingKind Cascading { get; init; } = CascadingKind.None;

    /// <summary>Origin of this entry (HKCU vs HKLM vs HKCR merged).</summary>
    public RegistryHiveOrigin Origin { get; init; } = RegistryHiveOrigin.Unknown;

    /// <summary>Full registry path of the verb key, e.g. "HKEY_CLASSES_ROOT\*\shell\open".</summary>
    public string RegistryPath { get; init; } = string.Empty;

    /// <summary>Children for cascading menus (SubCommands / nested shell).</summary>
    public IReadOnlyList<ContextMenuItem> Children { get; init; } = System.Array.Empty<ContextMenuItem>();

    /// <summary>True when the user has flagged the entry hidden via <c>HideBasedOnVelocityId</c> etc.</summary>
    public bool IsHidden => IsProgrammaticOnly || IsLegacyDisabled;

    /// <summary>True when the verb has cascading children but no command, i.e. it is a parent-only entry.</summary>
    public bool IsParentOnly => string.IsNullOrEmpty(Command) && Children.Count > 0;

    public override string ToString() => $"{DisplayName} ({VerbName})";
}

/// <summary>
/// Source of a verb's cascading children. Mirrors the three mechanisms Windows
/// supports natively for nested context menus.
/// </summary>
public enum CascadingKind
{
    /// <summary>This verb has no children.</summary>
    None,

    /// <summary>Children were declared via the <c>SubCommands</c> value (Win10+ CommandStore).</summary>
    SubCommands,

    /// <summary>Children were declared via the <c>ExtendedSubCommandsKey</c> value pointing to a registry path.</summary>
    ExtendedSubCommandsKey,

    /// <summary>Children were discovered as a nested <c>\shell</c> sub-tree (legacy form).</summary>
    NestedShell,
}
