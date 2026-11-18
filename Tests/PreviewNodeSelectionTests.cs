using System;
using System.Collections.Generic;
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// The preview must never contain a row that looks actionable but ignores clicks.
/// These tests cover the two ways a row gets an owner: an editable verb, or a
/// registered shell-extension handler ("共享" / "在记事本中打开").
/// </summary>
public class PreviewNodeSelectionTests
{
    private static ShellMenuNode Row(string text) => new() { Text = text };

    private static MenuItemViewModel Verb(string verb, string display)
        => new(new ContextMenuItem
        {
            VerbName = verb,
            DisplayName = display,
            RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\*\shell\" + verb,
        }, new IconService());

    [Fact]
    public void A_native_row_matching_a_verb_is_selectable()
    {
        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { Row("通过 Code 打开") },
            new[] { Verb("vscode", "通过 Code 打开") });

        var node = Assert.Single(nodes);
        Assert.True(node.CanSelect);
        Assert.NotNull(node.Item);
        Assert.False(node.IsBuiltIn);
    }

    [Fact]
    public void A_share_handler_row_is_selectable_even_though_no_verb_matches()
    {
        // "共享" is contributed by a shellex DLL; there is no registry verb, so the
        // row used to render as a dead reference entry.
        var handlers = new[]
        {
            new ShellExtensionInfo
            {
                DisplayName = "Sharing",
                FriendlyName = "Shell extensions for sharing",
                Clsid = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}",
                SourceKey = "Sharing",
            },
        };

        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { Row("共享") },
            Array.Empty<MenuItemViewModel>(),
            handlers);

        var node = Assert.Single(nodes);
        Assert.True(node.CanSelect);
        Assert.NotNull(node.Extension);
        Assert.Null(node.Item);
    }

    [Fact]
    public void An_open_with_handler_matches_the_localized_notepad_row()
    {
        var handlers = new[]
        {
            new ShellExtensionInfo
            {
                DisplayName = "Open With",
                Clsid = "{09799AFB-AD67-11d1-ABCD-00C04FC30936}",
                SourceKey = "Open With",
            },
        };

        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { Row("在记事本中打开") },
            Array.Empty<MenuItemViewModel>(),
            handlers);

        Assert.NotNull(Assert.Single(nodes).Extension);
    }

    [Fact]
    public void An_unknown_explorer_row_stays_reference_only()
    {
        // 刷新 belongs to Explorer itself: nothing is registered, nothing can be
        // selected, and the UI must not pretend otherwise.
        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { Row("刷新") },
            Array.Empty<MenuItemViewModel>());

        var node = Assert.Single(nodes);
        Assert.False(node.CanSelect);
        Assert.True(node.IsBuiltIn);
    }

    [Fact]
    public void Rows_keep_shell_state_and_shortcut_split()
    {
        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { new ShellMenuNode { Text = "撤消\tCtrl+Z", IsEnabled = false } },
            Array.Empty<MenuItemViewModel>());

        var node = Assert.Single(nodes);
        Assert.Equal("撤消", node.Header);
        Assert.Equal("Ctrl+Z", node.Shortcut);
        Assert.False(node.IsEnabled);
    }

    [Fact]
    public void Duplicate_labels_resolve_to_the_per_user_verb_and_are_flagged()
    {
        // encrypt-bde / encrypt-bde-elev share a label on this machine. Picking the
        // first match silently bound both rows to one key, so an edit could land on
        // the wrong one; the effective (HKCU) copy now wins and the row says 重名.
        var machine = new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = "encrypt-bde",
            DisplayName = "启用 BitLocker(&B)",
            Origin = RegistryHiveOrigin.LocalMachine,
            RegistryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Drive\shell\encrypt-bde",
        }, new IconService());
        var user = new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = "encrypt-bde-elev",
            DisplayName = "启用 BitLocker(&B)",
            Origin = RegistryHiveOrigin.CurrentUser,
            RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\Drive\shell\encrypt-bde-elev",
        }, new IconService());

        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { new ShellMenuNode { Text = "启用 BitLocker(&B)" } },
            new[] { machine, user });

        var node = Assert.Single(nodes);
        Assert.True(node.IsAmbiguousMatch);
        Assert.Equal("重名", node.Badge);
        Assert.Equal("encrypt-bde-elev", node.Item!.VerbName);
    }

    [Fact]
    public void A_unique_label_is_not_flagged_as_ambiguous()
    {
        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { new ShellMenuNode { Text = "通过 Code 打开" } },
            new[] { Verb("vscode", "通过 Code 打开") });

        Assert.False(Assert.Single(nodes).IsAmbiguousMatch);
    }

    [Fact]
    public void Compose_without_a_handler_list_still_works()
    {
        // The 3-arg overload is what the existing call sites use; passing null must
        // not turn previously selectable rows into dead ones.
        var nodes = MenuPreviewComposer.ComposeNative(
            new[] { Row("通过 Code 打开") },
            new[] { Verb("vscode", "通过 Code 打开") },
            null);

        Assert.True(Assert.Single(nodes).CanSelect);
    }
}
