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

    private ContextMenuItem ParseVerb(RegistryKey verbKey, string verbName, RegistryQueryEntry rootEntry, int depth)
    {
        var defaultName = verbKey.GetValue(string.Empty) as string;
        var muiVerb = verbKey.GetValue("MUIVerb") as string;
        var icon = verbKey.GetValue("Icon") as string;
        var position = verbKey.GetValue("Position") as string;
        var valueNames = verbKey.GetValueNames();
        var hasExtended = valueNames.Any(n => string.Equals(n, "Extended", StringComparison.OrdinalIgnoreCase));
        var hasProgrammaticOnly = valueNames.Any(n => string.Equals(n, "ProgrammaticAccessOnly", StringComparison.OrdinalIgnoreCase));
        var hasNeverDefault = valueNames.Any(n => string.Equals(n, "NeverDefault", StringComparison.OrdinalIgnoreCase));
        var hasLegacyDisable = valueNames.Any(n => string.Equals(n, "LegacyDisable", StringComparison.OrdinalIgnoreCase));

        var displayName = ResolveDisplayName(muiVerb, defaultName, verbName);
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
            IsSystemVerb = SystemVerbs.Contains(verbName),
            Origin = rootEntry.Origin,
            RegistryPath = $@"{rootEntry.FullPath}\{verbKey.Name.Substring(verbKey.Name.LastIndexOf('\\') + 1)}",
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

    private static string ResolveDisplayName(string? muiVerb, string? defaultName, string verbName)
    {
        // MUIVerb is the string Explorer prefers. It may be a literal or an
        // "@<dll>,-<id>" indirect reference; resolve the latter through MUI.
        if (!string.IsNullOrEmpty(muiVerb))
        {
            if (muiVerb.StartsWith('@'))
            {
                var resolved = MuiStringHelper.Resolve(muiVerb);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved!;
            }
            else
            {
                return muiVerb;
            }
        }

        if (!string.IsNullOrEmpty(defaultName))
        {
            if (defaultName!.StartsWith('@'))
            {
                var resolved = MuiStringHelper.Resolve(defaultName);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved!;
            }
            else
            {
                return defaultName;
            }
        }

        // Last resort: show the raw indirect reference so the user still sees something.
        if (!string.IsNullOrEmpty(muiVerb))
            return muiVerb!;
        if (!string.IsNullOrEmpty(defaultName))
            return defaultName!;
        return verbName;
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
                    children.Add(ParseVerb(childKey, v, childEntry, depth));
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
