using Microsoft.Win32;

namespace RCMenuManager.Services;

public interface IBackupService
{
    string Export(RegistryHive hive, string subKey, string scopeId, string verbName);
}
