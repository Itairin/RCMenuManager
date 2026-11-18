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

    /// <summary>
    /// Keyboard hint the shell puts after a tab (e.g. "Ctrl+Z" on undo).
    /// Explorer right-aligns it, so it is kept apart from the label.
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>Icon taken straight from the shell's own menu item.</summary>
    public ImageSource? Icon { get; init; }

    public MenuItemViewModel? Item { get; init; }

    /// <summary>
    /// Set when the row comes from a <c>shellexContextMenuHandlers</c> entry.
    /// These rows are not editable, but they are identifiable — which is what
    /// makes them selectable and lets the details pane explain the origin.
    /// </summary>
    public ShellExtensionInfo? Extension { get; init; }

    /// <summary>True when clicking the row can select something.</summary>
    public bool CanSelect => Item is not null || Extension is not null;

    /// <summary>
    /// True when more than one verb renders with this label. The row still resolves to
    /// the effective one, but the user is told the label is not unique.
    /// </summary>
    public bool IsAmbiguousMatch { get; init; }

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
        IReadOnlyList<MenuItemViewModel> items,
        IReadOnlyList<ShellExtensionInfo>? shellExtensions = null)
    {
        var lookup = BuildTextLookup(items);
        var extensions = BuildExtensionLookup(shellExtensions);
        return Normalize(native.Select(n => FromNative(n, lookup, extensions)).ToList());
    }

    /// <summary>
    /// Maps a shell-extension handler to the names Explorer might print for it.
    /// "共享" / "在记事本中打开" come from <c>shellex\ContextMenuHandlers</c>
    /// (Sharing, Open With), so they have no registry verb and used to be
    /// impossible to select: the row rendered but clicking did nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, ShellExtensionInfo> BuildExtensionLookup(
        IReadOnlyList<ShellExtensionInfo>? shellExtensions)
    {
        var map = new Dictionary<string, ShellExtensionInfo>(StringComparer.OrdinalIgnoreCase);
        if (shellExtensions is null) return map;
        foreach (var ext in shellExtensions)
        {
            void Add(string? key)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                var normalized = NormalizeText(key);
                if (normalized.Length > 0 && !map.ContainsKey(normalized)) map[normalized] = ext;
            }

            // The registered name, the raw key name, and the CLSID's own name are all
            // candidates for the text Explorer ends up drawing.
            Add(ext.DisplayName);
            Add(ext.SourceKey);
            Add(ext.FriendlyName);
            foreach (var alias in ShellExtensionAliases(ext)) Add(alias);
        }
        return map;
    }

    /// <summary>
    /// Well-known handler names Explorer localizes, so the CLSID-based registry name
    /// ("Shell extensions for sharing") still matches the menu row ("共享").
    /// </summary>
    private static IEnumerable<string> ShellExtensionAliases(ShellExtensionInfo ext)
    {
        var clsid = (ext.Clsid ?? string.Empty).Trim().ToUpperInvariant();
        return clsid switch
        {
            // Sharing / ModernSharing
            "{F81E9010-6EA4-11CE-A7FF-00AA003CA9F6}" => new[] { "共享", "Share", "Share with" },
            "{E2BF9676-5F8F-435C-97EB-11607A5BEDF7}" => new[] { "共享", "Share" },
            // Open With
            "{09799AFB-AD67-11D1-ABCD-00C04FC30936}" => new[] { "打开方式", "Open with", "在记事本中打开", "Notepad" },
            // Copy as path
            "{F3D06E7C-1E45-4A26-847E-F9FCDEE59BE0}" => new[] { "复制为路径", "Copy as path" },
            // SendTo
            "{7BA4C740-9E81-11CF-99D3-00AA004AE837}" => new[] { "发送到", "Send to" },
            _ => Array.Empty<string>(),
        };
    }

    private static PreviewNode FromNative(
        ShellMenuNode node,
        IReadOnlyDictionary<string, List<MenuItemViewModel>> lookup,
        IReadOnlyDictionary<string, ShellExtensionInfo> extensions)
    {
        if (node.IsSeparator) return PreviewNode.Separator();

        var label = node.Text;
        string? shortcut = null;
        var tab = label.IndexOf('\t');
        if (tab >= 0)
        {
            shortcut = label[(tab + 1)..].Trim();
            label = label[..tab].TrimEnd();
        }

        var key = NormalizeText(label);
        lookup.TryGetValue(key, out var candidates);
        var match = candidates is null ? null : PickMatch(candidates);
        var ambiguous = candidates is { Count: > 1 };
        extensions.TryGetValue(key, out var extension);

        // A row is selectable when the app knows *something* about where it comes
        // from: either an editable verb, or a registered shell-extension handler.
        // Rows that are genuinely Explorer-internal (刷新, 查看, 属性 ...) still have
        // no owner and stay reference-only, which is correct — there is nothing to
        // edit, and pretending otherwise would break the "what you see is what is
        // registered" promise.
        return new PreviewNode
        {
            Header = label,
            Shortcut = string.IsNullOrEmpty(shortcut) ? null : shortcut,
            Item = match,
            Extension = extension,
            IsBuiltIn = match is null && extension is null,
            IsEnabled = node.IsEnabled,
            IsDefault = node.IsDefault,
            IsChecked = node.IsChecked,
            Icon = node.Icon,
            // Shift-only verbs only show up in the extended menu, which the
            // shell already filtered; the badge keeps that visible. A duplicate label
            // is flagged so the user knows the row maps to one of several verbs.
            Badge = match is { IsExtended: true } ? "Shift" : ambiguous ? "重名" : null,
            IsAmbiguousMatch = ambiguous,
            Children = node.Children.Select(c => FromNative(c, lookup, extensions)).ToArray(),
        };
    }

    /// <summary>
    /// Display text to every verb that renders with it. A list is kept because two
    /// different verbs can share a label (a per-user override next to the machine one,
    /// or two apps using the same friendly name); picking the first match blindly made
    /// the preview select the wrong key, and the following edit then landed on it.
    /// </summary>
    private static IReadOnlyDictionary<string, List<MenuItemViewModel>> BuildTextLookup(
        IReadOnlyList<MenuItemViewModel> items)
    {
        var map = new Dictionary<string, List<MenuItemViewModel>>(StringComparer.OrdinalIgnoreCase);
        void Add(MenuItemViewModel item)
        {
            var key = NormalizeText(item.DisplayName);
            if (key.Length > 0)
            {
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<MenuItemViewModel>(1);
                list.Add(item);
            }
            foreach (var child in item.Children) Add(child);
        }
        foreach (var item in items) Add(item);
        return map;
    }

    /// <summary>
    /// Chooses which verb a rendered row refers to. Per-user wins over machine-wide
    /// because that is the copy Windows itself prefers; the rest keep list order.
    /// </summary>
    private static MenuItemViewModel? PickMatch(List<MenuItemViewModel> candidates)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        return candidates.FirstOrDefault(c => c.WriteHive == Microsoft.Win32.RegistryHive.CurrentUser)
               ?? candidates[0];
    }

    /// <summary>
    /// "打开(&amp;O)" -> "打开", "&amp;Open WezTerm here" -> "Open WezTerm here":
    /// menu text carries the access-key marker and the accelerator suffix, the
    /// parsed display name does not.
    /// </summary>
    private static string NormalizeText(string text) => Helpers.MenuTextHelper.NormalizeForMatch(text);

    public static PreviewNode FromItem(MenuItemViewModel item)
    {
        // A verb Explorer will not draw must not look clickable in the fallback
        // preview either: the details pane says "菜单中隐藏", and the popup showing
        // it as a normal enabled row contradicted that.
        return new PreviewNode
        {
            Header = item.DisplayName,
            Item = item,
            IsBuiltIn = false,
            IsEnabled = !item.IsHiddenFromMenu,
            Badge = item.IsExtended ? "Shift" : item.IsHiddenFromMenu ? "隐藏" : null,
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
        // Carrying the handler makes the row selectable here too. The native path can
        // resolve a handler from the row text; the fallback path has no shell text to
        // match, so without this the same entry was clickable in one preview mode and
        // dead in the other.
        return new PreviewNode { Header = header, Extension = ext, IsBuiltIn = false };
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
