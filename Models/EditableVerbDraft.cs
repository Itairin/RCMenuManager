namespace RCMenuManager.Models;

/// <summary>Mutable form-state object used by EditPanel and AddVerbDialog.</summary>
public sealed class EditableVerbDraft
{
    public string VerbName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string IconExpression { get; set; } = string.Empty;
    public bool IsExtended { get; set; }
    public string Position { get; set; } = "Default";
    public bool IsParentOnly { get; set; }
}
