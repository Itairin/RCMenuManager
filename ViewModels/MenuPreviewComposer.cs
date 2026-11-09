using System;
using System.Collections.Generic;
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public enum PreviewNodeKind
{
    Item,
    Separator,
}

/// <summary>
/// One row of the preview menu. Either a built-in Explorer entry
/// (<see cref="IsBuiltIn"/>, read-only reference) or a registry verb
/// (<see cref="Item"/> set, clickable and selectable for editing).
/// </summary>
public sealed class PreviewNode
{
    public PreviewNodeKind Kind { get; init; } = PreviewNodeKind.Item;

    public string Header { get; init; } = string.Empty;

    public bool IsBuiltIn { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>Semibold, like Explorer renders the default verb (Open).</summary>
    public bool IsDefault { get; init; }

    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    /// <summary>Right-side badge ("Shift" for Extended verbs).</summary>
    public string? Badge { get; init; }

    public MenuItemViewModel? Item { get; init; }

    public IReadOnlyList<PreviewNode> Children { get; init; } = Array.Empty<PreviewNode>();

    public bool HasChildren => Children.Count > 0;

    public static PreviewNode Separator() => new() { Kind = PreviewNodeKind.Separator };

    public override string ToString() => Kind == PreviewNodeKind.Separator ? "---" : Header;
}

/// <summary>
/// Composes the preview menu the way Explorer would draw it: built-in entries
/// in their native order, with this app's verbs spliced into the slot the shell
/// actually uses (after "New", before "Properties"), honouring
/// <c>Position=Top/Bottom</c> and hiding <c>Extended</c> verbs unless Shift is
/// simulated.
/// </summary>
public static class MenuPreviewComposer
{
    public static IReadOnlyList<PreviewNode> Compose(
        MenuScope scope,
        IReadOnlyList<MenuItemViewModel> items,
        IReadOnlyList<ShellExtensionInfo> shellExtensions,
        bool showExtended = false,
        bool clipboardHasFiles = true)
    {
        var layout = SystemMenuCatalog.Build(scope, clipboardHasFiles);
        var nodes = new List<PreviewNode>();

        var visible = items.Where(i => showExtended || !i.IsExtended).ToList();
        var top = visible.Where(i => i.IsTop).ToList();
        var bottom = visible.Where(i => i.IsBottom && !i.IsTop).ToList();
        var middle = visible.Where(i => !i.IsTop && !i.IsBottom).ToList();

        // Position=Top verbs are painted above the shell's own block.
        if (top.Count > 0)
        {
            nodes.AddRange(top.Select(FromItem));
            nodes.Add(PreviewNode.Separator());
        }

        nodes.AddRange(layout.Before.Select(FromEntry));

        // Everything the shell does not position itself lands after "New".
        if (middle.Count > 0)
        {
            nodes.Add(PreviewNode.Separator());
            nodes.AddRange(middle.Select(FromItem));
        }

        if (bottom.Count > 0)
        {
            nodes.Add(PreviewNode.Separator());
            nodes.AddRange(bottom.Select(FromItem));
        }

        // DLL-based handlers sit in the same third-party block as the verbs.
        if (shellExtensions.Count > 0)
        {
            nodes.Add(PreviewNode.Separator());
            nodes.AddRange(shellExtensions.Select(FromShellExtension));
        }

        if (layout.After.Count > 0)
        {
            nodes.Add(PreviewNode.Separator());
            nodes.AddRange(layout.After.Select(FromEntry));
        }

        return Normalize(nodes);
    }

    public static PreviewNode FromItem(MenuItemViewModel item)
    {
        return new PreviewNode
        {
            Header = item.DisplayName,
            Item = item,
            IsBuiltIn = false,
            IsEnabled = true,
            Badge = item.IsExtended ? "Shift" : null,
            Children = item.Children.Select(FromItem).ToArray(),
        };
    }

    public static PreviewNode FromEntry(SystemMenuEntry entry)
    {
        if (entry.Kind == SystemMenuEntryKind.Separator) return PreviewNode.Separator();
        return new PreviewNode
        {
            Header = entry.Header,
            IsBuiltIn = true,
            IsEnabled = entry.IsEnabled,
            IsDefault = entry.IsDefault,
            IsCheckable = entry.IsCheckable,
            IsChecked = entry.IsChecked,
            Children = entry.Children.Select(FromEntry).ToArray(),
        };
    }

    private static PreviewNode FromShellExtension(ShellExtensionInfo ext)
    {
        // Explorer lists handlers by their registered display name; the raw
        // key/CLSID only shows up when the handler has no name at all.
        var header = !string.IsNullOrWhiteSpace(ext.DisplayName) && ext.DisplayName != ext.SourceKey
            ? ext.DisplayName
            : ext.SourceKey;
        return new PreviewNode { Header = header, IsBuiltIn = true };
    }

    /// <summary>Drops leading / trailing separators and collapses runs of them.</summary>
    private static IReadOnlyList<PreviewNode> Normalize(IReadOnlyList<PreviewNode> nodes)
    {
        var result = new List<PreviewNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Kind == PreviewNodeKind.Separator)
            {
                if (result.Count == 0 || result[^1].Kind == PreviewNodeKind.Separator) continue;
            }
            result.Add(node);
        }
        while (result.Count > 0 && result[^1].Kind == PreviewNodeKind.Separator)
            result.RemoveAt(result.Count - 1);
        return result;
    }
}
