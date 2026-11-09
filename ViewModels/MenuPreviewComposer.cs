using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
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

    /// <summary>Icon taken straight from the shell's own menu item.</summary>
    public ImageSource? Icon { get; init; }

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

        // The catalog already renders the shell's own entries. A verb that
        // duplicates one of them (DesktopBackground\Shell\Personalize vs the
        // built-in "个性化") would otherwise show up twice.
        var builtInHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in layout.Before.Concat(layout.After))
            if (entry.Kind != SystemMenuEntryKind.Separator)
                builtInHeaders.Add(NormalizeText(entry.Header));

        var visible = items
            .Where(i => showExtended || !i.IsExtended)
            .Where(i => !builtInHeaders.Contains(NormalizeText(i.DisplayName)))
            .ToList();
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

    /// <summary>
    /// Maps the menu Windows built onto preview rows. Nothing is reordered or
    /// filtered: the shell's own output is authoritative, and rows that belong
    /// to a verb this app can edit are linked back so clicking still selects it.
    /// </summary>
    public static IReadOnlyList<PreviewNode> ComposeNative(
        IReadOnlyList<ShellMenuNode> native,
        IReadOnlyList<MenuItemViewModel> items)
    {
        var lookup = BuildTextLookup(items);
        return Normalize(native.Select(n => FromNative(n, lookup)).ToList());
    }

    private static PreviewNode FromNative(ShellMenuNode node, IReadOnlyDictionary<string, MenuItemViewModel> lookup)
    {
        if (node.IsSeparator) return PreviewNode.Separator();

        lookup.TryGetValue(NormalizeText(node.Text), out var match);
        return new PreviewNode
        {
            Header = node.Text,
            Item = match,
            IsBuiltIn = match is null,
            IsEnabled = node.IsEnabled,
            IsDefault = node.IsDefault,
            IsChecked = node.IsChecked,
            Icon = node.Icon,
            // Shift-only verbs only show up in the extended menu, which the
            // shell already filtered; the badge keeps that visible.
            Badge = match is { IsExtended: true } ? "Shift" : null,
            Children = node.Children.Select(c => FromNative(c, lookup)).ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, MenuItemViewModel> BuildTextLookup(IReadOnlyList<MenuItemViewModel> items)
    {
        var map = new Dictionary<string, MenuItemViewModel>(StringComparer.OrdinalIgnoreCase);
        void Add(MenuItemViewModel item)
        {
            var key = NormalizeText(item.DisplayName);
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = item;
            foreach (var child in item.Children) Add(child);
        }
        foreach (var item in items) Add(item);
        return map;
    }

    /// <summary>
    /// "打开(&amp;O)" -> "打开", "&amp;Open WezTerm here" -> "Open WezTerm here":
    /// menu text carries the access-key marker and the accelerator suffix, the
    /// parsed display name does not.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '&')
            {
                // "&&" is a literal ampersand; a lone "&" marks the access key.
                if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append('&'); i++; }
                continue;
            }
            sb.Append(c);
        }
        var result = sb.ToString().Trim();
        var open = result.LastIndexOf('(');
        if (open > 0 && result.EndsWith(")", StringComparison.Ordinal))
        {
            // "(O)" / "(&O)" -> drop the accelerator suffix, keep real
            // parentheses such as "压缩(zipped)文件夹".
            var suffix = result.Substring(open + 1, result.Length - open - 2).TrimStart('&');
            if (suffix.Length is 1 or 2 && suffix.All(char.IsLetterOrDigit))
                result = result.Substring(0, open).Trim();
        }
        return result;
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
