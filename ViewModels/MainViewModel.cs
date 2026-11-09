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

    public WinVersionService VersionInfo => _ver;

    public ObservableCollection<ScopeOption> Scopes { get; } = new();
    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();
    public EditPanelViewModel EditPanel { get; } = new();
    /// <summary>Read-only shell extensions (shellex\ContextMenuHandlers) for
    /// the current scope. Shown in the preview popup so the user sees the
    /// same options Explorer would, but they can't be edited from this app.</summary>
    public IReadOnlyList<ShellExtensionInfo> ShellExtensions { get; private set; } = Array.Empty<ShellExtensionInfo>();
    /// <summary>Filtered view used by the Preview tab to hide Extended items by default.</summary>
    public ICollectionView PreviewView { get; }

    [ObservableProperty]
    private ScopeOption? _selectedScope;

    [ObservableProperty]
    private MenuItemViewModel? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    [ObservableProperty]
    private bool _showExtended;

    partial void OnShowExtendedChanged(bool value) => PreviewView.Refresh();

    partial void OnSelectedItemChanged(MenuItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
        if (EditPanel.IsEditing) EditPanel.CancelEdit();
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

    /// <summary>Reads the menu Windows builds for the current scope; the preview
    /// uses it so it matches Explorer exactly instead of an approximation.</summary>
    public IShellContextMenuReader ShellMenu => _shellMenu;

    public bool IsAdministrator { get; }

    /// <summary>Set by App.xaml.cs from --scope= argument; consumed once during startup.</summary>
    public string? PendingScopeId { get; set; }

    public MainViewModel(
        RegistryService registry, MenuParserService parser, IconService icons,
        RegistryWriteService writer, IBackupService backup, IOperationLog log,
        IWin11MenuService win11, WinVersionService ver,
        IFileTypeService fileTypes, IPresetService presets,
        IShellContextMenuReader shellMenu) {
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

        PreviewView = CollectionViewSource.GetDefaultView(MenuItems);
        PreviewView.Filter = obj => obj is MenuItemViewModel m && (_showExtended || !m.IsExtended);

        Scopes.Add(new ScopeOption("文件 (HKCR\\*\\shell)", MenuScope.AllFiles));
        Scopes.Add(new ScopeOption("文件夹 (HKCR\\Directory\\shell)", MenuScope.Folder));
        Scopes.Add(new ScopeOption("文件夹背景 (HKCR\\Directory\\Background\\shell)", MenuScope.FolderBackground));
        Scopes.Add(new ScopeOption("驱动器 (HKCR\\Drive\\shell)", MenuScope.Drive));
        Scopes.Add(new ScopeOption("桌面 (HKCR\\DesktopBackground\\Shell)", MenuScope.Desktop));
        Scopes.Add(new ScopeOption("文件与文件夹 (AllFilesystemObjects)", MenuScope.AllFilesystemObjects));

        IsAdministrator = UacHelper.IsAdministrator();
        SelectedScope = ResolvePendingScope() ?? Scopes[0];
    }

    private ScopeOption? ResolvePendingScope()
    {
        if (string.IsNullOrEmpty(PendingScopeId)) return null;
        var scope = MenuScope.FromScopeId(PendingScopeId);
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

    partial void OnSelectedScopeChanged(ScopeOption? value)
    {
        if (value is not null)
            _ = LoadAsync(value.Scope);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedScope is null) return;
        await LoadAsync(SelectedScope.Scope);
    }

    [RelayCommand]
    private void SelectPreviewItem(MenuItemViewModel? item)
    {
        if (item is not null) SelectedItem = item;
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
        MenuItems.Clear();
        ShellExtensions = Array.Empty<ShellExtensionInfo>();

        // The shell menu is cached per scope; a (re)load means the registry may
        // have changed under it, so drop it and start rebuilding immediately —
        // in parallel with the registry parse — so the first right-click in the
        // Preview tab usually hits a warm cache (a cold read can take ~2s when
        // image/video shell extensions have to be loaded).
        _shellMenu.Invalidate();
        _ = Task.Run(() => _shellMenu.TryRead(scope, false));
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

            foreach (var item in items)
                MenuItems.Add(new MenuItemViewModel(item, _icons));
            ShellExtensions = extensions;
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


