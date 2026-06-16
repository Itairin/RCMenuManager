using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class OperationLogServiceTests : IDisposable
{
    private readonly string _logPath;

    public OperationLogServiceTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), "RCMenuManagerLogTests", $"{Guid.NewGuid():N}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    [Fact]
    public void Append_writes_one_line_json_per_call()
    {
        var svc = new OperationLogService(_logPath);
        svc.Append(new OperationLogEntry(
            timestamp: DateTime.UtcNow,
            scopeId: "Folder",
            verb: "open",
            op: "Disable",
            hive: RegistryHive.CurrentUser,
            subKey: @"Software\Classes\Directory\shell\open",
            backupPath: @"C:\backups\open.reg",
            success: true,
            error: null));
        svc.Append(new OperationLogEntry(
            timestamp: DateTime.UtcNow,
            scopeId: "Folder",
            verb: "open",
            op: "Enable",
            hive: RegistryHive.CurrentUser,
            subKey: @"Software\Classes\Directory\shell\open",
            backupPath: @"C:\backups\open2.reg",
            success: false,
            error: "boom"));

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);

        var entry1 = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("Disable", entry1.GetProperty("op").GetString());
        Assert.True(entry1.GetProperty("success").GetBoolean());

        var entry2 = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.Equal("boom", entry2.GetProperty("error").GetString());
        Assert.False(entry2.GetProperty("success").GetBoolean());
    }

    public void Dispose()
    {
        try { File.Delete(_logPath); } catch { }
    }
}
