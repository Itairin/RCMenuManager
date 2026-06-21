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
}
