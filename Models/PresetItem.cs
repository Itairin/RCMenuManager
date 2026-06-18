namespace RCMenuManager.Models;

public sealed class PresetItem
{
    public string Scope { get; set; } = string.Empty;
    public string VerbName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Extended { get; set; }
    public string Position { get; set; } = "Default";
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsBuiltIn { get; set; } = true;
}
