using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.Views.Dialogs;

namespace RCMenuManager.ViewModels;

public partial class Win11DialogViewModel : ObservableObject
{
    private readonly IWin11MenuService _svc;
    private readonly WinVersionService _ver;

    public ObservableCollection<Win11BlockItem> Blocks { get; } = new();

    [ObservableProperty] private bool _isWindows11;
    [ObservableProperty] private bool _isNewMenuEnabled;
    [ObservableProperty] private Win11BlockItem? _selectedBlock;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    /// <summary>True while the toggle is being synced from the service, not clicked.</summary>
    private bool _suppressToggle;

    /// <summary>
    /// Set when the user actually changed the menu style. The caller uses it to
    /// decide whether the cached shell menu and the list are now stale.
    /// </summary>
    public bool ToggleChanged { get; private set; }

    public bool HasSelection => SelectedBlock is not null;

    public Win11DialogViewModel(IWin11MenuService svc, WinVersionService ver)
    {
        _svc = svc;
        _ver = ver;
        IsWindows11 = ver.IsWindows11;
        if (IsWindows11) Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (!IsWindows11) return;
        try
        {
            _suppressToggle = true;
            try { IsNewMenuEnabled = _svc.IsNewMenuEnabled; }
            finally { _suppressToggle = false; }
            Blocks.Clear();
            foreach (var b in _svc.GetBlockList().OrderBy(b => b.VerbName))
                Blocks.Add(b);
            StatusText = $"共 {Blocks.Count} 项 Block";
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (Exception ex)
        {
            StatusText = "读取失败：" + ex.Message;
        }
    }

    partial void OnIsNewMenuEnabledChanged(bool value)
    {
        if (!IsWindows11) return;
        // Reflecting the state back into the toggle during construction / Refresh must
        // not be mistaken for a user decision.
        if (_suppressToggle) return;

        if (!ConfirmSwitch(value))
        {
            _suppressToggle = true;
            try { IsNewMenuEnabled = _svc.IsNewMenuEnabled; }
            finally { _suppressToggle = false; }
            return;
        }

        try
        {
            _svc.SetNewMenuEnabled(value);
            ToggleChanged = true;
            StatusText = value
                ? "已切换到 Win11 新菜单（需重启资源管理器）"
                : "已切换到经典菜单（需重启资源管理器）";
        }
        catch (Exception ex)
        {
            StatusText = "切换失败：" + ex.Message;
            _suppressToggle = true;
            try { IsNewMenuEnabled = _svc.IsNewMenuEnabled; }
            finally { _suppressToggle = false; }
        }
    }

    /// <summary>
    /// Switching menus restarts Explorer's whole menu pipeline for every user of this
    /// account, and the registry trick behind it is undocumented, so it is confirmed
    /// once and labelled experimental instead of being applied on a stray click.
    /// </summary>
    private static bool ConfirmSwitch(bool toNewMenu)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w is Win11Dialog)
                    ?? Application.Current?.MainWindow;
        var body = toNewMenu
            ? "将删除经典菜单的 CLSID 覆盖键，恢复 Win11 新菜单。\n\n"
              + "该开关依赖未公开的注册表行为，系统更新后可能失效。切换前会自动备份，可在「备份」里还原。"
            : "将写入经典菜单的 CLSID 覆盖键，让右键菜单回到 Win10 样式。\n\n"
              + "该开关依赖未公开的注册表行为，系统更新后可能失效。切换前会自动备份，可在「备份」里还原。";
        return ConfirmDialog.Ask(owner, "切换右键菜单样式", body,
            confirmText: "切换", cancelText: "取消", dangerous: false);
    }

    partial void OnSelectedBlockChanged(Win11BlockItem? value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    private void RemoveBlock(Win11BlockItem? item)
    {
        if (item is null) return;
        try
        {
            _svc.RemoveFromBlock(item.VerbName);
            StatusText = "已移除 " + item.VerbName;
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "移除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (!IsWindows11) return;
        var ok = ConfirmDialog.Show(
            "重启资源管理器",
            "将结束所有 explorer.exe 进程后重新拉起。进行中的复制 / 移动窗口会丢失进度，确认继续？",
            confirmText: "重启", isDestructive: true);
        if (!ok) return;
        IsBusy = true;
        StatusText = "正在重启资源管理器 ...";
        try
        {
            await Task.Run(() => _svc.RestartExplorer());
            StatusText = "已重启资源管理器";
        }
        catch (Exception ex)
        {
            StatusText = "重启失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
