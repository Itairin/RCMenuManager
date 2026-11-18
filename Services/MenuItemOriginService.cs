using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Works out how each menu item got there, using only data available on this
/// machine. The order of the checks matters: the most specific evidence wins, so
/// a verb this tool wrote is reported as such even though it also looks like a
/// normal registry key.
/// <para>
/// Evidence is never invented. When nothing matches, the item is reported as
/// <see cref="MenuItemSource.Unknown"/> rather than guessed at — a wrong label is
/// worse than no label, because the user acts on it.
/// </para>
/// </summary>
public sealed class MenuItemOriginService
{
    private readonly IOperationLog? _log;
    private readonly IPresetService? _presets;

    /// <summary>"scope|verb" keys, so a preset only claims its own scope.</summary>
    private Lazy<HashSet<string>> _knownPresetKeys;
    private Lazy<HashSet<string>> _createdKeys;

    public MenuItemOriginService(
        IOperationLog? log = null,
        IPresetService? presets = null)
    {
        _log = log;
        _presets = presets;

        _knownPresetKeys = new Lazy<HashSet<string>>(LoadPresetKeys);
        _createdKeys = new Lazy<HashSet<string>>(LoadCreatedKeys);
    }

    /// <summary>
    /// Drops the cached evidence. The preset catalog and the operation log are live
    /// state: after applying a preset the same key legitimately becomes "written by
    /// this tool", and a process-lifetime cache would keep saying otherwise until the
    /// app restarted.
    /// </summary>
    public void Invalidate()
    {
        _knownPresetKeys = new Lazy<HashSet<string>>(LoadPresetKeys);
        _createdKeys = new Lazy<HashSet<string>>(LoadCreatedKeys);
    }

    /// <summary>
    /// Analyses one parsed verb.
    /// <paramref name="scopeId"/> is required: a preset is defined per scope, and
    /// matching on the verb name alone marked Windows' own <c>open</c> verb as
    /// "written by this tool" merely because the Drive preset happens to be
    /// called <c>open</c>.
    /// </summary>
    public MenuItemOrigin Analyse(ContextMenuItem item, string? scopeId)
    {
        if (item is null) return MenuItemOrigin.Unknown;

        // 0) Rows the shell generated itself have no registry identity to analyse.
        if (item.IsShellProvided)
        {
            return new MenuItemOrigin(
                MenuItemSource.ShellProvided,
                "扩展提供",
                item.ShellSourceDetail ?? "该行由系统菜单直接生成，注册表里没有对应的可编辑项。",
                Evidence: null);
        }

        // 1) Our own writes. Two independent proofs, both scoped:
        //    - the operation log recorded a *creation* for this verb, or
        //    - a preset for this exact scope declares this verb, and the key really
        //      lives in HKCU (the only hive presets are written to).
        //    Edits performed through this tool do not count: backing up a
        //    third-party verb before editing it is not "adding" it.
        if (WasCreatedByThisTool(item) || IsAppliedPreset(item, scopeId))
        {
            return new MenuItemOrigin(
                MenuItemSource.ThisTool,
                "本工具",
                "由 RCMenuManager 写入（操作日志或推荐预设中有对应记录），可以放心编辑或删除。",
                Evidence: item.RegistryPath);
        }

        // 2) Shared CommandStore definition: the key lives in the system command
        //    library, so it belongs to whatever parent references it.
        if (item.IsSharedDefinition)
        {
            return new MenuItemOrigin(
                MenuItemSource.CommandStore,
                "命令库",
                "来自系统共享命令库（CommandStore），可能被多个菜单同时引用；改动会影响所有引用它的菜单。",
                Evidence: item.RegistryPath);
        }

        // 3) ExplorerCommandHandler: a Win11-era handler identified by CLSID. These
        //    keys carry no command line at all, which is exactly why the list used to
        //    show the raw key name as the menu label.
        // The parser already read this while building the model; only fall back to a
        // registry read when the caller constructed the item another way.
        var handlerClsid = !string.IsNullOrWhiteSpace(item.ExplorerCommandHandler)
            ? item.ExplorerCommandHandler
            : ReadValue(item, "ExplorerCommandHandler");
        if (!string.IsNullOrWhiteSpace(handlerClsid))
        {
            var name = ResolveClsidName(handlerClsid!);
            var dll = ResolveClsidServer(handlerClsid!);
            var fromApp = IsOutsideWindows(dll);
            return new MenuItemOrigin(
                MenuItemSource.ExplorerCommand,
                fromApp ? "应用处理器" : "系统处理器",
                $@"由 Explorer 命令处理器 {handlerClsid} 实现"
                + (string.IsNullOrEmpty(name) ? "。" : $"（{name}）。")
                + (fromApp
                    ? "该处理器由第三方应用注册，卸载对应应用后菜单项才会消失。"
                    : "该处理器随 Windows 提供。")
                + "此类项没有命令行，行为由处理器自身决定。",
                Evidence: dll ?? handlerClsid);
        }

        // 4) DelegateExecute: still a COM handler, but the older shell-extension shape.
        if (item.HasDelegateExecute)
        {
            return new MenuItemOrigin(
                MenuItemSource.ShellExtension,
                "COM 处理器",
                $"由 COM 处理器 {item.DelegateExecute} 执行，没有命令行；编辑命令不会生效。",
                Evidence: item.DelegateExecute);
        }

        // 5) Windows' own verbs: those shipped in the merged class keys, and those
        //    whose command launches a system binary (BitLocker's bde*.exe,
        //    Explorer's own launchers). Without the second test every Windows
        //    BitLocker entry was reported as "installed by an application".
        if (item.IsSystemVerb || IsSystemOwned(item) || RunsSystemBinary(item) || LabelFromWindowsResource(item))
        {
            return new MenuItemOrigin(
                MenuItemSource.SystemBuiltIn,
                "系统内置",
                "Windows 自带的功能（资源管理器或系统资源提供标签与行为），不是应用安装的。",
                Evidence: item.RegistryPath);
        }

        // 6) Anything left with a real command line was installed by an application.
        if (!string.IsNullOrWhiteSpace(item.Command))
        {
            var exe = TryExtractExecutable(item.ExpandedCommand);
            return new MenuItemOrigin(
                MenuItemSource.Application,
                "应用注册",
                string.IsNullOrEmpty(exe)
                    ? "由第三方应用写入注册表注册的菜单项。"
                    : $"由应用 {exe} 注册；删除它通常不会影响应用本身，但升级后可能被重新写入。",
                Evidence: item.Command);
        }

        // 7) A cascading parent with no command: the children carry the behaviour.
        if (item.Children.Count > 0)
        {
            return new MenuItemOrigin(
                MenuItemSource.Application,
                "应用注册",
                "由应用注册的级联菜单父项（本身没有命令，行为在子项中）。",
                Evidence: item.RegistryPath);
        }

        return MenuItemOrigin.Unknown;
    }

    // ------------------------------------------------------------------ evidence

    /// <summary>
    /// True when the operation log recorded a creation for this exact key. Matching on
    /// the verb name alone credited any same-named key — including a third-party one
    /// in the other hive — to this tool, which then told the user it was safe to
    /// delete.
    /// </summary>
    private bool WasCreatedByThisTool(ContextMenuItem item)
    {
        var path = NormalizeForCompare(item.RegistryPath);
        return path.Length > 0 && _createdKeys.Value.Contains(path);
    }

    /// <summary>Lower-cased hive + sub-key, so HKLM and HKCU keys never collide.</summary>
    private static string NormalizeForCompare(string? registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath)) return string.Empty;
        var (hive, subKey) = SplitRegistryPath(registryPath!);
        return hive.ToString().ToLowerInvariant() + "|" + subKey.ToLowerInvariant();
    }

    /// <summary>
    /// True when a preset for this exact scope declares this verb *and* the key
    /// sits in HKCU. Presets are only ever written per-user, so a machine-wide key
    /// with the same name was put there by something else.
    /// </summary>
    private bool IsAppliedPreset(ContextMenuItem item, string? scopeId)
    {
        if (string.IsNullOrEmpty(item.VerbName) || string.IsNullOrEmpty(scopeId)) return false;
        // Derived from the path rather than from Origin: Origin is only populated by
        // the merged-query parser, and an item built anywhere else would silently
        // lose its attribution.
        if (!IsUserHive(item)) return false;
        return _knownPresetKeys.Value.Contains(PresetKey(scopeId!, item.VerbName));
    }

    /// <summary>True when the verb key lives under HKEY_CURRENT_USER.</summary>
    private static bool IsUserHive(ContextMenuItem item)
    {
        var path = item.RegistryPath;
        if (string.IsNullOrEmpty(path))
            return item.Origin == RegistryHiveOrigin.CurrentUser;
        return path.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);
    }

    private static string PresetKey(string scopeId, string verbName)
        => scopeId.Trim().ToLowerInvariant() + "|" + verbName.Trim().ToLowerInvariant();

    /// <summary>Built-in catalog keys, resolved once per process.</summary>
    private static readonly Lazy<HashSet<string>> BuiltInPresetKeys =
        new(LoadBuiltInPresetKeys, System.Threading.LazyThreadSafetyMode.PublicationOnly);

    private static HashSet<string> LoadBuiltInPresetKeys()
    {
        try
        {
            return ToKeySet(PresetService.ReadEmbeddedDefault());
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private HashSet<string> LoadPresetKeys()
    {
        var set = new HashSet<string>(BuiltInPresetKeys.Value, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_presets is not null)
                foreach (var key in ToKeySet(_presets.Load().Presets)) set.Add(key);
        }
        catch
        {
            // A broken catalog must not disable source analysis entirely.
        }
        return set;
    }

    private static HashSet<string> ToKeySet(IEnumerable<PresetItem> presets)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in presets)
            if (!string.IsNullOrWhiteSpace(p.VerbName) && !string.IsNullOrWhiteSpace(p.Scope))
                set.Add(PresetKey(p.Scope, p.VerbName));
        return set;
    }

    /// <summary>Operations that bring a verb key into existence.</summary>
    private static readonly HashSet<string> CreationOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreateRoot", "CreateChild", "CreateParent",
    };

    /// <summary>
    /// Verbs this tool created, taken from the (rotated) operation log.
    /// <para>
    /// Backups are deliberately *not* used as evidence: every edit snapshots the
    /// existing key first, so the folder is full of files for verbs the user never
    /// created here — treating those as "added by this tool" labelled VS Code's
    /// shell entry as our own.
    /// </para>
    /// </summary>
    private HashSet<string> LoadCreatedKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_log is not null)
                foreach (var e in _log.ReadAll())
                    if (e.success && CreationOps.Contains(e.op) && !string.IsNullOrWhiteSpace(e.subKey))
                        set.Add(NormalizeForCompare($"{e.hive}\\{e.subKey}"));
        }
        catch
        {
            // Unreadable log: nothing can be proven, so nothing is claimed.
        }
        return set;
    }

    /// <summary>
    /// Shell-owned keys: the shell supplies the label (MUIVerb / resource) and the
    /// key carries no command of its own. Shipped with Windows, not installed.
    /// </summary>
    private static bool IsSystemOwned(ContextMenuItem item)
        => string.IsNullOrWhiteSpace(item.Command)
           && !item.HasDelegateExecute
           && item.Children.Count == 0;

    /// <summary>
    /// True when the verb launches something inside the Windows directory
    /// (%SystemRoot%). Those are OS components registering themselves — BitLocker's
    /// bde*.exe launchers were previously reported as "installed by an application".
    /// </summary>
    private static bool RunsSystemBinary(ContextMenuItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Command)) return false;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(root)) return false;

        var text = item.ExpandedCommand.Length > 0 ? item.ExpandedCommand : item.Command;
        var exe = TryExtractExecutablePath(text);
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            // A bare name ("cmd.exe") resolves through PATH, which for these verbs
            // means System32 — still an OS binary.
            if (!exe!.Contains('\\') && !exe.Contains('/')) return true;
            var full = Path.GetFullPath(exe);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the MUIVerb / default value is an indirect reference into a Windows
    /// DLL, e.g. <c>@efscore.dll,-101</c>. The label then comes from the OS itself,
    /// which means the entry is a Windows feature even when it has no command line
    /// of its own (文件所有权 / EFS is the common case).
    /// </summary>
    private static bool LabelFromWindowsResource(ContextMenuItem item)
    {
        var raw = item.MuiVerb;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Helpers.MuiStringHelper.IsIndirectReference(raw)) return false;

        var name = raw!.TrimStart('@');
        var comma = name.IndexOf(',');
        if (comma > 0) name = name.Substring(0, comma);
        if (name.Length == 0) return false;

        var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(root)) return false;

        try
        {
            // Bare file names ("efscore.dll") live in System32.
            var full = name.Contains('\\') || name.Contains(':')
                ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(name))
                : Path.Combine(root, "System32", name);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>First token of a command line, with quotes removed.</summary>
    /// <summary>InProcServer32 / LocalServer32 of a CLSID, when registered.</summary>
    private static string? ResolveClsidServer(string clsid)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InProcServer32");
            return key?.GetValue(string.Empty) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the path is a real file outside %SystemRoot%.</summary>
    private static bool IsOutsideWindows(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path!.Trim('"'));
            if (!Path.IsPathRooted(expanded)) return false;
            return !Path.GetFullPath(expanded).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryExtractExecutablePath(string command)
    {
        var text = command.Trim();
        if (text.Length == 0) return null;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text.Substring(1, end - 1) : null;
        }
        var space = text.IndexOf(' ');
        return space < 0 ? text : text.Substring(0, space);
    }

    private string? ReadValue(ContextMenuItem item, string name)
    {
        try
        {
            var (hive, subKey) = SplitRegistryPath(item.RegistryPath);
            using var root = OpenRoot(hive);
            using var key = root?.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolves a CLSID to its registered friendly name (may be a raw id).</summary>
    private static string? ResolveClsidName(string clsid)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID{clsid}");
            var name = key?.GetValue(string.Empty) as string;
            if (string.IsNullOrWhiteSpace(name)) return null;
            // Some handlers only register the key name itself; that is not a label.
            return name.StartsWith('{') ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? Path.GetFileName(text.Substring(1, end - 1)) : null;
        }
        var space = text.IndexOf(' ');
        var first = space < 0 ? text : text.Substring(0, space);
        return first.Contains('\\') || first.Contains('/') ? Path.GetFileName(first) : null;
    }

    private static (RegistryHive hive, string subKey) SplitRegistryPath(string registryPath)
    {
        var idx = registryPath.IndexOf('\\');
        if (idx < 0) return (RegistryHive.CurrentUser, registryPath);
        var hiveName = registryPath.Substring(0, idx).ToUpperInvariant();
        var subKey = registryPath.Substring(idx + 1);
        var hive = hiveName switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => RegistryHive.CurrentUser,
        };
        return (hive, subKey);
    }

    private static RegistryKey? OpenRoot(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => Registry.ClassesRoot,
        RegistryHive.CurrentUser => Registry.CurrentUser,
        RegistryHive.LocalMachine => Registry.LocalMachine,
        RegistryHive.Users => Registry.Users,
        RegistryHive.CurrentConfig => Registry.CurrentConfig,
        _ => null,
    };
}
