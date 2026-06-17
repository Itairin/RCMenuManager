using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public partial class BackupDialogViewModel : ObservableObject
{
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;

    public ObservableCollection<BackupRecord> Backups { get; } = new();
    public ObservableCollection<OperationLogEntryViewModel> Operations { get; } = new();
    public string BackupDir { get; }

    [ObservableProperty] private BackupRecord? _selectedBackup;
    [ObservableProperty] private OperationLogEntryViewModel? _selectedOperation;
    [ObservableProperty] private string _statusText = "正在加载...";

    public bool HasSelection => SelectedBackup is not null;
    public Func<BackupRecord, Task<bool>>? OnRestoreRequested { get; set; }

    public BackupDialogViewModel(IBackupService backup, IOperationLog log, string backupDir)
    {
        _backup = backup; _log = log; BackupDir = backupDir; Refresh();
    }

    public void Refresh()
    {
        var log = _log.ReadAll();
        var logByPath = log.Where(e => !string.IsNullOrEmpty(e.backupPath))
            .GroupBy(e => e.backupPath!).ToDictionary(g => g.Key, g => g.Last());

        Backups.Clear();
        foreach (var rec in _backup.List())
        {
            if (logByPath.TryGetValue(rec.FilePath, out var e))
                Backups.Add(rec with { Operation = e.op, RegistryPath = $"{e.hive}\\{e.subKey}", Success = e.success, Error = e.error });
            else Backups.Add(rec);
        }

        Operations.Clear();
        foreach (var e in log.AsEnumerable().Reverse())
            Operations.Add(new OperationLogEntryViewModel(e));

        StatusText = $"共 {Backups.Count} 条备份 / {Operations.Count} 条日志";
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private async Task RestoreAsync(BackupRecord? rec)
    {
        if (rec is null || OnRestoreRequested is null) return;
        var ok = await OnRestoreRequested(rec);
        if (ok) Refresh();
    }

    [RelayCommand]
    private void DeleteBackup(BackupRecord? rec)
    {
        if (rec is null) return;
        _backup.Delete(rec.FilePath);
        Refresh();
    }

    [RelayCommand]
    private void OpenBackupFolder()
        => Process.Start(new ProcessStartInfo { FileName = BackupDir, UseShellExecute = true });

    [RelayCommand]
    private void RevealInExplorer(BackupRecord? rec)
    {
        if (rec is null) return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{rec.FilePath}\"" });
    }

    partial void OnSelectedBackupChanged(BackupRecord? value) => OnPropertyChanged(nameof(HasSelection));
}
