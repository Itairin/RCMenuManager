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

    /// <summary>
    /// True when the verb's stored label is an indirect <c>MUIVerb</c>. The parser
    /// prefers MUIVerb over the default value, so writing only the default value left
    /// such a rename invisible: the list kept showing the old text.
    /// </summary>
    public bool HasMuiVerb { get; set; }

    /// <summary>Name as the UI displayed it, before this edit.</summary>
    public string OriginalDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// True when that name was inferred (no label stored in the key). Such a name
    /// must only be written when the user actually changed it: otherwise editing the
    /// icon would stamp a guessed label into the registry as if it were official.
    /// </summary>
    public bool HasInferredName { get; set; }
}
