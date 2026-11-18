using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Helpers;
using RCMenuManager.Services;
using RCMenuManager.Views.Dialogs;

namespace RCMenuManager.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void Edit(MenuItemViewModel? item)
    {
        if (item is null || item.IsReadOnly) return;
        EditPanel.BeginEdit(item);
    }

    [RelayCommand]
    private void CancelEdit() => EditPanel.CancelEdit();

    [RelayCommand]
    private async Task SaveEditAsync(MenuItemViewModel? item)
    {
        if (item is null || item.IsReadOnly) return;
        if (item.IsSystemVerb && !ConfirmSystemVerb(item.VerbName)) return;
        var draft = EditPanel.Snapshot(item);
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey, new PendingAction
            {
                Kind = PendingActionKinds.SaveEdit,
                VerbName = item.VerbName,
                Draft = draft,
                IncludeCommand = item.CanEditCommand,
            })) return;
        try
        {
            ClearError();
            // One transaction: a single .reg snapshot, a single shell notification
            // and a single audit line for the whole draft.
            await Task.Run(() => _writer.SaveEdits(
                item.WriteHive, item.SubKey, ScopeIdOrEmpty(), item.VerbName, draft, item.CanEditCommand));
            EditPanel.CancelEdit();
            StatusText = $"已保存 {item.VerbName}";
            await RefreshAsync();
        }
        catch (RegistryConflictException ex)
        {
            ReportError(ex.Message);
        }
        catch (Exception ex)
        {
            ReportError($"操作失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(MenuItemViewModel? item)
    {
        if (item is null || item.IsReadOnly) return;
        if (item.IsSystemVerb && !ConfirmSystemVerb(item.VerbName)) return;
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey, new PendingAction
            {
                Kind = PendingActionKinds.Toggle,
                VerbName = item.VerbName,
                Enable = item.IsDisabled,
            })) return;
        try
        {
            ClearError();
            // Toggle direction is driven by IsDisabled so a verb that was
            // disabled via LegacyDisable (no ProgrammaticAccessOnly) still
            // gets fully re-enabled on the next click.
            var target = item;
            if (target.IsDisabled)
                await Task.Run(() => _writer.Enable(target.WriteHive, target.SubKey, ScopeIdOrEmpty(), target.VerbName));
            else
                await Task.Run(() => _writer.Disable(target.WriteHive, target.SubKey, ScopeIdOrEmpty(), target.VerbName));
            StatusText = $"{item.VerbName} 状态已切换";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ReportError($"操作失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(MenuItemViewModel? item)
    {
        if (item is null || item.IsReadOnly) return;
        // One dialog instead of a chain of three or four. A stack of near-identical
        // prompts trains people to click through them, which is exactly how a system
        // verb gets deleted by accident; the risks are listed once, the decision is
        // taken once.
        var risks = new List<string>(3);
        if (item.IsSystemVerb)
            risks.Add("这是系统关键 verb，修改可能导致资源管理器行为异常");
        if (item.IsSharedDefinition)
            risks.Add("该项来自系统命令库（CommandStore），可能被多个菜单引用，删除会同时影响它们");
        if (item.HasDelegateExecute)
            risks.Add("该项由系统 COM 处理器执行（DelegateExecute）");

        var body = $"将删除 {item.VerbName}。删除前会自动备份为 .reg 文件，可从「备份」还原。";
        if (risks.Count > 0)
            body += Environment.NewLine + Environment.NewLine + "注意："
                    + Environment.NewLine + string.Join(Environment.NewLine, risks.Select(r => "· " + r));

        if (!ConfirmDialog.Show("删除菜单项", body, confirmText: "删除", isDestructive: true)) return;
        if (!await EnsureAdministratorAsync(item.WriteHive, item.SubKey, new PendingAction
            {
                Kind = PendingActionKinds.Delete,
                VerbName = item.VerbName,
            })) return;
        try
        {
            ClearError();
            var target = item;
            await Task.Run(() => _writer.Delete(target.WriteHive, target.SubKey, ScopeIdOrEmpty(), target.VerbName));
            StatusText = $"已删除 {item.VerbName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ReportError($"操作失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddRootAsync()
    {
        if (SelectedScope is null) return;
        var dlg = new AddVerbDialog(_icons, "新增菜单项")
        {
            Owner = Application.Current?.MainWindow,
            AllowMachineScope = true,
        };
        if (dlg.ShowDialog() != true) return;
        var draft = dlg.Result;
        // Machine scope is what the toolbar dropdown implies (HKCR/HKLM), so the
        // dialog now asks instead of silently writing to HKCU and leaving the user to
        // wonder why the entry only shows up for their own account.
        var hive = dlg.TargetHive;
        var parentSubKey = hive == RegistryHive.CurrentUser
            ? SelectedScope.Scope.HkcuShellSubKey
            : SelectedScope.Scope.ShellSubKey;
        if (!await EnsureAdministratorAsync(hive, parentSubKey, new PendingAction
            {
                Kind = PendingActionKinds.AddRoot,
                VerbName = draft.VerbName,
                Draft = draft,
            })) return;
        try
        {
            ClearError();
            await Task.Run(() => _writer.CreateRootVerb(hive, parentSubKey, ScopeIdOrEmpty(), draft));
            StatusText = $"已新增 {draft.VerbName}";
            await RefreshAsync();
        }
        catch (RegistryConflictException ex)
        {
            ReportError(ex.Message);
        }
        catch (Exception ex)
        {
            ReportError($"操作失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddChildAsync(MenuItemViewModel? parent)
    {
        if (parent is null) return;
        var dlg = new AddVerbDialog(_icons, $"在 {parent.VerbName} 下新增子项", allowParentOnly: false) { Owner = Application.Current?.MainWindow };
        if (dlg.ShowDialog() != true) return;
        var draft = dlg.Result;
        if (!await EnsureAdministratorAsync(parent.WriteHive, parent.SubKey, new PendingAction
            {
                Kind = PendingActionKinds.AddChild,
                VerbName = draft.VerbName,
                Draft = draft,
            })) return;
        try
        {
            ClearError();
            var target = parent;
            await Task.Run(() => _writer.CreateChildVerb(target.WriteHive, target.SubKey, ScopeIdOrEmpty(), draft));
            StatusText = $"已新增 {draft.VerbName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ReportError($"操作失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadCustomExtensionAsync()
    {
        var ext = (CustomExtensionInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(ext)) return;
        await SwitchToExtensionScopeAsync(ext);
    }

    [RelayCommand]
    private void ShowBackups()
    {
        var owner = Application.Current?.MainWindow;
        var vm = new BackupDialogViewModel(_backup, _log, BackupService.DefaultBackupDir())
        {
            OnRestoreRequested = HandleRestoreAsync,
        };
        var dlg = new BackupDialog { Owner = owner, DataContext = vm };
        dlg.ShowDialog();
    }

    private async Task<bool> HandleRestoreAsync(BackupRecord rec)
    {
        // Validation and confirmation come first: the old order closed the backup
        // list before checking anything, so a rejected or impossible restore dropped
        // the user back to the main window with no way to pick another file.
        if (rec.RegistryPath is null)
        {
            StatusText = "无法恢复：该备份文件里没有可识别的注册表路径";
            return false;
        }

        var ok = ConfirmDialog.Show(
            "恢复备份",
            "将导入备份文件 " + System.IO.Path.GetFileName(rec.FilePath)
            + "，覆盖当前注册表项：" + Environment.NewLine + rec.RegistryPath
            + Environment.NewLine + Environment.NewLine
            + "恢复前会自动把当前状态再备份一次（可在「备份」里回退这次恢复）。",
            confirmText: "恢复", isDestructive: true);
        if (!ok) return false;

        var (hive, subKey) = ParseRegistryPath(rec.RegistryPath);
        if (!await EnsureAdministratorAsync(hive, subKey, new PendingAction
            {
                Kind = PendingActionKinds.Restore,
                VerbName = rec.VerbName,
                BackupPath = rec.FilePath,
            })) return false;

        CloseBackupDialog();

        try
        {
            ClearError();
            var target = rec;
            var keyScope = ScopeIdOrEmpty();
            // One audited transaction: pre-restore snapshot, import, shell notify, log.
            await Task.Run(() => _writer.RestoreFromBackup(
                hive, subKey, keyScope, target.VerbName, target.FilePath));
            StatusText = "已恢复 " + rec.VerbName;
            await RefreshAsync();
            return true;
        }
        catch (System.Exception ex)
        {
            ReportError("恢复失败：" + ex.Message);
            StatusText = "恢复失败：" + ex.Message;
            return false;
        }
    }

    private static void CloseBackupDialog()
    {
        foreach (Window w in Application.Current!.Windows)
            if (w is BackupDialog) { w.Close(); break; }
    }

    private static (RegistryHive hive, string subKey) ParseRegistryPath(string registryPath)
    {
        var idx = registryPath.IndexOf('\\');
        if (idx < 0) return (RegistryHive.CurrentUser, registryPath);
        var hiveName = registryPath.Substring(0, idx);
        var subKey = registryPath.Substring(idx + 1);
        var hive = hiveName switch
        {
            "HKCU" => RegistryHive.CurrentUser,
            "HKLM" => RegistryHive.LocalMachine,
            "HKCR" => RegistryHive.ClassesRoot,
            "HKU" => RegistryHive.Users,
            _ => RegistryHive.CurrentUser,
        };
        return (hive, subKey);
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
    private async Task<bool> EnsureAdministratorAsync(RegistryHive hive, string subKey, PendingAction? action = null)
    {
        if (_writer.CanWrite(hive)) return true;
        var ok = ConfirmDialog.Show(
            "需要管理员权限",
            $"该操作需要写入 {hive}\\{subKey}，当前非管理员。是否以管理员身份重启 RCMenuManager？"
            + Environment.NewLine + Environment.NewLine
            + "重启后会自动继续刚才的操作，不需要重新填写。",
            confirmText: "重启并继续", isDestructive: false);
        if (!ok) return false;

        // Record the action before relaunching: the new process only carries a flag,
        // the details (draft, target key, backup path) live in the pending-action file.
        var resume = action is not null;
        if (resume)
        {
            action!.Hive = hive;
            action.SubKey = subKey;
            action.ScopeId = ScopeIdOrEmpty();
            RememberForResume(action);
        }

        var args = $"--scope={ScopeIdOrEmpty()}" + (resume ? " --resume" : string.Empty);
        if (UacHelper.RelaunchAsAdmin(args))
        {
            Application.Current.Shutdown();
        }
        else if (resume)
        {
            // Elevation was declined at the UAC prompt, so nothing will replay the
            // action; keeping the record would fire it on some unrelated later launch.
            _pendingActions.Clear();
        }

        await Task.CompletedTask;
        return false;
    }

    [RelayCommand]
    private async Task ShowWin11Async()
    {
        var owner = Application.Current?.MainWindow;
        var vm = new Win11DialogViewModel(_win11, _ver);
        var dlg = new Win11Dialog { Owner = owner, DataContext = vm };
        dlg.ShowDialog();

        if (vm.ToggleChanged)
        {
            // Switching the menu style changes what Explorer builds, so both the
            // cached native menu and the list have to be re-read.
            _shellMenu.Invalidate();
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ShowPresetsAsync()
    {
        var owner = Application.Current?.MainWindow;
        var vm = new PresetDialogViewModel(_presets);
        var dlg = new PresetDialog { Owner = owner, DataContext = vm };
        dlg.ShowDialog();

        // Presets write straight to the registry, so without a reload the list keeps
        // showing the state from before the dialog opened — the panel and the real
        // menu disagree until the user presses 刷新.
        if (vm.AppliedCount > 0 || vm.ErrorCount > 0)
        {
            // Newly created verbs must now read as "本工具"; the cached evidence would
            // still call them "应用注册" until the next launch.
            _origins.Invalidate();
            await RefreshAsync();
        }
    }

    public async Task OnFileDroppedAsync(string[] paths)
    {
        IsDragOver = false;
        if (paths is null || paths.Length == 0)
        {
            StatusText = "未识别拖入内容";
            return;
        }
        try
        {
            var first = paths[0];
            var info = _fileTypes.Identify(first);
            switch (info.Kind)
            {
                case DragDropKind.Drive:
                    SetDropTarget(first, DropTargetKind.Drive);
                    SwitchToBuiltInScope(MenuScope.Drive, $"已读取驱动器 {first} 的右键菜单");
                    break;
                case DragDropKind.Folder:
                    SetDropTarget(first, DropTargetKind.Folder);
                    SwitchToBuiltInScope(MenuScope.Folder, $"已读取文件夹 {first} 的右键菜单");
                    break;
                case DragDropKind.File:
                    SetDropTarget(first, DropTargetKind.File);
                    var ext = Path.GetExtension(first);
                    if (string.IsNullOrEmpty(ext))
                        SwitchToBuiltInScope(MenuScope.AllFiles, $"已读取 {Path.GetFileName(first)} 的右键菜单（无扩展名）");
                    else
                        await SwitchToExtensionScopeAsync(ext);
                    break;
                default:
                    StatusText = "不支持拖入的内容：" + first;
                    return;
            }

            // Settling the scope triggers a reload; make sure the banner and the status
            // line agree on what is being shown.
            StatusText += DropTarget is null ? string.Empty : $" · 目标 {DropTarget.KindText}";
        }
        catch (Exception ex)
        {
            StatusText = "切换失败：" + ex.Message;
        }
    }

    private void SetDropTarget(string path, DropTargetKind kind)
        => DropTarget = new DropTarget(path, kind);

    /// <summary>
    /// Drops the dropped-item context and goes back to browsing the class scope, so the
    /// list stops claiming it describes one particular object.
    /// </summary>
    /// <summary>
    /// A dropped folder has two different menus: the folder item itself, and the blank
    /// area inside it. Only one of them is what the user meant, so both are reachable
    /// instead of guessing.
    /// </summary>
    [RelayCommand]
    private void UseBackgroundMenu()
    {
        if (DropTarget is null || DropTarget.Kind != DropTargetKind.Folder) return;
        DropTarget = DropTarget with { Kind = DropTargetKind.FolderBackground };
        SwitchToBuiltInScope(MenuScope.FolderBackground, $"已读取 {DropTarget.Path} 空白处的右键菜单");
    }

    /// <summary>Back from the background menu to the folder item's own menu.</summary>
    [RelayCommand]
    private void UseFolderItemMenu()
    {
        if (DropTarget is null || DropTarget.Kind != DropTargetKind.FolderBackground) return;
        DropTarget = DropTarget with { Kind = DropTargetKind.Folder };
        SwitchToBuiltInScope(MenuScope.Folder, $"已读取文件夹 {DropTarget.Path} 自身的右键菜单");
    }

    public bool CanSwitchFolderMenu => DropTarget?.Kind is DropTargetKind.Folder or DropTargetKind.FolderBackground;

    [RelayCommand]
    private void ClearDropTarget()
    {
        DropTarget = null;
        StatusText = SelectedScope is null ? "就绪" : $"已回到通用作用域：{SelectedScope.Label}";
    }

    private async Task SwitchToExtensionScopeAsync(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return;
        if (!ext.StartsWith('.')) ext = "." + ext;
        var progId = _registry.ResolveProgId(ext);
        var scope = MenuScope.ForExtension(ext, progId);
        var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
        var option = new ScopeOption(label, scope);
        TrimCustomOptions();
        Scopes.Add(option);
        SelectedScope = option;
        StatusText = $"已切换到 {label}";
        await Task.CompletedTask;
    }

    private void SwitchToBuiltInScope(MenuScope scope, string statusMessage)
    {
        var existing = Scopes.FirstOrDefault(s => s.Scope.Equals(scope));
        if (existing is not null)
        {
            SelectedScope = existing;
        }
        else
        {
            var opt = new ScopeOption(scope.DisplayName, scope);
            Scopes.Add(opt);
            SelectedScope = opt;
        }
        StatusText = statusMessage;
    }
}
