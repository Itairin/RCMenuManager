using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IPresetService
{
    PresetCatalog Load();
    bool IsApplied(PresetItem item);

    /// <summary>True when a verb with this name already exists in that scope (either hive).</summary>
    bool IsVerbPresent(string scopeId, string verbName);
    void Apply(PresetItem item, bool overwrite);
    void SaveUserPreset(PresetItem item);
    void Import(string filePath);
    void Export(string filePath);
    string UserPresetsPath { get; }
}
