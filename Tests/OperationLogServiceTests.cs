using System;
using System.IO;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class OperationLogServiceTests
{
    [Fact]
    public void ReadAll_returns_empty_when_file_missing()
    {
        var svc = new OperationLogService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "op.log"));
        Assert.Empty(svc.ReadAll());
    }

    [Fact]
    public void ReadAll_skips_corrupt_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"), "op.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "{\"timestamp\":\"2026-06-17T10:00:00Z\",\"scopeId\":\"Folder\",\"verb\":\"x\",\"op\":\"Disable\",\"hive\":0,\"subKey\":\"a\",\"backupPath\":null,\"success\":true,\"error\":null}" + Environment.NewLine
            + "{ this is not valid json" + Environment.NewLine);
        var svc = new OperationLogService(path);
        Assert.Single(svc.ReadAll());
    }

    [Fact]
    public void Append_rotates_once_the_cap_is_reached()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "op.log");

        // 200-byte cap: the first entry fits, the next one triggers a rotation.
        var svc = new OperationLogService(path, maxBytes: 200);
        for (var i = 0; i < 6; i++)
            svc.Append(Entry($"verb-{i}"));

        Assert.True(File.Exists(path + ".1"), "previous generation should exist");
        Assert.True(new FileInfo(path).Length < 400, "active log should have been trimmed by rotation");
        Assert.Contains(svc.ReadAll(), e => e.verb.StartsWith("verb-", StringComparison.Ordinal));
    }

    private static OperationLogEntry Entry(string verb) => new(
        DateTime.UtcNow, "Folder", verb, "Disable", Microsoft.Win32.RegistryHive.CurrentUser,
        @"Software\Classes\Directory\shell\x", null, success: true, error: null);
}
