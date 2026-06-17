using System;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class RegistryWriteServiceTests
{
    private const string Verb = @"Software\Classes\Directory\shell\open";

    private static (RegistryWriteService svc, InMemoryRegistryWriter writer, RecordingBackup backup, RecordingLog log) Make(bool admin = false)
    {
        var writer = new InMemoryRegistryWriter();
        var backup = new RecordingBackup();
        var log = new RecordingLog();
        var svc = new RegistryWriteService(writer, backup, log, () => admin, notifyShell: () => { });
        return (svc, writer, backup, log);
    }

    private static void Seed(InMemoryRegistryWriter writer)
    {
        writer.CreateSubKey(RegistryHive.CurrentUser, Verb);
        writer.SetStringValue(RegistryHive.CurrentUser, Verb, string.Empty, "Open");
        writer.CreateSubKey(RegistryHive.CurrentUser, Verb + @"\command");
        writer.SetStringValue(RegistryHive.CurrentUser, Verb + @"\command", string.Empty, "explorer.exe %1");
    }

    [Fact]
    public void Disable_sets_ProgrammaticAccessOnly()
    {
        var (svc, writer, backup, log) = Make();
        Seed(writer);
        svc.Disable(RegistryHive.CurrentUser, Verb, "Folder", "open");
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, Verb, "ProgrammaticAccessOnly"));
        Assert.Equal(1, backup.CallCount);
        Assert.True(log.LastSuccess);
    }

    [Fact]
    public void Enable_removes_ProgrammaticAccessOnly()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        writer.SetStringValue(RegistryHive.CurrentUser, Verb, "ProgrammaticAccessOnly", string.Empty);
        svc.Enable(RegistryHive.CurrentUser, Verb, "Folder", "open");
        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, Verb, "ProgrammaticAccessOnly"));
    }

    [Fact]
    public void Delete_removes_subtree()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        svc.Delete(RegistryHive.CurrentUser, Verb, "Folder", "open");
        Assert.False(writer.KeyExists(RegistryHive.CurrentUser, Verb));
    }

    [Fact]
    public void UpdateDisplayName_sets_default_value()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        svc.UpdateDisplayName(RegistryHive.CurrentUser, Verb, "Folder", "open", "新名");
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, Verb, string.Empty));
    }

    [Fact]
    public void UpdateCommand_writes_to_command_subkey()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        svc.UpdateCommand(RegistryHive.CurrentUser, Verb, "Folder", "open", "notepad.exe %1");
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, Verb + @"\command", string.Empty));
    }

    [Fact]
    public void UpdateIcon_with_empty_string_deletes_icon_value()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        writer.SetStringValue(RegistryHive.CurrentUser, Verb, "Icon", "shell32.dll,0");
        svc.UpdateIcon(RegistryHive.CurrentUser, Verb, "Folder", "open", string.Empty);
        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, Verb, "Icon"));
    }

    [Fact]
    public void SetExtended_toggles_value()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        svc.SetExtended(RegistryHive.CurrentUser, Verb, "Folder", "open", true);
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, Verb, "Extended"));
        svc.SetExtended(RegistryHive.CurrentUser, Verb, "Folder", "open", false);
        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, Verb, "Extended"));
    }

    [Fact]
    public void SetPosition_writes_when_top_or_bottom_and_deletes_when_default()
    {
        var (svc, writer, _, _) = Make();
        Seed(writer);
        svc.SetPosition(RegistryHive.CurrentUser, Verb, "Folder", "open", "Top");
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, Verb, "Position"));
        svc.SetPosition(RegistryHive.CurrentUser, Verb, "Folder", "open", "Default");
        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, Verb, "Position"));
    }

    [Fact]
    public void Write_to_HKLM_when_not_admin_throws_ElevationRequired()
    {
        var (svc, writer, _, _) = Make(admin: false);
        writer.CreateSubKey(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\open");
        Assert.Throws<ElevationRequiredException>(() =>
            svc.Disable(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\open", "Folder", "open"));
    }

    [Fact]
    public void Write_to_HKLM_when_admin_succeeds()
    {
        var (svc, writer, _, _) = Make(admin: true);
        writer.CreateSubKey(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\open");
        svc.Disable(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\open", "Folder", "open");
        Assert.True(writer.ValueExists(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\open", "ProgrammaticAccessOnly"));
    }

    [Fact]
    public void Backup_called_before_write()
    {
        var (svc, writer, backup, _) = Make();
        Seed(writer);
        backup.OnExport = () =>
        {
            Assert.False(writer.ValueExists(RegistryHive.CurrentUser, Verb, "ProgrammaticAccessOnly"));
        };
        svc.Disable(RegistryHive.CurrentUser, Verb, "Folder", "open");
        Assert.Equal(1, backup.CallCount);
    }

    [Fact]
    public void CreateRoot_creates_verb_and_command_subkeys()
    {
        var (svc, writer, _, _) = Make();
        var draft = new EditableVerbDraft
        {
            VerbName = "MyVerb",
            DisplayName = "我的菜单",
            Command = "notepad.exe",
            IsExtended = false,
            Position = "Top",
        };
        svc.CreateRootVerb(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", "Folder", draft);
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb"));
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb\command"));
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb", string.Empty));
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb", "Position"));
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb\command", string.Empty));
    }

    [Fact]
    public void CreateRoot_throws_RegistryConflict_when_verb_already_exists()
    {
        var (svc, writer, _, _) = Make();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\MyVerb");
        var draft = new EditableVerbDraft { VerbName = "MyVerb", DisplayName = "X", Command = "x.exe" };
        Assert.Throws<RegistryConflictException>(() =>
            svc.CreateRootVerb(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", "Folder", draft));
    }

    [Fact]
    public void CreateRoot_parent_only_creates_shell_subkey_without_command()
    {
        var (svc, writer, _, _) = Make();
        var draft = new EditableVerbDraft
        {
            VerbName = "Cascade",
            DisplayName = "级联",
            IsParentOnly = true,
        };
        svc.CreateRootVerb(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell", "Folder", draft);
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade"));
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell"));
        Assert.False(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\command"));
    }

    [Fact]
    public void CreateChild_writes_under_parent_shell_subkey()
    {
        var (svc, writer, _, _) = Make();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell");
        var draft = new EditableVerbDraft
        {
            VerbName = "Child1",
            DisplayName = "子项 1",
            Command = "child.exe",
        };
        svc.CreateChildVerb(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade", "Folder", draft);
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell\Child1"));
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell\Child1\command"));
    }

    [Fact]
    public void CreateChild_creates_parent_shell_subkey_when_missing()
    {
        var (svc, writer, _, _) = Make();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade");
        var draft = new EditableVerbDraft
        {
            VerbName = "Child1",
            DisplayName = "子项 1",
            Command = "child.exe",
        };
        svc.CreateChildVerb(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade", "Folder", draft);
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell"));
        Assert.True(writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\Cascade\shell\Child1"));
    }
}

internal sealed class RecordingBackup : IBackupService
{
    public int CallCount;
    public Action? OnExport;
    public string Export(RegistryHive hive, string subKey, string scopeId, string verbName)
    {
        CallCount++;
        OnExport?.Invoke();
        return $"recorded:{hive}:{subKey}";
    }
    public IReadOnlyList<RCMenuManager.Models.BackupRecord> List() => Array.Empty<RCMenuManager.Models.BackupRecord>();
    public void Import(string filePath) { }
    public void Delete(string filePath) { }
}

internal sealed class RecordingLog : IOperationLog
{
    public bool LastSuccess;
    public string? LastError;
    public void Append(OperationLogEntry entry)
    {
        LastSuccess = entry.success;
        LastError = entry.error;
    }
    public IReadOnlyList<OperationLogEntry> ReadAll() => Array.Empty<OperationLogEntry>();
}
