using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Exports registry sub-trees to .reg files before a write, so every mutation can
/// be rolled back. Two rules matter for correctness:
/// <list type="bullet">
/// <item>A missing key is not an error — create operations have no previous state
/// to snapshot, so <see cref="Export"/> returns null.</item>
/// <item>File names must be unique even for several writes inside the same second
/// (a single "save" touches up to five values), otherwise the original snapshot
/// gets overwritten by a half-applied one.</item>
/// </list>
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly string _backupDir;
    private readonly IRegistrySnapshotTool _tool;

    public BackupService() : this(DefaultBackupDir()) { }
    public BackupService(string backupDir) : this(backupDir, new RegExeSnapshotTool()) { }

    public BackupService(string backupDir, IRegistrySnapshotTool tool)
    {
        _backupDir = backupDir;
        _tool = tool;
        Directory.CreateDirectory(_backupDir);
    }

    public static string DefaultBackupDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "backups");

    public string? Export(RegistryHive hive, string subKey, string scopeId, string verbName)
    {
        // Nothing to snapshot for a brand-new verb; the caller is creating it.
        if (!KeyExists(hive, subKey))
            return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fullPath = AllocateUniquePath(stamp, scopeId, verbName);
        var fullKeyPath = $"{HiveDisplayName(hive)}\\{subKey}";
        _tool.Export(fullKeyPath, fullPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"备份文件未生成：{fullPath}");
        return fullPath;
    }

    public IReadOnlyList<BackupRecord> List()
    {
        if (!Directory.Exists(_backupDir)) return Array.Empty<BackupRecord>();
        var records = new List<BackupRecord>();
        foreach (var f in Directory.EnumerateFiles(_backupDir, "*.reg"))
        {
            var r = BackupRecord.FromFile(f, Array.Empty<OperationLogEntry>());
            if (r is not null) records.Add(r);
        }
        records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return records;
    }

    public void Import(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("备份文件不存在", filePath);
        _tool.Import(filePath);
    }

    public void Delete(string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    /// <summary>
    /// Picks a file name that is guaranteed free on disk. The timestamp only has
    /// second resolution, and one user action can issue several writes for the
    /// same verb, so a numeric sequence is appended and probed until unused.
    /// </summary>
    private string AllocateUniquePath(string stamp, string scopeId, string verbName)
    {
        var scopePart = Sanitize(scopeId);
        var verbPart = Sanitize(verbName);
        for (var seq = 0; seq < 1000; seq++)
        {
            var candidate = Path.Combine(_backupDir, $"{stamp}-{seq:D3}-{scopePart}-{verbPart}.reg");
            if (!File.Exists(candidate))
                return candidate;
        }
        // Extremely unlikely; still prefer a unique file over overwriting a snapshot.
        return Path.Combine(_backupDir, $"{stamp}-{Guid.NewGuid():N}-{scopePart}-{verbPart}.reg");
    }

    private static bool KeyExists(RegistryHive hive, string subKey)
    {
        try
        {
            using var root = OpenRoot(hive);
            using var key = root?.OpenSubKey(subKey);
            return key is not null;
        }
        catch
        {
            // Unreadable key (ACL / 32-64 bit mismatch) behaves like "absent":
            // we skip the snapshot rather than blocking the write.
            return false;
        }
    }

    private static RegistryKey? OpenRoot(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => Registry.ClassesRoot,
        RegistryHive.CurrentUser => Registry.CurrentUser,
        RegistryHive.LocalMachine => Registry.LocalMachine,
        RegistryHive.Users => Registry.Users,
        RegistryHive.CurrentConfig => Registry.CurrentConfig,
        _ => null,
    };

    private static string HiveDisplayName(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.Users => "HKU",
        _ => hive.ToString(),
    };

    private static string Sanitize(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
