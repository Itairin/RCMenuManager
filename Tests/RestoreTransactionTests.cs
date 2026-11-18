using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Restore used to call the import directly: no snapshot, no audit line. Those two
/// facts are what make a bad restore survivable, so they are pinned here.
/// </summary>
public class RestoreTransactionTests
{
    [Fact]
    public void Restore_snapshots_the_current_state_and_logs_the_operation()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v");
        var backup = new RecordingBackup();
        var log = new CollectingLog();
        var svc = new RegistryWriteService(writer, backup, log, () => true, () => { }, _ => { });

        svc.RestoreFromBackup(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v",
            "Folder", "v", @"C:\backups\old.reg");

        Assert.Equal(1, backup.Exports);
        Assert.Single(log.Entries);
        Assert.Equal("Restore", log.Entries[0].op);
        Assert.True(log.Entries[0].success);
        Assert.NotNull(log.Entries[0].backupPath);
    }

    [Fact]
    public void Restore_records_a_failure_when_the_import_throws()
    {
        var backup = new RecordingBackup();
        var log = new CollectingLog();
        var svc = new RegistryWriteService(new InMemoryRegistryWriter(), backup, log, () => true, () => { },
            _ => throw new InvalidOperationException("bad reg"));

        Assert.Throws<InvalidOperationException>(() =>
            svc.RestoreFromBackup(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v",
                "Folder", "v", @"C:\backups\old.reg"));

        Assert.Single(log.Entries);
        Assert.False(log.Entries[0].success);
        Assert.Contains("bad reg", log.Entries[0].error);
    }

    [Fact]
    public void Restore_refuses_to_run_without_an_import_capability()
    {
        var svc = new RegistryWriteService(new InMemoryRegistryWriter(), new RecordingBackup(),
            new CollectingLog(), () => true, () => { });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.RestoreFromBackup(RegistryHive.CurrentUser, @"Software\x", "Folder", "x", @"C:\x.reg"));

        Assert.Contains("导入", ex.Message);
    }

    [Fact]
    public void Restore_requires_elevation_for_a_machine_key()
    {
        var svc = new RegistryWriteService(new InMemoryRegistryWriter(), new RecordingBackup(),
            new CollectingLog(), () => false, () => { }, _ => { });

        Assert.Throws<ElevationRequiredException>(() =>
            svc.RestoreFromBackup(RegistryHive.LocalMachine, @"SOFTWARE\Classes\x", "Folder", "x", @"C:\x.reg"));
    }

    private sealed class RecordingBackup : IBackupService
    {
        public int Exports { get; private set; }
        public string? Export(RegistryHive hive, string subKey, string scopeId, string verbName)
        {
            Exports++;
            return @"C:\backups\pre-restore.reg";
        }
        public IReadOnlyList<BackupRecord> List() => Array.Empty<BackupRecord>();
        public void Import(string filePath) { }
        public void Delete(string filePath) { }
    }

    private sealed class CollectingLog : IOperationLog
    {
        public List<OperationLogEntry> Entries { get; } = new();
        public void Append(OperationLogEntry entry) => Entries.Add(entry);
        public IReadOnlyList<OperationLogEntry> ReadAll() => Entries;
    }
}
