using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public sealed class PresetGroup
{
    public string Scope { get; }
    public string DisplayName { get; }
    public ObservableCollection<PresetItemViewModel> Items { get; } = new();
    public PresetGroup(string scope, string displayName) { Scope = scope; DisplayName = displayName; }
}

public partial class PresetDialogViewModel : ObservableObject
{
    private static readonly (string Scope, string DisplayName)[] GroupOrder =
    {
        ("AllFiles", "文件 (所有文件)"),
        ("Folder", "文件夹"),
        ("FolderBackground", "文件夹背景"),
        ("Drive", "驱动器"),
        ("Desktop", "桌面"),
    };

    private readonly IPresetService _service;

    public ObservableCollection<PresetGroup> Groups { get; } = new();
    public ObservableCollection<PresetItemViewModel> AllItems { get; } = new();

    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private int _alreadyPresentCount;

    public bool HasSelection => SelectedCount > 0;

    /// <summary>e.g. "共 31 项 · 已存在 9 项（已自动勾选）".</summary>
    public string SummaryText => AlreadyPresentCount > 0
        ? $"共 {AllItems.Count} 项 · 已存在 {AlreadyPresentCount} 项（已自动勾选）"
        : $"共 {AllItems.Count} 项预设";

    public PresetDialogViewModel(IPresetService service)
    {
        _service = service;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var cat = _service.Load();
            AllItems.Clear();
            Groups.Clear();
            foreach (var (scope, display) in GroupOrder)
            {
                var group = new PresetGroup(scope, display);
                foreach (var p in cat.Presets.Where(p => p.Scope == scope))
                    AddItem(group, p);
                Groups.Add(group);
            }
            var extItems = cat.Presets.Where(p => p.Scope.StartsWith("FileExt:", StringComparison.OrdinalIgnoreCase)).ToList();
            if (extItems.Count > 0)
            {
                var group = new PresetGroup("FileExt", "文件类型扩展");
                foreach (var p in extItems)
                    AddItem(group, p);
                Groups.Add(group);
            }
            AlreadyPresentCount = AllItems.Count(i => i.IsApplied);
            UpdateCounters();
            StatusText = SummaryText;
            OnPropertyChanged(nameof(SummaryText));
        }
        catch (Exception ex)
        {
            StatusText = "加载预设失败: " + ex.Message;
        }
    }

    /// <summary>
    /// Builds one row and mirrors the machine's current state onto it.
    /// <para>
    /// A preset that is already present is checked *and* marked applied. That is
    /// the whole point of the recommendation panel: it has to show what is
    /// actually enabled right now. Leaving existing items unchecked made the list
    /// disagree with the menu the user sees in Explorer, and pressing
    /// 「应用选中」 then did nothing for them while looking like it should have.
    /// </para>
    /// </summary>
    private void AddItem(PresetGroup group, PresetItem preset)
    {
        var exists = _service.IsApplied(preset);
        var vm = new PresetItemViewModel(preset)
        {
            IsApplied = exists,
            IsSelected = exists,
            State = exists ? PresetApplyState.Applied : PresetApplyState.Pending,
        };
        vm.PropertyChanged += OnItemPropertyChanged;
        group.Items.Add(vm);
        AllItems.Add(vm);
    }

    [RelayCommand]
    public async Task ApplySelectedAsync()
    {
        var targets = AllItems.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0) return;
        IsBusy = true;
        AppliedCount = SkippedCount = ErrorCount = 0;
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    await Task.Run(() => _service.Apply(item.Model, OverwriteExisting));
                    item.IsApplied = true;
                    item.State = PresetApplyState.Applied;
                    item.LastError = null;
                    AppliedCount++;
                }
                catch (PresetConflictException)
                {
                    item.State = PresetApplyState.Exists;
                    SkippedCount++;
                }
                catch (Exception ex)
                {
                    item.State = PresetApplyState.Error;
                    item.LastError = ex.Message;
                    ErrorCount++;
                }
            }
            // The counters above describe this run; the presence counters describe the
            // machine, so both have to be refreshed or the summary keeps claiming the
            // pre-apply state.
            AlreadyPresentCount = AllItems.Count(i => i.IsApplied);
            StatusText = $"应用 {AppliedCount} · 跳过 {SkippedCount} · 失败 {ErrorCount}"
                         + $" · 已存在 {AlreadyPresentCount}";
            OnPropertyChanged(nameof(SummaryText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Import()
    {
        var dlg = new OpenFileDialog { Filter = "预设 JSON (*.json)|*.json", Title = "导入推荐设置" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _service.Import(dlg.FileName);
            StatusText = "已导入: " + dlg.FileName;
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "导入失败: " + ex.Message;
        }
    }

    [RelayCommand]
    public void Export()
    {
        var dlg = new SaveFileDialog { Filter = "预设 JSON (*.json)|*.json", Title = "导出推荐设置", FileName = "presets.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _service.Export(dlg.FileName);
            StatusText = "已导出: " + dlg.FileName;
        }
        catch (Exception ex)
        {
            StatusText = "导出失败: " + ex.Message;
        }
    }

    [RelayCommand]
    public void SelectAllInGroup(PresetGroup? group)
    {
        if (group is null) return;
        foreach (var i in group.Items) i.IsSelected = true;
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var i in AllItems) i.IsSelected = false;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresetItemViewModel.IsSelected))
            UpdateCounters();
    }

    private void UpdateCounters()
    {
        SelectedCount = AllItems.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(HasSelection));
    }
}
