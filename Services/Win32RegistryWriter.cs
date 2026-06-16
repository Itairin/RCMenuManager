using System;
using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>
/// Production IRegistryWriter backed by Microsoft.Win32.RegistryKey. Each call
/// opens a writable key, performs the action, and disposes the handle so we
/// never hold long-lived registry handles.
/// </summary>
public sealed class Win32RegistryWriter : IRegistryWriter
{
    public bool KeyExists(RegistryHive hive, string subKey)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(subKey, writable: false);
        return key is not null;
    }

    public void CreateSubKey(RegistryHive hive, string subKey)
    {
        using var root = OpenRoot(hive);
        using var key = root.CreateSubKey(subKey, writable: true);
    }

    public void DeleteSubKeyTree(RegistryHive hive, string subKey)
    {
        using var root = OpenRoot(hive);
        root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    public void SetStringValue(RegistryHive hive, string subKey, string name, string value)
    {
        using var root = OpenRoot(hive);
        using var key = root.CreateSubKey(subKey, writable: true);
        if (key is null)
            throw new InvalidOperationException($"Cannot create or open key {hive}\\{subKey}");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(RegistryHive hive, string subKey, string name)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public bool ValueExists(RegistryHive hive, string subKey, string name)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(subKey, writable: false);
        if (key is null) return false;
        foreach (var n in key.GetValueNames())
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static RegistryKey OpenRoot(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default),
        RegistryHive.CurrentUser => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
        RegistryHive.LocalMachine => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default),
        RegistryHive.Users => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default),
        RegistryHive.CurrentConfig => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, RegistryView.Default),
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };
}
