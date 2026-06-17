# Phase 5: Win11 新菜单控制 设计文档

## 1. 目标

让用户在 RCMenuManager 内一键切换 Win11 新版右键菜单（"显示更多选项"折叠菜单），并能管理 Win11 新菜单的 Block 列表（哪些应用被强制隐藏在新菜单里）。

具体新增一个 `Win11Dialog` 弹窗，从 `ScopeBar` 顶部按钮打开：
- 顶部一个大 `ToggleButton`，开/关 Win11 新菜单
- 中间一个 `DataGrid` 列出 Block 列表里的所有 verb，附 [移除] 按钮
- 底部"重启资源管理器"按钮 + "关闭" 按钮 + 状态栏

切换新菜单后必须重启 `explorer.exe` 才生效；本应用只写注册表 + 提示用户，不替用户重启 Explorer（避免误杀用户当前资源管理器会话里的拖拽 / 复制进度）。

## 2. 范围

**In scope：**
- `Services/WinVersionService.cs`（新）：`IsWindows11` 属性，根据 `Environment.OSVersion.Version.Build >= 22000` 判定。
- `Services/Win11MenuService.cs`（新）：
  - `bool IsNewMenuEnabled`：读 `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` 键的存在性（键存在 = 新菜单被禁用 = `IsNewMenuEnabled == false`）。
  - `void SetNewMenuEnabled(bool enabled)`：开启时删 `InprocServer32`；关闭时建 `InprocServer32` + `(Default)=""`。
  - `IReadOnlyList<Win11BlockItem> GetBlockList()`：枚举 `HKCU\Software\Microsoft\Windows\CurrentVersion\Shell\Block` 下的子键名。
  - `void RemoveFromBlock(string verbName)`：删对应子键。
  - `void RestartExplorer()`：`taskkill /f /im explorer.exe` + `Start-Process explorer.exe`。
- `Models/Win11BlockItem.cs`（新）：`sealed record Win11BlockItem(string VerbName)`
- `ViewModels/Win11DialogViewModel.cs`（新）：`IsNewMenuEnabled`（双向）/ `Blocks` 集合 / `IsWindows11` / `StatusText` / `RestartExplorerCommand` / `RemoveBlockCommand` / `RefreshCommand` / `IsBusy`
- `Views/Dialogs/Win11Dialog.xaml` + code-behind（新）：布局如上
- `Views/Controls/ScopeBar.xaml`（改）：在"备份"按钮前新增"Win11"按钮，`IsEnabled` 绑 `VersionInfo.IsWindows11`（**非 Win11 显示但禁用** = 决策 B）
- `ViewModels/MainViewModel.cs` + `MainViewModel.Commands.cs`（改）：加 `ShowWin11Command`、暴露 `VersionInfo`，ctor 注入 `IWin11MenuService` + `WinVersionService`
- `App.xaml.cs`（改）：DI 注册 `WinVersionService` + `IWin11MenuService → Win11MenuService`
- 单元测试：`WinVersionService` 派生类 mock；`Win11MenuService` 写 HKCU 沙箱

**Out of scope（YAGNI）：**
- Block 列表的"添加"按钮（用户用 `regedit` / 专用工具）
- 自动重启 Explorer（永远需要用户主动确认）
- `IsNewMenuEnabled` 切换后**自动**杀 Explorer
- Win10 系统的 Win11 风格强制启用
- 任务栏 / 通知区域 / Explorer 其他设置的 Win11 行为定制

## 3. 架构

```
ScopeBar 顶部 +"Win11" 按钮（IsEnabled 绑 VersionInfo.IsWindows11）
   └─ ShowWin11Command → Win11Dialog.Show(Application.Current.MainWindow)
        └─ Win11Dialog 持有 Win11DialogViewModel
             ├─ ToggleButton (IsChecked 绑 IsNewMenuEnabled 双向)
             ├─ DataGrid<Win11BlockItem>  + 移除按钮
             ├─ 状态栏: StatusText
             └─ 底部: [重启资源管理器]  [关闭]

Services
  WinVersionService            ─→ IsWindows11 : bool (ctor 一次性计算)
  IWin11MenuService ─→ Win11MenuService
    .IsNewMenuEnabled          : bool
    .SetNewMenuEnabled(bool)
    .GetBlockList()            : IReadOnlyList<Win11BlockItem>
    .RemoveFromBlock(verbName)
    .RestartExplorer()         : void
```

注册表路径：
- 切换开关：`HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`
  - 开启（用新菜单）：删 `InprocServer32` 子键（系统默认）
  - 关闭（回经典）：建 `InprocServer32` 子键 + `(Default) = ""`（空字符串）
  - 备注：官方文档与社区一致用空字符串而非完全不建键，更稳
- Block 列表：`HKCU\Software\Microsoft\Windows\CurrentVersion\Shell\Block`
  - 每个子键名 = 一个被隐藏的 verb
  - dev doc 写的 `Shell Extensions\Blocked` 是旧版"按 CLSID 阻塞 Shell 扩展"，与 Win11 新菜单"按 verb 名称隐藏"不是同一路径。本设计采用 Win11 实际行为正确的 `Shell\Block`。

## 4. 组件

### 4.1 `Services/WinVersionService.cs`（新）

```csharp
using System;

namespace RCMenuManager.Services;

public class WinVersionService
{
    public virtual bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;
}
```

`virtual` 让测试用派生类覆写。`IsWindows11` 是属性（不是方法），XAML 可直接绑。

### 4.2 `Services/IWin11MenuService.cs`（新）

```csharp
using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IWin11MenuService
{
    bool IsNewMenuEnabled { get; }
    void SetNewMenuEnabled(bool enabled);
    IReadOnlyList<Models.Win11BlockItem> GetBlockList();
    void RemoveFromBlock(string verbName);
    void RestartExplorer();
}
```

### 4.3 `Services/Win11MenuService.cs`（新）

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace RCMenuManager.Services;

public sealed class Win11MenuService : IWin11MenuService
{
    private const string DefaultToggleRoot = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    private const string DefaultBlockRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell\Block";
    private const string InprocSubKey = "InprocServer32";

    private readonly string _toggleRoot;
    private readonly string _blockRoot;

    public Win11MenuService() : this(DefaultToggleRoot, DefaultBlockRoot) { }
    public Win11MenuService(string toggleRoot, string blockRoot)
    {
        _toggleRoot = toggleRoot;
        _blockRoot = blockRoot;
    }

    public bool IsNewMenuEnabled => !KeyExists(_toggleRoot);

    public void SetNewMenuEnabled(bool enabled)
    {
        var inprocPath = _toggleRoot + @"\" + InprocSubKey;
        var exists = KeyExists(_toggleRoot);
        if (enabled && exists)
        {
            DeleteTree(_toggleRoot);
        }
        else if (!enabled && !exists)
        {
            CreateKey(inprocPath);
            SetDefault(inprocPath, "");
        }
    }

    public IReadOnlyList<Models.Win11BlockItem> GetBlockList()
    {
        var list = new List<Models.Win11BlockItem>();
        using var root = Registry.CurrentUser.OpenSubKey(_blockRoot, writable: false);
        if (root is null) return list;
        foreach (var name in root.GetSubKeyNames())
            list.Add(new Models.Win11BlockItem(name));
        return list;
    }

    public void RemoveFromBlock(string verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName)) return;
        DeleteTree(_blockRoot + @"\" + verbName);
    }

    public void RestartExplorer()
    {
        var kill = new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
            Arguments = "/f /im explorer.exe",
        };
        using (var p = Process.Start(kill))
        {
            p?.WaitForExit(3000);
        }
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
    }

    // ---- private registry helpers ----
    private static bool KeyExists(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return k is not null;
    }

    private static void CreateKey(string subKey)
    {
        using var k = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
    }

    private static void SetDefault(string subKey, string value)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        k?.SetValue("", value, RegistryValueKind.String);
    }

    private static void DeleteTree(string subKey)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }
}
```

> - `IsNewMenuEnabled` 语义：用户视角的"Win11 新菜单是否生效"。键不存在 = 新菜单生效（默认）。
> - 切换时**不**自动重启 Explorer；用户点"重启资源管理器"按钮触发。
> - `RestartExplorer` 同步阻塞，调用方应放在后台线程（`Task.Run`）。
> - 用 `Microsoft.Win32.Registry` 直接操作（不绕 `IRegistryWriter`），与 `BackupService` / `MenuParserService` 一致。
> - ctor 接受可选的 `(toggleRoot, blockRoot)` 参数供测试注入沙箱路径；DI 容器用无参 ctor。
### 4.4 `Models/Win11BlockItem.cs`（新）

```csharp
namespace RCMenuManager.Models;

public sealed record Win11BlockItem(string VerbName);
```

极简 record。WPF DataGrid 用 `VerbName` 作显示列。

### 4.5 `ViewModels/Win11DialogViewModel.cs`（新）

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCMenuManager.Helpers;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.ViewModels;

public partial class Win11DialogViewModel : ObservableObject
{
    private readonly IWin11MenuService _svc;
    private readonly WinVersionService _ver;

    public ObservableCollection<Win11BlockItem> Blocks { get; } = new();

    [ObservableProperty] private bool _isWindows11;
    [ObservableProperty] private bool _isNewMenuEnabled;
    [ObservableProperty] private Win11BlockItem? _selectedBlock;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isBusy;

    public bool HasSelection => SelectedBlock is not null;

    public Win11DialogViewModel(IWin11MenuService svc, WinVersionService ver)
    {
        _svc = svc;
        _ver = ver;
        IsWindows11 = ver.IsWindows11;
        if (IsWindows11) Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (!IsWindows11) return;
        try
        {
            IsNewMenuEnabled = _svc.IsNewMenuEnabled;
            Blocks.Clear();
            foreach (var b in _svc.GetBlockList().OrderBy(b => b.VerbName))
                Blocks.Add(b);
            StatusText = $"共 {Blocks.Count} 项 Block";
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (Exception ex)
        {
            StatusText = "读取失败：" + ex.Message;
        }
    }

    partial void OnIsNewMenuEnabledChanged(bool value)
    {
        if (!IsWindows11) return;
        try
        {
            _svc.SetNewMenuEnabled(value);
            StatusText = value
                ? "已切换到 Win11 新菜单（需重启资源管理器）"
                : "已切换到经典菜单（需重启资源管理器）";
        }
        catch (Exception ex)
        {
            StatusText = "切换失败：" + ex.Message;
            IsNewMenuEnabled = _svc.IsNewMenuEnabled; // 复原 UI
        }
    }

    partial void OnSelectedBlockChanged(Win11BlockItem? value) => OnPropertyChanged(nameof(HasSelection));

    [RelayCommand]
    private void RemoveBlock(Win11BlockItem? item)
    {
        if (item is null) return;
        try
        {
            _svc.RemoveFromBlock(item.VerbName);
            StatusText = "已移除 " + item.VerbName;
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "移除失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (!IsWindows11) return;
        var ok = ConfirmDialog.Show(
            "重启资源管理器",
            "将结束所有 explorer.exe 进程后重新拉起。进行中的复制 / 移动窗口会丢失进度，确认继续？",
            confirmText: "重启", isDestructive: true);
        if (!ok) return;
        IsBusy = true;
        StatusText = "正在重启资源管理器 ...";
        try
        {
            await Task.Run(() => _svc.RestartExplorer());
            StatusText = "已重启资源管理器";
        }
        catch (Exception ex)
        {
            StatusText = "重启失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

> - `OnIsNewMenuEnabledChanged` 内捕获异常并复原 UI 状态。
> - 非 Win11 时所有写操作被短路，XAML 端切到提示模板。

### 4.6 `Views/Dialogs/Win11Dialog.xaml` + code-behind（新）

布局：
- `Window` 标题 "Win11 新菜单"、`Width="560"`、`Height="440"`
- 顶部：说明文字（"开启新菜单后必须重启资源管理器才生效。"）
- ToggleButton：`Content="使用 Win11 新菜单"`、`IsChecked 绑 IsNewMenuEnabled (TwoWay)`、`IsEnabled 绑 IsWindows11`
- 中间：`DataGrid<Win11BlockItem>`，列：`VerbName` + 操作列（[移除] 按钮）
- 底部 `StackPanel`（水平）：
  - 状态栏 `TextBlock` 绑 `StatusText`
  - "重启资源管理器" 按钮 → `RestartExplorerCommand`（`IsEnabled 绑 IsWindows11` + `IsBusy`）
  - "关闭" 按钮 → 直接 `Close()`

非 Win11 时：DataGrid + ToggleButton 换成灰色说明文字（"当前系统不是 Win11，本功能不可用。"），仅保留"关闭"按钮。

### 4.7 `ViewModels/MainViewModel.cs`（改）

ctor 注入新参数：
```csharp
private readonly IWin11MenuService _win11;
private readonly WinVersionService _ver;
public WinVersionService VersionInfo => _ver;  // 给 ScopeBar 绑 IsWindows11

public MainViewModel(
    RegistryService registry, MenuParserService parser, IconService icons,
    RegistryWriteService writer, IBackupService backup, IOperationLog log,
    IWin11MenuService win11, WinVersionService ver)
{
    ...
    _win11 = win11;
    _ver = ver;
}
```

### 4.8 `ViewModels/MainViewModel.Commands.cs`（改）

新增：
```csharp
[RelayCommand]
private void ShowWin11()
{
    var owner = Application.Current?.MainWindow;
    var vm = new Win11DialogViewModel(_win11, _ver);
    var dlg = new Win11Dialog { Owner = owner, DataContext = vm };
    dlg.ShowDialog();
}
```

### 4.9 `Views/Controls/ScopeBar.xaml`（改）

列定义 + 1 列，把"备份"往后推一列：

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto" />  <!-- 0 作用域 -->
    <ColumnDefinition Width="320" />    <!-- 1 作用域下拉 -->
    <ColumnDefinition Width="Auto" />  <!-- 2 自定义扩展名 -->
    <ColumnDefinition Width="160" />    <!-- 3 扩展名输入 -->
    <ColumnDefinition Width="Auto" />  <!-- 4 加载 -->
    <ColumnDefinition Width="Auto" />  <!-- 5 Win11 (新) -->
    <ColumnDefinition Width="Auto" />  <!-- 6 备份 (原 5) -->
    <ColumnDefinition Width="*" />     <!-- 7 弹簧 (原 6) -->
    <ColumnDefinition Width="Auto" />  <!-- 8 刷新 (原 7) -->
</Grid.ColumnDefinitions>
...
<Button Grid.Column="5" Content="Win11" Margin="6,0,0,0" Padding="12,4"
        Command="{Binding ShowWin11Command}"
        IsEnabled="{Binding VersionInfo.IsWindows11}" />
<Button Grid.Column="6" Content="备份" Margin="6,0,8,0" Padding="12,4"
        HorizontalAlignment="Right"
        Command="{Binding ShowBackupsCommand}" />
```

> **关键决策（非 Win11 系统 = 选项 B）**：`Win11` 按钮**始终显示**，`IsEnabled` 绑 `VersionInfo.IsWindows11`，在 Win10 / Win Server 上呈灰态。理由：让用户知道这个功能存在，但不会误触发。

### 4.10 `App.xaml.cs`（改）

DI 注册追加：
```csharp
services.AddSingleton<WinVersionService>();
services.AddSingleton<IWin11MenuService, Win11MenuService>();
```

## 5. 数据流

1. 用户点 `ScopeBar` 的"Win11"按钮（仅 Win11 可点）→ `ShowWin11Command` → `new Win11Dialog()` → ctor `Win11DialogViewModel(_win11, _ver)` → `Refresh()` 读 `IsNewMenuEnabled` + `GetBlockList()` → 弹窗显示。
2. 用户拨动 `ToggleButton` → `IsNewMenuEnabled` 双向绑定 → `OnIsNewMenuEnabledChanged` → 写注册表 → 状态栏提示"需重启资源管理器"。
3. 用户选 Block 列表某行 → `SelectedBlock` 变 → `HasSelection` 变 → 行的"移除"按钮可点。
4. 用户点"移除" → `RemoveBlockCommand` → `svc.RemoveFromBlock` → `Refresh()`。
5. 用户点"重启资源管理器" → 弹 `ConfirmDialog`（破坏性）→ 用户确认 → 后台线程 `taskkill explorer.exe` + `Start-Process explorer.exe` → 状态栏反馈。

## 6. UI 行为

| 场景 | 行为 |
|---|---|
| Win11 启动弹窗 | 同步读两个注册表位置，秒级 |
| Win10 / 其他启动 | 弹窗内显示提示页"当前系统不是 Win11"，所有写操作禁用 |
| Toggle 拨到 ON | 删 `InprocServer32` 键；状态栏："已切换到 Win11 新菜单（需重启资源管理器）" |
| Toggle 拨到 OFF | 建 `InprocServer32` + `(Default)=""`；状态栏："已切换到经典菜单（需重启资源管理器）" |
| 切换写注册表失败 | 状态栏："切换失败：{ex.Message}"，Toggle 回弹到正确状态 |
| Block 列表为空 | DataGrid 显示"共 0 项 Block" |
| 移除 Block 成功 | 列表刷新，状态栏："已移除 {verb}" |
| 重启 Explorer 确认 | 弹 `ConfirmDialog`（破坏性）→ 确认 → 后台执行 |
| 用户点"关闭" | 直接 `Close()`，无副作用 |

## 7. 错误处理

| 异常 | 处理 |
|---|---|
| `IsWindows11` 检测失败（异常） | 弹窗进入"非 Win11 模式"（灰态） |
| `IsNewMenuEnabled` 读失败 | 抛 → 弹窗 `StatusText` 显示，按钮禁用 |
| `SetNewMenuEnabled` 写失败 | 抛 → `OnIsNewMenuEnabledChanged` 捕获 → UI 复原 + `StatusText` |
| `RemoveFromBlock` 失败 | 抛 → `StatusText` 显示，列表不变 |
| `GetBlockList` `Shell\Block` 不存在 | 返回空列表 |
| `RestartExplorer` 失败 | `StatusText` 显示错误，UI 不卡死（`IsBusy` 解锁） |
| `taskkill` 超时（explorer 占用文件） | 同步等 3s；explorer 起不来用户重启即可 |

## 8. 测试

新增单测（`Tests/WinVersionServiceTests.cs` + `Tests/Win11MenuServiceTests.cs`）：
- `WinVersionService.IsWindows11` 派生类模拟：构造一个 `WinVersionService` 子类覆写 `OSVersion` 提供器，断言 `Build < 22000` 返 `false` / `Build >= 22000` 返 `true`。
- `Win11MenuService.SetNewMenuEnabled(true)` 沙箱下：先 `SetNewMenuEnabled(false)` 建键 → 再 `SetNewMenuEnabled(true)` → `ToggleRoot` 键消失。
- `Win11MenuService.SetNewMenuEnabled(false)` 沙箱下：建 `InprocServer32` 子键 + `(Default)=""`。
- `Win11MenuService.GetBlockList` 沙箱 `Shell\Block` 下建 3 个子键 → 返回 3 项。
- `Win11MenuService.RemoveFromBlock` 删存在的子键 → 键消失；删不存在的子键 → 无异常。
- 不测 `RestartExplorer`（杀进程 + 起进程，CI 不可控）。

测试用 `[Collection("RealRegistry")]` 写 HKCU 沙箱（与 `Win32RegistryWriterTests` / `BackupServiceTests` 同样模式）。`Win11MenuService` 内部用 `Registry.CurrentUser` 硬编码路径 → 需**在测试中 monkey-patch** 或把 `ToggleRoot` / `BlockRoot` 暴露为 `internal const` + `[InternalsVisibleTo]`。

**采用方案**：把 `ToggleRoot` / `BlockRoot` 改 `internal const` + 在 `RCMenuManager.csproj` 加 `<InternalsVisibleTo Include="RCMenuManager.Tests" />`，测试中建一个 `TestableWin11MenuService : Win11MenuService` 用 `new` 常量遮蔽（不可行，C# `const` 不能被派生类遮蔽）。

**最终方案**：`Win11MenuService` ctor 接受可选的 `(string toggleRoot, string blockRoot)` 参数，默认指向真实路径，测试里传沙箱路径。把这两个字段从 `const` 改为 `private readonly`。