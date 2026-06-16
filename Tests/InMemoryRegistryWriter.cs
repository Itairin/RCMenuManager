using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Services;

namespace RCMenuManager.Tests;

/// <summary>
/// Pure in-memory IRegistryWriter for unit tests. Models keys as a hash-set of
/// canonical "Hive\\sub\\path" strings, and values as a dictionary keyed by
/// "Hive\\sub\\path::name". Case-insensitive like the real registry.
/// </summary>
internal sealed class InMemoryRegistryWriter : IRegistryWriter
{
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public bool KeyExists(RegistryHive hive, string subKey) =>
        _keys.Contains(KeyId(hive, subKey));

    public void CreateSubKey(RegistryHive hive, string subKey)
    {
        var parts = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= parts.Length; i++)
            _keys.Add(KeyId(hive, string.Join('\\', parts.Take(i))));
    }

    public void DeleteSubKeyTree(RegistryHive hive, string subKey)
    {
        var prefix = KeyId(hive, subKey);
        _keys.RemoveWhere(k =>
            k.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase));
        var keysToRemove = _values.Keys.Where(k =>
            k.StartsWith(prefix + "::", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in keysToRemove) _values.Remove(k);
    }

    public void SetStringValue(RegistryHive hive, string subKey, string name, string value)
    {
        if (!KeyExists(hive, subKey))
            CreateSubKey(hive, subKey);
        _values[ValueId(hive, subKey, name)] = value;
    }

    public void DeleteValue(RegistryHive hive, string subKey, string name) =>
        _values.Remove(ValueId(hive, subKey, name));

    public bool ValueExists(RegistryHive hive, string subKey, string name) =>
        _values.ContainsKey(ValueId(hive, subKey, name));

    public string? GetValue(RegistryHive hive, string subKey, string name) =>
        _values.TryGetValue(ValueId(hive, subKey, name), out var v) ? v : null;

    private static string KeyId(RegistryHive hive, string subKey) => $"{hive}\\{subKey}";
    private static string ValueId(RegistryHive hive, string subKey, string name) => $"{KeyId(hive, subKey)}::{name}";
}
