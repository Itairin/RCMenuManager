using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Produces the menu Explorer would show for a scope. Rather than guessing the
/// layout from a hand-written catalog (which can never know about the Samsung /
/// ToDesk / UltraISO entries a machine happens to have), it asks the shell to
/// build its own menu and walks the resulting HMENU. That makes the preview
/// byte-for-byte the same list, including labels with accelerators, separators,
/// greyed / checked state and icons.
/// </summary>
public interface IShellContextMenuReader
{
    /// <summary>Null when the shell could not produce a menu for this scope.</summary>
    IReadOnlyList<ShellMenuNode>? TryRead(MenuScope scope, bool extendedVerbs);

    /// <summary>Drops cached menus; call after a registry write so the next read reflects it.</summary>
    void Invalidate();
}

public sealed class ShellContextMenuReader : IShellContextMenuReader
{
    public static ShellContextMenuReader Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<ShellMenuNode>?> _cache = new();

    public IReadOnlyList<ShellMenuNode>? TryRead(MenuScope scope, bool extendedVerbs)
    {
        var key = scope.ScopeId + (extendedVerbs ? "|ext" : "|norm");
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        IReadOnlyList<ShellMenuNode>? nodes = null;
        try
        {
            var target = PreviewTarget.Resolve(scope);
            if (target is not null)
                nodes = RunSta(() => ShellMenuInterop.Read(target.Value, extendedVerbs));
        }
        catch
        {
            // Any shell failure (locked-down machine, no such class) falls back
            // to the hand-built catalog instead of breaking the preview.
            nodes = null;
        }

        lock (_gate) _cache[key] = nodes;
        return nodes;
    }

    public void Invalidate()
    {
        lock (_gate) _cache.Clear();
    }

    /// <summary>
    /// Shell menus must be built on an STA thread with a message pump-free
    /// stack; the caller may be on any thread, so the work is isolated here.
    /// A stalled third-party handler must not hang the UI, hence the timeout.
    /// </summary>
    private static IReadOnlyList<ShellMenuNode>? RunSta(Func<IReadOnlyList<ShellMenuNode>> work)
    {
        IReadOnlyList<ShellMenuNode>? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
            return null;
        return failure is null ? result : null;
    }
}

/// <summary>
/// A shell menu needs a concrete object to describe. Scopes are abstract, so a
/// stable placeholder object of the right kind is created once per session
/// (an empty file with the requested extension, an empty folder, a drive).
/// </summary>
internal static class PreviewTarget
{
    public static ShellMenuInterop.Target? Resolve(MenuScope scope)
    {
        try
        {
            switch (scope.Type)
            {
                case ScopeType.FileExtension when !string.IsNullOrEmpty(scope.Extension):
                    return new ShellMenuInterop.Target(EnsureSampleFile("sample" + scope.Extension));
                case ScopeType.AllFiles:
                case ScopeType.AllFilesystemObjects:
                    return new ShellMenuInterop.Target(EnsureSampleFile("sample.dat"));
                case ScopeType.Folder:
                    return new ShellMenuInterop.Target(EnsureSampleFolder());
                case ScopeType.Drive:
                {
                    var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                    return new ShellMenuInterop.Target(drive + "\\");
                }
                default:
                    // Folder background and the desktop are produced by the
                    // shell *view*, not by an item, and cannot be reached
                    // through IShellItem; those keep the catalog layout.
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureSampleFile(string fileName)
    {
        var dir = RequiredDirectory();
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private static string EnsureSampleFolder()
    {
        var path = Path.Combine(RequiredDirectory(), "sample-folder");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RequiredDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RCMenuManager", "preview");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

internal static class ShellMenuInterop
{
    public readonly record struct Target(string Path);

    private const uint MF_BYPOSITION = 0x400;
    private const uint MF_SEPARATOR = 0x800;
    private const uint MF_CHECKED = 0x8;
    private const uint MF_GRAYED = 0x1;
    private const uint MF_DEFAULT = 0x1000;

    private const uint CMF_CANRENAME = 0x00000010;
    private const uint CMF_EXTENDEDVERBS = 0x00000020;

    private const int MIIM_BITMAP = 0x00000080;

    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

    public static IReadOnlyList<ShellMenuNode> Read(Target target, bool extendedVerbs)
    {
        var iidItem = IID_IShellItem;
        var bhid = BHID_SFUIObject;
        var iidContextMenu = IID_IContextMenu;

        SHCreateItemFromParsingName(target.Path, IntPtr.Zero, ref iidItem, out var item);
        item.BindToHandler(IntPtr.Zero, ref bhid, ref iidContextMenu, out var ctxObj);
        var ctx = (IContextMenu)ctxObj;

        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) throw new InvalidOperationException("CreatePopupMenu 失败");
        try
        {
            var flags = CMF_CANRENAME | (extendedVerbs ? CMF_EXTENDEDVERBS : 0u);
            ctx.QueryContextMenu(hMenu, 0, 1, 0x7FFF, flags);
            return Walk(hMenu, 0);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private static IReadOnlyList<ShellMenuNode> Walk(IntPtr menu, int depth)
    {
        var nodes = new List<ShellMenuNode>();
        if (depth > 3) return nodes;

        var count = GetMenuItemCount(menu);
        for (var i = 0; i < count; i++)
        {
            var state = GetMenuState(menu, (uint)i, MF_BYPOSITION);
            if ((state & MF_SEPARATOR) != 0)
            {
                nodes.Add(new ShellMenuNode { IsSeparator = true });
                continue;
            }

            var text = new StringBuilder(512);
            GetMenuString(menu, (uint)i, text, text.Capacity, MF_BYPOSITION);

            var sub = GetSubMenu(menu, i);
            nodes.Add(new ShellMenuNode
            {
                Text = text.ToString(),
                IsEnabled = (state & MF_GRAYED) == 0,
                IsChecked = (state & MF_CHECKED) != 0,
                IsDefault = (state & MF_DEFAULT) != 0,
                Icon = ReadIcon(menu, i),
                Children = sub == IntPtr.Zero ? Array.Empty<ShellMenuNode>() : Walk(sub, depth + 1),
            });
        }
        return nodes;
    }

    /// <summary>
    /// Shell menu rows carry a 16x16 HBITMAP. The bitmap belongs to the menu, so
    /// it is converted (and frozen) before the menu is destroyed — no DeleteObject.
    /// </summary>
    private static ImageSource? ReadIcon(IntPtr menu, int index)
    {
        try
        {
            var info = new MENUITEMINFO
            {
                cbSize = Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_BITMAP,
            };
            if (!GetMenuItemInfo(menu, (uint)index, true, ref info)) return null;

            // HBMMENU_CALLBACK (-1) / HBMMENU_SYSTEM (1) are pseudo-handles.
            if (info.hbmpItem == IntPtr.Zero || (long)info.hbmpItem <= 1) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                info.hbmpItem, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFO
    {
        public int cbSize;
        public int fMask;
        public int fType;
        public int fState;
        public int wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public int cch;
        public IntPtr hbmpItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr hMenu, int pos);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr hMenu, uint id, StringBuilder text, int maxCount, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetMenuState(IntPtr hMenu, uint id, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMenuItemInfo(IntPtr hMenu, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);
        void GetDisplayName(uint sigdnName, out IntPtr name);
        void GetAttributes(uint mask, out uint attrs);
        void Compare(IShellItem psi, uint hint, out int order);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        void QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    }
}
