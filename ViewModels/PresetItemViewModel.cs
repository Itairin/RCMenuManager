using CommunityToolkit.Mvvm.ComponentModel;
using RCMenuManager.Models;

namespace RCMenuManager.ViewModels;

public enum PresetApplyState { Pending, Applied, Exists, Error }

public partial class PresetItemViewModel : ObservableObject
{
    public PresetItem Model { get; }
    public string Scope => Model.Scope;
    public string VerbName => Model.VerbName;
    public string DisplayName => Model.DisplayName;
    public string Description => Model.Description;
    public string CommandPreview => Model.Command;
    public string Icon => Model.Icon;
    public bool IsExtended => Model.Extended;
    public bool IsBuiltIn => Model.IsBuiltIn;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private PresetApplyState _state = PresetApplyState.Pending;
    [ObservableProperty] private string? _lastError;

    public PresetItemViewModel(PresetItem model) { Model = model; }

    public EditableVerbDraft ToDraft() => new()
    {
        VerbName = Model.VerbName,
        DisplayName = Model.DisplayName,
        Command = Model.Command,
        IconExpression = Model.Icon,
        IsExtended = Model.Extended,
        Position = Model.Position,
        IsParentOnly = false,
    };
}
