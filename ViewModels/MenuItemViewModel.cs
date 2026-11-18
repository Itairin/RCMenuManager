using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

/// <summary>
/// Wraps a parsed <see cref="ContextMenuItem"/> and resolves its icon lazily
/// for binding. Children are exposed as a real ObservableCollection so the
/// TreeView's HierarchicalDataTemplate can recurse.
/// </summary>
public partial class MenuItemViewModel : ObservableObject
{
    private readonly IconService _iconService;

    public ContextMenuItem Model { get; }

    /// <summary>
    /// Where this item came from. Set by <see cref="Services.MenuItemOriginService"/>
    /// right after parsing so the tree, the badge column and the details pane all
    /// read the same answer instead of each re-deriving it.
    /// </summary>
    public MenuItemOrigin Origin { get; internal set; } = MenuItemOrigin.Unknown;

    /// <summary>Short badge text; empty when nothing was recognised.</summary>
    public string SourceBadge => Origin.Badge;

    /// <summary>Longer explanation for the details pane.</summary>
    public string SourceDetail => Origin.Detail;

    public bool HasSourceBadge => Origin.Source != MenuItemSource.Unknown;

    /// <summary>
    /// True when the key stores no label at all and the name had to be inferred
    /// (CLSID handler name, standard verb table, or the raw key). Showing such a
    /// name without saying so is what made the desktop scope look wrong.
    /// </summary>
    public bool IsNameInferred => Model.HasNoStoredLabel;

    public string NameHint => IsNameInferred
        ? "该注册表键没有存储显示名，此处名称是推断出来的（来自命令处理器或系统 verb 表）"
        : string.Empty;

    public ObservableCollection<MenuItemViewModel> Children { get; }

    public string DisplayName => Model.DisplayName;

    /// <summary>
    /// <see cref="DisplayName"/> with the shell's access-key marker removed, for the
    /// places that render plain text (list rows, details title). The preview menu
    /// keeps the marker so it can underline the letter like Explorer does.
    /// </summary>
    public string MenuText => Helpers.MenuTextHelper.StripAccessKeys(Model.DisplayName);
    public string VerbName => Model.VerbName;
    public string Command => Model.Command;
    public string ExpandedCommand => Model.ExpandedCommand;

    /// <summary>
    /// Best command text to show: when env-expansion produced a different
    /// string, prefer the expanded form so the user actually sees the file
    /// being launched. We always keep the raw string accessible via
    /// <see cref="Command"/>.
    /// </summary>
    public string DisplayCommand =>
        !string.IsNullOrEmpty(ExpandedCommand) && !string.Equals(ExpandedCommand, Command, StringComparison.Ordinal)
            ? ExpandedCommand
            : Command;

    public bool HasEnvExpansion =>
        !string.IsNullOrEmpty(ExpandedCommand)
        && !string.Equals(ExpandedCommand, Command, StringComparison.Ordinal);

    public string RegistryPath => Model.RegistryPath;

    /// <summary>True when the row comes from the shell's own menu and cannot be edited.</summary>
    public bool IsReadOnly => Model.IsShellProvided;

    /// <summary>Inverse of <see cref="IsReadOnly"/>, for binding button state.</summary>
    public bool CanEdit => !IsReadOnly;

    /// <summary>Why it is read-only (shown in the details pane).</summary>
    public string? ShellSourceDetail => Model.ShellSourceDetail;

    /// <summary>
    /// True for a registry verb the shell did not draw for this scope. The row is
    /// editable, but the list says so instead of pretending it is on the menu.
    /// </summary>
    public bool NotInShellMenu => Model.NotInShellMenu;

    public string MenuPresenceNote => NotInShellMenu
        ? "该注册表项当前没有被资源管理器显示（可能被 AppliesTo 条件、使用频率或系统版本抑制）。编辑仍然有效，但菜单里可能看不到。"
        : string.Empty;
    public string? MuiVerb => Model.MuiVerb;
    public string? Position => Model.Position;
    public CascadingKind Cascading => Model.Cascading;
    public string CascadingText => Model.Cascading switch
    {
        CascadingKind.SubCommands => "SubCommands",
        CascadingKind.ExtendedSubCommandsKey => "ExtendedSubCommandsKey",
        CascadingKind.NestedShell => "嵌套 shell",
        _ => string.Empty,
    };

    public string OriginText => Model.Origin switch
    {
        RegistryHiveOrigin.CurrentUser => "HKCU",
        RegistryHiveOrigin.LocalMachine => "HKLM",
        RegistryHiveOrigin.ClassesRoot => "HKCR",
        _ => string.Empty,
    };

    public bool IsExtended => Model.IsExtended;
    public bool IsProgrammaticOnly => Model.IsProgrammaticOnly;
    public bool IsLegacyDisabled => Model.IsLegacyDisabled;
    /// <summary>True when the verb is hidden from the Explorer context menu,
    /// whether it was disabled via the modern <c>ProgrammaticAccessOnly</c>
    /// or the legacy <c>LegacyDisable</c> flag. Used by the Enable/Disable
    /// toggle so it covers both mechanisms.</summary>
    public bool IsDisabled => IsProgrammaticOnly || IsLegacyDisabled;

    /// <summary>
    /// True when Explorer will not draw this row at all. The list still shows it —
    /// it is a real registry entry the user may want to revive — but presenting it
    /// like an ordinary item made the panel disagree with the menu: on this machine
    /// the 文件与文件夹 scope listed two entries that never appear in Explorer.
    /// </summary>
    public bool IsHiddenFromMenu => IsDisabled;

    /// <summary>
    /// True when visibility is decided at runtime by Explorer (usage frequency),
    /// which the registry cannot resolve. The row may or may not be drawn.
    /// </summary>
    public bool HasVelocityCondition => Model.HasVelocityCondition;

    public string VisibilityNote
    {
        get
        {
            if (IsDisabled)
                return "菜单中隐藏：注册表标记了 ProgrammaticAccessOnly / LegacyDisable，资源管理器不会显示它。点「启停」可恢复显示。";
            if (HasVelocityCondition)
                return "显示与否由资源管理器按使用频率决定（Hide/ShowBasedOnVelocityId），不同电脑上可能不同。";
            return string.Empty;
        }
    }
    public bool IsSystemVerb => Model.IsSystemVerb;
    public bool HasChildren => Children.Count > 0;
    public bool IsTop => string.Equals(Model.Position, "Top", StringComparison.OrdinalIgnoreCase);
    public bool IsBottom => string.Equals(Model.Position, "Bottom", StringComparison.OrdinalIgnoreCase);

    /// <summary>CLSID-backed execution (DelegateExecute). Such verbs have no command line.</summary>
    public string? DelegateExecute => Model.DelegateExecute;
    public bool HasDelegateExecute => Model.HasDelegateExecute;
    public string? AppliesTo => Model.AppliesTo;
    public bool HasLuaShield => Model.HasLuaShield;

    /// <summary>Resolved from the shared CommandStore; editing affects other menus too.</summary>
    public bool IsSharedDefinition => Model.IsSharedDefinition;

    /// <summary>
    /// False when editing the command line would break the verb: DelegateExecute
    /// verbs are executed by a COM handler, and cascading parents deliberately
    /// have no command of their own.
    /// </summary>
    public bool CanEditCommand => !HasDelegateExecute && !Model.IsParentOnly;

    /// <summary>Hive that should be targeted when writing back this verb.
    /// HKCR origins map to HKLM since HKCR is the merged virtual view.</summary>
    public Microsoft.Win32.RegistryHive WriteHive => Model.Origin switch
    {
        RegistryHiveOrigin.CurrentUser => Microsoft.Win32.RegistryHive.CurrentUser,
        RegistryHiveOrigin.LocalMachine => Microsoft.Win32.RegistryHive.LocalMachine,
        RegistryHiveOrigin.ClassesRoot => Microsoft.Win32.RegistryHive.LocalMachine,
        _ => Microsoft.Win32.RegistryHive.CurrentUser,
    };

    /// <summary>The verb's sub-key path (without hive prefix), parsed from RegistryPath.</summary>
    public string SubKey
    {
        get
        {
            var path = Model.RegistryPath;
            var idx = path.IndexOf('\\');
            var sub = idx < 0 ? string.Empty : path.Substring(idx + 1);
            // HKCR is a merged view; the writable home for a machine-wide entry is
            // HKLM\SOFTWARE\Classes. Without this prefix the write silently lands
            // in HKLM\<verb path>, creating a junk key instead of editing the verb.
            if (Model.Origin == RegistryHiveOrigin.ClassesRoot &&
                !sub.StartsWith(@"SOFTWARE\Classes\", StringComparison.OrdinalIgnoreCase))
                sub = @"SOFTWARE\Classes\" + sub;
            return sub;
        }
    }

    /// <summary>True when the verb can host nested children (no command yet, or already nested-shell).</summary>
    public bool CanHaveChildren =>
        string.IsNullOrEmpty(Command) || Cascading == CascadingKind.NestedShell;

    /// <summary>One-line summary for the badge area in the tree view.</summary>
    public string Flags
    {
        get
        {
            var parts = new List<string>(4);
            if (IsReadOnly) parts.Add("只读");
            if (NotInShellMenu) parts.Add("菜单中未显示");
            if (IsExtended) parts.Add("Shift");
            // The registry flag names stay out of the badge: both flags mean the same
            // thing to a user ("Explorer will not draw this"), and the details pane
            // spells out which one it is.
            if (IsHiddenFromMenu) parts.Add("菜单中隐藏");
            else if (HasVelocityCondition) parts.Add("按频率显示");
            if (IsSystemVerb) parts.Add("系统");
            if (HasDelegateExecute) parts.Add("COM 代理");
            if (HasLuaShield) parts.Add("UAC");
            if (IsSharedDefinition) parts.Add("共享");
            if (IsTop) parts.Add("置顶");
            if (IsBottom) parts.Add("置底");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Two-way bound to <c>TreeViewItem.IsSelected</c>. The list is rebuilt after
    /// every write (which destroys the containers), so the flag is kept on the item
    /// itself: that is what lets the view model restore the user's selection instead
    /// of leaving the details panel on an item that is no longer in the list.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _icon;

    public MenuItemViewModel(ContextMenuItem model, IconService iconService, ImageSource? shellIcon = null)
    {
        Model = model;
        _iconService = iconService;
        Children = new ObservableCollection<MenuItemViewModel>(
            model.Children.Select(c => new MenuItemViewModel(c, iconService)));
        // Shell-provided rows carry the bitmap Explorer drew; registry verbs fall back
        // to their Icon value.
        Icon = shellIcon ?? _iconService.Resolve(model.IconExpression);
    }
}
