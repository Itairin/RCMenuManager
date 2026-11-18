using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

public class MenuSearchTests
{
    private static MenuItemViewModel Item(
        string verb, string display, string command = "", string path = "", params ContextMenuItem[] children)
        => new(new ContextMenuItem
        {
            VerbName = verb,
            DisplayName = display,
            Command = command,
            RegistryPath = path,
            Children = children,
        }, new IconService());

    [Fact]
    public void Blank_query_matches_everything()
        => Assert.True(MainViewModel.Matches(Item("open", "打开"), "   "));

    [Fact]
    public void Matches_display_name_case_insensitively()
        => Assert.True(MainViewModel.Matches(Item("vscode", "通过 Code 打开"), "code"));

    [Fact]
    public void Matches_verb_name()
        => Assert.True(MainViewModel.Matches(Item("removempproperties", "删除媒体属性"), "RemoveMp"));

    [Fact]
    public void Matches_command_line()
        => Assert.True(MainViewModel.Matches(Item("x", "某菜单", "\"C:\\Tools\\rclone.exe\" copy"), "rclone.exe"));

    [Fact]
    public void Matches_registry_path()
        => Assert.True(MainViewModel.Matches(Item("x", "某菜单", path: @"HKEY_CLASSES_ROOT\Directory\shell\x"), "Directory"));

    [Fact]
    public void Parent_stays_visible_when_a_child_matches()
    {
        var child = new ContextMenuItem { VerbName = "child-verb", DisplayName = "子项", RegistryPath = @"HKCR\a\child-verb" };
        var parent = Item("parent", "父菜单", path: @"HKCR\a\parent", children: child);

        Assert.True(MainViewModel.Matches(parent, "child-verb"));
    }

    [Fact]
    public void Unrelated_query_does_not_match()
        => Assert.False(MainViewModel.Matches(Item("open", "打开", "notepad.exe"), "photoshop"));
}
