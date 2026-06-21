---
# Phase 7: 推荐设置 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a "推荐" dialog that lets users browse 35+ bundled HKCU context-menu presets grouped by scope, check + apply them in one shot, and import/export custom JSON.

**Architecture:** New `Models.PresetItem` POCO + `PresetService` over existing `IRegistryWriter` (for `IsApplied`) and `RegistryWriteService` (for `CreateRootVerb` with backup/log/SHChangeNotify). New `PresetDialog` WPF window + `PresetDialogViewModel` over `CommunityToolkit.Mvvm`. New 4th "推荐" button on `ScopeBar` next to "备份".

**Tech Stack:** WPF (.NET 9), CommunityToolkit.Mvvm, xUnit, System.Text.Json.

**Key references:**
- Design spec: `docs/superpowers/specs/2026-06-18-phase7-presets-design.md`
- Preset content: `DeDocs/PRESETS.md`
- Existing patterns: `Models/MenuScope.cs` (ShellSubKey, HkcuShellSubKey), `Models/EditableVerbDraft.cs`, `Services/RegistryWriteService.cs` (CreateRootVerb), `Services/IRegistryWriter.cs`, `Tests/InMemoryRegistryWriter.cs`, `Views/Dialogs/Win11Dialog.xaml` (dialog XAML pattern), `ViewModels/Win11DialogViewModel.cs` (dialog VM pattern).

**Conventions:**
- HKCU only — no admin needed; all writes go to `RegistryHive.CurrentUser` + `Software\Classes\...`.
- `MenuScope` already provides `ShellSubKey` (HKCR form) and `HkcuShellSubKey` (HKCU form). PresetService uses `HkcuShellSubKey` as the parentShellSubKey.
- The preset `Scope` field is the MenuScope's `ScopeId` string ("AllFiles" / "Folder" / "FolderBackground" / "Drive" / "Desktop" / "FileExt:.txt").
- Apply builds an `EditableVerbDraft { VerbName, DisplayName, Command, IconExpression=Icon, IsExtended=Extended, Position, IsParentOnly=false }` and calls `RegistryWriteService.CreateRootVerb(CurrentUser, parentShellSubKey, scopeId, draft)`.
- On conflict: overwrite=true performs Delete then Create, overwrite=false throws `PresetConflictException` so the VM can show "已存在" state.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `Resources/presets.json` | 35+ bundled presets; copied to output at build time |
| `RCMenuManager.csproj` | Add `<None Update="Resources\presets.json" CopyToOutputDirectory="PreserveNewest" />` |
| `Models/PresetItem.cs` | Single preset POCO; System.Text.Json serialized |
| `Models/PresetCatalog.cs` | `{ Version, Presets[] }` container |
| `Services/PresetConflictException.cs` | Conflict signal used by VM to render "已存在" state |
| `Services/IPresetService.cs` | Load / IsApplied / Apply / SaveUser / Import / Export contract |
| `Services/PresetService.cs` | JSON I/O + merging + registry-write orchestration |
| `ViewModels/PresetItemViewModel.cs` | UI row + `PresetApplyState` state machine |
| `ViewModels/PresetDialogViewModel.cs` | Grouped collection + Apply/Import/Export/Refresh commands + status |
| `Views/Dialogs/PresetDialog.xaml` | Dialog UI (5 Expanders + toolbar + status bar) |
| `Views/Dialogs/PresetDialog.xaml.cs` | InitializeComponent + close handler |
| `Views/Controls/ScopeBar.xaml` | Insert "推荐" button after "备份" |
| `ViewModels/MainViewModel.cs` | Inject `IPresetService` + new `ShowPresetsCommand` |
| `App.xaml.cs` | DI registration `IPresetService -> PresetService` |
| `Tests/PresetServiceTests.cs` | 11 cases covering Load/IsApplied/Apply/overwrite/import/export |
| `Tests/PresetItemViewModelTests.cs` | 2 cases covering state change + ToDraft |
| `docs/superpowers/smoke/2026-06-17-phase7-smoke.md` | Manual smoke checklist |

---

## Task 1: 打包 presets.json

**Files:**
- Create: `Resources/presets.json`
- Modify: `RCMenuManager.csproj`

- [ ] **Step 1: 创建 `Resources/presets.json`**

Write the following JSON exactly (35 entries, scope field uses `MenuScope.ScopeId` strings):

```json
{
  "version": "1.0",
  "presets": [
    { "scope": "AllFiles", "verbName": "notepad", "displayName": "用记事本打开", "command": "notepad.exe \"%1\"", "icon": "imageres.dll,-64", "extended": false, "position": "Default", "description": "快速编辑文本文件", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "vscode", "displayName": "用 VS Code 打开", "command": "code \"%1\"", "icon": "code.exe,0", "extended": false, "position": "Default", "description": "开发者必备", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "copypath", "displayName": "复制文件路径", "command": "cmd /c echo \"%1\" | clip", "icon": "imageres.dll,-5302", "extended": false, "position": "Default", "description": "复制完整路径到剪贴板", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "copyname", "displayName": "复制文件名", "command": "cmd /c echo \"%~nx1\" | clip", "icon": "imageres.dll,-5302", "extended": false, "position": "Default", "description": "仅复制文件名", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "openhere", "displayName": "在终端中打开", "command": "pwsh -NoExit -Command \"cd '%~dp1'\"", "icon": "powershell.exe,0", "extended": false, "position": "Default", "description": "PowerShell 终端", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "hash", "displayName": "文件哈希校验", "command": "certutil -hashfile \"%1\" SHA256", "icon": "imageres.dll,-67", "extended": false, "position": "Default", "description": "计算文件哈希", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "runas", "displayName": "以管理员身份运行", "command": "\"runas.exe\" /user:Administrator \"%1\"", "icon": "imageres.dll,-78", "extended": true, "position": "Default", "description": "提权运行 (仅 Shift 显示)", "isSystem": false, "isBuiltIn": true },
    { "scope": "AllFiles", "verbName": "openwith", "displayName": "打开方式...", "command": "rundll32.exe shell32.dll,OpenAs_RunDLL \"%1\"", "icon": "imageres.dll,-5301", "extended": false, "position": "Default", "description": "选择打开程序", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "vscode", "displayName": "在 VS Code 中打开", "command": "code \"%V\"", "icon": "code.exe,0", "extended": false, "position": "Default", "description": "VS Code 打开目录", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "openhere", "displayName": "在终端中打开", "command": "pwsh -NoExit -Command \"cd '%V'\"", "icon": "powershell.exe,0", "extended": false, "position": "Default", "description": "PowerShell 终端", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "gitbash", "displayName": "在 Git Bash 中打开", "command": "\"C:\\Program Files\\Git\\git-bash.exe\" --cd=\"%V\"", "icon": "bash.exe,0", "extended": false, "position": "Default", "description": "Git 终端", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "newfile", "displayName": "新建文件", "command": "cmd /c echo. > \"%V\\新建文件.txt\"", "icon": "imageres.dll,-64", "extended": false, "position": "Default", "description": "快速创建空文件", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "folderstats", "displayName": "文件夹大小统计", "command": "powershell -Command \"Get-ChildItem -Recurse '%V' | Measure-Object -Property Length -Sum\"", "icon": "imageres.dll,-67", "extended": true, "position": "Default", "description": "计算文件夹大小", "isSystem": false, "isBuiltIn": true },
    { "scope": "Folder", "verbName": "copyfolderpath", "displayName": "复制文件夹路径", "command": "cmd /c echo \"%V\" | clip", "icon": "imageres.dll,-5302", "extended": false, "position": "Default", "description": "复制完整路径", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "openhere", "displayName": "在终端中打开", "command": "pwsh -NoExit -Command \"cd '%V'\"", "icon": "powershell.exe,0", "extended": false, "position": "Default", "description": "当前目录打开终端", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "vscode", "displayName": "在 VS Code 中打开", "command": "code \"%V\"", "icon": "code.exe,0", "extended": false, "position": "Default", "description": "VS Code 打开", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "newtxt", "displayName": "新建文本文档", "command": "cmd /c echo. > \"%V\\新建文本文档.txt\"", "icon": "imageres.dll,-64", "extended": false, "position": "Default", "description": "快速创建", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "showhidden", "displayName": "显示隐藏文件", "command": "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v Hidden /t REG_DWORD /d 1 /f", "icon": "imageres.dll,-527", "extended": true, "position": "Default", "description": "切换显示隐藏", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "hidehidden", "displayName": "隐藏文件", "command": "reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced\" /v Hidden /t REG_DWORD /d 2 /f", "icon": "imageres.dll,-527", "extended": true, "position": "Default", "description": "切换隐藏文件", "isSystem": false, "isBuiltIn": true },
    { "scope": "FolderBackground", "verbName": "refreshicons", "displayName": "刷新图标缓存", "command": "ie4uinit.exe -show", "icon": "imageres.dll,-5305", "extended": false, "position": "Default", "description": "刷新图标", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "settings", "displayName": "打开系统设置", "command": "ms-settings:", "icon": "imageres.dll,-121", "extended": false, "position": "Default", "description": "快速打开设置", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "taskmgr", "displayName": "打开任务管理器", "command": "taskmgr.exe", "icon": "imageres.dll,-5305", "extended": false, "position": "Default", "description": "快速打开任务管理器", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "regedit", "displayName": "打开注册表编辑器", "command": "regedit.exe", "icon": "imageres.dll,-5007", "extended": false, "position": "Default", "description": "打开注册表", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "devmgr", "displayName": "打开设备管理器", "command": "devmgmt.msc", "icon": "imageres.dll,-27", "extended": false, "position": "Default", "description": "打开设备管理器", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "control", "displayName": "打开控制面板", "command": "control.exe", "icon": "imageres.dll,-5002", "extended": false, "position": "Default", "description": "打开控制面板", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "cmd", "displayName": "在此处打开命令行", "command": "cmd.exe", "icon": "cmd.exe,0", "extended": false, "position": "Default", "description": "打开 CMD", "isSystem": false, "isBuiltIn": true },
    { "scope": "Desktop", "verbName": "powershell", "displayName": "在此处打开 PowerShell", "command": "pwsh.exe", "icon": "powershell.exe,0", "extended": false, "position": "Default", "description": "打开 PowerShell", "isSystem": false, "isBuiltIn": true },
    { "scope": "Drive", "verbName": "openhere", "displayName": "在终端中打开", "command": "pwsh -NoExit -Command \"cd '%V'\"", "icon": "powershell.exe,0", "extended": false, "position": "Default", "description": "PowerShell 终端", "isSystem": false, "isBuiltIn": true },
    { "scope": "Drive", "verbName": "open", "displayName": "打开", "command": "explorer.exe \"%V\"", "icon": "shell32.dll,-16777", "extended": false, "position": "Default", "description": "打开驱动器", "isSystem": false, "isBuiltIn": true },
    { "scope": "Drive", "verbName": "diskmgmt", "displayName": "磁盘管理", "command": "diskmgmt.msc", "icon": "imageres.dll,-5305", "extended": false, "position": "Default", "description": "打开磁盘管理", "isSystem": false, "isBuiltIn": true },
    { "scope": "Drive", "verbName": "properties", "displayName": "属性", "command": "rundll32.exe shell32.dll,Properties_RunDLL \"%V\"", "icon": "imageres.dll,-5306", "extended": false, "position": "Default", "description": "查看属性", "isSystem": false, "isBuiltIn": true }
  ]
}
```

- [ ] **Step 2: 在 `RCMenuManager.csproj` 添加复制规则**

Modify `D:\Itair\RCMenuManager\RCMenuManager.csproj`. Insert this `<ItemGroup>` before `</Project>`:

```xml
  <ItemGroup>
    <None Update="Resources\presets.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 3: 验证 `dotnet build` 复制文件**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`. Then `Get-ChildItem D:\Itair\RCMenuManager\bin\Debug\net9.0-windows\Resources\presets.json` returns the file.

- [ ] **Step 4: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Resources/presets.json RCMenuManager.csproj
git commit -m "feat: bundle 35+ context menu presets as Resources/presets.json"
```

---

## Task 2: PresetItem + PresetCatalog 模型

**Files:**
- Create: `Models/PresetItem.cs`
- Create: `Models/PresetCatalog.cs`

- [ ] **Step 1: 创建 `Models/PresetItem.cs`**

```csharp
namespace RCMenuManager.Models;

public sealed class PresetItem
{
    public string Scope { get; set; } = string.Empty;
    public string VerbName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Extended { get; set; }
    public string Position { get; set; } = "Default";
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsBuiltIn { get; set; } = true;
}
```

- [ ] **Step 2: 创建 `Models/PresetCatalog.cs`**

```csharp
using System.Collections.Generic;

namespace RCMenuManager.Models;

public sealed class PresetCatalog
{
    public string Version { get; set; } = "1.0";
    public List<PresetItem> Presets { get; set; } = new();
}
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`.

- [ ] **Step 4: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Models/PresetItem.cs Models/PresetCatalog.cs
git commit -m "feat: add PresetItem and PresetCatalog POCO models"
```

---

## Task 3: PresetConflictException

**Files:**
- Create: `Services/PresetConflictException.cs`

- [ ] **Step 1: 创建文件**

```csharp
using System;

namespace RCMenuManager.Services;

public sealed class PresetConflictException : Exception
{
    public string VerbName { get; }
    public string Scope { get; }

    public PresetConflictException(string scope, string verbName)
        : base($"预设 {scope}/{verbName} 已存在，请勾选「覆盖」后再应用。")
    {
        Scope = scope;
        VerbName = verbName;
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`.

- [ ] **Step 3: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Services/PresetConflictException.cs
git commit -m "feat: add PresetConflictException for overwrite-required signal"
```

---

## Task 4: IPresetService + PresetService (TDD)

**Files:**
- Create: `Services/IPresetService.cs`
- Create: `Services/PresetService.cs`
- Create: `Tests/PresetServiceTests.cs`

- [ ] **Step 1: 写失败测试 — 合并内置 + 用户预设**

Create `Tests/PresetServiceTests.cs` with the following content (uses `InMemoryRegistryWriter` and reuses the `RecordingBackup` / `RecordingLog` types already in `Tests/RegistryWriteServiceTests.cs`):

```csharp
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
        Assert.Equal(1, svc.Load().Presets.Count);
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
```

- [ ] **Step 2: 创建 `Services/IPresetService.cs`**

```csharp
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IPresetService
{
    PresetCatalog Load();
    bool IsApplied(PresetItem item);
    void Apply(PresetItem item, bool overwrite);
    void SaveUserPreset(PresetItem item);
    void Import(string filePath);
    void Export(string filePath);
    string UserPresetsPath { get; }
}
```

- [ ] **Step 3: 跑测试确认 RED**

Run: `dotnet test D:\Itair\RCMenuManager\Tests\RCMenuManager.Tests.csproj --filter "FullyQualifiedName~PresetServiceTests" -v:normal`
Expected: 11 failed, 0 passed (PresetService class not yet defined).

- [ ] **Step 4: 创建 `Services/PresetService.cs` (GREEN)**

```csharp
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
            var cat = JsonSerializer.Deserialize<PresetCatalog>(stream);
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
        var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
```

- [ ] **Step 5: 跑测试确认 GREEN**

Run: `dotnet test D:\Itair\RCMenuManager\Tests\RCMenuManager.Tests.csproj --filter "FullyQualifiedName~PresetServiceTests" -v:normal`
Expected: 11 passed, 0 failed.

- [ ] **Step 6: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Services/IPresetService.cs Services/PresetService.cs Tests/PresetServiceTests.cs
git commit -m "feat: add IPresetService, PresetService, and 11 service tests"
```

---

## Task 5: PresetItemViewModel (TDD)

**Files:**
- Create: `ViewModels/PresetItemViewModel.cs`
- Create: `Tests/PresetItemViewModelTests.cs`

- [ ] **Step 1: 写失败测试 — `ToDraft` + 状态广播**

Create `Tests/PresetItemViewModelTests.cs`:

```csharp
using RCMenuManager.Models;
using RCMenuManager.ViewModels;
using Xunit;

namespace RCMenuManager.Tests;

public class PresetItemViewModelTests
{
    [Fact]
    public void ToDraft_copies_all_fields_from_model()
    {
        var model = new PresetItem
        {
            Scope = "AllFiles",
            VerbName = "vscode",
            DisplayName = "Open in VS Code",
            Command = "code %1",
            Icon = "code.exe,0",
            Extended = true,
            Position = "Top",
        };
        var vm = new PresetItemViewModel(model);
        var draft = vm.ToDraft();
        Assert.Equal("vscode", draft.VerbName);
        Assert.Equal("Open in VS Code", draft.DisplayName);
        Assert.Equal("code %1", draft.Command);
        Assert.Equal("code.exe,0", draft.IconExpression);
        Assert.True(draft.IsExtended);
        Assert.Equal("Top", draft.Position);
        Assert.False(draft.IsParentOnly);
    }

    [Fact]
    public void IsApplied_property_change_is_observable()
    {
        var vm = new PresetItemViewModel(new PresetItem { VerbName = "x" });
        var changed = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PresetItemViewModel.IsApplied)) changed = true; };
        vm.IsApplied = true;
        Assert.True(changed);
        Assert.True(vm.IsApplied);
    }
}
```

- [ ] **Step 2: 跑测试确认 RED**

Run: `dotnet test D:\Itair\RCMenuManager\Tests\RCMenuManager.Tests.csproj --filter "FullyQualifiedName~PresetItemViewModelTests" -v:normal`
Expected: 2 failed, 0 passed.

- [ ] **Step 3: 创建 `ViewModels/PresetItemViewModel.cs` (GREEN)**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using RCMenuManager.Models;

namespace RCMenuManager.ViewModels;

public enum PresetApplyState { Pending, Applied, Exists, Error }

public partial class PresetItemViewModel : ObservableObject
{
    public PresetItem Model { get; }
    public string Scope => Model.Scope;
    public string VerbName => Model.VerbName;
    public string DisplayName => Model.DisplayName;
    public string Description => Model.Description;
    public string CommandPreview => Model.Command;
    public string Icon => Model.Icon;
    public bool IsExtended => Model.Extended;
    public bool IsBuiltIn => Model.IsBuiltIn;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private PresetApplyState _state = PresetApplyState.Pending;
    [ObservableProperty] private string? _lastError;

    public PresetItemViewModel(PresetItem model) { Model = model; }

    public EditableVerbDraft ToDraft() => new()
    {
        VerbName = Model.VerbName,
        DisplayName = Model.DisplayName,
        Command = Model.Command,
        IconExpression = Model.Icon,
        IsExtended = Model.Extended,
        Position = Model.Position,
        IsParentOnly = false,
    };
}
```

- [ ] **Step 4: 跑测试确认 GREEN**

Run: `dotnet test D:\Itair\RCMenuManager\Tests\RCMenuManager.Tests.csproj --filter "FullyQualifiedName~PresetItemViewModelTests" -v:normal`
Expected: 2 passed, 0 failed.

- [ ] **Step 5: 提交**

```bash
cd D:\Itair\RCMenuManager
git add ViewModels/PresetItemViewModel.cs Tests/PresetItemViewModelTests.cs
git commit -m "feat: add PresetItemViewModel with PresetApplyState and 2 tests"
```

---

## Task 6: PresetDialogViewModel

**Files:**
- Create: `ViewModels/PresetDialogViewModel.cs`

`PresetDialogViewModel` 的命令交互走 smoke 覆盖 (见 Task 9). 这里只关心 WPF 友好 + 编译通过.

- [ ] **Step 1: 创建 `ViewModels/PresetDialogViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public sealed class PresetGroup
{
    public string Scope { get; }
    public string DisplayName { get; }
    public ObservableCollection<PresetItemViewModel> Items { get; } = new();
    public PresetGroup(string scope, string displayName) { Scope = scope; DisplayName = displayName; }
}

public partial class PresetDialogViewModel : ObservableObject
{
    private static readonly (string Scope, string DisplayName)[] GroupOrder =
    {
        ("AllFiles", "文件 (所有文件)"),
        ("Folder", "文件夹"),
        ("FolderBackground", "文件夹背景"),
        ("Drive", "驱动器"),
        ("Desktop", "桌面"),
    };

    private readonly IPresetService _service;

    public ObservableCollection<PresetGroup> Groups { get; } = new();
    public ObservableCollection<PresetItemViewModel> AllItems { get; } = new();

    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;

    public PresetDialogViewModel(IPresetService service)
    {
        _service = service;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var cat = _service.Load();
            AllItems.Clear();
            Groups.Clear();
            foreach (var (scope, display) in GroupOrder)
            {
                var group = new PresetGroup(scope, display);
                foreach (var p in cat.Presets.Where(p => p.Scope == scope))
                {
                    var vm = new PresetItemViewModel(p) { IsApplied = _service.IsApplied(p) };
                    vm.PropertyChanged += OnItemPropertyChanged;
                    group.Items.Add(vm);
                    AllItems.Add(vm);
                }
                Groups.Add(group);
            }
            var extItems = cat.Presets.Where(p => p.Scope.StartsWith("FileExt:", StringComparison.OrdinalIgnoreCase)).ToList();
            if (extItems.Count > 0)
            {
                var group = new PresetGroup("FileExt", "文件类型扩展");
                foreach (var p in extItems)
                {
                    var vm = new PresetItemViewModel(p) { IsApplied = _service.IsApplied(p) };
                    vm.PropertyChanged += OnItemPropertyChanged;
                    group.Items.Add(vm);
                    AllItems.Add(vm);
                }
                Groups.Add(group);
            }
            UpdateCounters();
            StatusText = $"共 {AllItems.Count} 项预设";
        }
        catch (Exception ex)
        {
            StatusText = "加载预设失败: " + ex.Message;
        }
    }

    [RelayCommand]
    public async Task ApplySelectedAsync()
    {
        var targets = AllItems.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0) return;
        IsBusy = true;
        AppliedCount = SkippedCount = ErrorCount = 0;
        try
        {
            foreach (var item in targets)
            {
                try
                {
                    await Task.Run(() => _service.Apply(item.Model, OverwriteExisting));
                    item.IsApplied = true;
                    item.State = PresetApplyState.Applied;
                    item.LastError = null;
                    AppliedCount++;
                }
                catch (PresetConflictException)
                {
                    item.State = PresetApplyState.Exists;
                    SkippedCount++;
                }
                catch (Exception ex)
                {
                    item.State = PresetApplyState.Error;
                    item.LastError = ex.Message;
                    ErrorCount++;
                }
            }
            StatusText = $"应用 {AppliedCount} · 跳过 {SkippedCount} · 失败 {ErrorCount}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Import()
    {
        var dlg = new OpenFileDialog { Filter = "预设 JSON (*.json)|*.json", Title = "导入推荐设置" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _service.Import(dlg.FileName);
            StatusText = "已导入: " + dlg.FileName;
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "导入失败: " + ex.Message;
        }
    }

    [RelayCommand]
    public void Export()
    {
        var dlg = new SaveFileDialog { Filter = "预设 JSON (*.json)|*.json", Title = "导出推荐设置", FileName = "presets.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _service.Export(dlg.FileName);
            StatusText = "已导出: " + dlg.FileName;
        }
        catch (Exception ex)
        {
            StatusText = "导出失败: " + ex.Message;
        }
    }

    [RelayCommand]
    public void SelectAllInGroup(PresetGroup? group)
    {
        if (group is null) return;
        foreach (var i in group.Items) i.IsSelected = true;
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var i in AllItems) i.IsSelected = false;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresetItemViewModel.IsSelected))
            UpdateCounters();
    }

    private void UpdateCounters()
    {
        SelectedCount = AllItems.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(HasSelection));
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`.

- [ ] **Step 3: 提交**

```bash
cd D:\Itair\RCMenuManager
git add ViewModels/PresetDialogViewModel.cs
git commit -m "feat: add PresetDialogViewModel with groups, apply/import/export commands"
```

---

## Task 7: PresetDialog UI

**Files:**
- Create: `Views/Dialogs/PresetDialog.xaml`
- Create: `Views/Dialogs/PresetDialog.xaml.cs`
- Create: `Converters/InverseBooleanToVisibilityConverter.cs`
- Create: `Converters/PresetStateToVisibilityConverter.cs`
- Modify: `App.xaml`

- [ ] **Step 1: 创建 `Views/Dialogs/PresetDialog.xaml.cs`**

```csharp
using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class PresetDialog : Window
{
    public PresetDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 2: 创建 `Converters/InverseBooleanToVisibilityConverter.cs`**

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 3: 创建 `Converters/PresetStateToVisibilityConverter.cs`**

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RCMenuManager.ViewModels;

namespace RCMenuManager.Converters;

public sealed class PresetStateToVisibilityConverter : IValueConverter
{
    public PresetApplyState TargetState { get; set; } = PresetApplyState.Exists;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PresetApplyState s && s == TargetState ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 4: 创建 `Views/Dialogs/PresetDialog.xaml`**

```xml
<Window x:Class="RCMenuManager.Views.Dialogs.PresetDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:RCMenuManager.ViewModels"
        Title="推荐设置"
        Width="760" Height="600"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        FontFamily="Segoe UI, Microsoft YaHei UI, sans-serif" FontSize="13">
    <Window.Resources>
        <Style x:Key="StatusBadge" TargetType="Border">
            <Setter Property="CornerRadius" Value="3" />
            <Setter Property="Padding" Value="6,1" />
            <Setter Property="VerticalAlignment" Value="Center" />
            <Setter Property="Margin" Value="6,0,0,0" />
        </Style>
    </Window.Resources>
    <DockPanel Margin="12">
        <Grid DockPanel.Dock="Top" Margin="0,0,0,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Button Grid.Column="0" Content="导入" Width="80" Height="28" Margin="0,0,8,0"
                    Command="{Binding ImportCommand}" />
            <Button Grid.Column="1" Content="导出" Width="80" Height="28" Margin="0,0,8,0"
                    Command="{Binding ExportCommand}" />
            <Button Grid.Column="2" Content="刷新" Width="80" Height="28"
                    Command="{Binding RefreshCommand}" />
            <CheckBox Grid.Column="4" Content="覆盖已存在的 verb"
                      IsChecked="{Binding OverwriteExisting}"
                      VerticalAlignment="Center" />
        </Grid>

        <Grid DockPanel.Dock="Bottom" Margin="0,10,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="{Binding StatusText}"
                       Foreground="#5F6B7A" FontSize="12"
                       VerticalAlignment="Center" />
            <Button Grid.Column="1" Content="应用选中" Width="100" Height="28" Margin="0,0,8,0"
                    Command="{Binding ApplySelectedCommand}"
                    IsEnabled="{Binding HasSelection}" />
            <Button Grid.Column="2" Content="关闭" Width="80" Height="28"
                    IsCancel="True" Click="OnCloseClick" />
        </Grid>

        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Groups}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate DataType="{x:Type vm:PresetGroup}">
                        <Expander Header="{Binding DisplayName}" IsExpanded="True" Margin="0,0,0,8">
                            <StackPanel>
                                <Grid Margin="4,0,4,6">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="勾选条目后点击「应用选中」"
                                               Foreground="#5F6B7A" FontSize="12" />
                                    <Button Grid.Column="1" Content="全选" Width="60" Height="24"
                                            Command="{Binding DataContext.SelectAllInGroupCommand,
                                                      RelativeSource={RelativeSource AncestorType=Window}}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                                <ItemsControl ItemsSource="{Binding Items}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate DataType="{x:Type vm:PresetItemViewModel}">
                                            <Border BorderBrush="#E1E3E8" BorderThickness="0,0,0,1"
                                                    Padding="6,6">
                                                <Grid>
                                                    <Grid.ColumnDefinitions>
                                                        <ColumnDefinition Width="Auto" />
                                                        <ColumnDefinition Width="*" />
                                                    </Grid.ColumnDefinitions>
                                                    <CheckBox Grid.Column="0" IsChecked="{Binding IsSelected, Mode=TwoWay}"
                                                              VerticalAlignment="Top" Margin="0,2,8,0" />
                                                    <StackPanel Grid.Column="1">
                                                        <StackPanel Orientation="Horizontal">
                                                            <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" />
                                                            <Border Style="{StaticResource StatusBadge}" Background="#E0E7FF">
                                                                <TextBlock Text="Shift" FontSize="11" Foreground="#1D4ED8"
                                                                           Visibility="{Binding IsExtended, Converter={StaticResource BoolToVis}}"/>
                                                            </Border>
                                                            <Border Style="{StaticResource StatusBadge}" Background="#FEF3C7">
                                                                <TextBlock Text="用户" FontSize="11" Foreground="#9A6700"
                                                                           Visibility="{Binding IsBuiltIn, Converter={StaticResource InvertBoolToVis}}"/>
                                                            </Border>
                                                            <Border Style="{StaticResource StatusBadge}" Background="#D1FAE5">
                                                                <TextBlock Text="已应用" FontSize="11" Foreground="#1F7A3A"
                                                                           Visibility="{Binding IsApplied, Converter={StaticResource BoolToVis}}"/>
                                                            </Border>
                                                            <Border Style="{StaticResource StatusBadge}" Background="#FEE2E2">
                                                                <TextBlock Text="已存在" FontSize="11" Foreground="#C9211E"
                                                                           Visibility="{Binding State, Converter={StaticResource StateToExistsVis}}"/>
                                                            </Border>
                                                            <Border Style="{StaticResource StatusBadge}" Background="#FEE2E2">
                                                                <TextBlock Text="错误" FontSize="11" Foreground="#C9211E"
                                                                           Visibility="{Binding State, Converter={StaticResource StateToErrorVis}}"/>
                                                            </Border>
                                                        </StackPanel>
                                                        <TextBlock Text="{Binding Description}" Foreground="#5F6B7A" FontSize="12" />
                                                        <TextBlock Text="{Binding CommandPreview}" Foreground="#1F2937" FontSize="11" FontFamily="Consolas, monospace" TextWrapping="Wrap" />
                                                    </StackPanel>
                                                </Grid>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Expander>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

- [ ] **Step 5: 在 `App.xaml` 注册转换器**

Replace the entire `App.xaml` content with:

```xml
<Application x:Class="RCMenuManager.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:RCMenuManager"
             xmlns:converters="clr-namespace:RCMenuManager.Converters">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Resources/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <converters:BoolToVisibilityConverter x:Key="BoolToVis" />
            <converters:InverseBooleanToVisibilityConverter x:Key="InvertBoolToVis" />
            <converters:PresetStateToVisibilityConverter x:Key="StateToExistsVis" TargetState="Exists" />
            <converters:PresetStateToVisibilityConverter x:Key="StateToErrorVis" TargetState="Error" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 6: 验证编译 + XAML 资源**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`. 资源查找错误请按 App.xaml 调整.

- [ ] **Step 7: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Views/Dialogs/PresetDialog.xaml Views/Dialogs/PresetDialog.xaml.cs Converters/InverseBooleanToVisibilityConverter.cs Converters/PresetStateToVisibilityConverter.cs App.xaml
git commit -m "feat: add PresetDialog UI with grouped expanders, badges, apply/import/export"
```

---

## Task 8: Wire ScopeBar button + MainViewModel.ShowPresetsCommand + DI

**Files:**
- Modify: `Views/Controls/ScopeBar.xaml`
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `App.xaml.cs`

- [ ] **Step 1: 在 `ScopeBar.xaml` 加 "推荐" 按钮 (在 "备份" 之后)**

Current ScopeBar.xaml has 9 `ColumnDefinition`s; the "刷新" button sits in Column 8 with `Width="Auto"` and a `Width="*"` spacer sits in Column 7. We will:
1. Change Column 7 from `Width="*"` to `Width="Auto"` so it can hold the new "推荐" button.
2. Add a new Column 8 with `Width="*"` to act as the new spacer.
3. Bump the "刷新" button from `Grid.Column="8"` to `Grid.Column="9"`.
4. Insert a new "推荐" button at `Grid.Column="7"`.

Replacement Grid block (also seen in the file under the root `<Grid>`):

```xml
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="320" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="160" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
```

New "推荐" button (insert between the "备份" button and the spacer in the existing markup):

```xml
        <Button Grid.Column="7" Content="推荐" Margin="6,0,0,0" Padding="12,4"
                Command="{Binding ShowPresetsCommand}" />
```

Change the "刷新" button's `Grid.Column="8"` to `Grid.Column="9"`.

- [ ] **Step 2: 在 `MainViewModel` 注入 `IPresetService` + 加 `ShowPresetsCommand`**

Edit `ViewModels/MainViewModel.cs`:

1. Add field next to `_fileTypes`:
   ```csharp
   private readonly IPresetService _presets;
   ```

2. Add `IPresetService presets` to the constructor parameter list (after `IFileTypeService fileTypes`):
   ```csharp
   public MainViewModel(
       RegistryService registry, MenuParserService parser, IconService icons,
       RegistryWriteService writer, IBackupService backup, IOperationLog log,
       IWin11MenuService win11, WinVersionService ver,
       IFileTypeService fileTypes, IPresetService presets) {
       _registry = registry;
       _parser = parser;
       _icons = icons;
       _writer = writer;
       _backup = backup;
       _log = log;
       _win11 = win11;
       _ver = ver;
       _fileTypes = fileTypes;
       _presets = presets;
       ...
   }
   ```

3. Add this command at the end of the class body:
   ```csharp
   [RelayCommand]
   private void ShowPresets()
   {
       var owner = Application.Current?.MainWindow;
       var vm = new PresetDialogViewModel(_presets);
       var dlg = new PresetDialog { Owner = owner, DataContext = vm };
       dlg.ShowDialog();
   }
   ```

- [ ] **Step 3: 在 `App.xaml.cs` 注册 `IPresetService`**

Edit `App.xaml.cs` `OnStartup` — insert the following line right after the `IFileTypeService` registration:

```csharp
            services.AddSingleton<IPresetService, PresetService>();
```

(Place it BEFORE the `RegistryWriteService` registration so DI ordering matches the design spec narrative.)

- [ ] **Step 4: 验证编译**

Run: `dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal`
Expected: `Build succeeded`. DI resolves `MainViewModel` by type, so parameter order in the constructor is not sensitive.

- [ ] **Step 5: 提交**

```bash
cd D:\Itair\RCMenuManager
git add Views/Controls/ScopeBar.xaml ViewModels/MainViewModel.cs App.xaml.cs
git commit -m "feat: wire 推荐 button on ScopeBar and ShowPresetsCommand in MainViewModel"
```

---

## Task 9: Smoke checklist + 全量验证

**Files:**
- Create: `docs/superpowers/smoke/2026-06-17-phase7-smoke.md`

- [ ] **Step 1: 写 smoke 清单**

```markdown
# Phase 7 手工冒烟 (推荐设置)

## 前置
- 已 `dotnet build` 成功
- 启动 RCMenuManager (推荐以管理员身份运行, 但非必须 — 全程 HKCU)

## 验证步骤

1. 打开对话框: 点 ScopeBar 上的 "推荐" 按钮, 对话框应弹出, 5 个分组 (文件 / 文件夹 / 文件夹背景 / 驱动器 / 桌面) 全部展开, 每组下列出对应预设.
2. 状态文本: 底部状态栏应显示 "共 N 项预设" (N = 35+).
3. 未应用: 之前没装过的预设, 右侧不显示 "已应用" 徽章.
4. 勾选 + 应用: 勾选 "文件 / 用记事本打开" 和 "文件 / 复制文件路径" 两项, 点 "应用选中". 状态应变成 "应用 2 · 跳过 0 · 失败 0", 两条右侧出现 "已应用" 徽章.
5. 注册表: 打开 `regedit`, 确认:
   - `HKCU\Software\Classes\*\shell\notepad\(Default)` = "用记事本打开"
   - `HKCU\Software\Classes\*\shell\notepad\Icon` = "imageres.dll,-64"
   - `HKCU\Software\Classes\*\shell\notepad\command\(Default)` = `notepad.exe "%1"`
   - `HKCU\Software\Classes\*\shell\copypath\command\(Default)` = `cmd /c echo "%1" | clip`
6. 真实菜单: 在资源管理器中右键任意 .txt, 应出现 "用记事本打开" 项. (部分场景需要 `ie4uinit.exe -show` 刷新图标缓存.)
7. 冲突处理: 不勾选 "覆盖已存在的 verb", 再次勾选 "用记事本打开" + 应用. 状态徽章变 "已存在", 状态栏 +1 跳过, 原 verb 不被修改.
8. 覆盖处理: 勾选 "覆盖已存在的 verb", 再点 "应用选中". 原 verb 被删 + 重建, 备份 .reg 出现在 `%LocalAppData%\RCMenuManager\backups\`.
9. Shift 预设: 应用 "文件 / 以管理员身份运行" (extended=true). 注册表应有 `Extended = ""` 空值. 资源管理器中 Shift+右键应看到该项.
10. 导入: 准备一个简单 JSON 文件, 用 "导入" 按钮选它. 状态栏显示 "已导入: <path>".
11. 导出: 点 "导出", 选个路径, 状态栏显示 "已导出". 打开文件, 确认包含内置预设.
12. 删除清理: 在注册表中手动删除测试创建的 verb, 回到对话框, 点 "刷新", "已应用" 徽章消失.
13. 重启验证: 关闭再打开 RCMenuManager, 推荐对话框再次打开, IsApplied 状态应保持正确.

## 清理

```powershell
Remove-Item -Path "HKCU:\Software\Classes\*\shell\notepad" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Classes\*\shell\copypath" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Classes\*\shell\runas" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "$env:LOCALAPPDATA\RCMenuManager\presets.json" -ErrorAction SilentlyContinue
```
```

- [ ] **Step 2: 全量构建 + 全量测试**

Run:
```bash
dotnet build D:\Itair\RCMenuManager\RCMenuManager.csproj -c Debug -v:minimal
dotnet test  D:\Itair\RCMenuManager\Tests\RCMenuManager.Tests.csproj -c Debug --no-restore -v:normal
```

Expected:
- `Build succeeded` 0 错误. 允许中文注释相关的 IDE0086 等无害 warning.
- `Passed: ...` 测试总数 = 现有 + 11 (PresetServiceTests) + 2 (PresetItemViewModelTests). 全部 Passed, 0 Failed.

有 Failed 必须修复, 不得跳过.

- [ ] **Step 3: 提交 smoke**

```bash
cd D:\Itair\RCMenuManager
git add docs/superpowers/smoke/2026-06-17-phase7-smoke.md
git commit -m "docs: phase 7 manual smoke checklist"
```

---

## Self-Review

设计文档 `docs/superpowers/specs/2026-06-18-phase7-presets-design.md` Section 4 列了 10 个组件, 本计划对应:

| 设计文档组件 | 计划 Task |
|---|---|
| 4.1 PresetItem | Task 2 |
| 4.2 PresetCatalog | Task 2 |
| 4.3 PresetConflictException | Task 3 |
| 4.4 IPresetService | Task 4 |
| 4.5 PresetService | Task 4 |
| 4.6 PresetItemViewModel | Task 5 |
| 4.7 PresetDialogViewModel + PresetGroup | Task 6 |
| 4.8 PresetDialog.xaml | Task 7 |
| 4.9 ScopeBar 改动 | Task 8 |
| 4.10 MainViewModel.ShowPresetsCommand | Task 8 |
| 5. App.xaml.cs DI | Task 8 |
| 8. PresetServiceTests | Task 4 |
| 8. PresetItemViewModelTests | Task 5 |

全覆盖. Smoke (Task 9) 覆盖 Section 6 / 7 的 UI 行为 + 错误处理.

## 类型一致性自检

- `IPresetService.Apply(item, overwrite)` 签名在 Service 4.5, VM 6 都用 `bool overwrite` ✓
- `EditableVerbDraft` 字段 (VerbName, DisplayName, Command, IconExpression, IsExtended, Position, IsParentOnly) 跟现有 `Models/EditableVerbDraft.cs` 完全一致 ✓
- `MenuScope.HkcuShellSubKey` (`Models/MenuScope.cs` 已有) — 跟 `PresetService.IsApplied` / `Apply` 一致 ✓
- `MenuScope.FromScopeId(...)` (`Models/MenuScope.cs` 已有) — 跟 `PresetService.IsApplied` / `Apply` 一致 ✓
- `RegistryWriteService.CreateRootVerb(hive, parentSubKey, scopeId, draft)` (`Services/RegistryWriteService.cs` 已有) — 签名匹配 ✓
- `RegistryWriteService.Delete(hive, subKey, scopeId, verbName)` — 用于 overwrite ✓

## 风险点

- XAML 资源 `StaticResource BoolToVis` 可能在 `App.xaml` 之前没注册: Task 7 Step 5 显式注册. 如果原 XAML 引用没问题, 这个改动是无害的.
- DI 参数顺序: 加 `IPresetService presets` 之后, `App.xaml.cs` 容器按类型解析, 无需按顺序.
- `BackupService` 依赖 `reg.exe` — 在测试中通过 `RecordingBackup` 桩类规避, 不会调到真实 reg.exe.

---

## 完成定义

Phase 7 完成当:
1. `dotnet build` 0 错误.
2. `dotnet test` 全部 pass (含 13 个新测试).
3. `git log --oneline` 显示 8+ 个 feat/docs commit.
4. Smoke 清单 13 步可执行 (不强制在 CI 中跑, 但要写完).
5. 主程序能正常启动 (visual check 在 smoke 步骤 1-3).
