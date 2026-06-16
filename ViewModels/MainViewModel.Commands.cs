using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Helpers;
using RCMenuManager.Views.Dialogs;

namespace RCMenuManager.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void Edit(MenuItemViewModel? item)
    {
        if (item is null) return;
        EditPanel.BeginEdit(item);
    }

    [RelayCommand]
    private void CancelEdit() => EditPanel.CancelEdit();

    [RelayCommand]
    private async Task SaveEditAsync(MenuItemViewModel? item)
    {
        if (item is null) return;
        if (item.IsSystemVerb && !ConfirmSystemVerb(item.VerbName)) return;
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey)) return;
        var draft = EditPanel.Snapshot(item);
        try
        {
            EditPanel.ErrorMessage = null;
            _writer.UpdateDisplayName(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft.DisplayName);
            if (!item.Model.IsParentOnly)
                _writer.UpdateCommand(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft.Command);
            _writer.UpdateIcon(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft.IconExpression);
            _writer.SetExtended(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft.IsExtended);
            _writer.SetPosition(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft.Position);
            EditPanel.CancelEdit();
            StatusText = $"已保存 {item.VerbName}";
            await RefreshAsync();
        }
        catch (RegistryConflictException ex)
        {
            EditPanel.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            EditPanel.ErrorMessage = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(MenuItemViewModel? item)
    {
        if (item is null) return;
        if (item.IsSystemVerb && !ConfirmSystemVerb(item.VerbName)) return;
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey)) return;
        try
        {
            EditPanel.ErrorMessage = null;
            if (item.IsProgrammaticOnly)
                _writer.Enable(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName);
            else
                _writer.Disable(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName);
            StatusText = $"{item.VerbName} 状态已切换";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            EditPanel.ErrorMessage = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(MenuItemViewModel? item)
    {
        if (item is null) return;
        var ok = ConfirmDialog.Show(
            "删除菜单项",
            $"将删除 {item.VerbName}。删除前会自动备份为 .reg 文件，确认继续？",
            confirmText: "删除", isDestructive: true);
        if (!ok) return;
        if (item.IsSystemVerb && !ConfirmSystemVerb(item.VerbName)) return;
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey)) return;
        try
        {
            EditPanel.ErrorMessage = null;
            _writer.Delete(item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName);
            StatusText = $"已删除 {item.VerbName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            EditPanel.ErrorMessage = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddRootAsync()
    {
        if (SelectedScope is null) return;
        var dlg = new AddVerbDialog(_icons, "新增菜单项") { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) return;
        var draft = dlg.Result;
        var hive = RegistryHive.CurrentUser;
        var parentSubKey = SelectedScope.Scope.HkcuShellSubKey;
        if (!await EnsureAdministratorAsync(hive, parentSubKey)) return;
        try
        {
            EditPanel.ErrorMessage = null;
            _writer.CreateRootVerb(hive, parentSubKey, ScopeIdOrEmpty(), draft);
            StatusText = $"已新增 {draft.VerbName}";
            await RefreshAsync();
        }
        catch (RegistryConflictException ex)
        {
            EditPanel.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            EditPanel.ErrorMessage = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddChildAsync(MenuItemViewModel? parent)
    {
        if (parent is null) return;
        var dlg = new AddVerbDialog(_icons, $"在 {parent.VerbName} 下新增子项", allowParentOnly: false) { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) return;
        var draft = dlg.Result;
        if (!await EnsureAdministratorAsync(parent.WriteHive, parent.SubKey)) return;
        try
        {
            EditPanel.ErrorMessage = null;
            _writer.CreateChildVerb(parent.WriteHive, parent.SubKey, ScopeIdOrEmpty(), draft);
            StatusText = $"已新增 {draft.VerbName}";
            await RefreshAsync();
        }
        catch (RegistryConflictException ex)
        {
            EditPanel.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            EditPanel.ErrorMessage = $"操作失败：{ex.Message}";
        }
    }

    private string ScopeIdOrEmpty() => SelectedScope?.Scope.ScopeId ?? string.Empty;

    private static bool ConfirmSystemVerb(string verbName)
        => ConfirmDialog.Show(
            "系统关键项",
            $"该项 ({verbName}) 是系统关键 verb，修改可能导致 Explorer 行为异常。确认继续？",
            confirmText: "继续", isDestructive: true);

    /// <summary>
    /// Returns true if the current process is allowed to write to (hive, subKey).
    /// When elevation is required, asks the user, then relaunches with admin
    /// privileges and shuts the current instance down. The caller should
    /// abort the current operation when this returns false.
    /// </summary>
    private async Task<bool> EnsureAdministratorAsync(RegistryHive hive, string subKey)
    {
        if (_writer.CanWrite(hive)) return true;
        var ok = ConfirmDialog.Show(
            "需要管理员权限",
            $"该操作需要写入 {hive}\\{subKey}，当前非管理员。是否以管理员身份重启 RCMenuManager？",
            confirmText: "重启", isDestructive: false);
        if (!ok) return false;
        var args = $"--scope={ScopeIdOrEmpty()}";
        if (UacHelper.RelaunchAsAdmin(args))
            Application.Current.Shutdown();
        await Task.CompletedTask;
        return false;
    }
}
