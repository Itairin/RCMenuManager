using System;
using System.Collections.Generic;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Read-only registry access: scope -> query-path resolution and ProgID lookup.
/// <para>
/// Only HKCU\Software\Classes and HKLM\SOFTWARE\Classes are queried, in that
/// order, so the first hit wins (HKCU overrides machine-wide registrations).
/// HKCR is intentionally absent from every list: it is the merged view of those
/// two locations, so probing it can never surface anything new — it only costs
/// extra key enumeration and would make write-back ambiguous.
/// </para>
/// </summary>
public class RegistryService
{
    /// <summary>
    /// Returns the registry sub-paths to inspect for a given scope, in the
    /// order the merge should happen (HKCU before HKLM). When the caller
    /// deduplicates by verb name they get an HKCU-wins behaviour, which
    /// matches Windows' own resolution.
    /// </summary>
    public IReadOnlyList<RegistryQueryEntry> GetShellQueryPaths(MenuScope scope)
    {
        var list = new List<RegistryQueryEntry>(4);
        switch (scope.Type)
        {
            case ScopeType.AllFiles:
                Add(list, @"*\shell");
                // Explorer also shows items registered under AllFilesystemObjects
                // when right-clicking a file (e.g. Work Folders, Offline Files).
                Add(list, @"AllFilesystemObjects\shell");
                break;

            case ScopeType.AllFilesystemObjects:
                Add(list, @"AllFilesystemObjects\shell");
                break;

            case ScopeType.Folder:
                Add(list, @"Directory\shell");
                // Folder is the legacy class; Windows still uses it for the built-in
                // "open/explore/opennew*" verbs that show on every folder.
                Add(list, @"Folder\shell");
                break;

            case ScopeType.FolderBackground:
                Add(list, @"Directory\Background\shell");
                break;

            case ScopeType.Drive:
                Add(list, @"Drive\shell");
                break;

            case ScopeType.Desktop:
                Add(list, @"DesktopBackground\Shell");
                break;

            case ScopeType.FileExtension:
                if (string.IsNullOrEmpty(scope.Extension))
                    break;
                var ext = scope.Extension!;
                // Per-extension shell on the extension key itself (rare but supported).
                Add(list, $@"{ext}\shell");
                // SystemFileAssociations\<ext>\shell hosts the "open"/"edit" entries
                // Explorer uses for arbitrary files of a type.
                Add(list, $@"SystemFileAssociations\{ext}\shell");
                if (!string.IsNullOrEmpty(scope.ProgId))
                {
                    var progId = scope.ProgId!;
                    Add(list, $@"{progId}\shell");
                    // SystemFileAssociations\<progId>\shell is where most built-in
                    // per-type handlers register.
                    Add(list, $@"SystemFileAssociations\{progId}\shell");
                }
                // Explorer keeps merging after the ProgID: the perceived type
                // ("text", "image", "video"...) carries the entries every file of
                // that kind shares - .txt gets 打开/编辑 from SystemFileAssociations
                // \text, not from txtfile - and finally the all-files classes.
                // Without these the list would not contain verbs the preview shows.
                var perceived = ResolvePerceivedType(ext, scope.ProgId);
                if (!string.IsNullOrEmpty(perceived))
                    Add(list, $@"SystemFileAssociations\{perceived}\shell");
                Add(list, @"*\shell");
                Add(list, @"AllFilesystemObjects\shell");
                break;
        }

        static void Add(List<RegistryQueryEntry> target, string subKeyUnderClasses)
        {
            target.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\" + subKeyUnderClasses, RegistryHiveOrigin.CurrentUser));
            target.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\" + subKeyUnderClasses, RegistryHiveOrigin.LocalMachine));
        }

        return list;
    }

    /// <summary>
    /// Reads the file type's <c>PerceivedType</c> ("text", "image", "video",
    /// "audio", "compressed", "document"). Windows stores it on the extension key
    /// and, for older types, on the ProgID instead.
    /// </summary>
    public string? ResolvePerceivedType(string extension, string? progId)
    {
        try
        {
            using (var extKey = Registry.ClassesRoot.OpenSubKey(extension))
            {
                if (extKey?.GetValue("PerceivedType") is string fromExt && !string.IsNullOrWhiteSpace(fromExt))
                    return fromExt;
            }
            if (!string.IsNullOrEmpty(progId))
            {
                using var progKey = Registry.ClassesRoot.OpenSubKey(progId);
                if (progKey?.GetValue("PerceivedType") is string fromProg && !string.IsNullOrWhiteSpace(fromProg))
                    return fromProg;
            }
        }
        catch
        {
            // Missing key / access denied: the type simply has no shared class.
        }
        return null;
    }

    /// <summary>
    /// Resolves the ProgID for a file extension, e.g. ".txt" -> "txtfile".
    /// Tries HKCU's UserChoice first (modern association), then falls back to
    /// the extension key default value.
    /// </summary>
    public string? ResolveProgId(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        ext = ext.ToLowerInvariant();

        // 1) UserChoice override (Windows 10+).
        try
        {
            using var uc = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
            var progId = uc?.GetValue("ProgId") as string;
            if (!string.IsNullOrEmpty(progId))
                return progId;
        }
        catch { /* tolerate UserChoice ACL failures */ }

        // 2) HKCU\Software\Classes\.ext default.
        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}");
            var progId = hkcu?.GetValue(string.Empty) as string;
            if (!string.IsNullOrEmpty(progId))
                return progId;
        }
        catch { }

        // 3) HKCR\.ext default (merged view) -- readable-only lookup, never queried
        //    for verb lists because it would duplicate the HKCU/HKLM probes.
        try
        {
            using var hkcr = Registry.ClassesRoot.OpenSubKey(ext);
            var progId = hkcr?.GetValue(string.Empty) as string;
            if (!string.IsNullOrEmpty(progId))
                return progId;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Opens a registry key for read access. Returns null when the key is
    /// missing or unreachable. Caller owns the disposable.
    /// </summary>
    public RegistryKey? OpenRead(RegistryHive hive, string subKey)
    {
        try
        {
            return hive switch
            {
                RegistryHive.ClassesRoot => Registry.ClassesRoot.OpenSubKey(subKey, writable: false),
                RegistryHive.CurrentUser => Registry.CurrentUser.OpenSubKey(subKey, writable: false),
                RegistryHive.LocalMachine => Registry.LocalMachine.OpenSubKey(subKey, writable: false),
                RegistryHive.Users => Registry.Users.OpenSubKey(subKey, writable: false),
                RegistryHive.CurrentConfig => Registry.CurrentConfig.OpenSubKey(subKey, writable: false),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
