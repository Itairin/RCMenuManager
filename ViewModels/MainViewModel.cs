using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RegistryService _registry;
    private readonly MenuParserService _parser;
    private readonly IconService _icons;

    public ObservableCollection<ScopeOption> Scopes { get; } = new();
    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();

    [ObservableProperty]
    private ScopeOption? _selectedScope;

    [ObservableProperty]
    private MenuItemViewModel? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    partial void OnSelectedItemChanged(MenuItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
    }

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _customExtensionInput = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsAdministrator { get; }

    public MainViewModel(RegistryService registry, MenuParserService parser, IconService icons)
    {
        _registry = registry;
        _parser = parser;
        _icons = icons;

        Scopes.Add(new ScopeOption("文件 (HKCR\\*\\shell)", MenuScope.AllFiles));
        Scopes.Add(new ScopeOption("文件夹 (HKCR\\Directory\\shell)", MenuScope.Folder));
        Scopes.Add(new ScopeOption("文件夹背景 (HKCR\\Directory\\Background\\shell)", MenuScope.FolderBackground));
        Scopes.Add(new ScopeOption("驱动器 (HKCR\\Drive\\shell)", MenuScope.Drive));
        Scopes.Add(new ScopeOption("桌面 (HKCR\\DesktopBackground\\Shell)", MenuScope.Desktop));
        Scopes.Add(new ScopeOption("文件与文件夹 (AllFilesystemObjects)", MenuScope.AllFilesystemObjects));

        IsAdministrator = Helpers.UacHelper.IsAdministrator();
        SelectedScope = Scopes[0];
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
    private async Task LoadCustomExtensionAsync()
    {
        var ext = (CustomExtensionInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(ext))
            return;
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        var progId = _registry.ResolveProgId(ext);
        var scope = MenuScope.ForExtension(ext, progId);
        var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
        var option = new ScopeOption(label, scope);
        // Insert after fixed scopes, replacing any prior custom row.
        TrimCustomOptions();
        Scopes.Add(option);
        SelectedScope = option;
        await Task.CompletedTask;
    }

    private void TrimCustomOptions()
    {
        for (int i = Scopes.Count - 1; i >= 0; i--)
        {
            if (Scopes[i].Scope.Type == ScopeType.FileExtension)
                Scopes.RemoveAt(i);
        }
    }

    public async Task LoadAsync(MenuScope scope)
    {
        IsLoading = true;
        StatusText = $"正在加载 {scope.DisplayName} ...";
        MenuItems.Clear();
        try
        {
            // Stay on the UI thread when reading the registry; HKCR/HKLM are fast and the
            // current process is a desktop app, so this avoids the synchronization dance
            // for a few hundred verbs at most.
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
