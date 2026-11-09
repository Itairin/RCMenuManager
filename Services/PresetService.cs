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
    /// <summary>Logical name of the presets.json embedded in the assembly.</summary>
    internal const string EmbeddedResourceName = "RCMenuManager.Resources.presets.json";

    private readonly IRegistryWriter _writer;
    private readonly RegistryWriteService _regWriter;
    private readonly string _builtInPath;
    private readonly bool _useEmbeddedDefault;

    public string UserPresetsPath { get; }

    public PresetService(IRegistryWriter writer, RegistryWriteService regWriter)
        : this(writer, regWriter,
               Path.Combine(AppContext.BaseDirectory, "Resources", "presets.json"),
               DefaultUserPresetsPath(),
               useEmbeddedDefault: true)
    {
    }

    public PresetService(
        IRegistryWriter writer,
        RegistryWriteService regWriter,
        string builtInPath,
        string userPath,
        bool useEmbeddedDefault = false)
    {
        _writer = writer;
        _regWriter = regWriter;
        _builtInPath = builtInPath;
        _useEmbeddedDefault = useEmbeddedDefault;
        UserPresetsPath = userPath;
        Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
    }

    public static string DefaultUserPresetsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "presets.json");

    public PresetCatalog Load()
    {
        var byKey = new Dictionary<string, PresetItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ReadBuiltIn())
            byKey[KeyOf(p)] = p;
        foreach (var u in ReadSafe(UserPresetsPath))
            byKey[KeyOf(u)] = u;
        return new PresetCatalog
        {
            Version = "1.0",
            Presets = byKey.Values.OrderBy(p => p.Scope).ThenBy(p => p.VerbName).ToList(),
        };
    }

    /// <summary>
    /// Built-in catalog: a presets.json sitting next to the exe wins (lets users
    /// swap in their own list without a rebuild); otherwise the copy embedded in
    /// the assembly is used, so a single-file publish has no loose dependency.
    /// </summary>
    private List<PresetItem> ReadBuiltIn()
    {
        if (!string.IsNullOrEmpty(_builtInPath) && File.Exists(_builtInPath))
        {
            var onDisk = ReadSafe(_builtInPath);
            if (onDisk.Count > 0) return onDisk;
        }
        return _useEmbeddedDefault ? ReadEmbeddedDefault() : new List<PresetItem>();
    }

    /// <summary>Reads the built-in presets compiled into this assembly.</summary>
    internal static List<PresetItem> ReadEmbeddedDefault()
    {
        try
        {
            using var stream = typeof(PresetService).Assembly
                .GetManifestResourceStream(EmbeddedResourceName);
            return stream is null ? new List<PresetItem>() : ReadStream(stream);
        }
        catch
        {
            return new List<PresetItem>();
        }
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
            return ReadStream(stream);
        }
        catch
        {
            try { File.Move(path, path + ".bak", overwrite: true); } catch { }
            return new List<PresetItem>();
        }
    }

    private static List<PresetItem> ReadStream(Stream stream)
    {
        var cat = JsonSerializer.Deserialize<PresetCatalog>(stream, JsonOpts);
        return cat?.Presets ?? new List<PresetItem>();
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
