using System;
using System.IO;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

[Collection("RealRegistry")]
public class BackupServiceTests : IDisposable
{
    private readonly string _sandbox = $@"Software\RCMenuManager.Tests\Backup\{Guid.NewGuid():N}";
    private readonly string _outDir;

    public BackupServiceTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outDir);
    }

    [Fact]
    public void Export_writes_reg_file_with_expected_naming()
    {
        var writer = new Win32RegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox);
        writer.SetStringValue(RegistryHive.CurrentUser, _sandbox, "Sample", "Hello");

        var svc = new BackupService(_outDir);
        var path = svc.Export(RegistryHive.CurrentUser, _sandbox, scopeId: "Folder", verbName: "TestVerb");

        Assert.True(File.Exists(path), $"backup file should exist at {path}");
        Assert.EndsWith(".reg", path);
        Assert.Contains("Folder", Path.GetFileName(path));
        Assert.Contains("TestVerb", Path.GetFileName(path));
        var content = File.ReadAllText(path, System.Text.Encoding.Unicode);
        Assert.Contains("Sample", content);
    }

    [Fact]
    public void Export_throws_when_target_key_missing()
    {
        var svc = new BackupService(_outDir);
        Assert.Throws<InvalidOperationException>(() =>
            svc.Export(RegistryHive.CurrentUser, _sandbox + @"\Missing", scopeId: "Folder", verbName: "TestVerb"));
    }

    public void Dispose()
    {
        try { new Win32RegistryWriter().DeleteSubKeyTree(RegistryHive.CurrentUser, _sandbox); } catch { }
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }
}
