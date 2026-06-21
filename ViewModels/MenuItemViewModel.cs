using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public ObservableCollection<MenuItemViewModel> Children { get; }

    public string DisplayName => Model.DisplayName;
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
    public bool IsSystemVerb => Model.IsSystemVerb;
    public bool HasChildren => Children.Count > 0;
    public bool IsTop => string.Equals(Model.Position, "Top", StringComparison.OrdinalIgnoreCase);
    public bool IsBottom => string.Equals(Model.Position, "Bottom", StringComparison.OrdinalIgnoreCase);

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
            return idx < 0 ? string.Empty : path.Substring(idx + 1);
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
            if (IsExtended) parts.Add("Shift");
            if (IsProgrammaticOnly) parts.Add("仅程序");
            if (IsLegacyDisabled) parts.Add("禁用");
            if (IsSystemVerb) parts.Add("系统");
            if (IsTop) parts.Add("置顶");
            if (IsBottom) parts.Add("置底");
            return string.Join(" · ", parts);
        }
    }

    [ObservableProperty]
    private BitmapSource? _icon;

    public MenuItemViewModel(ContextMenuItem model, IconService iconService)
    {
        Model = model;
        _iconService = iconService;
        Children = new ObservableCollection<MenuItemViewModel>(
            model.Children.Select(c => new MenuItemViewModel(c, iconService)));
        Icon = _iconService.Resolve(model.IconExpression);
    }
}
