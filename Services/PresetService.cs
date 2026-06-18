using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public sealed class PresetService : IPresetService
{
    private readonly IRegistryWriter _writer;
    private readonly RegistryWriteService _regWriter;
    private readonly string _builtInPath;

    public string UserPresetsPath { get; }

    public PresetService(IRegistryWriter writer, RegistryWriteService regWriter)
        : this(writer, regWriter,
               Path.Combine(AppContext.BaseDirectory, "Resources", "presets.json"),
               DefaultUserPresetsPath())
    {
    }

    public PresetService(IRegistryWriter writer, RegistryWriteService regWriter, string builtInPath, string userPath)
    {
        _writer = writer;
        _regWriter = regWriter;
        _builtInPath = builtInPath;
        UserPresetsPath = userPath;
        Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
    }

    public static string DefaultUserPresetsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "presets.json");

    public PresetCatalog Load()
    {
        var byKey = new Dictionary<string, PresetItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ReadSafe(_builtInPath))
            byKey[KeyOf(p)] = p;
        foreach (var u in ReadSafe(UserPresetsPath))
            byKey[KeyOf(u)] = u;
        return new PresetCatalog
        {
            Version = "1.0",
            Presets = byKey.Values.OrderBy(p => p.Scope).ThenBy(p => p.VerbName).ToList(),
        };
    }

    public bool IsApplied(PresetItem item)
    {
        var scope = MenuScope.FromScopeId(item.Scope);
        return _writer.KeyExists(RegistryHive.CurrentUser, scope.HkcuShellSubKey + "\\" + item.VerbName);
    }

    public void Apply(PresetItem item, bool overwrite)
    {
        var scope = MenuScope.FromScopeId(item.Scope);
        var draft = new EditableVerbDraft
        {
            VerbName = item.VerbName,
            DisplayName = item.DisplayName,
            Command = item.Command,
            IconExpression = item.Icon,
            IsExtended = item.Extended,
            Position = item.Position,
            IsParentOnly = false,
        };
        var parentSubKey = scope.HkcuShellSubKey;
        try
        {
            _regWriter.CreateRootVerb(RegistryHive.CurrentUser, parentSubKey, scope.ScopeId, draft);
        }
        catch (RegistryConflictException)
        {
            if (!overwrite) throw new PresetConflictException(item.Scope, item.VerbName);
            var verbKey = parentSubKey + "\\" + item.VerbName;
            _regWriter.Delete(RegistryHive.CurrentUser, verbKey, scope.ScopeId, item.VerbName);
            _regWriter.CreateRootVerb(RegistryHive.CurrentUser, parentSubKey, scope.ScopeId, draft);
        }
    }

    public void SaveUserPreset(PresetItem item)
    {
        item.IsBuiltIn = false;
        var items = ReadSafe(UserPresetsPath);
        var key = KeyOf(item);
        items.RemoveAll(p => KeyOf(p) == key);
        items.Add(item);
        WriteAll(UserPresetsPath, new PresetCatalog { Version = "1.0", Presets = items });
    }

    public void Import(string filePath)
    {
        foreach (var i in ReadSafe(filePath))
            SaveUserPreset(i);
    }

    public void Export(string filePath) => WriteAll(filePath, Load());

    private static string KeyOf(PresetItem p) =>
        (p.Scope ?? string.Empty).Trim().ToLowerInvariant() + "|" + (p.VerbName ?? string.Empty).Trim().ToLowerInvariant();

    private static List<PresetItem> ReadSafe(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<PresetItem>();
        try
        {
            using var stream = File.OpenRead(path);
            var cat = JsonSerializer.Deserialize<PresetCatalog>(stream, JsonOpts);
            return cat?.Presets ?? new List<PresetItem>();
        }
        catch
        {
            try { File.Move(path, path + ".bak", overwrite: true); } catch { }
            return new List<PresetItem>();
        }
    }

    private static void WriteAll(string path, PresetCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(catalog, JsonOpts);
        File.WriteAllText(path, json);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
