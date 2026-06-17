using Microsoft.Win32;
using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IBackupService
{
    string Export(RegistryHive hive, string subKey, string scopeId, string verbName);
    IReadOnlyList<Models.BackupRecord> List();
    void Import(string filePath);
    void Delete(string filePath);
}
