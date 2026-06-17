using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IWin11MenuService
{
    bool IsNewMenuEnabled { get; }
    void SetNewMenuEnabled(bool enabled);
    IReadOnlyList<Models.Win11BlockItem> GetBlockList();
    void RemoveFromBlock(string verbName);
    void RestartExplorer();
}