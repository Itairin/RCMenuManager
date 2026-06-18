using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IPresetService
{
    PresetCatalog Load();
    bool IsApplied(PresetItem item);
    void Apply(PresetItem item, bool overwrite);
    void SaveUserPreset(PresetItem item);
    void Import(string filePath);
    void Export(string filePath);
    string UserPresetsPath { get; }
}
