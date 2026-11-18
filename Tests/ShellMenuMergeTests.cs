using System;
using System.Collections.Generic;
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// The list is built from the shell's own menu and enriched with registry verbs.
/// A registry-only list can never be complete: UltraISO-style handler entries and
/// packaged-app commands such as "Open project in ChatGPT" exist in no class key.
/// </summary>
public class ShellMenuMergeTests
{
    private static readonly MenuParserService Parser = new(new RegistryService());

    private static ContextMenuItem Verb(string verb, string display) => new()
    {
        VerbName = verb,
        DisplayName = display,
        Command = "cmd.exe",
        RegistryPath = @"HKEY_CURRENT_USER\Software\Classes\Directory\shell\" + verb,
    };

    private static ShellMenuNode Row(string text, params ShellMenuNode[] children) => new()
    {
        Text = text,
        Children = children,
    };

    [Fact]
    public void Shell_only_rows_are_kept_as_read_only_entries()
    {
        var verbs = new[] { Verb("open", "打开(&O)") };
        var native = new[] { Row("打开(&O)"), Row("UltraISO"), Row("Open project in ChatGPT") };

        var merged = Parser.MergeWithShellMenu(verbs, native, null);

        Assert.Equal(3, merged.Count);
        Assert.False(merged[0].IsShellProvided);
        Assert.True(merged[1].IsShellProvided);
        Assert.True(merged[2].IsShellProvided);
        Assert.Contains("UltraISO", merged[1].DisplayName);
    }

    [Fact]
    public void Matched_verbs_keep_their_registry_identity()
    {
        var verbs = new[] { Verb("git_shell", "Open Git Ba&sh here") };
        var native = new[] { Row("Open Git Bash here") };

        var merged = Parser.MergeWithShellMenu(verbs, native, null);

        var row = Assert.Single(merged);
        Assert.False(row.IsShellProvided);
        Assert.Equal("git_shell", row.VerbName);
        Assert.False(string.IsNullOrEmpty(row.RegistryPath));
    }

    [Fact]
    public void Shell_order_wins_over_registry_order()
    {
        var verbs = new[] { Verb("a", "AAA"), Verb("b", "BBB"), Verb("c", "CCC") };
        var native = new[] { Row("CCC"), Row("AAA") };

        var merged = Parser.MergeWithShellMenu(verbs, native, null);

        Assert.Equal(new[] { "CCC", "AAA", "BBB" }, merged.Select(m => m.DisplayName).ToArray());
        // BBB exists only in the registry, so it is flagged rather than silently shown
        // as if the menu contained it.
        Assert.True(merged[2].NotInShellMenu);
    }

    [Fact]
    public void Extended_menu_does_not_duplicate_normal_rows()
    {
        // The Shift menu is a superset; merging it as rows duplicated everything.
        var verbs = new[] { Verb("open", "打开(&O)"), Verb("ext", "仅 Shift 项") };
        var normal = new[] { Row("打开(&O)") };
        var extended = new[] { Row("打开(&O)"), Row("仅 Shift 项") };

        var merged = Parser.MergeWithShellMenu(verbs, normal, extended);

        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "打开(&O)", "仅 Shift 项" }, merged.Select(m => m.DisplayName).ToArray());
    }

    [Fact]
    public void Nested_shell_rows_bring_their_children()
    {
        var native = new[] { Row("UltraISO", Row("加载到虚拟光驱"), Row("刻录光盘映像")) };

        var merged = Parser.MergeWithShellMenu(Array.Empty<ContextMenuItem>(), native, null);

        var row = Assert.Single(merged);
        Assert.Equal(2, row.Children.Count);
        Assert.All(row.Children, c => Assert.True(c.IsShellProvided));
    }

    [Fact]
    public void Without_a_shell_menu_the_registry_list_is_returned_unchanged()
    {
        var verbs = new[] { Verb("a", "AAA") };

        var merged = Parser.MergeWithShellMenu(verbs, null, null);

        Assert.Same(verbs[0], Assert.Single(merged));
    }
}
