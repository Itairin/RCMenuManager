# Phase 7: 推荐设置 设计文档

## 1. 目标

让用户打开 RCMenuManager 后能通过「推荐」按钮进入对话框，看到 `DeDocs/PRESETS.md` 里那 35+ 个常用右键菜单预设（记事本打开、VS Code、终端、哈希校验、磁盘管理等），按作用域分组，可勾选并一键应用到自己的 HKCU 注册表；同时支持 JSON 导入 / 导出 / 自定义持久化。整套流程走现有的 `RegistryWriteService`，自动获得 backup / log / 提权 / SHChangeNotify。

## 2. 范围

**In scope**

- `Resources/presets.json`（35+ 个内置预设，按 `PRESETS.md` 章节落地）
- `Models/PresetItem.cs` + `Models/PresetCatalog.cs`（POCO，System.Text.Json 序列化）
- `Services/IPresetService.cs` + `Services/PresetService.cs`（加载合并、IsApplied、应用、导入 / 导出 / 保存用户预设）
- `Services/PresetConflictException.cs`（verb 已存在时抛）
- `ViewModels/PresetItemViewModel.cs`（ObservableObject + IsSelected / IsApplied / IsBusy / ApplyCommand）
- `ViewModels/PresetDialogViewModel.cs`（分组列表 + 全选 / 应用选中 / 导入 / 导出 / 覆盖 / 状态）
- `Views/Dialogs/PresetDialog.xaml` + `.xaml.cs`（对话框 UI）
- ScopeBar 第 4 个按钮「推荐」+ `MainViewModel.ShowPresetsCommand`
- App.xaml.cs DI 注册 `IPresetService` + `PresetDialog`
- Tests：`PresetServiceTests`、`PresetItemViewModelTests`

**Out of scope（YAGNI）**

- 自动按文件类型选预设
- 检测软件是否安装后再决定是否显示某条预设（如「装了 VS Code 才显示 vscode 项」）
- 撤销 / 重做
- 预设 schema 升级迁移（version 字段保留，目前只支持 1.0）
- HKCR 写入（PRESETS.md 全部走 HKCU，不需提权）
- 计划任务 / 后台轮询

## 3. 架构

```
+-----------------------------------------------------+
|  MainWindow (ScopeBar)                              |
|    +---------------------------------------------+  |
|    |  Win11  备份  推荐  <-- 新按钮              |  |
|    +---------------------------------------------+  |
+-----------------------------------------------------+
             | ShowPresetsCommand
             v
+-----------------------------------------------------+
|  PresetDialog                                       |
|  +-----------------------------------------------+  |
|  |  PresetDialogViewModel                        |  |
|  |  - Groups (File / Folder / FolderBackground / |  |
|  |    Desktop / Drive)                          |  |
|  |  - Items: ObservableCollection<               |  |
|  |             PresetItemViewModel>             |  |
|  |  - Commands: ApplySelected / Refresh /       |  |
|  |              Import / Export                 |  |
|  +-----------------------------------------------+  |
|             | Inject IPresetService                |
|             v                                       |
|  PresetService                                       |
|    Load()         -> merge(Resources/presets.json,  |
|                          %LocalAppData%/presets.json) |
|    IsApplied(p)   -> HKCU\Software\Classes\<scope>\  |
|                       shell\<verb> exists?           |
|    Apply(p, ow)   -> RegistryWriteService            |
|                      .CreateRootVerb(draft)         |
|    SaveUser(p)    -> %LocalAppData%/presets.json     |
|    Import(path)   -> SaveUser(parsed)                |
|    Export(path)   -> write JSON                      |
+-----------------------------------------------------+
```

## 4. 组件

### 4.1 Models/PresetItem.cs

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

### 4.2 Models/PresetCatalog.cs

```csharp
namespace RCMenuManager.Models;

public sealed class PresetCatalog
{
    public string Version { get; set; } = "1.0";
    public List<PresetItem> Presets { get; set; } = new();
}
```

### 4.3 Services/PresetConflictException.cs

```csharp
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

### 4.4 Services/IPresetService.cs

```csharp
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

### 4.5 Services/PresetService.cs

- 构造接收 IRegistryWriter（读 IsApplied）+ RegistryWriteService（应用）+ 可选 string? overrideBuiltInPath（测试）。
- Load()：先读 Resources/presets.json（build 时复制到输出目录），再读 %LocalAppData%\RCMenuManager\presets.json；用户条目按 (Scope, VerbName) 覆盖内置；返回 PresetCatalog。
- IsApplied(item)：用 IRegistryWriter.KeyExists(RegistryHive.CurrentUser, "Software\\Classes\\" + Scope + "\\shell\\" + VerbName)。
- Apply(item, overwrite)：
  - 拼 EditableVerbDraft { VerbName, DisplayName, Command, IconExpression=Icon, IsExtended=Extended, Position, IsParentOnly=false }
  - 拼 parentShellSubKey = "Software\\Classes\\" + ScopeMap.ToShellSubKey(Scope)
  - 调 RegistryWriteService.CreateRootVerb(RegistryHive.CurrentUser, parentShellSubKey, scopeId=Scope, draft)
  - 若 RegistryConflictException 且 overwrite==true：先 RegistryWriteService.Delete(...) 再调 CreateRootVerb
  - 若 RegistryConflictException 且 overwrite==false：抛 PresetConflictException
  - 应用成功后写 OperationLog（已由 RegistryWriteService 写，不重复）
- SaveUserPreset：追加 / 覆盖到 %LocalAppData%\RCMenuManager\presets.json（IsBuiltIn=false）。
- Import(path)：读 JSON -> SaveUserPreset 逐条。
- Export(path)：把当前 Load() 结果写 JSON（保留 IsBuiltIn 标记）。

### 4.6 ViewModels/PresetItemViewModel.cs

```csharp
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
    public EditableVerbDraft ToDraft() => new() {
        VerbName = Model.VerbName,
        DisplayName = Model.DisplayName,
        Command = Model.Command,
        IconExpression = Model.Icon,
        IsExtended = Model.Extended,
        Position = Model.Position,
        IsParentOnly = false,
    };
}

public enum PresetApplyState { Pending, Applied, Exists, Error }
```

### 4.7 ViewModels/PresetDialogViewModel.cs

```csharp
public partial class PresetDialogViewModel : ObservableObject
{
    private readonly IPresetService _service;
    public ObservableCollection<PresetItemViewModel> AllItems { get; } = new();
    public ObservableCollection<PresetGroup> Groups { get; } = new();

    [ObservableProperty] private bool _overwriteExisting;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;

    [RelayCommand] private async Task RefreshAsync();
    [RelayCommand] private async Task ApplySelectedAsync();
    [RelayCommand] private void Import(string filePath);
    [RelayCommand] private void Export(string filePath);
    [RelayCommand] private void SelectAllInGroup(PresetGroup? group);
    [RelayCommand] private void ClearSelection();
}

public sealed class PresetGroup
{
    public string Scope { get; }
    public string DisplayName { get; }
    public ObservableCollection<PresetItemViewModel> Items { get; } = new();
    public PresetGroup(string scope, string displayName) { Scope = scope; DisplayName = displayName; }
}
```

### 4.8 Views/Dialogs/PresetDialog.xaml

- 顶部工具栏：导入 / 导出 / 刷新
- 中间滚动区：5 个 Expander（按 scope 分组），每组一个 ListBox，每行包含：复选框 + DisplayName + 描述 + 状态徽章 + 「应用」单条按钮
- 底部状态栏：「☐ 覆盖已存在的 verb」+ 状态文本 + 「应用选中」+「关闭」

### 4.9 ScopeBar 改动

Views/Controls/ScopeBar.xaml 增加第 4 个按钮（备份按钮之后、刷新按钮之前）：

```xml
<Button Grid.Column="?" Content="推荐" Margin="6,0,0,0" Padding="12,4"
        Command="{Binding ShowPresetsCommand}" />
```

### 4.10 MainViewModel.ShowPresetsCommand

跟 ShowBackupsCommand / ShowWin11Command 同模式：

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

ctor 注入 IPresetService _presets。

## 5. 数据流

1. 用户点「推荐」按钮 -> MainViewModel.ShowPresets -> new PresetDialogViewModel(_presets) -> 构造里 _service.Load() + 分组 -> ShowDialog()。
2. 用户勾选若干条 + 点「应用选中」 -> ApplySelectedAsync：
   - 对每条选中的 preset，try Apply(preset, OverwriteExisting)：
     - 成功 -> item.State = Applied；IsApplied = true；appliedCount++
     - 抛 PresetConflictException -> item.State = Exists；skippedCount++
     - 抛其他异常 -> item.State = Error；LastError = ex.Message；errorCount++
3. 用户点「导入」 -> Import 命令接收 OpenFileDialog.FileName -> _service.Import(path) -> RefreshAsync。
4. 用户点「导出」 -> Export 命令接收 SaveFileDialog.FileName -> _service.Export(path)。

## 6. UI 行为

| 场景 | 行为 |
|---|---|
| 打开对话框 | 自动 Load()，5 个分组按字母顺序展开 |
| 勾选 + 点「应用选中」 | 逐条应用，状态实时更新；底部状态栏显示 应用 X / 跳过 Y / 失败 Z |
| 不勾选任何项 | 「应用选中」按钮 IsEnabled = false |
| verb 已存在 + 未勾「覆盖」 | 状态徽章「已存在」，skippedCount++ |
| verb 已存在 + 勾「覆盖」 | 先 Delete 再 CreateRootVerb（自动 backup + log） |
| 「全选」分组按钮 | 选中该组所有项（仅 UI） |
| 「导入」成功 | 状态栏「已导入 N 条」，自动 Refresh |
| 「导出」成功 | 状态栏「已导出 N 条到 <path>」 |
| 当前未提权 | 不需提权（全部走 HKCU），UI 不弹提权对话框 |
| 拖入文件 / 切作用域时 | 对话框不自动关闭（独立窗口） |
| 应用过程中关闭窗口 | 用 CancellationToken / if (dlg.IsVisible) 保护，关闭窗口即停 |

## 7. 错误处理

| 异常 | 处理 |
|---|---|
| RegistryConflictException 且未勾「覆盖」 | 抛 PresetConflictException -> 状态徽章「已存在」 |
| RegistryConflictException 且已勾「覆盖」 | 内部吞掉，先 Delete 再 CreateRootVerb |
| UnauthorizedAccessException（HKCU 写被拒） | 状态徽章「错误：拒绝访问」，LastError 填 message |
| 导入 JSON 解析失败 | 状态栏「导入失败：<message>」，不破坏现有预设 |
| 导出 IO 失败 | 状态栏「导出失败：<message>」 |
| Resources/presets.json 缺失（构建异常） | 启动时 Load() 抛 -> 构造函数捕获 -> 状态栏「内置预设加载失败：<message>」 |

## 8. 测试

### Tests/PresetServiceTests.cs（用 InMemoryRegistryWriter）

- Load_merges_builtin_and_user：内置 2 + 用户 1 -> 3，按 (Scope, VerbName) 用户覆盖内置
- Load_returns_empty_catalog_when_no_files：测试隔离目录下两个文件都不存在
- IsApplied_true_when_hkcu_verb_key_exists：写一个 verb 进去，断言 true
- IsApplied_false_for_hklm_only：HKLM 有 HKCU 没有 -> false
- Apply_creates_verb_in_hkcu_software_classes：走 CreateRootVerb 路径，断言 Software\Classes\*\shell\notepad\(Default) = "用记事本打开"
- Apply_raises_preset_conflict_when_existing_and_no_overwrite：默认行为
- Apply_overwrites_when_flag_set：先 Delete 再写，断言成功
- SaveUserPreset_persists_to_user_path：写后 Load 能读回
- Import_replaces_duplicate_by_scope_verbname：用户 vscode 覆盖内置 vscode
- Export_roundtrips：Export -> 再 Load -> 内容等价
- Scope_to_shell_subkey_mapping：覆盖 5 个 scope

### Tests/PresetItemViewModelTests.cs

- IsApplied_propagates_via_property_changed
- ToDraft_copies_all_fields

不测 PresetDialog.xaml（smoke 覆盖）；不测 PresetDialogViewModel 的命令交互（需要 mock IPresetService + mock 异步流，smoke 覆盖）。

## 9. 风险

| 风险 | 缓解 |
|---|---|
| 35+ 个 verb 一键应用，写入耗时 | 串行 + 状态徽章；单条操作有 backup + log，未来可加 progress |
| 用户导入恶意 JSON | 解析时验证 scope 枚举、verbName 字符限制（[A-Za-z0-9_-]{1,64}） |
| 应用过程中用户取消 | IsCancelable + 关闭时停掉 background loop |
| EditableVerbDraft 字段变更影响 | 转换点集中（PresetItemViewModel.ToDraft），单测覆盖 |
| Resources/presets.json 大小 | 35 条 ~ 8KB，build 时一次性复制到输出，运行时只读一次 |
| presets.json 用户文件损坏 | Load 捕获并把坏文件备份到 .bak，继续加载内置 |

## 10. 实施顺序

1. Models（PresetItem / PresetCatalog）- 无依赖
2. PresetConflictException
3. Resources/presets.json（数据优先）
4. IPresetService + PresetService（含 InMemoryRegistryWriter 集成测试）
5. PresetItemViewModel
6. PresetDialogViewModel
7. PresetDialog.xaml + .xaml.cs
8. ScopeBar 按钮 + MainViewModel.ShowPresetsCommand + App.xaml.cs DI
9. smoke 清单 + 最终验证