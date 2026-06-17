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
}
