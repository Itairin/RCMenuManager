using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>One log line. Written as a single JSON object per file line.</summary>
public sealed record OperationLogEntry(
    DateTime timestamp,
    string scopeId,
    string verb,
    string op,
    RegistryHive hive,
    string subKey,
    string? backupPath,
    bool success,
    string? error);

/// <summary>
/// Append-only JSON log of every write attempt (one JSON object per line).
/// The file is capped and rotated: an unbounded log eventually becomes a
/// support problem ("operations.log is 40 MB") for a tool that writes here on
/// every edit.
/// </summary>
public sealed class OperationLogService : IOperationLog
{
    private const long MaxBytes = 5L * 1024 * 1024;
    private const int KeptGenerations = 2; // operations.log.1, operations.log.2

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _lock = new();

    public OperationLogService() : this(DefaultPath()) { }

    public OperationLogService(string path) : this(path, MaxBytes) { }

    /// <summary>Size cap is injectable so rotation can be covered by tests.</summary>
    internal OperationLogService(string path, long maxBytes)
    {
        _path = path;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "operations.log");

    public void Append(OperationLogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_path, json + Environment.NewLine);
            }
            catch (IOException)
            {
                // A second instance (or an indexer/AV) holding the file must not
                // turn a successful registry write into a failed one.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Shifts operations.log -> .1 -> .2 once the active file exceeds the cap.</summary>
    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes) return;

        for (var gen = KeptGenerations - 1; gen >= 1; gen--)
        {
            var from = $"{_path}.{gen}";
            var to = $"{_path}.{gen + 1}";
            if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }
        File.Move(_path, _path + ".1", overwrite: true);
    }

    public IReadOnlyList<OperationLogEntry> ReadAll()
    {
        if (!File.Exists(_path)) return Array.Empty<OperationLogEntry>();
        var list = new List<OperationLogEntry>();
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { list.Add(JsonSerializer.Deserialize<OperationLogEntry>(line)!); }
            catch { }
        }
        return list;
    }
}
