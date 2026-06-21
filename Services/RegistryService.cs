using System;
using System.Collections.Generic;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Read-only registry access used by M1. Exposes scope -> query-paths
/// resolution and ProgID lookup for arbitrary file extensions. Writes are
/// added in M2.
/// </summary>
public class RegistryService
{
    /// <summary>
    /// Returns the registry sub-paths to inspect for a given scope, in the
    /// order the merge should happen (HKCU before HKLM before HKCR). When the
    /// caller deduplicates by verb name they get an HKCU-wins behaviour, which
    /// matches Windows' own resolution.
    /// </summary>
    public IReadOnlyList<RegistryQueryEntry> GetShellQueryPaths(MenuScope scope)
    {
        var list = new List<RegistryQueryEntry>(4);
        switch (scope.Type)
        {
            case ScopeType.AllFiles:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\*\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\*\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"*\shell", RegistryHiveOrigin.ClassesRoot));
                // Explorer also shows items registered under AllFilesystemObjects
                // when right-clicking a file (e.g. Work Folders, Offline Files).
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\AllFilesystemObjects\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\AllFilesystemObjects\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"AllFilesystemObjects\shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.AllFilesystemObjects:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\AllFilesystemObjects\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\AllFilesystemObjects\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"AllFilesystemObjects\shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.Folder:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"Directory\shell", RegistryHiveOrigin.ClassesRoot));
                // Folder is the legacy class; Windows still uses it for the built-in
                // "open/explore/opennew*" verbs that show on every folder.
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Folder\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Folder\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"Folder\shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.FolderBackground:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Directory\Background\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\Background\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"Directory\Background\shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.Drive:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\Drive\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Drive\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"Drive\shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.Desktop:
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, @"Software\Classes\DesktopBackground\Shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, @"SOFTWARE\Classes\DesktopBackground\Shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, @"DesktopBackground\Shell", RegistryHiveOrigin.ClassesRoot));
                break;

            case ScopeType.FileExtension:
                if (string.IsNullOrEmpty(scope.Extension))
                    break;
                var ext = scope.Extension!;
                // Per-extension shell on the extension key itself (rare but supported).
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\{ext}\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{ext}\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, $@"{ext}\shell", RegistryHiveOrigin.ClassesRoot));
                // SystemFileAssociations\<ext>\shell hosts the "open"/"edit" entries
                // Explorer uses for arbitrary files of a type.
                list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\SystemFileAssociations\{ext}\shell", RegistryHiveOrigin.CurrentUser));
                list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\SystemFileAssociations\{ext}\shell", RegistryHiveOrigin.LocalMachine));
                list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, $@"SystemFileAssociations\{ext}\shell", RegistryHiveOrigin.ClassesRoot));
                if (!string.IsNullOrEmpty(scope.ProgId))
                {
                    var progId = scope.ProgId!;
                    list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\{progId}\shell", RegistryHiveOrigin.CurrentUser));
                    list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\{progId}\shell", RegistryHiveOrigin.LocalMachine));
                    list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, $@"{progId}\shell", RegistryHiveOrigin.ClassesRoot));
                    // SystemFileAssociations\<progId>\shell is where most built-in
                    // per-type handlers register.
                    list.Add(new RegistryQueryEntry(RegistryHive.CurrentUser, $@"Software\Classes\SystemFileAssociations\{progId}\shell", RegistryHiveOrigin.CurrentUser));
                    list.Add(new RegistryQueryEntry(RegistryHive.LocalMachine, $@"SOFTWARE\Classes\SystemFileAssociations\{progId}\shell", RegistryHiveOrigin.LocalMachine));
                    list.Add(new RegistryQueryEntry(RegistryHive.ClassesRoot, $@"SystemFileAssociations\{progId}\shell", RegistryHiveOrigin.ClassesRoot));
                }
                break;
        }
        return list;
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

        // 3) HKCR\.ext default (merged view).
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
