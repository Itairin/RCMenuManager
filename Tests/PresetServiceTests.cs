using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class PresetServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemoryRegistryWriter _writer;
    private readonly RegistryWriteService _regWriter;

    public PresetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerPresetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _writer = new InMemoryRegistryWriter();
        var backup = new RecordingBackup();
        var log = new RecordingLog();
        _regWriter = new RegistryWriteService(_writer, backup, log, () => true, notifyShell: () => { });
    }

    public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

    private PresetService MakeService(string builtInJson, string? userJson = null)
    {
        var builtInPath = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(builtInPath, builtInJson);
        var userPath = Path.Combine(_tempDir, "user.json");
        if (userJson is not null) File.WriteAllText(userPath, userJson);
        return new PresetService(_writer, _regWriter, builtInPath, userPath);
    }

    [Fact]
    public void Load_merges_builtin_and_user_with_user_overriding_builtin()
    {
        var builtIn = "{\"version\":\"1.0\",\"presets\":["
            + "{\"scope\":\"AllFiles\",\"verbName\":\"notepad\",\"displayName\":\"Builtin\",\"command\":\"b.exe\",\"icon\":\"\",\"extended\":false,\"position\":\"Default\",\"description\":\"\",\"isSystem\":false,\"isBuiltIn\":true},"
            + "{\"scope\":\"Folder\",\"verbName\":\"vscode\",\"displayName\":\"BuiltinVS\",\"command\":\"b.exe\",\"icon\":\"\",\"extended\":false,\"position\":\"Default\",\"description\":\"\",\"isSystem\":false,\"isBuiltIn\":true}"
            + "]}";
        var user = "{\"version\":\"1.0\",\"presets\":["
            + "{\"scope\":\"AllFiles\",\"verbName\":\"notepad\",\"displayName\":\"User\",\"command\":\"u.exe\",\"icon\":\"\",\"extended\":false,\"position\":\"Default\",\"description\":\"\",\"isSystem\":false,\"isBuiltIn\":false}"
            + "]}";
        var svc = MakeService(builtIn, user);
        var cat = svc.Load();
        Assert.Equal(2, cat.Presets.Count);
        var notepad = cat.Presets.Single(p => p.VerbName == "notepad");
        Assert.Equal("User", notepad.DisplayName);
        Assert.False(notepad.IsBuiltIn);
    }

    [Fact]
    public void Load_returns_empty_catalog_when_builtin_has_no_entries()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}", userJson: null);
        Assert.Empty(svc.Load().Presets);
    }

    [Fact]
    public void IsApplied_true_when_hkcu_verb_key_exists()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        _writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad");
        Assert.True(svc.IsApplied(new PresetItem { Scope = "AllFiles", VerbName = "notepad" }));
    }

    [Fact]
    public void IsApplied_false_when_hkcu_verb_key_missing()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        Assert.False(svc.IsApplied(new PresetItem { Scope = "AllFiles", VerbName = "notepad" }));
    }

    [Fact]
    public void Apply_creates_verb_in_hkcu_software_classes()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        var item = new PresetItem
        {
            Scope = "AllFiles", VerbName = "notepad", DisplayName = "用记事本打开",
            Command = "notepad.exe \"%1\"", Icon = "imageres.dll,-64", Extended = false,
        };
        svc.Apply(item, overwrite: false);
        Assert.True(_writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad"));
        Assert.Equal("用记事本打开", _writer.GetValue(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad", ""));
        Assert.Equal("imageres.dll,-64", _writer.GetValue(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad", "Icon"));
        Assert.True(_writer.KeyExists(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad\command"));
        Assert.Equal("notepad.exe \"%1\"", _writer.GetValue(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad\command", ""));
    }

    [Fact]
    public void Apply_raises_preset_conflict_when_existing_and_no_overwrite()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        _writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad");
        var item = new PresetItem { Scope = "AllFiles", VerbName = "notepad", DisplayName = "X", Command = "x.exe" };
        Assert.Throws<PresetConflictException>(() => svc.Apply(item, overwrite: false));
    }

    [Fact]
    public void Apply_overwrites_when_flag_set()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        _writer.CreateSubKey(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad");
        _writer.SetStringValue(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad", "", "OLD");
        var item = new PresetItem { Scope = "AllFiles", VerbName = "notepad", DisplayName = "NEW", Command = "new.exe" };
        svc.Apply(item, overwrite: true);
        Assert.Equal("NEW", _writer.GetValue(RegistryHive.CurrentUser, @"Software\Classes\*\shell\notepad", ""));
    }

    [Fact]
    public void SaveUserPreset_persists_to_user_path()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        svc.SaveUserPreset(new PresetItem { Scope = "Folder", VerbName = "myverb", DisplayName = "My", Command = "x.exe", IsBuiltIn = false });
        var reloaded = svc.Load();
        var saved = reloaded.Presets.Single(p => p.VerbName == "myverb");
        Assert.Equal("Folder", saved.Scope);
        Assert.False(saved.IsBuiltIn);
    }

    [Fact]
    public void SaveUserPreset_replaces_existing_user_entry_with_same_scope_verbname()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        svc.SaveUserPreset(new PresetItem { Scope = "Folder", VerbName = "v", DisplayName = "A", Command = "a", IsBuiltIn = false });
        svc.SaveUserPreset(new PresetItem { Scope = "Folder", VerbName = "v", DisplayName = "B", Command = "b", IsBuiltIn = false });
        Assert.Single(svc.Load().Presets);
        Assert.Equal("B", svc.Load().Presets[0].DisplayName);
    }

    [Fact]
    public void Import_replaces_duplicate_by_scope_verbname()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        var importFile = Path.Combine(_tempDir, "import.json");
        File.WriteAllText(importFile, "{\"version\":\"1.0\",\"presets\":["
            + "{\"scope\":\"Folder\",\"verbName\":\"v\",\"displayName\":\"Imported\",\"command\":\"i\",\"icon\":\"\",\"extended\":false,\"position\":\"Default\",\"description\":\"\",\"isSystem\":false,\"isBuiltIn\":false}"
            + "]}");
        svc.Import(importFile);
        Assert.Equal("Imported", svc.Load().Presets.Single().DisplayName);
    }

    [Fact]
    public void Export_roundtrips_through_disk()
    {
        var svc = MakeService("{\"version\":\"1.0\",\"presets\":[]}");
        svc.SaveUserPreset(new PresetItem { Scope = "Desktop", VerbName = "x", DisplayName = "X", Command = "x.exe", IsBuiltIn = false });
        var outPath = Path.Combine(_tempDir, "export.json");
        svc.Export(outPath);
        var svc2 = MakeService("{\"version\":\"1.0\",\"presets\":[]}", userJson: File.ReadAllText(outPath));
        Assert.Equal("X", svc2.Load().Presets.Single().DisplayName);
    }

    [Fact]
    public void ReadEmbeddedDefault_returns_the_shipped_catalog()
    {
        var presets = PresetService.ReadEmbeddedDefault();
        Assert.True(presets.Count >= 30, $"expected the full built-in catalog, got {presets.Count}");
        Assert.Contains(presets, p => p.VerbName == "notepad" && p.Scope == "AllFiles");
        Assert.All(presets, p => Assert.False(string.IsNullOrWhiteSpace(p.Command)));
    }

    /// <summary>
    /// The catalog ships inside the exe, so it is used on machines that are not
    /// the build machine. A hard-coded "C:\Program Files\..." would silently
    /// point at nothing there; environment-variable forms (%ProgramFiles%,
    /// %SystemRoot%, %LOCALAPPDATA%) resolve on whatever machine runs it.
    /// </summary>
    [Fact]
    public void Built_in_presets_avoid_machine_specific_absolute_paths()
    {
        foreach (var preset in PresetService.ReadEmbeddedDefault())
            Assert.DoesNotMatch(@"[A-Za-z]:\\", preset.Command);
    }

    [Fact]
    public void Load_falls_back_to_embedded_presets_when_no_file_beside_exe()
    {
        // A single-file publish ships no presets.json at all; the catalog must
        // still be populated, otherwise the preset page comes up empty.
        var missing = Path.Combine(_tempDir, "does-not-exist", "presets.json");
        var userPath = Path.Combine(_tempDir, "user-embedded.json");
        var svc = new PresetService(_writer, _regWriter, missing, userPath, useEmbeddedDefault: true);
        var cat = svc.Load();
        Assert.NotEmpty(cat.Presets);
        Assert.Equal(PresetService.ReadEmbeddedDefault().Count, cat.Presets.Count);
    }

    [Fact]
    public void Load_prefers_a_presets_file_next_to_the_exe_over_the_embedded_copy()
    {
        var overrideJson = "{\"version\":\"1.0\",\"presets\":["
            + "{\"scope\":\"Folder\",\"verbName\":\"override\",\"displayName\":\"Local\",\"command\":\"l.exe\",\"icon\":\"\",\"extended\":false,\"position\":\"Default\",\"description\":\"\",\"isSystem\":false,\"isBuiltIn\":true}"
            + "]}";
        var builtInPath = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(builtInPath, overrideJson);
        var svc = new PresetService(_writer, _regWriter, builtInPath,
            Path.Combine(_tempDir, "user-override.json"), useEmbeddedDefault: true);
        var cat = svc.Load();
        Assert.Single(cat.Presets);
        Assert.Equal("override", cat.Presets[0].VerbName);
    }

    [Fact]
    public void Load_keeps_embedded_presets_when_the_local_file_is_corrupt()
    {
        var builtInPath = Path.Combine(_tempDir, "presets.json");
        File.WriteAllText(builtInPath, "{ not json at all");
        var svc = new PresetService(_writer, _regWriter, builtInPath,
            Path.Combine(_tempDir, "user-corrupt.json"), useEmbeddedDefault: true);
        Assert.NotEmpty(svc.Load().Presets);
        Assert.True(File.Exists(builtInPath + ".bak"));
    }
}
