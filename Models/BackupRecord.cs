using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        string scope, verb;
        var lastDash = rest.LastIndexOf('-');
        if (lastDash < 0) { scope = string.Empty; verb = rest; }
        else { scope = rest.Substring(0, lastDash); verb = rest.Substring(lastDash + 1); }
        var match = log.FirstOrDefault(e => e.backupPath == filePath);
        return new BackupRecord(
            FilePath: filePath, Timestamp: ts, ScopeId: scope, VerbName: verb,
            Operation: match?.op ?? "未知",
            RegistryPath: match is null ? null : $"{match.hive}\\{match.subKey}",
            Success: match?.success, Error: match?.error);
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
