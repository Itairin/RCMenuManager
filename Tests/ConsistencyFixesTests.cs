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
/// The four behaviours this round had to make consistent: nested verb paths, the
/// scope-wide "already exists" answer, MUIVerb renames, and which rows the preview
/// lets the user act on.
/// </summary>
public class ConsistencyFixesTests
{
    // ---- nested verb paths -------------------------------------------------

    [Fact]
    public void Nested_shell_child_keeps_its_full_registry_path()
    {
        // A child verb lives at ...\<parent>\shell\<child>. Rebuilding the path from
        // the scope root dropped the middle segment, so saving wrote a junk key and
        // deleting silently missed.
        var nested = new ContextMenuItem
        {
            VerbName = "child",
            DisplayName = "child",
            RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\Directory\shell\parent\shell\child",
        };
        var parentContext = new ContextMenuItem
        {
            VerbName = "parent",
            DisplayName = "parent",
            RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\Directory\shell\parent",
            Children = new[] { nested },
        };

        var vm = new MenuItemViewModel(parentContext, new IconService());

        Assert.Equal(@"HKEY_CURRENT_USER\Software\Classes\Directory\shell\parent\shell\child",
            vm.Children[0].Model.RegistryPath);
        // The write coordinate is derived from that path, so it must not lose a level.
        Assert.Equal(@"Software\Classes\Directory\shell\parent\shell\child", vm.Children[0].SubKey);
    }

    // ---- rename semantics --------------------------------------------------

    [Fact]
    public void Renaming_a_verb_with_MUIVerb_updates_MUIVerb_too()
    {
        var writer = new InMemoryRegistryWriter();
        writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v");
        writer.SetStringValue(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v",
            "MUIVerb", "@shell32.dll,-8506");

        var log = new CollectingLog();
        var svc = new RegistryWriteService(writer, new NullBackup(), log, () => true, () => { });
        svc.SaveEdits(RegistryHive.CurrentUser, @"Software\Classes\Directory\shell\v", "Folder", "v",
            new EditableVerbDraft
            {
                VerbName = "v",
                DisplayName = "我的名字",
                HasMuiVerb = true,
                OriginalDisplayName = "旧名字",
            }, includeCommand: false);

        Assert.Equal("我的名字", writer.GetValue(RegistryHive.CurrentUser,
            @"Software\Classes\Directory\shell\v", "MUIVerb"));
    }

    [Fact]
    public void An_untouched_inferred_name_is_not_written_to_the_registry()
    {
        // Editing only the icon must not turn a guessed label ("编辑贴纸") into stored
        // data, because the key deliberately has no label of its own.
        var writer = new InMemoryRegistryWriter();
        var path = @"Software\Classes\*\shell\handler";
        writer.CreateSubKey(RegistryHive.CurrentUser, path);

        var svc = new RegistryWriteService(writer, new NullBackup(), new CollectingLog(), () => true, () => { });
        svc.SaveEdits(RegistryHive.CurrentUser, path, "AllFiles", "handler",
            new EditableVerbDraft
            {
                VerbName = "handler",
                DisplayName = "编辑贴纸",
                HasInferredName = true,
                OriginalDisplayName = "编辑贴纸",
                IconExpression = "imageres.dll,-1",
            }, includeCommand: false);

        Assert.False(writer.ValueExists(RegistryHive.CurrentUser, path, string.Empty));
        Assert.True(writer.ValueExists(RegistryHive.CurrentUser, path, "Icon"));
    }

    // ---- preview rows ------------------------------------------------------

    [Fact]
    public void Simplified_preview_does_not_offer_hidden_verbs_as_normal_rows()
    {
        var hidden = new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = "pintohome",
            DisplayName = "固定到快速访问",
            IsProgrammaticOnly = true,
            RegistryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\AllFilesystemObjects\shell\pintohome",
        }, new IconService());

        var node = MenuPreviewComposer.FromItem(hidden);

        Assert.False(node.IsEnabled);
        Assert.Equal("隐藏", node.Badge);
    }

    [Fact]
    public void Visible_verb_stays_enabled_in_the_simplified_preview()
    {
        var item = new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = "vscode",
            DisplayName = "通过 Code 打开",
            Command = "code \"%1\"",
            RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\*\shell\vscode",
        }, new IconService());

        Assert.True(MenuPreviewComposer.FromItem(item).IsEnabled);
    }

    // ---- helpers -----------------------------------------------------------

    private sealed class CollectingLog : IOperationLog
    {
        public List<OperationLogEntry> Entries { get; } = new();
        public void Append(OperationLogEntry entry) => Entries.Add(entry);
        public IReadOnlyList<OperationLogEntry> ReadAll() => Entries;
    }

    private sealed class NullBackup : IBackupService
    {
        public string? Export(RegistryHive hive, string subKey, string scopeId, string verbName) => null;
        public IReadOnlyList<BackupRecord> List() => Array.Empty<BackupRecord>();
        public void Import(string filePath) { }
        public void Delete(string filePath) { }
    }
}
