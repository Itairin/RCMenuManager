using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

    public WinVersionService VersionInfo => _ver;

    public ObservableCollection<ScopeOption> Scopes { get; } = new();
    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();
    public EditPanelViewModel EditPanel { get; } = new();
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

    public bool IsAdministrator { get; }

    /// <summary>Set by App.xaml.cs from --scope= argument; consumed once during startup.</summary>
    public string? PendingScopeId { get; set; }

    public MainViewModel(
        RegistryService registry, MenuParserService parser, IconService icons,
        RegistryWriteService writer, IBackupService backup, IOperationLog log,
        IWin11MenuService win11, WinVersionService ver) {
        _registry = registry;
        _parser = parser;
        _icons = icons;
        _writer = writer;
        _backup = backup;
        _log = log;
        _win11 = win11;
        _ver = ver;

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

    [RelayCommand]
    private async Task LoadCustomExtensionAsync()
    {
        var ext = (CustomExtensionInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(ext)) return;
        if (!ext.StartsWith('.')) ext = "." + ext;
        var progId = _registry.ResolveProgId(ext);
        var scope = MenuScope.ForExtension(ext, progId);
        var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
        var option = new ScopeOption(label, scope);
        TrimCustomOptions();
        Scopes.Add(option);
        SelectedScope = option;
        await Task.CompletedTask;
    }

    private void TrimCustomOptions()
    {
        for (int i = Scopes.Count - 1; i >= 0; i--)
            if (Scopes[i].Scope.Type == ScopeType.FileExtension)
                Scopes.RemoveAt(i);
    }

    public async Task LoadAsync(MenuScope scope)
    {
        IsLoading = true;
        StatusText = $"正在加载 {scope.DisplayName} ...";
        MenuItems.Clear();
        try
        {
            var items = await Task.Run(() => _parser.GetMenuItems(scope));
            foreach (var item in items)
                MenuItems.Add(new MenuItemViewModel(item, _icons));
            StatusText = $"{scope.DisplayName} · 共 {MenuItems.Count} 项" + (IsAdministrator ? "" : " · 当前未提权 (asInvoker)");
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}


