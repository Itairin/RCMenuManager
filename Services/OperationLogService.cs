using System;
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
/// Append-only JSON log of every write attempt. The file is created on first
/// use and never rotated; Phase 6 will read this list to drive its UI.
/// </summary>
public sealed class OperationLogService : IOperationLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public OperationLogService() : this(DefaultPath()) { }

    public OperationLogService(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "operations.log");

    public void Append(OperationLogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        lock (_lock)
            File.AppendAllText(_path, json + Environment.NewLine);
    }
}
