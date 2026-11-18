using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Windows.Data;
using RCMenuManager.Helpers;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.Views.Dialogs;

namespace RCMenuManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RegistryService _registry;
    private readonly MenuParserService _parser;
    private readonly IconService _icons;
    private readonly RegistryWriteService _writer;
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;
    private readonly IWin11MenuService _win11;
    private readonly WinVersionService _ver;
    private readonly IFileTypeService _fileTypes;
    private readonly IPresetService _presets;
    private readonly IShellContextMenuReader _shellMenu;
    private readonly ISettingsService _settings;
    private readonly PendingActionStore _pendingActions;
    private readonly MenuItemOriginService _origins;

    /// <summary>True while the constructor applies persisted preferences, so restoring
    /// them does not immediately write the same values back to disk.</summary>
    private bool _restoring;

    public WinVersionService VersionInfo => _ver;

    public ObservableCollection<ScopeOption> Scopes { get; } = new();
    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();
    public EditPanelViewModel EditPanel { get; } = new();
    /// <summary>Read-only shell extensions (shellex\ContextMenuHandlers) for
    /// the current scope. Shown in the preview popup so the user sees the
    /// same options Explorer would, but they can't be edited from this app.</summary>
    public IReadOnlyList<ShellExtensionInfo> ShellExtensions { get; private set; } = Array.Empty<ShellExtensionInfo>();
    /// <summary>
    /// List-pane view; the search box filters this one. It has to be a view of its
    /// own — two different filters cannot share the collection's default view.
    /// </summary>
    public ICollectionView FilteredItems { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ScopeOption? _selectedScope;

    /// <summary>
    /// The file / folder the user dropped, or null when browsing a class scope.
    /// It only changes where the menu is <em>read</em> from; writes still target the
    /// scope's class key.
    /// </summary>
    [ObservableProperty]
    private DropTarget? _dropTarget;

    public bool HasDropTarget => DropTarget is not null;

    [ObservableProperty]
    private MenuItemViewModel? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    partial void OnSelectedExtensionChanged(ShellExtensionInfo? value)
        => OnPropertyChanged(nameof(HasSelectedExtension));

    [ObservableProperty]
    private bool _showExtended;

    partial void OnShowExtendedChanged(bool value)
    {
        // The preview composes from ShowExtended directly; there is no filtered view
        // to refresh, and leaving one on the default collection view would silently
        // filter any future binding to MenuItems.
        if (_restoring) return;
        _settings.Current.ShowExtended = value;
        _settings.Save();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredItems.Refresh();
        OnPropertyChanged(nameof(ListSummary));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>Badge above the list: total, or filtered-vs-total while searching.</summary>
    public string ListSummary => string.IsNullOrWhiteSpace(SearchText)
        ? $"共 {MenuItems.Count} 项"
        : $"筛选 {FilteredItems.Cast<object>().Count()} / 共 {MenuItems.Count} 项";

    partial void OnSelectedItemChanged(MenuItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
        if (value is not null) SelectedExtension = null;
        if (EditPanel.IsEditing) EditPanel.CancelEdit();

        // Drive the TreeView from the item flag: selecting by instance alone cannot
        // survive a rebuild, and a deleted item would stay "selected" forever.
        // The previous row is cleared explicitly — leaving two rows flagged selected
        // meant clicking the stale one produced no SelectedItemChanged, so it looked
        // selected but the details pane kept showing the other item.
        if (_lastSelected is not null && !ReferenceEquals(_lastSelected, value))
            _lastSelected.IsSelected = false;
        if (value is not null && !value.IsSelected) value.IsSelected = true;
        _lastSelected = value;
    }

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _customExtensionInput = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDragOver;

    /// <summary>Surfaced in the error banner. Kept separate from
    /// <see cref="EditPanelViewModel.ErrorMessage"/> because add / delete /
    /// preset-apply failures happen outside edit mode, where that panel is hidden.</summary>
    [ObservableProperty]
    private string? _errorBanner;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private MenuItemViewModel? _lastSelected;

    /// <summary>Reads the menu Windows builds for the current scope; the preview
    /// uses it so it matches Explorer exactly instead of an approximation.</summary>
    public IShellContextMenuReader ShellMenu => _shellMenu;

    public bool IsAdministrator { get; }

    /// <summary>Set by App.xaml.cs from --scope= argument; consumed once during startup.</summary>
    public string? PendingScopeId { get; set; }

    /// <summary>
    /// Set from <c>--resume</c>: this process is the elevated relaunch of one that had
    /// to restart mid-operation, so the recorded action should be replayed.
    /// </summary>
    public bool ResumePendingAction { get; set; }

    /// <summary>Persists the action that could not run yet because elevation was refused.</summary>
    private void RememberForResume(PendingAction action) => _pendingActions.Save(action);

    /// <summary>
    /// Replays the operation that triggered the elevation relaunch. Called once at
    /// startup when <c>--resume</c> is present; the record is cleared either way so a
    /// failed replay cannot loop across restarts.
    /// </summary>
    public async Task RunPendingActionAsync()
    {
        var action = _pendingActions.Peek();
        _pendingActions.Clear();
        if (action is null)
        {
            StatusText = "没有可继续的操作（可能已超时或已完成）";
            return;
        }

        if (!_writer.CanWrite(action.Hive))
        {
            ReportError("仍没有管理员权限，未执行上次的操作。请重新发起。");
            return;
        }

        try
        {
            ClearError();
            await Task.Run(() => ApplyPending(action));
            StatusText = $"已继续上次的操作：{action.Kind} {action.VerbName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ReportError($"继续上次操作失败：{ex.Message}");
        }
    }

    private void ApplyPending(PendingAction action)
    {
        var scopeId = string.IsNullOrEmpty(action.ScopeId) ? ScopeIdOrEmpty() : action.ScopeId;
        switch (action.Kind)
        {
            case PendingActionKinds.SaveEdit when action.Draft is not null:
                _writer.SaveEdits(action.Hive, action.SubKey, scopeId, action.VerbName,
                    action.Draft, action.IncludeCommand);
                break;

            case PendingActionKinds.Toggle:
                if (action.Enable)
                    _writer.Enable(action.Hive, action.SubKey, scopeId, action.VerbName);
                else
                    _writer.Disable(action.Hive, action.SubKey, scopeId, action.VerbName);
                break;

            case PendingActionKinds.Delete:
                _writer.Delete(action.Hive, action.SubKey, scopeId, action.VerbName);
                break;

            case PendingActionKinds.AddRoot when action.Draft is not null:
                _writer.CreateRootVerb(action.Hive, action.SubKey, scopeId, action.Draft);
                break;

            case PendingActionKinds.AddChild when action.Draft is not null:
                _writer.CreateChildVerb(action.Hive, action.SubKey, scopeId, action.Draft);
                break;

            case PendingActionKinds.Restore when !string.IsNullOrEmpty(action.BackupPath):
                // Same audited path as an interactive restore: an elevated replay must
                // not be the one operation without a snapshot and a log line.
                _writer.RestoreFromBackup(action.Hive, action.SubKey, scopeId,
                    action.VerbName, action.BackupPath!);
                break;

            default:
                throw new System.InvalidOperationException($"无法继续的操作类型：{action.Kind}");
        }
    }

    public MainViewModel(
        RegistryService registry, MenuParserService parser, IconService icons,
        RegistryWriteService writer, IBackupService backup, IOperationLog log,
        IWin11MenuService win11, WinVersionService ver,
        IFileTypeService fileTypes, IPresetService presets,
        IShellContextMenuReader shellMenu, ISettingsService settings,
        PendingActionStore pendingActions, MenuItemOriginService origins) {
        _settings = settings;
        _pendingActions = pendingActions;
        _origins = origins;
        _registry = registry;
        _parser = parser;
        _icons = icons;
        _writer = writer;
        _backup = backup;
        _log = log;
        _win11 = win11;
        _ver = ver;
        _fileTypes = fileTypes;
        _presets = presets;
        _shellMenu = shellMenu;

        FilteredItems = new ListCollectionView(MenuItems)
        {
            Filter = obj => obj is MenuItemViewModel m && MatchesSearch(m),
        };

        Scopes.Add(new ScopeOption("文件 (HKCR\\*\\shell)", MenuScope.AllFiles));
        Scopes.Add(new ScopeOption("文件夹 (HKCR\\Directory\\shell)", MenuScope.Folder));
        Scopes.Add(new ScopeOption("文件夹背景 (HKCR\\Directory\\Background\\shell)", MenuScope.FolderBackground));
        Scopes.Add(new ScopeOption("驱动器 (HKCR\\Drive\\shell)", MenuScope.Drive));
        Scopes.Add(new ScopeOption("桌面 (HKCR\\DesktopBackground\\Shell)", MenuScope.Desktop));
        Scopes.Add(new ScopeOption("文件与文件夹 (AllFilesystemObjects)", MenuScope.AllFilesystemObjects));

        IsAdministrator = UacHelper.IsAdministrator();

        _restoring = true;
        try
        {
            ShowExtended = _settings.Current.ShowExtended;
            // --scope= is applied by App.OnStartup (PendingScopeId is set after this
            // constructor runs), so only the persisted scope is resolved here.
            SelectedScope = ResolvePersistedScope() ?? Scopes[0];
        }
        finally
        {
            _restoring = false;
        }
    }

    private ScopeOption? ResolvePendingScope()
    {
        if (string.IsNullOrEmpty(PendingScopeId)) return null;
        var scope = RehydrateScope(PendingScopeId);
        foreach (var s in Scopes)
            if (s.Scope.Equals(scope))
                return s;
        if (scope.Type == ScopeType.FileExtension)
        {
            var opt = new ScopeOption(scope.DisplayName, scope);
            Scopes.Add(opt);
            return opt;
        }
        return null;
    }

    partial void OnDropTargetChanged(DropTarget? value)
    {
        OnPropertyChanged(nameof(HasDropTarget));
        OnPropertyChanged(nameof(CanSwitchFolderMenu));
        if (SelectedScope is not null) _ = LoadAsync(SelectedScope.Scope);
    }

    partial void OnSelectedScopeChanged(ScopeOption? value)
    {
        if (value is null) return;
        if (!_restoring)
        {
            _settings.Current.LastScopeId = value.Scope.ScopeId;
            _settings.Save();
        }
        _ = LoadAsync(value.Scope);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedScope is null) return;
        await LoadAsync(SelectedScope.Scope);
    }

    /// <summary>
    /// Selection coming from the preview popup. Handles both kinds of identifiable
    /// row: an editable verb selects it in the list; a shell-extension handler has
    /// no verb to select, so it is surfaced in the details pane on its own instead
    /// of the click silently doing nothing.
    /// </summary>
    [RelayCommand]
    private void SelectPreviewNode(PreviewNode? node)
    {
        if (node is null) return;

        if (node.Item is not null)
        {
            SelectedExtension = null;
            SelectedItem = node.Item;
            return;
        }

        if (node.Extension is not null)
        {
            SelectedItem = null;
            SelectedExtension = node.Extension;
        }
    }

    /// <summary>
    /// Shell-extension row picked from the preview. Mutually exclusive with
    /// <see cref="SelectedItem"/>: a row cannot be both an editable verb and an
    /// opaque DLL handler, and the details panel has different things to say.
    /// </summary>
    [ObservableProperty]
    private ShellExtensionInfo? _selectedExtension;

    public bool HasSelectedExtension => SelectedExtension is not null;

    /// <summary>
    /// Rebuilds a scope from its stable id, restoring the ProgID that the id does not
    /// carry. Without this, relaunching elevated on <c>FileExt:.txt</c> silently lost
    /// the txfile-based half of the menu.
    /// </summary>
    private MenuScope RehydrateScope(string scopeId)
    {
        var probe = MenuScope.FromScopeId(scopeId);
        if (probe.Type != ScopeType.FileExtension || string.IsNullOrEmpty(probe.Extension))
            return probe;
        var progId = _registry.ResolveProgId(probe.Extension!);
        return MenuScope.FromScopeId(scopeId, progId);
    }

    /// <summary>ProgID lookup used when a scope has to be rebuilt from its id.</summary>
    public string? ResolveProgIdFor(string extension) => _registry.ResolveProgId(extension);

    /// <summary>Reopens the scope the user had selected when they last quit.</summary>
    private ScopeOption? ResolvePersistedScope()
    {
        var id = _settings.Current.LastScopeId;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var scope = RehydrateScope(id);
        foreach (var s in Scopes)
            if (s.Scope.Equals(scope))
                return s;
        if (scope.Type == ScopeType.FileExtension)
        {
            var opt = new ScopeOption(scope.DisplayName, scope);
            Scopes.Add(opt);
            return opt;
        }
        return null;
    }

    private void TrimCustomOptions()
    {
        for (int i = Scopes.Count - 1; i >= 0; i--)
            if (Scopes[i].Scope.Type == ScopeType.FileExtension)
                Scopes.RemoveAt(i);
    }

    public async Task LoadAsync(MenuScope scope)
    {
        // Switching scopes quickly used to race: two loads ran concurrently and the
        // slower one could populate the list while the header showed the other scope.
        // Every load now takes a version ticket and cancels its predecessor; results
        // from a stale ticket are discarded instead of applied.
        var version = Interlocked.Increment(ref _loadVersion);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        StatusText = $"正在加载 {scope.DisplayName} ...";
        // Every write ends in a reload, and the reload rebuilds the whole list. Keep
        // the selected item's registry path so the details pane does not fall back to
        // its empty state after each save / toggle / delete.
        var previousSelectionPath = SelectedItem?.RegistryPath;
        // The handler may no longer exist after an app was uninstalled; keeping the
        // snapshot would describe a DLL that is gone.
        SelectedExtension = null;
        MenuItems.Clear();
        ShellExtensions = Array.Empty<ShellExtensionInfo>();

        // The shell menu is cached per scope; a (re)load means the registry may
        // have changed under it, so drop it and start rebuilding immediately —
        // in parallel with the registry parse — so the first right-click in the
        // Preview tab usually hits a warm cache (a cold read can take ~2s when
        // image/video shell extensions have to be loaded).
        //
        // Both variants are warmed: the normal menu and the Shift menu are
        // cached separately, and reading only the normal one meant the first
        // right-click after ticking "显示 Shift 项" blocked the UI thread for a
        // full cold read. Doing it here keeps that cost off the UI thread.
        _shellMenu.Invalidate();
        var prewarmTarget = DropTarget?.Path;
        _ = Task.Run(() =>
        {
            _shellMenu.TryRead(scope, false, prewarmTarget);
            _shellMenu.TryRead(scope, true, prewarmTarget);
        });
        try
        {
            var token = cts.Token;
            var (items, extensions) = await Task.Run(() =>
            {
                var parsed = _parser.GetMenuItems(scope);
                token.ThrowIfCancellationRequested();
                return (parsed, _parser.GetShellExtensionHandlers(scope));
            }, token);

            if (version != Volatile.Read(ref _loadVersion))
                return;

            // The shell's own menu is the backbone; registry verbs are matched into it.
            // A registry-only list cannot contain UltraISO-style handler entries or
            // packaged-app commands, and its order would not match Explorer either.
            var targetPath = DropTarget?.Path;
            var ordered = _parser.MergeWithShellMenu(
                items,
                _shellMenu.TryRead(scope, extendedVerbs: false, targetPath),
                _shellMenu.TryRead(scope, extendedVerbs: true, targetPath));

            foreach (var item in ordered)
            {
                var vm = new MenuItemViewModel(item, _icons);
                AttachOrigins(vm, scope.ScopeId);
                MenuItems.Add(vm);
            }
            ShellExtensions = extensions;

            // Restored by registry path. A path that no longer exists means the verb
            // was just deleted, so the selection is cleared instead of resurrected.
            SelectedItem = previousSelectionPath is null
                ? null
                : FindByPath(MenuItems, previousSelectionPath);
            OnPropertyChanged(nameof(ListSummary));

            StatusText = $"{scope.DisplayName} · 共 {MenuItems.Count} 项" + (IsAdministrator ? "" : " · 当前未提权 (asInvoker)");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load; the newer call owns the UI state.
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _loadVersion))
                StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion))
                IsLoading = false;
        }
    }

    /// <summary>
    /// Labels every item (and its children) with where it came from. Done once per
    /// load, off the parsed model, so the tree badge, the details pane and the
    /// preview all agree.
    /// </summary>
    private void AttachOrigins(MenuItemViewModel item, string scopeId)
    {
        item.Origin = _origins.Analyse(item.Model, scopeId);
        foreach (var child in item.Children) AttachOrigins(child, scopeId);
    }

    private static MenuItemViewModel? FindByPath(IEnumerable<MenuItemViewModel> items, string path)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.RegistryPath, path, StringComparison.OrdinalIgnoreCase))
                return item;
            var nested = FindByPath(item.Children, path);
            if (nested is not null) return nested;
        }
        return null;
    }

    internal bool MatchesSearch(MenuItemViewModel item) => Matches(item, SearchText);

    /// <summary>
    /// Search predicate for the list pane: matches display name, verb name, command
    /// and registry path. A parent stays visible when any descendant matches, so a
    /// hit is never hidden behind a branch the user has to expand first.
    /// </summary>
    internal static bool Matches(MenuItemViewModel item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        if (Contains(item.DisplayName, q) || Contains(item.VerbName, q)
            || Contains(item.Command, q) || Contains(item.ExpandedCommand, q)
            || Contains(item.RegistryPath, q))
            return true;
        foreach (var child in item.Children)
            if (Matches(child, q)) return true;
        return false;
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows a failure that happened outside the edit panel (add / delete /
    /// restore / preset apply) and keeps the same text in the panel for context.</summary>
    public void ReportError(string message)
    {
        ErrorBanner = message;
        EditPanel.ErrorMessage = message;
    }

    public void ClearError()
    {
        ErrorBanner = null;
        EditPanel.ErrorMessage = null;
    }

    [RelayCommand]
    private void DismissError() => ErrorBanner = null;
}


