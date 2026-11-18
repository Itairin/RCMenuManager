using System;
using System.Collections.Generic;

namespace RCMenuManager.Models;

/// <summary>
/// How a menu item came to exist. The distinction matters because it decides what
/// the app may safely do with it: an entry installed by an application can be
/// switched off, but a shell verb Explorer owns has no registry command to edit.
/// </summary>
public enum MenuItemSource
{
    /// <summary>Nothing was recognised; the row is shown as-is.</summary>
    Unknown,

    /// <summary>Written by this tool (matching verb + scope in its own preset catalog or operations log).</summary>
    ThisTool,

    /// <summary>Registered by a third-party application's installer.</summary>
    Application,

    /// <summary>Contributed by a shell extension DLL (<c>shellex\ContextMenuHandlers</c>).</summary>
    ShellExtension,

    /// <summary>Implemented by an <c>ExplorerCommandHandler</c> CLSID (Win11 style handler).</summary>
    ExplorerCommand,

    /// <summary>Windows' own built-in verb, e.g. <c>open</c> / <c>pintohome</c>.</summary>
    SystemBuiltIn,

    /// <summary>Resolved from the shared <c>CommandStore</c> definition.</summary>
    CommandStore,

    /// <summary>
    /// A row the shell drew but no registry verb backs (extension DLL, MSIX command,
    /// Explorer's own entries). Read-only by nature.
    /// </summary>
    ShellProvided,
}

/// <summary>
/// The analysis result shown in the UI. <see cref="Label"/> is the short text on
/// the badge; <see cref="Detail"/> explains it in the details panel.
/// </summary>
public sealed record MenuItemOrigin(
    MenuItemSource Source,
    string Label,
    string Detail,
    string? Evidence = null)
{
    public static MenuItemOrigin Unknown { get; } =
        new(MenuItemSource.Unknown, "未知", "未能识别该菜单项的来源。");

    /// <summary>Short badge text; empty for Unknown so the UI stays quiet.</summary>
    public string Badge => Source == MenuItemSource.Unknown ? string.Empty : Label;
}
