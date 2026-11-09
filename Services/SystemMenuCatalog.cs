using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// The system menu laid out in two halves: <see cref="Before"/> is everything
/// Explorer draws above the verbs contributed by this app, <see cref="After"/>
/// is the fixed tail (Properties / Display settings ...). The composer splices
/// registry verbs between the two so the preview keeps Explorer's grouping.
/// </summary>
public sealed class SystemMenuLayout
{
    public SystemMenuLayout(IReadOnlyList<SystemMenuEntry> before, IReadOnlyList<SystemMenuEntry> after)
    {
        Before = before;
        After = after;
    }

    public IReadOnlyList<SystemMenuEntry> Before { get; }
    public IReadOnlyList<SystemMenuEntry> After { get; }
}

/// <summary>
/// Rebuilds the parts of the shell context menu that do not live in the
/// registry keys this app edits. Windows composes them from the shell's own
/// resources (view modes, sort orders) plus a few registry-driven templates
/// (<c>ShellNew</c>) and the SendTo folder, so the text and grouping here
/// mirror what Explorer shows on a zh-CN system.
/// </summary>
public static class SystemMenuCatalog
{
    /// <summary>View modes offered by the "View" sub-menu, in Explorer's order.</summary>
    private static readonly string[] ViewModes =
    {
        "超大图标", "大图标", "中等图标", "小图标", "列表", "详细信息", "平铺", "内容",
    };

    private static readonly Lazy<IReadOnlyList<SystemMenuEntry>> LazyNewItems =
        new(BuildNewSubmenu, LazyThreadSafetyMode.PublicationOnly);

    private static readonly Lazy<IReadOnlyList<SystemMenuEntry>> LazySendToItems =
        new(BuildSendToSubmenu, LazyThreadSafetyMode.PublicationOnly);

    public static SystemMenuLayout Build(MenuScope scope, bool clipboardHasFiles = true)
        => Build(scope, clipboardHasFiles, newItems: null, sendToItems: null);

    /// <summary>
    /// Warms the machine-dependent sub-menus (ShellNew enumeration + SendTo
    /// folder). Enumerating HKCR takes a few hundred milliseconds on a loaded
    /// machine; doing it on a background thread at startup keeps the first
    /// right-click in the Preview tab instant.
    /// </summary>
    public static void Prewarm()
    {
        _ = LazyNewItems.Value;
        _ = LazySendToItems.Value;
    }

    /// <summary>
    /// <paramref name="newItems"/> / <paramref name="sendToItems"/> exist so tests
    /// can pin the machine-dependent sub-menus and assert the layout without
    /// depending on what happens to be installed.
    /// </summary>
    public static SystemMenuLayout Build(
        MenuScope scope,
        bool clipboardHasFiles,
        IReadOnlyList<SystemMenuEntry>? newItems,
        IReadOnlyList<SystemMenuEntry>? sendToItems)
    {
        return scope.Type switch
        {
            ScopeType.Folder => FolderLayout(scope, clipboardHasFiles, newItems, includeGrantAccess: true),
            ScopeType.FolderBackground => FolderLayout(scope, clipboardHasFiles, newItems, includeGrantAccess: false),
            ScopeType.Desktop => DesktopLayout(clipboardHasFiles, newItems),
            ScopeType.Drive => DriveLayout(),
            _ => FileLayout(scope, sendToItems),
        };
    }

    // ---------------------------------------------------------------- layouts

    private static SystemMenuLayout FileLayout(MenuScope scope, IReadOnlyList<SystemMenuEntry>? sendTo)
    {
        var openWith = BuildOpenWithSubmenu(scope);
        var before = new List<SystemMenuEntry>
        {
            SystemMenuEntry.Default("打开"),
            openWith,
            SystemMenuEntry.Separator,
            SystemMenuEntry.Cascade("发送到", (sendTo ?? LazySendToItems.Value).ToArray()),
            SystemMenuEntry.Separator,
            SystemMenuEntry.Item("剪切"),
            SystemMenuEntry.Item("复制"),
            SystemMenuEntry.Item("创建快捷方式"),
            SystemMenuEntry.Item("删除"),
            SystemMenuEntry.Item("重命名"),
        };
        var after = new List<SystemMenuEntry> { SystemMenuEntry.Item("属性") };
        return new SystemMenuLayout(before, after);
    }

    private static SystemMenuLayout FolderLayout(
        MenuScope scope, bool clipboardHasFiles, IReadOnlyList<SystemMenuEntry>? newItems, bool includeGrantAccess)
    {
        var before = new List<SystemMenuEntry>
        {
            ViewSubmenu(),
            SortBySubmenu(),
            SystemMenuEntry.Item("刷新"),
            SystemMenuEntry.Separator,
            Paste("粘贴", clipboardHasFiles),
            Paste("粘贴快捷方式", clipboardHasFiles),
            SystemMenuEntry.Separator,
        };
        if (includeGrantAccess)
        {
            before.Add(SystemMenuEntry.Cascade("授予访问权限",
                SystemMenuEntry.Item("特定用户…"),
                SystemMenuEntry.Item("删除访问权限")));
            before.Add(SystemMenuEntry.Separator);
        }
        before.Add(SystemMenuEntry.Cascade("新建", (newItems ?? LazyNewItems.Value).ToArray()));

        var after = new List<SystemMenuEntry> { SystemMenuEntry.Item("属性") };
        return new SystemMenuLayout(before, after);
    }

    private static SystemMenuLayout DesktopLayout(bool clipboardHasFiles, IReadOnlyList<SystemMenuEntry>? newItems)
    {
        var before = new List<SystemMenuEntry>
        {
            ViewSubmenu(),
            SortBySubmenu(),
            SystemMenuEntry.Item("刷新"),
            SystemMenuEntry.Separator,
            Paste("粘贴", clipboardHasFiles),
            Paste("粘贴快捷方式", clipboardHasFiles),
            SystemMenuEntry.Separator,
            SystemMenuEntry.Cascade("新建", (newItems ?? LazyNewItems.Value).ToArray()),
        };
        // The desktop menu ends with Display settings / Personalize, no separator
        // between them and none before them beyond the one the composer adds.
        var after = new List<SystemMenuEntry>
        {
            SystemMenuEntry.Item("显示设置"),
            SystemMenuEntry.Item("个性化"),
        };
        return new SystemMenuLayout(before, after);
    }

    private static SystemMenuLayout DriveLayout()
    {
        var before = new List<SystemMenuEntry>
        {
            SystemMenuEntry.Default("打开"),
            SystemMenuEntry.Item("在终端中打开"),
            SystemMenuEntry.Separator,
            SystemMenuEntry.Item("固定到“快速访问”"),
            SystemMenuEntry.Separator,
            SystemMenuEntry.Item("格式化…"),
        };
        var after = new List<SystemMenuEntry> { SystemMenuEntry.Item("属性") };
        return new SystemMenuLayout(before, after);
    }

    // ------------------------------------------------------------- sub-menus

    /// <summary>"View" sub-menu: 8 view modes, one of them checked.</summary>
    public static SystemMenuEntry ViewSubmenu()
    {
        // Explorer's default folder view is Details on a fresh profile.
        var children = ViewModes
            .Select(m => SystemMenuEntry.Check(m, string.Equals(m, "详细信息", StringComparison.Ordinal)))
            .ToArray();
        return SystemMenuEntry.Cascade("查看", children);
    }

    /// <summary>"Sort by" sub-menu: the four keys, then Ascending / Descending.</summary>
    public static SystemMenuEntry SortBySubmenu()
    {
        return SystemMenuEntry.Cascade("排序方式",
            SystemMenuEntry.Check("名称", true),
            SystemMenuEntry.Check("修改日期", false),
            SystemMenuEntry.Check("类型", false),
            SystemMenuEntry.Check("大小", false),
            SystemMenuEntry.Separator,
            SystemMenuEntry.Check("递增", true),
            SystemMenuEntry.Check("递减", false));
    }

    private static SystemMenuEntry Paste(string header, bool clipboardHasFiles)
        => new() { Header = header, IsEnabled = clipboardHasFiles };

    /// <summary>
    /// "Open with" sub-menu. Explorer builds it from the extension's
    /// <c>OpenWithProgids</c>; when the scope is not extension-specific the
    /// menu only offers the picker, which is also what Explorer does for
    /// un-associated files.
    /// </summary>
    private static SystemMenuEntry BuildOpenWithSubmenu(MenuScope scope)
    {
        var children = new List<SystemMenuEntry>();
        foreach (var name in GetOpenWithPrograms(scope))
            children.Add(SystemMenuEntry.Item(name));
        if (children.Count > 0)
            children.Add(SystemMenuEntry.Separator);
        children.Add(SystemMenuEntry.Item("选择其他应用"));
        return SystemMenuEntry.Cascade("打开方式", children.ToArray());
    }

    private static IEnumerable<string> GetOpenWithPrograms(MenuScope scope)
    {
        if (string.IsNullOrEmpty(scope.Extension)) yield break;

        string[] progIds;
        try
        {
            using var extKey = Registry.ClassesRoot.OpenSubKey(scope.Extension);
            using var openWith = extKey?.OpenSubKey("OpenWithProgids");
            progIds = openWith?.GetValueNames() ?? Array.Empty<string>();
        }
        catch
        {
            yield break;
        }

        var names = new List<string>();
        foreach (var progId in progIds)
        {
            var friendly = ResolveFriendlyTypeName(progId);
            if (!string.IsNullOrWhiteSpace(friendly) && !names.Contains(friendly, StringComparer.OrdinalIgnoreCase))
                names.Add(friendly!);
        }
        foreach (var name in names)
            yield return name;
    }

    private static string? ResolveFriendlyTypeName(string progId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(progId);
            if (key is null) return null;
            var raw = key.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Values of the form "@C:\Windows\System32\shell32.dll,-1234" are
            // indirect strings; resolve them the same way the parser does.
            var text = Helpers.MuiStringHelper.Resolve(raw) ?? raw;
            // "txtfile" style ProgIDs often only have a type name, not an app
            // name — keep it only when it looks like a readable label.
            return text.StartsWith('@') ? null : text;
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------- registry data

    /// <summary>
    /// "New" sub-menu built from the machine's <c>ShellNew</c> registrations,
    /// with Folder / Shortcut pinned on top exactly like Explorer does.
    /// </summary>
    private static IReadOnlyList<SystemMenuEntry> BuildNewSubmenu()
    {
        var list = new List<SystemMenuEntry>
        {
            new() { Header = "文件夹", IsDefault = true },
            SystemMenuEntry.Item("快捷方式"),
            SystemMenuEntry.Separator,
        };

        var fromShellNew = new List<(string Label, string Ext)>();
        try
        {
            // Never dispose Registry.ClassesRoot itself — it is a shared static
            // handle; only the sub-keys opened below own their lifetime.
            foreach (var ext in Registry.ClassesRoot.GetSubKeyNames())
            {
                if (!ext.StartsWith('.')) continue;
                try
                {
                    using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
                    using var shellNew = extKey?.OpenSubKey("ShellNew");
                    if (shellNew is null) continue;

                    // Honour the documented opt-outs.
                    if (shellNew.GetValue("NoExtension") is not null) continue;

                    var progId = extKey?.GetValue(null) as string;
                    var label = string.IsNullOrEmpty(progId) ? ext.TrimStart('.') : ResolveFriendlyTypeName(progId!);
                    if (string.IsNullOrWhiteSpace(label)) label = ext.TrimStart('.').ToUpperInvariant();
                    fromShellNew.Add((label!, ext));
                }
                catch
                {
                    // A single unreadable extension must not break the menu.
                }
            }
        }
        catch
        {
            // Registry unavailable (tests / locked down): fall back to the
            // templates every Windows install ships.
            fromShellNew.AddRange(new (string, string)[]
            {
                ("文本文档", ".txt"),
                ("BMP 图像", ".bmp"),
                ("压缩(zipped)文件夹", ".zip"),
                ("RTF 文档", ".rtf"),
            });
        }

        foreach (var entry in fromShellNew
                     .DistinctBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(e => e.Label, StringComparer.CurrentCulture))
            list.Add(SystemMenuEntry.Item(entry.Label));

        return list;
    }

    /// <summary>"Send to" sub-menu mirrored from %APPDATA%\Microsoft\Windows\SendTo.</summary>
    private static IReadOnlyList<SystemMenuEntry> BuildSendToSubmenu()
    {
        var items = new List<SystemMenuEntry>();
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir)
                             .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCulture))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(name))
                        items.Add(SystemMenuEntry.Item(name));
                }
            }
        }
        catch
        {
            // Ignored: fall through to the defaults below.
        }

        if (items.Count == 0)
        {
            items.AddRange(new[]
            {
                SystemMenuEntry.Item("蓝牙设备"),
                SystemMenuEntry.Item("桌面（创建快捷方式）"),
                SystemMenuEntry.Item("文档"),
                SystemMenuEntry.Item("邮件收件人"),
                SystemMenuEntry.Item("压缩(zipped)文件夹"),
            });
        }
        return items;
    }
}
