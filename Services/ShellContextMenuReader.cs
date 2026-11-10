using System;
using System.Collections.Concurrent;
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

    /// <summary>
    /// A failure is remembered for this long so a handler that hangs (the read
    /// is abandoned after 10s) is not retried on every single right-click.
    /// After it expires the shell is asked again, so a transient stall — a
    /// busy disk, a handler that was still loading at startup — heals itself
    /// instead of degrading the preview for the rest of the session.
    /// </summary>
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<ShellMenuNode>> _cache = new();
    private readonly Dictionary<string, DateTime> _failedAt = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _readGates = new();
    private int _generation;

    public IReadOnlyList<ShellMenuNode>? TryRead(MenuScope scope, bool extendedVerbs)
    {
        var key = scope.ScopeId + (extendedVerbs ? "|ext" : "|norm");
        if (TryGetCached(key, out var cached)) return cached;

        // One read per key at a time. The UI thread (right-click) and the
        // background prewarm both want the same menu right after startup;
        // without this they would each load every shell extension twice.
        var gate = _readGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            // The winner may already have filled the cache while we waited.
            if (TryGetCached(key, out cached)) return cached;

            // Taken right before the shell is asked, not before the wait: this
            // is the point the answer starts to be derived from. If a registry
            // write invalidates while the read is running, the result is stale
            // and must not be published into the cache.
            var generation = Volatile.Read(ref _generation);

            IReadOnlyList<ShellMenuNode>? nodes;
            try
            {
                nodes = Read(scope, extendedVerbs);
            }
            catch
            {
                // Any shell failure (locked-down machine, no such class) falls
                // back to the hand-built catalog instead of breaking the preview.
                nodes = null;
            }

            lock (_gate)
            {
                if (generation != _generation)
                {
                    // Superseded by an invalidate: hand the result to the caller
                    // but keep it out of the cache.
                    return nodes;
                }
                if (nodes is { Count: > 0 })
                {
                    // Empty / null results are *not* cached as menus: they are
                    // recorded as failures with an expiry instead.
                    _cache[key] = nodes;
                    _failedAt.Remove(key);
                }
                else
                {
                    _failedAt[key] = DateTime.UtcNow;
                }
            }
            return nodes;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryGetCached(string key, out IReadOnlyList<ShellMenuNode>? nodes)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                nodes = hit;
                return true;
            }
            if (_failedAt.TryGetValue(key, out var at) && DateTime.UtcNow - at < FailureCooldown)
            {
                nodes = null;
                return true;
            }
            nodes = null;
            return false;
        }
    }

    private static IReadOnlyList<ShellMenuNode>? Read(MenuScope scope, bool extendedVerbs)
    {
        switch (scope.Type)
        {
            case ScopeType.Desktop:
                return RunSta(() => ShellMenuInterop.ReadBackground(null, extendedVerbs));
            case ScopeType.FolderBackground:
            {
                var folder = PreviewTarget.BackgroundFolder();
                return RunSta(() => ShellMenuInterop.ReadBackground(folder, extendedVerbs));
            }
            default:
            {
                var target = PreviewTarget.Resolve(scope);
                return target is null ? null : RunSta(() => ShellMenuInterop.Read(target.Value, extendedVerbs));
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
            // A pending cooldown is dropped too: the caller usually invalidates
            // right after writing the registry, and the whole point of the
            // invalidate is "try again now".
            _failedAt.Clear();
            _generation++;
        }
    }

    /// <summary>
    /// Shell menus must be built on an STA thread with a message pump-free
    /// stack; the caller may be on any thread, so the work is isolated here.
    /// A stalled third-party handler must not hang the UI, hence the timeout.
    /// </summary>
    private static IReadOnlyList<ShellMenuNode>? RunSta(Func<IReadOnlyList<ShellMenuNode>?> work)
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
                    // Any other scope has no item to describe; the callers that
                    // matter (desktop / folder background) are handled above.
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Folder used to describe a "folder background" menu. The verbs there are
    /// the same for every folder on the machine, so an empty scratch folder is
    /// enough and nothing on the user's disk is touched.
    /// </summary>
    public static string BackgroundFolder() => EnsureSampleFolder();

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
    private const uint MF_CHECKED = 0x8;
    private const uint MF_GRAYED = 0x1;
    private const uint MF_DEFAULT = 0x1000;

    private const uint CMF_CANRENAME = 0x00000010;
    private const uint CMF_EXTENDEDVERBS = 0x00000020;

    private const int MIIM_BITMAP = 0x00000080;
    private const int MIIM_FTYPE = 0x00000100;
    private const int MIIM_STATE = 0x00000001;
    private const int MIIM_SUBMENU = 0x00000004;
    private const int MFT_SEPARATOR = 0x00000800;

    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid IID_IShellFolder = new("000214e6-0000-0000-c000-000000000046");
    private static readonly Guid IID_IShellView = new("000214e3-0000-0000-c000-000000000046");
    private static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid IID_IShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    private const uint CLSCTX_ALL = 0x17;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;

    public static IReadOnlyList<ShellMenuNode> Read(Target target, bool extendedVerbs)
    {
        var iidItem = IID_IShellItem;
        var bhid = BHID_SFUIObject;
        var iidContextMenu = IID_IContextMenu;

        SHCreateItemFromParsingName(target.Path, IntPtr.Zero, ref iidItem, out var item);
        item.BindToHandler(IntPtr.Zero, ref bhid, ref iidContextMenu, out var ctxObj);
        var ctx = (IContextMenu)ctxObj;

        return Build(ctx, extendedVerbs);
    }

    /// <summary>
    /// Reads the menu of a *view background* (the desktop, or the empty area of
    /// a folder). Those menus are not owned by an item, so the item-based path
    /// above cannot produce them: the shell view that owns the background is
    /// asked for its own <c>IContextMenu</c>.
    /// <paramref name="folderPath"/> null means the desktop.
    /// </summary>
    public static IReadOnlyList<ShellMenuNode>? ReadBackground(string? folderPath, bool extendedVerbs)
    {
        var view = string.IsNullOrEmpty(folderPath) ? GetDesktopView() : GetFolderBackgroundView(folderPath);
        if (view is null) return null;

        var iidContextMenu = IID_IContextMenu;
        view.GetItemObject(0 /* SVGIO_BACKGROUND */, ref iidContextMenu, out var ctxObj);
        if (ctxObj is not IContextMenu ctx) { return null; }
        return Build(ctx, extendedVerbs);
    }

    /// <summary>
    /// A detached view over the folder. The view has no window, but it still
    /// owns the background object Explorer would show, so its menu can be read
    /// without opening an Explorer window.
    /// </summary>
    private static IShellView? GetFolderBackgroundView(string folderPath)
    {
        var iidFolder = IID_IShellFolder;
        var iidView = IID_IShellView;

        SHGetDesktopFolder(out var desktop);
        SHParseDisplayName(folderPath, IntPtr.Zero, out var pidl, 0, out _);
        if (pidl == IntPtr.Zero) return null;

        IShellFolder folder;
        try
        {
            desktop.BindToObject(pidl, IntPtr.Zero, ref iidFolder, out var folderObj);
            if (folderObj is not IShellFolder bound) return null;
            folder = bound;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }

        folder.CreateViewObject(IntPtr.Zero, ref iidView, out var viewObj);
        if (viewObj is not IShellView view) return null;
        return view;
    }

    /// <summary>
    /// The desktop's live view, reached the way Explorer itself reaches it:
    /// the shell-windows collection hands out the desktop window's dispatch,
    /// which exposes the top-level browser, which owns the active view.
    /// </summary>
    private static IShellView? GetDesktopView()
    {
        var clsid = CLSID_ShellWindows;
        var iidShellWindows = IID_IShellWindows;
        CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iidShellWindows, out var shellWindowsObj);
        var shellWindows = (IShellWindows)shellWindowsObj;

        // FindWindowSW dereferences the VARIANT pointers, so NULL crashes the
        // process. A zeroed VARIANT is VT_EMPTY, which is what the shell expects
        // for "no location filter".
        var emptyLoc = Marshal.AllocHGlobal(24);
        var emptyRoot = Marshal.AllocHGlobal(24);
        try
        {
            for (var i = 0; i < 24; i++)
            {
                Marshal.WriteByte(emptyLoc, i, 0);
                Marshal.WriteByte(emptyRoot, i, 0);
            }
            shellWindows.FindWindowSW(emptyLoc, emptyRoot, SWC_DESKTOP, out var desktopHwnd, SWFO_NEEDDISPATCH, out var dispatch);
            if (dispatch is not IShellServiceProvider provider) { return null; }

            var sid = SID_STopLevelBrowser;
            var iidBrowser = IID_IShellBrowser;
            provider.QueryService(ref sid, ref iidBrowser, out var browserObj);
            if (browserObj is not IShellBrowser browser) { return null; }

            browser.QueryActiveShellView(out var view);
            return view;
        }
        finally
        {
            Marshal.FreeHGlobal(emptyLoc);
            Marshal.FreeHGlobal(emptyRoot);
        }
    }

    private static IReadOnlyList<ShellMenuNode> Build(IContextMenu ctx, bool extendedVerbs)
    {
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
            // GetMenuState reports MF_SEPARATOR for ordinary popup items built
            // by the shell (its "查看" submenu comes back as 0x810), so the item
            // type has to be read from MENUITEMINFO instead.
            var info = new MENUITEMINFO
            {
                cbSize = Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_FTYPE | MIIM_STATE | MIIM_SUBMENU,
            };
            GetMenuItemInfo(menu, (uint)i, true, ref info);
            if ((info.fType & MFT_SEPARATOR) != 0)
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
                IsEnabled = (info.fState & MF_GRAYED) == 0,
                IsChecked = (info.fState & MF_CHECKED) != 0,
                IsDefault = (info.fState & MF_DEFAULT) != 0,
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
    private static extern bool GetMenuItemInfo(IntPtr hMenu, uint item, bool byPosition, ref MENUITEMINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetDesktopFolder(
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint clsContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    /// <summary>
    /// Dual interface (derives from IDispatch), so the four IDispatch slots must
    /// be declared first — otherwise every call lands one slot too early.
    /// </summary>
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void GetTypeInfoCount(out uint count);
        void GetTypeInfo(uint typeInfo, uint lcid, out IntPtr info);
        void GetIDsOfNames(ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names,
            uint count, uint lcid, [MarshalAs(UnmanagedType.LPArray)] int[] dispIds);
        void Invoke(int dispIdMember, ref Guid riid, uint lcid, ushort flags,
            IntPtr parameters, IntPtr result, IntPtr exceptionInfo);

        void get_Count(out int count);
        void Item(int index, [MarshalAs(UnmanagedType.Interface)] out object dispatch);
        void _NewEnum(out IntPtr enumVariant);
        void Register(IntPtr dispatch, int hwnd, int swClass, out int cookie);
        void RegisterPending(int threadId, IntPtr loc, IntPtr root, int swClass, out int cookie);
        void Revoke(int cookie);
        void OnNavigate(int cookie, IntPtr loc);
        void OnActivated(int cookie, bool active);
        void FindWindowSW(IntPtr loc, IntPtr locRoot, int swClass, out int hwnd, int options,
            [MarshalAs(UnmanagedType.Interface)] out object dispatch);
        void OnCreate(IntPtr dispatch);
        void ProcessAttachDetach(bool attach);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    /// <summary>Inherits IOleWindow, so the two window methods come first.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enterMode);
        void InsertMenusSB(IntPtr hmenuShared, IntPtr menuWidths);
        void SetMenuSB(IntPtr hmenuShared, IntPtr holeMenu, IntPtr hwndOwner);
        void RemoveMenusSB(IntPtr hmenuShared);
        void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        void EnableModelessSB(bool enable);
        void TranslateAcceleratorSB(IntPtr msg, ushort id);
        void BrowseObject(IntPtr pidl, uint flags);
        void GetViewStateStream(uint mode, out IntPtr stream);
        void GetControlWindow(uint id, out IntPtr hwnd);
        void SendControlMsg(uint id, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView view);
        void OnViewWindowActive(IntPtr view);
        void SetToolbarItems(IntPtr buttons, uint count, uint flags);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);
        void GetDisplayName(uint sigdnName, out IntPtr name);
        void GetAttributes(uint mask, out uint attrs);
        void Compare(IShellItem psi, uint hint, out int order);
    }

    /// <summary>
    /// Only <c>CreateViewObject</c> and <c>BindToObject</c> are used; the other
    /// slots must still be declared to keep the vtable offsets correct.
    /// </summary>
    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name,
            out uint eaten, out IntPtr pidl, out uint attrs);
        void EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetAttributesOf(uint count, IntPtr pidls, ref uint attrs);
        void GetUIObjectOf(IntPtr hwnd, uint count, IntPtr pidls, ref Guid riid, IntPtr reserved,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetDisplayNameOf(IntPtr pidl, uint flags, out IntPtr name);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint flags, out IntPtr newPidl);
    }

    /// <summary>Inherits IOleWindow, so the two window methods come first.</summary>
    [ComImport, Guid("000214e3-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(bool enterMode);
        void TranslateAccelerator(IntPtr msg);
        void EnableModeless(bool enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(IntPtr prevView, IntPtr settings, IntPtr rect, IntPtr hwndParent, out IntPtr hwndView);
        void DestroyViewWindow();
        void GetCurrentInfo(out IntPtr fs);
        void AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam);
        void SaveViewState();
        void SelectItem(IntPtr pidl, uint flags);
        void GetItemObject(uint item, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        void QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    }
}
