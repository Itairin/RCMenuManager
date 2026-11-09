using System;
using System.Collections.Generic;

namespace RCMenuManager.Models;

/// <summary>
/// A node of the "built-in" part of a context menu — the entries Explorer itself
/// contributes (Open, Sort by, New, Properties, ...). These never come from the
/// registry keys this app manages, so they are read-only reference data used by
/// the preview to look like the real menu.
/// </summary>
public sealed class SystemMenuEntry
{
    public SystemMenuEntryKind Kind { get; init; } = SystemMenuEntryKind.Item;

    /// <summary>Menu text as Explorer renders it (Chinese resources on a zh-CN system).</summary>
    public string Header { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// True for the shell's default verb (Open). Explorer renders it semibold,
    /// so the preview must do the same to look authentic.
    /// </summary>
    public bool IsDefault { get; init; }

    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    public IReadOnlyList<SystemMenuEntry> Children { get; init; } = Array.Empty<SystemMenuEntry>();

    public bool HasChildren => Children.Count > 0;

    public static SystemMenuEntry Separator { get; } = new() { Kind = SystemMenuEntryKind.Separator };

    public static SystemMenuEntry Item(string header) => new() { Header = header };

    public static SystemMenuEntry Default(string header) => new() { Header = header, IsDefault = true };

    public static SystemMenuEntry Cascade(string header, params SystemMenuEntry[] children)
        => new() { Header = header, Children = children };

    public static SystemMenuEntry Check(string header, bool isChecked)
        => new() { Header = header, IsCheckable = true, IsChecked = isChecked };

    public override string ToString() => Kind == SystemMenuEntryKind.Separator ? "---" : Header;
}

public enum SystemMenuEntryKind
{
    Item,
    Separator,
}
