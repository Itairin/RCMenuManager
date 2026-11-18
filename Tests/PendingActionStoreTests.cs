using System;
using System.IO;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class PendingActionStoreTests
{
    private static string TempPath() => Path.Combine(
        Path.GetTempPath(), "RCMenuManagerTests", Guid.NewGuid().ToString("N"), "pending-action.json");

    [Fact]
    public void Save_then_peek_returns_the_same_action()
    {
        var store = new PendingActionStore(TempPath());
        store.Save(new PendingAction
        {
            Kind = PendingActionKinds.SaveEdit,
            Hive = RegistryHive.LocalMachine,
            SubKey = @"Software\Classes\Directory\shell\x",
            ScopeId = "Folder",
            VerbName = "x",
            IncludeCommand = true,
            Draft = new EditableVerbDraft { VerbName = "x", DisplayName = "显示名", Command = "cmd.exe" },
        });

        var loaded = store.Peek();

        Assert.NotNull(loaded);
        Assert.Equal(PendingActionKinds.SaveEdit, loaded!.Kind);
        Assert.Equal(RegistryHive.LocalMachine, loaded.Hive);
        Assert.Equal(@"Software\Classes\Directory\shell\x", loaded.SubKey);
        Assert.Equal("显示名", loaded.Draft!.DisplayName);
        Assert.True(loaded.IncludeCommand);
    }

    [Fact]
    public void Peek_returns_null_when_no_file_exists()
        => Assert.Null(new PendingActionStore(TempPath()).Peek());

    [Fact]
    public void Peek_returns_null_for_corrupt_json()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(new PendingActionStore(path).Peek());
    }

    [Fact]
    public void Peek_returns_null_once_the_action_expires()
    {
        // Replaying "delete this verb" ten minutes later is not the request the user
        // made, so a stale record must not fire.
        var path = TempPath();
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var store = new PendingActionStore(path, () => now);
        store.Save(new PendingAction { Kind = PendingActionKinds.Delete, VerbName = "gone" });

        var later = new PendingActionStore(path, () => now + PendingActionStore.MaxAge + TimeSpan.FromSeconds(1));
        Assert.Null(later.Peek());

        var justInTime = new PendingActionStore(path, () => now + PendingActionStore.MaxAge - TimeSpan.FromSeconds(1));
        Assert.NotNull(justInTime.Peek());
    }

    [Fact]
    public void Clear_removes_the_record_so_it_cannot_replay_twice()
    {
        var store = new PendingActionStore(TempPath());
        store.Save(new PendingAction { Kind = PendingActionKinds.Toggle, VerbName = "v" });

        store.Clear();

        Assert.Null(store.Peek());
        Assert.Null(new PendingActionStore(store.FilePath).Peek());
    }

    [Fact]
    public void Peek_does_not_consume_the_record()
    {
        // Startup reads it to decide what to show, then clears it explicitly once the
        // replay actually starts; a peek that deleted the file would make that ordering
        // impossible.
        var store = new PendingActionStore(TempPath());
        store.Save(new PendingAction { Kind = PendingActionKinds.Delete, VerbName = "v" });

        Assert.NotNull(store.Peek());
        Assert.NotNull(store.Peek());
    }
}
