using System;
using System.IO;
using Microsoft.Win32;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Naming / lifetime rules of the backup service. The external reg.exe call is
/// replaced by a fake so these run anywhere; the real tool is exercised by
/// <see cref="BackupServiceRegExeTests"/>.
/// </summary>
[Collection("RealRegistry")]
public class BackupServiceTests : IDisposable
{
    private readonly string _sandbox = $@"Software\RCMenuManager.Tests\Backup\{Guid.NewGuid():N}";
    private readonly string _outDir;
    private readonly FakeSnapshotTool _tool = new();

    public BackupServiceTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outDir);
    }

    private BackupService NewService() => new(_outDir, _tool);

    [Fact]
    public void Export_writes_reg_file_with_expected_naming()
    {
        var writer = new Win32RegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox);
        writer.SetStringValue(RegistryHive.CurrentUser, _sandbox, "Sample", "Hello");

        var path = NewService().Export(RegistryHive.CurrentUser, _sandbox, scopeId: "Folder", verbName: "TestVerb");

        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"backup file should exist at {path}");
        Assert.EndsWith(".reg", path);
        Assert.Contains("Folder", Path.GetFileName(path));
        Assert.Contains("TestVerb", Path.GetFileName(path));
        Assert.Equal(1, _tool.ExportCount);
        Assert.Equal($@"HKCU\{_sandbox}", _tool.LastExportKeyPath);
    }

    [Fact]
    public void Export_returns_null_when_target_key_missing()
    {
        // Create operations have no previous state: a missing key must not throw,
        // otherwise "add verb" and "apply preset" can never succeed.
        var path = NewService().Export(RegistryHive.CurrentUser, _sandbox + @"\Missing", scopeId: "Folder", verbName: "TestVerb");
        Assert.Null(path);
        Assert.Equal(0, _tool.ExportCount);
    }

    /// <summary>
    /// Several writes for the same verb can land inside the same second; each one
    /// must get its own file, otherwise the first (pre-write) snapshot is
    /// overwritten by a half-applied one.
    /// </summary>
    [Fact]
    public void Export_allocates_a_distinct_file_per_call()
    {
        var writer = new Win32RegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox);

        var svc = NewService();
        var first = svc.Export(RegistryHive.CurrentUser, _sandbox, "Folder", "SameVerb");
        var second = svc.Export(RegistryHive.CurrentUser, _sandbox, "Folder", "SameVerb");
        var third = svc.Export(RegistryHive.CurrentUser, _sandbox, "Folder", "SameVerb");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);
        Assert.NotEqual(first, third);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.True(File.Exists(third));
    }

    public void Dispose()
    {
        try { new Win32RegistryWriter().DeleteSubKeyTree(RegistryHive.CurrentUser, _sandbox); } catch { }
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }
}

/// <summary>
/// Exercises the real reg.exe pipeline. Returns early (rather than failing) when
/// the host forbids launching reg.exe — e.g. locked-down build agents; CI runners
/// do run it.
/// </summary>
[Collection("RealRegistry")]
public class BackupServiceRegExeTests : IDisposable
{
    private readonly string _sandbox = $@"Software\RCMenuManager.Tests\RegExe\{Guid.NewGuid():N}";
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerRegExeTests", Guid.NewGuid().ToString("N"));

    public BackupServiceRegExeTests() => Directory.CreateDirectory(_outDir);

    [Fact]
    public void Export_and_import_roundtrip_through_reg_exe()
    {
        var writer = new Win32RegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, _sandbox);
        writer.SetStringValue(RegistryHive.CurrentUser, _sandbox, "Sample", "Hello");

        var svc = new BackupService(_outDir);
        string? path;
        try
        {
            path = svc.Export(RegistryHive.CurrentUser, _sandbox, "Folder", "Roundtrip");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return; // reg.exe unavailable here
        }

        Assert.NotNull(path);
        Assert.Contains("Sample", File.ReadAllText(path!, System.Text.Encoding.Unicode));

        using (var key = Registry.CurrentUser.OpenSubKey(_sandbox, writable: true))
        {
            key!.DeleteValue("Sample");
        }

        svc.Import(path!);
        using var reopened = Registry.CurrentUser.OpenSubKey(_sandbox);
        Assert.Equal("Hello", reopened?.GetValue("Sample"));
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
        var list = new BackupService(_dir, new FakeSnapshotTool()).List();
        Assert.Equal(2, list.Count);
        Assert.Equal("B", list[0].VerbName);
        Assert.Equal("A", list[1].VerbName);
    }

    [Fact]
    public void List_returns_empty_when_dir_missing()
        => Assert.Empty(new BackupService(Path.Combine(_dir, "nope"), new FakeSnapshotTool()).List());

    [Fact]
    public void Delete_removes_existing_file()
    {
        var path = Path.Combine(_dir, "20260617-142530-Folder-A.reg");
        File.WriteAllText(path, "x");
        new BackupService(_dir, new FakeSnapshotTool()).Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_silent_on_missing()
        => new BackupService(_dir, new FakeSnapshotTool()).Delete(Path.Combine(_dir, "never-existed.reg"));
}

internal sealed class FakeSnapshotTool : IRegistrySnapshotTool
{
    public int ExportCount { get; private set; }
    public int ImportCount { get; private set; }
    public string? LastExportKeyPath { get; private set; }

    public void Export(string fullKeyPath, string filePath)
    {
        ExportCount++;
        LastExportKeyPath = fullKeyPath;
        File.WriteAllText(filePath, "Windows Registry Editor Version 5.00\r\n\r\n[" + fullKeyPath + "]\r\n");
    }

    public void Import(string filePath) => ImportCount++;
}
