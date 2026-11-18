using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Helpers;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Turns the registry "shell" sub-trees into a tree of <see cref="ContextMenuItem"/>.
/// Handles all three cascading mechanisms Windows supports natively
/// (<c>SubCommands</c>, <c>ExtendedSubCommandsKey</c>, nested <c>\shell</c>),
/// resolves MUIVerb resources via <see cref="MuiStringHelper"/>, and merges
/// multiple hives into one verb list (HKCU wins, then HKLM, then HKCR).
/// </summary>
public class MenuParserService
{
    // From DEVELOPMENT.md / REGISTRY_REFERENCE.md.
    private static readonly HashSet<string> SystemVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "edit", "print", "explore", "find",
        "opennewwindow", "opennewprocess", "copyaspath",
        "printto", "runas", "runasuser",
    };

    private readonly RegistryService _registry;

    public MenuParserService(RegistryService registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns the merged verb list for the given scope. Verbs from earlier
    /// query entries (HKCU first) shadow those that come later. The result is
    /// stable-sorted by the verb's <c>Position</c> hint: Top first, default
    /// next, Bottom last - which mirrors Explorer's own ordering.
    /// </summary>
    public IReadOnlyList<ContextMenuItem> GetMenuItems(MenuScope scope)
    {
        var queries = _registry.GetShellQueryPaths(scope);
        var seen = new Dictionary<string, ContextMenuItem>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var entry in queries)
        {
            using var key = _registry.OpenRead(entry.Hive, entry.SubKey);
            if (key is null)
                continue;

            foreach (var sub in key.GetSubKeyNames())
            {
                if (seen.ContainsKey(sub))
                    continue;
                using var verbKey = key.OpenSubKey(sub, writable: false);
                if (verbKey is null)
                    continue;
                var item = ParseVerb(verbKey, sub, entry, depth: 0);
                seen[sub] = item;
                ordered.Add(sub);
            }
        }

        return SortByPosition(ordered.Select(v => seen[v]));
    }

    /// <summary>
    /// Builds the list the way Explorer builds the menu: the shell's own output is the
    /// backbone (labels, order, sub-menus, disabled state), and registry verbs are
    /// matched into it so they stay editable.
    /// <para>
    /// This is the fix for "列表比真实菜单少": entries such as UltraISO (a shellex
    /// handler whose submenu the DLL builds at runtime) and packaged-app commands such
    /// as "Open project in ChatGPT" are absent from every class key under
    /// <c>Directory\shell</c>, so a registry-only list can never contain them. Rows
    /// that exist only in the registry are appended and flagged
    /// <see cref="ContextMenuItem.NotInShellMenu"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<ContextMenuItem> MergeWithShellMenu(
        IReadOnlyList<ContextMenuItem> registryItems,
        IReadOnlyList<ShellMenuNode>? normalMenu,
        IReadOnlyList<ShellMenuNode>? extendedMenu)
    {
        if (normalMenu is not { Count: > 0 } && extendedMenu is not { Count: > 0 })
            return SortByPosition(registryItems);

        var remaining = new List<ContextMenuItem>(registryItems);
        var merged = new List<ContextMenuItem>(registryItems.Count + 8);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Emit(IReadOnlyList<ShellMenuNode> nodes, bool extendedOnly)
        {
            foreach (var node in nodes)
            {
                if (node.IsSeparator) continue;

                var label = MenuTextHelper.NormalizeForMatch(node.Text);
                var match = remaining.FirstOrDefault(i =>
                    !used.Contains(i.VerbName) &&
                    string.Equals(MenuTextHelper.NormalizeForMatch(i.DisplayName), label, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    used.Add(match.VerbName);
                    merged.Add(match);
                    continue;
                }

                // Nothing editable behind this row: keep it as a read-only entry so the
                // list mirrors the menu instead of silently dropping it.
                merged.Add(new ContextMenuItem
                {
                    // No verb of its own: repeating the label here made the row print its
                    // title twice (name + "verb" column).
                    VerbName = string.Empty,
                    DisplayName = label.Length > 0 ? label : node.Text,
                    HasNoStoredLabel = false,
                    IsShellProvided = true,
                    ShellSourceDetail = extendedOnly
                        ? "该行只出现在 Shift+右键 菜单中，由系统或应用扩展提供，本工具无法编辑。"
                        : "该行由系统菜单直接生成（可能是 shell 扩展 DLL、MSIX 包应用或 Explorer 自身功能），注册表里没有对应的可编辑 verb。",
                    Children = node.Children
                        .Where(c => !c.IsSeparator)
                        .Select(c => new ContextMenuItem
                        {
                            VerbName = string.Empty,
                            DisplayName = MenuTextHelper.NormalizeForMatch(c.Text),
                            IsShellProvided = true,
                            ShellSourceDetail = "子项由扩展在运行时生成，本工具无法编辑。",
                        })
                        .ToList(),
                });
            }
        }

        Emit(normalMenu ?? Array.Empty<ShellMenuNode>(), extendedOnly: false);

        // The Shift menu is a superset of the normal one, so it must not add rows of
        // its own: doing that duplicated every entry (Open Git GUI here / Open Git &GUI
        // here, 打开 / 打开(&O) …). It is only consulted to match verbs that Explorer
        // shows exclusively in the extended menu.
        if (extendedMenu is { Count: > 0 })
        {
            foreach (var node in extendedMenu)
            {
                if (node.IsSeparator) continue;
                var label = MenuTextHelper.NormalizeForMatch(node.Text);
                var match = remaining.FirstOrDefault(i =>
                    !used.Contains(i.VerbName) &&
                    string.Equals(MenuTextHelper.NormalizeForMatch(i.DisplayName), label, StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                used.Add(match.VerbName);
                merged.Add(match);
            }
        }

        // Registry verbs the shell did not draw: still listed (they can be edited) but
        // flagged, otherwise the list would contradict the menu in the other direction.
        foreach (var leftover in SortByPosition(remaining.Where(i => !used.Contains(i.VerbName))))
        {
            merged.Add(new ContextMenuItem
            {
                VerbName = leftover.VerbName,
                DisplayName = leftover.DisplayName,
                Command = leftover.Command,
                ExpandedCommand = leftover.ExpandedCommand,
                IconExpression = leftover.IconExpression,
                MuiVerb = leftover.MuiVerb,
                Position = leftover.Position,
                IsExtended = leftover.IsExtended,
                IsProgrammaticOnly = leftover.IsProgrammaticOnly,
                IsNeverDefault = leftover.IsNeverDefault,
                IsLegacyDisabled = leftover.IsLegacyDisabled,
                DelegateExecute = leftover.DelegateExecute,
                ExplorerCommandHandler = leftover.ExplorerCommandHandler,
                AppliesTo = leftover.AppliesTo,
                HasLuaShield = leftover.HasLuaShield,
                HasVelocityCondition = leftover.HasVelocityCondition,
                IsSharedDefinition = leftover.IsSharedDefinition,
                IsSystemVerb = leftover.IsSystemVerb,
                HasNoStoredLabel = leftover.HasNoStoredLabel,
                Origin = leftover.Origin,
                RegistryPath = leftover.RegistryPath,
                Children = leftover.Children,
                Cascading = leftover.Cascading,
                NotInShellMenu = true,
            });
        }

        return merged;
    }

    /// <summary>Same as <see cref="GetMenuItems"/> but lets the caller specify a single registry root.</summary>
    public IReadOnlyList<ContextMenuItem> GetMenuItemsFrom(RegistryQueryEntry entry)
    {
        var list = new List<ContextMenuItem>();
        using var key = _registry.OpenRead(entry.Hive, entry.SubKey);
        if (key is null) return list;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var verbKey = key.OpenSubKey(sub, writable: false);
            if (verbKey is null) continue;
            list.Add(ParseVerb(verbKey, sub, entry, depth: 0));
        }
        return SortByPosition(list);
    }

    /// <summary>
    /// Enumerates the <c>shellex\ContextMenuHandlers</c> entries registered for
    /// the given scope. These are DLLs that contribute menu items dynamically
    /// (e.g. "授予访问权限", "包含到库中", "发送到", "Share with"). We resolve each
    /// CLSID against HKCR\CLSID to surface a friendly name + DLL path.
    /// </summary>
    public IReadOnlyList<ShellExtensionInfo> GetShellExtensionHandlers(MenuScope scope)
    {
        var paths = GetShellExtensionQueryPaths(scope);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ShellExtensionInfo>();

        foreach (var path in paths)
        {
            using var key = _registry.OpenRead(path.Hive, path.SubKey);
            if (key is null) continue;

            foreach (var sub in key.GetSubKeyNames())
            {
                if (seen.Contains(sub)) continue;
                seen.Add(sub);

                using var handlerKey = key.OpenSubKey(sub, writable: false);
                var clsid = handlerKey?.GetValue(string.Empty) as string;
                if (string.IsNullOrEmpty(clsid)) continue;

                var (name, dll) = ResolveClsid(clsid);
                results.Add(new ShellExtensionInfo
                {
                    // FriendlyName keeps the CLSID's own name so the matcher can use
                    // it; DisplayName is what the UI shows when nothing better exists.
                    FriendlyName = string.IsNullOrEmpty(name) ? string.Empty : name,
                    DisplayName = string.IsNullOrEmpty(name) ? sub : name,
                    Clsid = clsid,
                    DllPath = dll,
                    ScopeId = scope.ScopeId,
                    SourceKey = sub,
                });
            }
        }

        return results;
    }

    private (string name, string dll) ResolveClsid(string clsid)
    {
        try
        {
            using var clsidKey = _registry.OpenRead(RegistryHive.ClassesRoot, $@"CLSID\{clsid}");
            var name = clsidKey?.GetValue(string.Empty) as string ?? string.Empty;
            var dll = clsidKey?.OpenSubKey("InprocServer32")?.GetValue(string.Empty) as string ?? string.Empty;
            return (name, dll);
        }
 catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static IReadOnlyList<RegistryQueryEntry> GetShellExtensionQueryPaths(MenuScope scope)
    {
        // HKCU first, then HKLM. HKCR is deliberately not probed: it is the merged
        // view of HKCU\Software\Classes + HKLM\SOFTWARE\Classes, so every entry it
        // yields is already covered (and would only cost extra key enumeration).
        var list = new List<RegistryQueryEntry>();
        switch (scope.Type)
        {
            case ScopeType.AllFiles:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\*\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\*\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                break;
            case ScopeType.Folder:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Directory\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Folder\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Folder\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                break;
            case ScopeType.FolderBackground:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                break;
            case ScopeType.Drive:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Drive\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Drive\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                break;
            case ScopeType.Desktop:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\DesktopBackground\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                break;
            case ScopeType.FileExtension:
                if (string.IsNullOrEmpty(scope.Extension)) break;
                var ext = scope.Extension!;
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\{ext}\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{ext}\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                if (!string.IsNullOrEmpty(scope.ProgId))
                {
                    var progId = scope.ProgId!;
                    list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\{progId}\shellex\ContextMenuHandlers", RegistryHiveOrigin.CurrentUser));
                    list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{progId}\shellex\ContextMenuHandlers", RegistryHiveOrigin.LocalMachine));
                }
                break;
        }
        return list;
    }

    /// <summary>
    /// Stable-sorts items so that <c>Position=Top</c> entries come first,
    /// regular entries next, and <c>Position=Bottom</c> entries last. Ties
    /// fall back to the original index, preserving registry insertion order
    /// within each bucket.
    /// </summary>
    private static IReadOnlyList<ContextMenuItem> SortByPosition(IEnumerable<ContextMenuItem> items)
    {
        return items
            .Select((item, index) => (item, index))
            .OrderBy(t => PositionRank(t.item.Position))
            .ThenBy(t => t.index)
            .Select(t => t.item)
            .ToList();
    }

    private static int PositionRank(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return 1;
        if (position.Equals("Top", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (position.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private ContextMenuItem ParseVerb(RegistryKey verbKey, string verbName, RegistryQueryEntry rootEntry, int depth, bool isSharedDefinition = false)
    {
        var defaultName = verbKey.GetValue(string.Empty) as string;
        var muiVerb = verbKey.GetValue("MUIVerb") as string;
        var icon = verbKey.GetValue("Icon") as string;
        var position = verbKey.GetValue("Position") as string;
        var delegateExecute = verbKey.GetValue("DelegateExecute") as string;
        var explorerCommandHandler = verbKey.GetValue("ExplorerCommandHandler") as string;
        var appliesTo = verbKey.GetValue("AppliesTo") as string;
        var valueNames = verbKey.GetValueNames();
        var hasExtended = valueNames.Any(n => string.Equals(n, "Extended", StringComparison.OrdinalIgnoreCase));
        var hasProgrammaticOnly = valueNames.Any(n => string.Equals(n, "ProgrammaticAccessOnly", StringComparison.OrdinalIgnoreCase));
        var hasNeverDefault = valueNames.Any(n => string.Equals(n, "NeverDefault", StringComparison.OrdinalIgnoreCase));
        var hasLegacyDisable = valueNames.Any(n => string.Equals(n, "LegacyDisable", StringComparison.OrdinalIgnoreCase));
        var hasLuaShield = valueNames.Any(n => string.Equals(n, "HasLUAShield", StringComparison.OrdinalIgnoreCase));
        var hasVelocity = valueNames.Any(n =>
            string.Equals(n, "HideBasedOnVelocityId", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "ShowBasedOnVelocityId", StringComparison.OrdinalIgnoreCase));

        var displayName = ResolveDisplayName(muiVerb, defaultName, verbName, explorerCommandHandler);
        var command = ReadCommand(verbKey);
        var expandedCommand = string.IsNullOrEmpty(command)
            ? string.Empty
            : SafeExpandEnvironment(command);
        var (children, cascading) = depth >= 4
            ? (Array.Empty<ContextMenuItem>() as IReadOnlyList<ContextMenuItem>, CascadingKind.None)
            : ParseChildren(verbKey, rootEntry, depth + 1);

        return new ContextMenuItem
        {
            VerbName = verbName,
            DisplayName = displayName,
            Command = command,
            ExpandedCommand = expandedCommand,
            IconExpression = string.IsNullOrEmpty(icon) ? null : icon,
            MuiVerb = muiVerb,
            Position = position,
            IsExtended = hasExtended,
            IsProgrammaticOnly = hasProgrammaticOnly,
            IsNeverDefault = hasNeverDefault,
            IsLegacyDisabled = hasLegacyDisable,
            DelegateExecute = string.IsNullOrEmpty(delegateExecute) ? null : delegateExecute,
            ExplorerCommandHandler = string.IsNullOrEmpty(explorerCommandHandler) ? null : explorerCommandHandler,
            HasNoStoredLabel = string.IsNullOrWhiteSpace(defaultName) && string.IsNullOrWhiteSpace(muiVerb),
            AppliesTo = string.IsNullOrEmpty(appliesTo) ? null : appliesTo,
            HasLuaShield = hasLuaShield,
            HasVelocityCondition = hasVelocity,
            IsSharedDefinition = isSharedDefinition,
            IsSystemVerb = SystemVerbs.Contains(verbName),
            Origin = rootEntry.Origin,
            // The key's own full path is the only coordinate the write side can trust.
            // Rebuilding it from the scope root lost the "\<parent>\shell\" segment for
            // nested children, so saving one wrote to a fresh junk key and deleting one
            // silently did nothing.
            RegistryPath = verbKey.Name,
            Children = children,
            Cascading = cascading,
        };
    }

    private static string ReadCommand(RegistryKey verbKey)
    {
        try
        {
            using var cmd = verbKey.OpenSubKey("command", writable: false);
            return cmd?.GetValue(string.Empty) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeExpandEnvironment(string command)
    {
        try
        {
            return Environment.ExpandEnvironmentVariables(command);
        }
        catch
        {
            return command;
        }
    }

    /// <summary>
    /// Turns the stored values into the text a user should see.
    /// <para>
    /// The order mirrors Explorer: MUIVerb wins, then the default value, both of
    /// which may be indirect (<c>@dll,-id</c>) references. When neither exists the
    /// previous code fell through to the raw key name, which is how
    /// <c>HKLM\...\DesktopBackground\Shell\.SpotlightLearnMore</c> ended up showing
    /// ".SpotlightLearnMore" in the UI while Explorer shows a localized label — the
    /// name lives in the <c>ExplorerCommandHandler</c> CLSID, not in the key.
    /// </para>
    /// </summary>
    private static string ResolveDisplayName(string? muiVerb, string? defaultName, string verbName,
        string? explorerCommandHandler)
    {
        // MUIVerb is the string Explorer prefers. It may be a literal or an
        // "@<dll>,-<id>" indirect reference; resolve the latter through MUI.
        if (!string.IsNullOrEmpty(muiVerb))
        {
            if (!MuiStringHelper.IsIndirectReference(muiVerb))
                return muiVerb;

            var resolved = MuiStringHelper.Resolve(muiVerb);
            // IsIndirectReference / Resolve both hand back the input unchanged when
            // they cannot do better; an unresolved "@dll,-id" is not a label.
            if (!string.IsNullOrEmpty(resolved) && !MuiStringHelper.IsIndirectReference(resolved))
                return resolved!;
        }

        if (!string.IsNullOrEmpty(defaultName))
        {
            if (!MuiStringHelper.IsIndirectReference(defaultName))
                return defaultName;

            var resolved = MuiStringHelper.Resolve(defaultName);
            if (!string.IsNullOrEmpty(resolved) && !MuiStringHelper.IsIndirectReference(resolved))
                return resolved!;
        }

        // Win11 command handlers keep their label in the CLSID registration.
        var fromHandler = ResolveHandlerLabel(explorerCommandHandler);
        if (!string.IsNullOrEmpty(fromHandler))
            return fromHandler!;

        // Shell-owned verbs (open/runas/edit/...) store their label in Windows,
        // not in the registry — Explorer shows 打开(O) where the key says "open".
        var standard = Helpers.StandardVerbLabels.Resolve(verbName);
        if (standard is not null)
            return standard;

        // An unresolved resource reference is worse than the key name: the key at
        // least tells the user which verb this is. The item is flagged as having no
        // stored label, so the UI marks it as inferred instead of presenting a
        // registry identifier as a menu title.
        return verbName;
    }

    /// <summary>
    /// Reads the display name a Win11 <c>ExplorerCommandHandler</c> registers for
    /// itself. The HKCR default value is usually the handler's own type name (for
    /// example "Edit Stickers"), which is what Explorer puts in the menu.
    /// </summary>
    private static string? ResolveHandlerLabel(string? handlerClsid)
    {
        if (string.IsNullOrWhiteSpace(handlerClsid)) return null;

        // Curated names win: some handlers register only a technical identifier
        // (".SpotlightLearnMore") while Explorer draws a localized sentence.
        var known = Helpers.StandardVerbLabels.ResolveHandler(handlerClsid);
        if (!string.IsNullOrEmpty(known)) return known;

        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{handlerClsid}");
            var raw = key?.GetValue(string.Empty) as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // A GUID as a name is not a label.
            if (raw!.StartsWith('{')) return null;
            if (raw.StartsWith('@'))
                return MuiStringHelper.Resolve(raw) ?? null;
            return raw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Discovers cascading children using the three mechanisms Windows supports,
    /// in priority order: <c>SubCommands</c> (Win10+ CommandStore), then
    /// <c>ExtendedSubCommandsKey</c> (pointer to another shell-like tree), then
    /// the legacy nested <c>\shell</c> sub-tree.
    /// </summary>
    private (IReadOnlyList<ContextMenuItem> children, CascadingKind kind) ParseChildren(
        RegistryKey verbKey, RegistryQueryEntry rootEntry, int depth)
    {
        // Path A: SubCommands (REG_SZ semicolon list, sometimes REG_MULTI_SZ).
        var subCommands = verbKey.GetValue("SubCommands");
        if (subCommands is not null)
        {
            var children = new List<ContextMenuItem>();
            foreach (var v in ExpandSubCommands(subCommands))
            {
                if (string.IsNullOrWhiteSpace(v))
                    continue;
                var (childKey, childEntry) = OpenCommandStore(v);
                if (childKey is null || childEntry is null)
                    continue;
                try
                {
                    // Children resolved from CommandStore are shared definitions:
                    // the same verb can be referenced by several parents (and by
                    // other users' menus), so they are flagged for the UI.
                    children.Add(ParseVerb(childKey, v, childEntry, depth, isSharedDefinition: true));
                }
                finally
                {
                    childKey.Dispose();
                }
            }
            return (SortByPosition(children), CascadingKind.SubCommands);
        }

        // Path B: ExtendedSubCommandsKey points at another shell-like sub-tree
        // (commonly used by cloud-storage providers and Office handlers).
        var extendedKeyPath = verbKey.GetValue("ExtendedSubCommandsKey") as string;
        if (!string.IsNullOrWhiteSpace(extendedKeyPath))
        {
            var children = ParseExtendedSubCommandsKey(extendedKeyPath, rootEntry, depth);
            if (children.Count > 0)
                return (SortByPosition(children), CascadingKind.ExtendedSubCommandsKey);
        }

        // Path C: nested \shell\<child>.
        try
        {
            using var nested = verbKey.OpenSubKey("shell", writable: false);
            if (nested is null)
                return (Array.Empty<ContextMenuItem>(), CascadingKind.None);
            var children = new List<ContextMenuItem>();
            foreach (var subName in nested.GetSubKeyNames())
            {
                using var childKey = nested.OpenSubKey(subName, writable: false);
                if (childKey is null) continue;
                children.Add(ParseVerb(childKey, subName, rootEntry, depth));
            }
            return children.Count == 0
                ? ((IReadOnlyList<ContextMenuItem>)Array.Empty<ContextMenuItem>(), CascadingKind.None)
                : (SortByPosition(children), CascadingKind.NestedShell);
        }
        catch
        {
            return (Array.Empty<ContextMenuItem>(), CascadingKind.None);
        }
    }

    /// <summary>
    /// Resolves an <c>ExtendedSubCommandsKey</c> path. The value usually looks
    /// like <c>SomeProvider\\ShellEx\\ContextMenu</c> and is interpreted relative
    /// to <c>HKCR</c>; we also probe HKCU/HKLM Software\Classes for completeness.
    /// </summary>
    private List<ContextMenuItem> ParseExtendedSubCommandsKey(string keyPath, RegistryQueryEntry rootEntry, int depth)
    {
        var probes = new[]
        {
            new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\{keyPath}", RegistryHiveOrigin.CurrentUser),
            new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{keyPath}", RegistryHiveOrigin.LocalMachine),
            new RegistryQueryEntry(RegistryHive.ClassesRoot, keyPath, RegistryHiveOrigin.ClassesRoot),
        };

        foreach (var probe in probes)
        {
            using var root = _registry.OpenRead(probe.Hive, probe.SubKey);
            if (root is null)
                continue;

            // ExtendedSubCommandsKey targets sometimes point at the verb root
            // directly, and sometimes at a key whose own \shell child holds the
            // verb list. Look in both shapes.
            using var shellKey = root.OpenSubKey("shell", writable: false) ?? root;
            var children = new List<ContextMenuItem>();
            foreach (var subName in shellKey.GetSubKeyNames())
            {
                using var childKey = shellKey.OpenSubKey(subName, writable: false);
                if (childKey is null) continue;
                children.Add(ParseVerb(childKey, subName, probe, depth));
            }
            if (children.Count > 0)
                return children;
        }

        return new List<ContextMenuItem>();
    }

    private static IEnumerable<string> ExpandSubCommands(object value)
    {
        switch (value)
        {
            case string[] arr:
                foreach (var s in arr) yield return s.Trim();
                yield break;
            case string single:
                foreach (var part in single.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    yield return part.Trim();
                yield break;
        }
    }

    /// <summary>
    /// Opens a verb declared via <c>SubCommands</c>. Win10+ stores these under
    /// HKLM\\...\\CommandStore\\shell, but apps may also register their own
    /// definitions in HKCU\\Software\\Classes or HKCR\\CommandStore. We probe
    /// each and return both the key and a synthetic query entry so the caller
    /// can attribute the registry path correctly in the UI.
    /// </summary>
    private (RegistryKey? key, RegistryQueryEntry? entry) OpenCommandStore(string verb)
    {
        // 1) HKLM CommandStore (most common on Windows 10/11).
        var key = _registry.OpenRead(RegistryHive.LocalMachine,
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\{verb}");
        if (key is not null)
            return (key, new RegistryQueryEntry(RegistryHive.LocalMachine,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell", RegistryHiveOrigin.LocalMachine));

        // 2) HKCU CommandStore (rare but valid - per-user overrides).
        key = _registry.OpenRead(RegistryHive.CurrentUser,
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\{verb}");
        if (key is not null)
            return (key, new RegistryQueryEntry(RegistryHive.CurrentUser,
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell", RegistryHiveOrigin.CurrentUser));

        // 3) HKCR\CommandStore fallback (some installers use this shape).
        key = _registry.OpenRead(RegistryHive.ClassesRoot, $@"CommandStore\shell\{verb}");
        if (key is not null)
            return (key, new RegistryQueryEntry(RegistryHive.ClassesRoot,
                @"CommandStore\shell", RegistryHiveOrigin.ClassesRoot));

        return (null, null);
    }
}
