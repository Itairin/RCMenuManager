using System.Collections.Generic;

namespace RCMenuManager.Models;

public sealed class PresetCatalog
{
    public string Version { get; set; } = "1.0";
    public List<PresetItem> Presets { get; set; } = new();
}
