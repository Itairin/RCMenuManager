using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Services;

namespace RCMenuManager.Models;

public sealed record BackupRecord(
    string FilePath,
    DateTime Timestamp,
    string ScopeId,
    string VerbName,
    string Operation,
    string? RegistryPath,
    bool? Success,
    string? Error)
{
    public string SuccessText => Success switch
    {
        true => "成功",
        false => "失败",
        _ => "未知",
    };

    public static BackupRecord? FromFile(string filePath, IReadOnlyList<OperationLogEntry> log)
    {
        var fileName = Path.GetFileName(filePath);
        if (!fileName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) return null;
        var stem = fileName.Substring(0, fileName.Length - 4);
        const int tsLen = 15;
        if (stem.Length < tsLen || stem[8] != '-') return null;
        if (!DateTime.TryParseExact(stem.Substring(0, tsLen), "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var ts)) return null;
        var rest = stem.Length > tsLen + 1 ? stem.Substring(tsLen + 1) : string.Empty;
        // Newer files carry a zero-padded collision counter between the timestamp
        // and the scope ("20260617-142530-001-Folder-OpenWith"), because several
        // writes for the same verb can land inside the same second.
        var seqDash = rest.IndexOf('-');
        if (seqDash > 0 && seqDash <= 4 && int.TryParse(rest.Substring(0, seqDash), out _))
            rest = rest.Substring(seqDash + 1);
        string scope, verb;
        var lastDash = rest.LastIndexOf('-');
        if (lastDash < 0) { scope = string.Empty; verb = rest; }
        else { scope = rest.Substring(0, lastDash); verb = rest.Substring(lastDash + 1); }
        var match = log.FirstOrDefault(e => e.backupPath == filePath);
        // The operation log is rotated (5 MB cap) and a backup easily outlives its
        // entry, so the .reg header is the authoritative fallback: every file
        // written by reg.exe starts with "[HKEY_...\path]". Without this, an old
        // backup is listed but reports "没有关联的操作日志记录" when restored.
        var registryPath = match is null
            ? TryReadRegistryPath(filePath)
            : $"{ShortHive(match.hive)}\\{match.subKey}";
        return new BackupRecord(
            FilePath: filePath, Timestamp: ts, ScopeId: scope, VerbName: verb,
            Operation: match?.op ?? "未知",
            RegistryPath: registryPath,
            Success: match?.success, Error: match?.error);
    }

    /// <summary>
    /// Renders a hive as the short name the rest of the app parses (HKCU / HKLM / ...).
    /// <c>hive.ToString()</c> would produce "LocalMachine", which
    /// <c>MainViewModel.ParseRegistryPath</c> does not recognise — it falls back to
    /// HKCU, so a machine-wide backup was restored without asking for elevation and
    /// then failed against HKLM.
    /// </summary>
    private static string ShortHive(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.Users => "HKU",
        RegistryHive.CurrentConfig => "HKCC",
        _ => hive.ToString(),
    };

    /// <summary>
    /// Reads the first "[HKEY_...\]" line of a .reg file and returns it in the
    /// "HKCU\Software\..." form used by the rest of the app. reg.exe writes UTF-16
    /// with a BOM by default, so both encodings are handled.
    /// </summary>
    internal static string? TryReadRegistryPath(string filePath)
    {
        const int maxBytes = 64 * 1024;
        try
        {
            if (!File.Exists(filePath)) return null;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)Math.Min(stream.Length, maxBytes);
            if (length <= 0) return null;
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = stream.Read(buffer, read, length - read);
                if (n <= 0) break;
                read += n;
            }
            var text = DecodeRegText(buffer);
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length < 3 || line[0] != '[' || line[^1] != ']') continue;
                return MapHive(line.Substring(1, line.Length - 2));
            }
        }
        catch
        {
            // Unreadable backup: leave RegistryPath null instead of guessing.
        }
        return null;
    }

    private static string DecodeRegText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    /// <summary>Maps the long registry root in a .reg header to the short hive name.</summary>
    private static string? MapHive(string fullKeyPath)
    {
        var sep = fullKeyPath.IndexOf('\\');
        var root = sep < 0 ? fullKeyPath : fullKeyPath.Substring(0, sep);
        var rest = sep < 0 ? string.Empty : fullKeyPath.Substring(sep + 1);
        var shortName = root.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKCU",
            "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKEY_CLASSES_ROOT" => "HKCR",
            "HKEY_USERS" => "HKU",
            "HKEY_CURRENT_CONFIG" => "HKCC",
            _ => null,
        };
        if (shortName is null) return null;
        return string.IsNullOrEmpty(rest) ? shortName : shortName + "\\" + rest;
    }

    public static IReadOnlyList<BackupRecord> ReadDirectory(string dir, IReadOnlyList<OperationLogEntry> log)
    {
        if (!Directory.Exists(dir)) return Array.Empty<BackupRecord>();
        var records = new List<BackupRecord>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.reg"))
        {
            var r = FromFile(f, log);
            if (r is not null) records.Add(r);
        }
        records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return records;
    }
}
