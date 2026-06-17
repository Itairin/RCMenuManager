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

public class BackupServiceListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RCMenuManagerBackupListTests", Guid.NewGuid().ToString("N"));
    public BackupServiceListTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void List_returns_records_newest_first_and_ignores_non_reg()
    {
        File.WriteAllText(Path.Combine(_dir, "20260617-142530-Folder-A.reg"), "x");
        File.WriteAllText(Path.Combine(_dir, "20260617-150000-Folder-B.reg"), "x");
        File.WriteAllText(Path.Combine(_dir, "ignore.txt"), "noise");
        var list = new BackupService(_dir).List();
        Assert.Equal(2, list.Count);
        Assert.Equal("B", list[0].VerbName);
        Assert.Equal("A", list[1].VerbName);
    }

    [Fact]
    public void List_returns_empty_when_dir_missing() => Assert.Empty(new BackupService(Path.Combine(_dir, "nope")).List());

    [Fact]
    public void Delete_removes_existing_file()
    {
        var path = Path.Combine(_dir, "20260617-142530-Folder-A.reg");
        File.WriteAllText(path, "x");
        new BackupService(_dir).Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_silent_on_missing() => new BackupService(_dir).Delete(Path.Combine(_dir, "never-existed.reg"));
}
