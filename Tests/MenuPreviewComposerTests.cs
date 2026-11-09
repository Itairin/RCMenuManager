using System;
using System.Collections.Generic;
using System.Linq;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Locks the preview down to Explorer's real menu shape: which built-in entries
/// appear, in what order, how they are grouped, and where the registry verbs are
/// spliced in. These are the assertions that keep the preview from silently
/// drifting back into a flat list of verbs.
/// </summary>
public class MenuPreviewComposerTests
{
    private static readonly IReadOnlyList<SystemMenuEntry> TestNewItems = new[]
    {
        new SystemMenuEntry { Header = "文件夹", IsDefault = true },
        SystemMenuEntry.Item("快捷方式"),
        SystemMenuEntry.Separator,
        SystemMenuEntry.Item("文本文档"),
    };

    private static readonly IReadOnlyList<SystemMenuEntry> TestSendTo = new[]
    {
        SystemMenuEntry.Item("桌面（创建快捷方式）"),
        SystemMenuEntry.Item("文档"),
    };

    private static SystemMenuLayout Layout(MenuScope scope, bool clipboard = true)
        => SystemMenuCatalog.Build(scope, clipboard, TestNewItems, TestSendTo);

    private static string[] Text(IEnumerable<SystemMenuEntry> entries)
        => entries.Select(e => e.Kind == SystemMenuEntryKind.Separator ? "---" : e.Header).ToArray();

    private static string[] Text(IEnumerable<PreviewNode> nodes)
        => nodes.Select(n => n.Kind == PreviewNodeKind.Separator ? "---" : n.Header).ToArray();

    private static MenuItemViewModel Item(string verb, string display, string? position = null, bool extended = false)
    {
        return new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = verb,
            DisplayName = display,
            Position = position,
            IsExtended = extended,
            RegistryPath = $@"HKEY_CLASSES_ROOT\Directory\shell\{verb}",
        }, new IconService());
    }

    // ------------------------------------------------------------ file scope

    [Fact]
    public void File_menu_starts_with_bold_Open_then_OpenWith()
    {
        var layout = Layout(MenuScope.AllFiles);
        var before = layout.Before;

        Assert.Equal("打开", before[0].Header);
        Assert.True(before[0].IsDefault);
        Assert.Equal("打开方式", before[1].Header);
        Assert.True(before[1].HasChildren);

        var after = Text(layout.After);
        Assert.Equal(new[] { "属性" }, after);
    }

    [Fact]
    public void File_menu_keeps_Explorer_grouping()
    {
        var expected = new[]
        {
            "打开", "打开方式", "---",
            "发送到", "---",
            "剪切", "复制", "创建快捷方式", "删除", "重命名",
        };
        Assert.Equal(expected, Text(Layout(MenuScope.AllFiles).Before));
    }

    [Fact]
    public void File_menu_send_to_uses_supplied_entries()
    {
        var sendTo = Layout(MenuScope.AllFiles).Before.Single(e => e.Header == "发送到");
        Assert.Equal(new[] { "桌面（创建快捷方式）", "文档" }, sendTo.Children.Select(c => c.Header).ToArray());
    }

    [Fact]
    public void File_menu_open_with_offers_picker_when_no_extra_handlers()
    {
        var openWith = Layout(MenuScope.AllFiles).Before.Single(e => e.Header == "打开方式");
        Assert.Contains("选择其他应用", openWith.Children.Select(c => c.Header));
    }

    // ---------------------------------------------------------- folder scope

    [Fact]
    public void Folder_menu_matches_Explorer_view_block()
    {
        var expected = new[]
        {
            "查看", "排序方式", "刷新", "---",
            "粘贴", "粘贴快捷方式", "---",
            "授予访问权限", "---",
            "新建",
        };
        Assert.Equal(expected, Text(Layout(MenuScope.Folder).Before));
    }

    [Fact]
    public void Folder_background_has_no_grant_access_block()
    {
        var headers = Text(Layout(MenuScope.FolderBackground).Before);
        Assert.DoesNotContain("授予访问权限", headers);
        Assert.Equal("新建", headers[^1]);
    }

    [Fact]
    public void View_submenu_lists_eight_modes_with_details_checked()
    {
        var view = Layout(MenuScope.Folder).Before[0];
        Assert.Equal("查看", view.Header);
        Assert.Equal(8, view.Children.Count);
        Assert.Equal(
            new[] { "超大图标", "大图标", "中等图标", "小图标", "列表", "详细信息", "平铺", "内容" },
            view.Children.Select(c => c.Header).ToArray());

        var checkedItems = view.Children.Where(c => c.IsChecked).Select(c => c.Header).ToArray();
        Assert.Equal(new[] { "详细信息" }, checkedItems);
        Assert.All(view.Children, c => Assert.True(c.IsCheckable));
    }

    [Fact]
    public void Sort_submenu_lists_name_date_type_size_then_direction()
    {
        var sort = Layout(MenuScope.Folder).Before[1];
        Assert.Equal("排序方式", sort.Header);
        Assert.Equal(
            new[] { "名称", "修改日期", "类型", "大小", "---", "递增", "递减" },
            Text(sort.Children));

        Assert.True(sort.Children[0].IsChecked);            // 名称
        Assert.True(sort.Children[5].IsChecked);            // 递增
        Assert.False(sort.Children[6].IsChecked);           // 递减
        Assert.All(sort.Children.Where(c => c.Kind == SystemMenuEntryKind.Item),
            c => Assert.True(c.IsCheckable));
    }

    [Fact]
    public void Paste_is_disabled_when_clipboard_has_no_files()
    {
        var enabled = Layout(MenuScope.Folder, clipboard: true).Before.Where(e => e.Header == "粘贴").ToArray();
        var disabled = Layout(MenuScope.Folder, clipboard: false).Before.Where(e => e.Header == "粘贴").ToArray();

        Assert.True(enabled[0].IsEnabled);
        Assert.False(disabled[0].IsEnabled);
    }

    [Fact]
    public void New_submenu_pins_folder_and_shortcut_on_top()
    {
        var newMenu = Layout(MenuScope.Folder).Before[^1];
        Assert.Equal("新建", newMenu.Header);
        Assert.Equal("文件夹", newMenu.Children[0].Header);
        Assert.True(newMenu.Children[0].IsDefault);
        Assert.Equal("快捷方式", newMenu.Children[1].Header);
        Assert.Equal(SystemMenuEntryKind.Separator, newMenu.Children[2].Kind);
    }

    // ------------------------------------------------------- desktop / drive

    [Fact]
    public void Desktop_menu_ends_with_display_settings_and_personalize()
    {
        var after = Layout(MenuScope.Desktop).After;
        Assert.Equal(new[] { "显示设置", "个性化" }, after.Select(e => e.Header).ToArray());
    }

    [Fact]
    public void Drive_menu_has_open_pin_and_format_blocks()
    {
        var before = Text(Layout(MenuScope.Drive).Before);
        Assert.Equal(new[] { "打开", "在终端中打开", "---", "固定到“快速访问”", "---", "格式化…" }, before);
        Assert.True(Layout(MenuScope.Drive).Before[0].IsDefault);
        Assert.Equal(new[] { "属性" }, Text(Layout(MenuScope.Drive).After));
    }

    // ------------------------------------------------------------- composing

    [Fact]
    public void Registry_verbs_are_spliced_between_new_and_properties()
    {
        var nodes = MenuPreviewComposer.Compose(
            MenuScope.Folder,
            new[] { Item("vscode", "Open with Code") },
            Array.Empty<ShellExtensionInfo>(),
            clipboardHasFiles: true);

        var headers = Text(nodes);
        var newIndex = Array.IndexOf(headers, "新建");
        var verbIndex = Array.IndexOf(headers, "Open with Code");
        var propIndex = Array.IndexOf(headers, "属性");

        Assert.True(newIndex < verbIndex, "自定义项应排在“新建”之后");
        Assert.True(verbIndex < propIndex, "自定义项应排在“属性”之前");
        Assert.Equal("---", headers[verbIndex - 1]);
        Assert.Equal("---", headers[verbIndex + 1]);
    }

    [Fact]
    public void Top_position_verbs_are_painted_above_the_shell_block()
    {
        var nodes = MenuPreviewComposer.Compose(
            MenuScope.Folder,
            new[] { Item("pinned", "固定项", "Top") },
            Array.Empty<ShellExtensionInfo>());

        var headers = Text(nodes);
        Assert.Equal("固定项", headers[0]);
        Assert.Equal("---", headers[1]);
        Assert.Equal("查看", headers[2]);
    }

    [Fact]
    public void Bottom_position_verbs_land_right_before_properties()
    {
        var nodes = MenuPreviewComposer.Compose(
            MenuScope.Folder,
            new[] { Item("tail", "尾项", "Bottom") },
            Array.Empty<ShellExtensionInfo>());

        var headers = Text(nodes);
        var verbIndex = Array.IndexOf(headers, "尾项");
        var propIndex = Array.IndexOf(headers, "属性");
        Assert.True(verbIndex > 0);
        Assert.True(verbIndex < propIndex, "Bottom 项应排在“属性”之前");
        Assert.Equal("---", headers[propIndex - 1]);
    }

    [Fact]
    public void Extended_verbs_are_hidden_until_enabled()
    {
        var items = new[] { Item("shiftonly", "仅 Shift", extended: true) };

        var hidden = Text(MenuPreviewComposer.Compose(MenuScope.Folder, items, Array.Empty<ShellExtensionInfo>()));
        var shown = Text(MenuPreviewComposer.Compose(MenuScope.Folder, items, Array.Empty<ShellExtensionInfo>(), showExtended: true));

        Assert.DoesNotContain("仅 Shift", hidden);
        Assert.Contains("仅 Shift", shown);
    }

    [Fact]
    public void Extended_verbs_carry_a_shift_badge()
    {
        var nodes = MenuPreviewComposer.Compose(
            MenuScope.Folder,
            new[] { Item("shiftonly", "仅 Shift", extended: true) },
            Array.Empty<ShellExtensionInfo>(),
            showExtended: true);

        Assert.Equal("Shift", nodes.Single(n => n.Header == "仅 Shift").Badge);
        Assert.Null(nodes.Single(n => n.Header == "查看").Badge);
    }

    [Fact]
    public void Shell_extensions_are_listed_before_properties()
    {
        var extensions = new[]
        {
            new ShellExtensionInfo { DisplayName = "7-Zip", SourceKey = "{23170F69}", Clsid = "..." },
        };
        var nodes = MenuPreviewComposer.Compose(MenuScope.Folder, Array.Empty<MenuItemViewModel>(), extensions);

        var headers = Text(nodes);
        var extIndex = Array.IndexOf(headers, "7-Zip");
        var propIndex = Array.IndexOf(headers, "属性");
        Assert.True(extIndex > 0);
        Assert.True(extIndex < propIndex, "Shell 扩展应排在“属性”之前");
        Assert.True(nodes.Single(n => n.Header == "7-Zip").IsBuiltIn);
    }

    [Fact]
    public void Menu_never_starts_ends_or_repeats_separators()
    {
        var nodes = MenuPreviewComposer.Compose(
            MenuScope.Desktop,
            new[] { Item("a", "A", "Top"), Item("b", "B", "Top"), Item("c", "C"), Item("d", "D", "Bottom") },
            new[] { new ShellExtensionInfo { DisplayName = "Ext", SourceKey = "{X}" } });

        Assert.NotEqual(PreviewNodeKind.Separator, nodes[0].Kind);
        Assert.NotEqual(PreviewNodeKind.Separator, nodes[^1].Kind);
        for (var i = 1; i < nodes.Count; i++)
            Assert.False(nodes[i].Kind == PreviewNodeKind.Separator && nodes[i - 1].Kind == PreviewNodeKind.Separator,
                $"第 {i} 项出现连续分隔符");
    }

    [Fact]
    public void Registry_nodes_expose_the_view_model_for_selection()
    {
        var vmItem = Item("vscode", "Open with Code");
        var nodes = MenuPreviewComposer.Compose(MenuScope.Folder, new[] { vmItem }, Array.Empty<ShellExtensionInfo>());
        var node = nodes.Single(n => n.Header == "Open with Code");

        Assert.Same(vmItem, node.Item);
        Assert.False(node.IsBuiltIn);
    }

    [Fact]
    public void Cascading_verbs_keep_their_children()
    {
        var parent = new MenuItemViewModel(new ContextMenuItem
        {
            VerbName = "cascade",
            DisplayName = "级联",
            RegistryPath = @"HKEY_CLASSES_ROOT\Directory\shell\cascade",
            Children = new[]
            {
                new ContextMenuItem { VerbName = "c1", DisplayName = "子项 1" },
                new ContextMenuItem { VerbName = "c2", DisplayName = "子项 2" },
            },
        }, new IconService());

        var node = MenuPreviewComposer.Compose(MenuScope.Folder, new[] { parent }, Array.Empty<ShellExtensionInfo>())
            .Single(n => n.Header == "级联");

        Assert.Equal(new[] { "子项 1", "子项 2" }, node.Children.Select(c => c.Header).ToArray());
    }

    // --------------------------------------------- extension / mixed scopes

    [Fact]
    public void Extension_scope_reuses_the_file_skeleton()
    {
        var scope = MenuScope.ForExtension(".txt", "txtfile");
        var nodes = MenuPreviewComposer.Compose(
            scope, Array.Empty<MenuItemViewModel>(), Array.Empty<ShellExtensionInfo>());

        Assert.Equal("打开", nodes[0].Header);
        Assert.True(nodes[0].IsDefault);
        Assert.Contains(nodes, n => n.Header == "发送到");
        Assert.Equal("属性", nodes[^1].Header);
    }

    [Fact]
    public void All_filesystem_objects_scope_uses_the_file_skeleton()
    {
        var layout = Layout(MenuScope.AllFilesystemObjects);

        Assert.Equal("打开", layout.Before[0].Header);
        Assert.DoesNotContain("新建", layout.Before.Select(e => e.Header));
        Assert.Equal("属性", layout.After[0].Header);
    }

    [Fact]
    public void Prewarm_keeps_repeated_composition_cheap()
    {
        SystemMenuCatalog.Prewarm();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            SystemMenuCatalog.Build(MenuScope.Folder, clipboardHasFiles: true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"20 次构建耗时 {sw.ElapsedMilliseconds}ms，说明 ShellNew/SendTo 缓存没有生效");
    }
}
