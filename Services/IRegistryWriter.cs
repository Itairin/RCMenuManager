using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>
/// Minimal write surface for verb-key manipulation. Designed so RegistryWriteService
/// can be unit-tested against an in-memory implementation without touching the real
/// system registry.
/// </summary>
public interface IRegistryWriter
{
    bool KeyExists(RegistryHive hive, string subKey);
    void CreateSubKey(RegistryHive hive, string subKey);
    void DeleteSubKeyTree(RegistryHive hive, string subKey);
    void SetStringValue(RegistryHive hive, string subKey, string name, string value);
    void DeleteValue(RegistryHive hive, string subKey, string name);
    bool ValueExists(RegistryHive hive, string subKey, string name);
}
