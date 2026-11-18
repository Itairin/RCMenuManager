using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Source analysis must never guess. Each test pins one rule that decides what the
/// user is told about an item, because a wrong label changes what they dare delete.
/// </summary>
public class MenuItemOriginServiceTests
{
    private static MenuItemOriginService Service(IOperationLog? log = null, IPresetService? presets = null)
        => new(log, presets);

    private static MenuItemViewModel Item(
        string verb, string command = "", string path = @"HKEY_CURRENT_USER\Software\Classes\*\shell\x",
        bool shared = false, bool delegateExecute = false, string? handler = null)
        => new(new ContextMenuItem
        {
            VerbName = verb,
            DisplayName = verb,
            Command = command,
            ExpandedCommand = command,
            RegistryPath = path,
            IsSharedDefinition = shared,
            DelegateExecute = delegateExecute ? "{11111111-1111-1111-1111-111111111111}" : null,
            ExplorerCommandHandler = handler,
        }, new IconService());

    [Fact]
    public void CommandStore_children_are_reported_as_command_store()
    {
        var origin = Service().Analyse(Item("copyaspath", shared: true).Model, "AllFiles");

        Assert.Equal(MenuItemSource.CommandStore, origin.Source);
    }

    [Fact]
    public void DelegateExecute_verb_is_reported_as_com_handler()
    {
        var origin = Service().Analyse(Item("SomeVerb", delegateExecute: true).Model, "AllFiles");

        Assert.Equal(MenuItemSource.ShellExtension, origin.Source);
        Assert.Contains("COM", origin.Detail);
    }

    [Fact]
    public void ExplorerCommandHandler_is_reported_as_handler_not_as_raw_key()
    {
        var origin = Service().Analyse(
            Item("EditStickers", handler: "{3C0E1E28-51F1-4130-98AC-0FBFB5FE4638}").Model, "Desktop");

        Assert.Equal(MenuItemSource.ExplorerCommand, origin.Source);
    }

    [Fact]
    public void Verb_with_a_command_is_attributed_to_an_application()
    {
        var origin = Service().Analyse(
            Item("git_shell", "\"E:\\Git\\git-bash.exe\" --cd=%1").Model, "Folder");

        Assert.Equal(MenuItemSource.Application, origin.Source);
        Assert.Contains("git-bash.exe", origin.Detail);
    }

    [Fact]
    public void System_owned_verb_without_command_is_built_in()
        => Assert.Equal(MenuItemSource.SystemBuiltIn,
            Service().Analyse(Item("pintohome").Model, "Folder").Source);

    [Fact]
    public void Known_system_verb_is_built_in_even_with_a_command()
        => Assert.Equal(MenuItemSource.SystemBuiltIn,
            Service().Analyse(Item("open", @"C:\Windows\Explorer.exe").Model, "Folder").Source);

    [Fact]
    public void Creation_in_the_operation_log_is_credited_to_this_tool()
    {
        var log = new FakeLog(new OperationLogEntry(
            DateTime.UtcNow, "Folder", "myverb", "CreateRoot",
            RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\myverb",
            null, success: true, error: null));

        // The evidence is keyed by hive + sub-key, so the item has to point at the
        // same key the log recorded — otherwise nothing is proven.
        var item = Item("myverb", "cmd.exe",
            @"HKEY_CURRENT_USER\Software\Classes\Directory\shell\myverb").Model;

        var origin = Service(log).Analyse(item, "Folder");

        Assert.Equal(MenuItemSource.ThisTool, origin.Source);
    }

    [Fact]
    public void An_edit_snapshot_is_not_evidence_of_creation()
    {
        // Backing a verb up before editing it (op "Save") says nothing about who
        // created it; treating it as ownership mislabelled VS Code's entry.
        var log = new FakeLog(new OperationLogEntry(
            DateTime.UtcNow, "Folder", "AnyCode", "Save",
            RegistryHive.LocalMachine, @"SOFTWARE\Classes\Directory\shell\AnyCode",
            @"C:\b\x.reg", success: true, error: null));

        var origin = Service(log).Analyse(
            Item("AnyCode", "\"C:\\VS\\VSLauncher.exe\" \"%1\"", @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\shell\AnyCode").Model,
            "Folder");

        Assert.NotEqual(MenuItemSource.ThisTool, origin.Source);
        Assert.Equal(MenuItemSource.Application, origin.Source);
    }

    [Fact]
    public void A_failed_creation_is_not_credited()
    {
        var log = new FakeLog(new OperationLogEntry(
            DateTime.UtcNow, "Folder", "broken", "CreateRoot",
            RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\broken",
            null, success: false, error: "denied"));

        Assert.NotEqual(MenuItemSource.ThisTool,
            Service(log).Analyse(Item("broken", "x.exe").Model, "Folder").Source);
    }

    [Fact]
    public void A_preset_in_another_scope_does_not_claim_the_key()
    {
        // "open" is a preset for Drive only. Windows' own Folder\shell\open must not
        // be attributed to this tool just because the names collide.
        var presets = new FakePresets(
            new PresetItem { Scope = "Drive", VerbName = "open" },
            new PresetItem { Scope = "Drive", VerbName = "properties" });

        var origin = Service(null, presets).Analyse(
            Item("open", @"C:\Windows\Explorer.exe",
                 @"HKEY_CURRENT_USER\Software\Classes\Folder\shell\open").Model,
            "Folder");

        Assert.NotEqual(MenuItemSource.ThisTool, origin.Source);
    }

    [Fact]
    public void A_preset_in_the_matching_scope_in_hkcu_is_credited()
    {
        var presets = new FakePresets(new PresetItem { Scope = "Folder", VerbName = "myfolder" });

        var origin = Service(null, presets).Analyse(
            Item("myfolder", "cmd.exe",
                 @"HKEY_CURRENT_USER\Software\Classes\Directory\shell\myfolder").Model,
            "Folder");

        Assert.Equal(MenuItemSource.ThisTool, origin.Source);
    }

    [Fact]
    public void A_preset_key_in_hklm_is_not_credited_to_this_tool()
    {
        // Presets are only ever written per-user; a machine-wide key of the same
        // name came from something else.
        var presets = new FakePresets(new PresetItem { Scope = "Folder", VerbName = "myfolder" });

        var origin = Service(null, presets).Analyse(
            Item("myfolder", "cmd.exe",
                 @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\shell\myfolder").Model,
            "Folder");

        Assert.NotEqual(MenuItemSource.ThisTool, origin.Source);
    }

    private sealed class FakeLog : IOperationLog
    {
        private readonly List<OperationLogEntry> _entries;
        public FakeLog(params OperationLogEntry[] entries) => _entries = entries.ToList();
        public void Append(OperationLogEntry entry) => _entries.Add(entry);
        public IReadOnlyList<OperationLogEntry> ReadAll() => _entries;
    }

    private sealed class FakePresets : IPresetService
    {
        private readonly List<PresetItem> _items;
        public FakePresets(params PresetItem[] items) => _items = items.ToList();
        public PresetCatalog Load() => new() { Version = "1.0", Presets = _items };
        public bool IsApplied(PresetItem item) => false;
        public bool IsVerbPresent(string scopeId, string verbName) => false;
        public void Apply(PresetItem item, bool overwrite) { }
        public void SaveUserPreset(PresetItem item) { }
        public void Import(string filePath) { }
        public void Export(string filePath) { }
        public string UserPresetsPath => string.Empty;
    }
}
