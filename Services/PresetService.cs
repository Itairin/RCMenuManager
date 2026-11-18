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
    private readonly RegistryService? _registry;
    private readonly string _builtInPath;
    private readonly bool _useEmbeddedDefault;

    public string UserPresetsPath { get; }

    public PresetService(IRegistryWriter writer, RegistryWriteService regWriter, RegistryService registry)
        : this(writer, regWriter,
               Path.Combine(AppContext.BaseDirectory, "Resources", "presets.json"),
               DefaultUserPresetsPath(),
               useEmbeddedDefault: true,
               registry: registry)
    {
    }

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
        bool useEmbeddedDefault = false,
        RegistryService? registry = null)
    {
        _writer = writer;
        _regWriter = regWriter;
        _registry = registry;
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

    /// <summary>
    /// True when this preset is already present on the machine.
    /// <para>
    /// Both hives are checked: an entry installed machine-wide (HKLM) makes the
    /// preset "already there" just as much as a per-user one does, and reporting it
    /// as unapplied made the dialog offer to create a duplicate — which then landed
    /// in HKCU and produced two identical menu rows.
    /// </para>
    /// </summary>
    /// <summary>
    /// True when the scope already offers this verb, checked over the same query
    /// paths the list uses. Keeping one definition is what stops the panel from
    /// saying "not applied" about an entry the list is already showing.
    /// </summary>
    public bool IsApplied(PresetItem item) => IsVerbPresent(item.Scope, item.VerbName);

    /// <summary>Same check for an arbitrary scope id, used by the dialog.</summary>
    public bool IsVerbPresent(string scopeId, string verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName)) return false;
        var scope = MenuScope.FromScopeId(scopeId);

        if (_registry is not null)
            return _registry.VerbExistsInScope(scope, verbName);

        // Fallback for callers that did not supply a RegistryService (tests): check
        // the two homes a preset can live in.
        return _writer.KeyExists(RegistryHive.CurrentUser, scope.HkcuShellSubKey + "\\" + verbName);
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

        // The scope may already provide this verb through another query path
        // (AllFilesystemObjects, SystemFileAssociations, or the machine hive). The
        // row is on the menu either way, so creating the per-user copy only adds a
        // duplicate — report "已经有"，which is also what the panel's checkbox said.
        var existing = _registry?.FindVerbInScope(scope, item.VerbName);
        if (existing is not null
            && !(existing.Value.Hive == RegistryHive.CurrentUser && existing.Value.SubKey == parentSubKey + "\\" + item.VerbName))
        {
            throw new PresetConflictException(item.Scope, item.VerbName);
        }

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
