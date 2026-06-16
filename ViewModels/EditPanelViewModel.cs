using CommunityToolkit.Mvvm.ComponentModel;
using RCMenuManager.Models;

namespace RCMenuManager.ViewModels;

/// <summary>
/// Two-state form-state holder for DetailsPanel: view mode (read-only) vs.
/// edit mode (form bindings against EditableVerbDraft). Also surfaces the
/// inline error string used to render conflict / write failures.
/// </summary>
public partial class EditPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _draftDisplayName = string.Empty;

    [ObservableProperty]
    private string _draftCommand = string.Empty;

    [ObservableProperty]
    private string _draftIcon = string.Empty;

    [ObservableProperty]
    private bool _draftExtended;

    [ObservableProperty]
    private string _draftPosition = "Default";

    public void BeginEdit(MenuItemViewModel item)
    {
        ErrorMessage = null;
        DraftDisplayName = item.DisplayName;
        DraftCommand = item.Command;
        DraftIcon = item.Model.IconExpression ?? string.Empty;
        DraftExtended = item.IsExtended;
        DraftPosition = item.IsTop ? "Top" : item.IsBottom ? "Bottom" : "Default";
        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        ErrorMessage = null;
    }

    public EditableVerbDraft Snapshot(MenuItemViewModel item) => new()
    {
        VerbName = item.VerbName,
        DisplayName = (DraftDisplayName ?? string.Empty).Trim(),
        Command = (DraftCommand ?? string.Empty).Trim(),
        IconExpression = (DraftIcon ?? string.Empty).Trim(),
        IsExtended = DraftExtended,
        Position = DraftPosition ?? "Default",
        IsParentOnly = item.Model.IsParentOnly,
    };
}
