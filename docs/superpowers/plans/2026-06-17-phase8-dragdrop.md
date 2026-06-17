# Phase 8: 拖拽识别 Implementation Plan

> REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 主窗口任意位置可接受资源管理器拖入，自动识别文件 / 文件夹 / 驱动器并切到对应作用域。

**Architecture:** MainWindow 加 AllowDrop + overlay Border（Visibility 绑 MainViewModel.IsDragOver）。MainWindow.xaml.cs 桥接 4 个 WPF 拖拽事件到 MainViewModel.OnFileDroppedAsync。IFileTypeService.Identify(path) 纯函数判定 File / Folder / Drive / Unknown。MainViewModel 抽出 SwitchToExtensionScopeAsync helper 给现有 LoadCustomExtensionAsync 和新的 drop 流程共用。

**Tech Stack:** WPF / .NET 9 / CommunityToolkit.Mvvm / System.IO

**Spec:** docs/superpowers/specs/2026-06-17-phase8-dragdrop-design.md

---

## Task 1: DragDropInfo model

**Files:**
- Create: Models/DragDropInfo.cs

### Step 1: 创建 record + enum

Models/DragDropInfo.cs 创建：

`csharp
namespace RCMenuManager.Models;

public enum DragDropKind { Unknown, File, Folder, Drive }

public sealed record DragDropInfo(string Path, DragDropKind Kind);
`

### Step 2: 构建验证

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

### Step 3: 提交

`ash
git -c core.autocrlf=false add Models/DragDropInfo.cs
git -c core.autocrlf=false commit -m "feat: add DragDropInfo model"
`

---

## Task 2: IFileTypeService + FileTypeService + 集成测

**Files:**
- Create: Services/IFileTypeService.cs
- Create: Services/FileTypeService.cs
- Create: Tests/FileTypeServiceTests.cs

### Step 1: 写接口

Services/IFileTypeService.cs 创建：

`csharp
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IFileTypeService
{
    DragDropInfo Identify(string path);
}
`

### Step 2: 写实现

Services/FileTypeService.cs 创建：

`csharp
using System.IO;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public sealed class FileTypeService : IFileTypeService
{
    public DragDropInfo Identify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DragDropInfo(path ?? string.Empty, DragDropKind.Unknown);

        if (IsDriveRoot(path)) return new DragDropInfo(path, DragDropKind.Drive);
        if (Directory.Exists(path)) return new DragDropInfo(path, DragDropKind.Folder);
        if (File.Exists(path)) return new DragDropInfo(path, DragDropKind.File);
        return new DragDropInfo(path, DragDropKind.Unknown);
    }

    private static bool IsDriveRoot(string path)
    {
        if (path.Length < 2 || path.Length > 3) return false;
        if (!char.IsLetter(path[0]) || path[1] != ':') return false;
        return path.Length == 2 || path[2] == '\\' || path[2] == '/';
    }
}
`

### Step 3: 写集成测

Tests/FileTypeServiceTests.cs 创建：

`csharp
using System;
using System.IO;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class FileTypeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileTypeService _svc = new();

    public FileTypeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerDragDropTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Identify_drive_root_uppercase() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"C:\").Kind);

    [Fact]
    public void Identify_drive_root_lowercase_no_backslash() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"d:").Kind);

    [Fact]
    public void Identify_drive_root_with_forward_slash() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"E:/").Kind);

    [Fact]
    public void Identify_existing_folder() =>
        Assert.Equal(DragDropKind.Folder, _svc.Identify(_tempDir).Kind);

    [Fact]
    public void Identify_existing_file() =>
        Assert.Equal(DragDropKind.File, _svc.Identify(Path.Combine(_tempDir, "a.txt")).Kind);

    [Fact]
    public void Identify_nonexistent_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(Path.Combine(_tempDir, "nope.bin")).Kind);

    [Fact]
    public void Identify_empty_string_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(string.Empty).Kind);

    [Fact]
    public void Identify_null_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(null!).Kind);

    [Fact]
    public void Identify_preserves_path() =>
        Assert.Equal(@"C:\", _svc.Identify(@"C:\").Path);
}
`

### Step 4: 构建 + 跑测试

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

Run: dotnet test RCMenuManager.sln --nologo -v:m --filter FullyQualifiedName~FileTypeServiceTests
Expected: 9 tests pass

### Step 5: 提交

`ash
git -c core.autocrlf=false add Services/IFileTypeService.cs Services/FileTypeService.cs Tests/FileTypeServiceTests.cs
git -c core.autocrlf=false commit -m "feat: add FileTypeService for drag drop path identification"
`

---

## Task 3: MainViewModel 加 IsDragOver + OnFileDropped + 重构

**Files:**
- Modify: ViewModels/MainViewModel.cs
- Modify: ViewModels/MainViewModel.Commands.cs

### Step 1: MainViewModel.cs 加字段 + 注入

加在 private readonly IOperationLog _log; 之后：

`csharp
private readonly IFileTypeService _fileTypes;

[ObservableProperty] private bool _isDragOver;
`

改 ctor 签名（追加 IFileTypeService fileTypes）：

`csharp
public MainViewModel(
    RegistryService registry, MenuParserService parser, IconService icons,
    RegistryWriteService writer, IBackupService backup, IOperationLog log,
    IWin11MenuService win11, WinVersionService ver,
    IFileTypeService fileTypes)
{
    ...
    _fileTypes = fileTypes;
}
`

> 顶部加 using RCMenuManager.Models;（如已有则跳过）

### Step 2: 重构 + 加 OnFileDroppedAsync

打开 ViewModels/MainViewModel.Commands.cs，把现有 LoadCustomExtensionAsync 替换：

旧：
`csharp
[RelayCommand]
private async Task LoadCustomExtensionAsync()
{
    var ext = (CustomExtensionInput ?? string.Empty).Trim();
    if (string.IsNullOrEmpty(ext)) return;
    if (!ext.StartsWith('.')) ext = "." + ext;
    var progId = _registry.ResolveProgId(ext);
    var scope = MenuScope.ForExtension(ext, progId);
    var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
    var option = new ScopeOption(label, scope);
    TrimCustomOptions();
    Scopes.Add(option);
    SelectedScope = option;
    await Task.CompletedTask;
}
`

新：
`csharp
[RelayCommand]
private async Task LoadCustomExtensionAsync()
{
    var ext = (CustomExtensionInput ?? string.Empty).Trim();
    if (string.IsNullOrEmpty(ext)) return;
    await SwitchToExtensionScopeAsync(ext);
}

public async Task OnFileDroppedAsync(string[] paths)
{
    IsDragOver = false;
    if (paths is null || paths.Length == 0)
    {
        StatusText = "未识别拖入内容";
        return;
    }
    try
    {
        var first = paths[0];
        var info = _fileTypes.Identify(first);
        switch (info.Kind)
        {
            case DragDropKind.Drive:
                SwitchToBuiltInScope(MenuScope.Drive, $"已切换到驱动器 {first}");
                break;
            case DragDropKind.Folder:
                SwitchToBuiltInScope(MenuScope.Folder, $"已切换到文件夹 {first}");
                break;
            case DragDropKind.File:
                var ext = Path.GetExtension(first);
                if (string.IsNullOrEmpty(ext))
                    SwitchToBuiltInScope(MenuScope.AllFiles, "无扩展名，已切换到通用文件");
                else
                    await SwitchToExtensionScopeAsync(ext);
                break;
            default:
                StatusText = "不支持的拖入内容：" + first;
                return;
        }
    }
    catch (Exception ex)
    {
        StatusText = "切换失败：" + ex.Message;
    }
}

private async Task SwitchToExtensionScopeAsync(string ext)
{
    if (string.IsNullOrWhiteSpace(ext)) return;
    if (!ext.StartsWith('.')) ext = "." + ext;
    var progId = _registry.ResolveProgId(ext);
    var scope = MenuScope.ForExtension(ext, progId);
    var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
    var option = new ScopeOption(label, scope);
    TrimCustomOptions();
    Scopes.Add(option);
    SelectedScope = option;
    StatusText = $"已切换到 {label}";
    await Task.CompletedTask;
}

private void SwitchToBuiltInScope(MenuScope scope, string statusMessage)
{
    var existing = Scopes.FirstOrDefault(s => s.Scope.Equals(scope));
    if (existing is not null)
    {
        SelectedScope = existing;
    }
    else
    {
        var opt = new ScopeOption(scope.DisplayName, scope);
        Scopes.Add(opt);
        SelectedScope = opt;
    }
    StatusText = statusMessage;
}
`

顶部加 using System.Linq;（如已有则跳过；Path 在 System.IO 已有，文件顶部也有）。

### Step 3: 构建验证

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

### Step 4: 提交

`ash
git -c core.autocrlf=false add ViewModels/MainViewModel.cs ViewModels/MainViewModel.Commands.cs
git -c core.autocrlf=false commit -m "feat: add IsDragOver + OnFileDropped and refactor scope switch helper"
`

---

## Task 4: MainWindow.xaml 加 overlay

**Files:**
- Modify: MainWindow.xaml

### Step 1: 加 overlay Border

在 <Window ...> 元素上加 AllowDrop="True" + 4 个事件：
`xml
<Window x:Class="RCMenuManager.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:c="clr-namespace:RCMenuManager.Views.Controls"
        Title="RCMenuManager"
        Width="1180" Height="720"
        MinWidth="900" MinHeight="560"
        Background="{StaticResource WindowBackground}"
        UseLayoutRounding="True"
        TextOptions.TextFormattingMode="Display"
        FontFamily="Segoe UI, Microsoft YaHei UI, sans-serif"
        FontSize="13"
        AllowDrop="True"
        DragEnter="OnWindowDragEnter"
        DragOver="OnWindowDragOver"
        DragLeave="OnWindowDragLeave"
        Drop="OnWindowDrop">
`

在主 <Grid Margin="12"> 末尾追加 overlay Border（覆盖整个 Grid，跨所有行）：
`xml
        <!-- DragDrop overlay -->
        <Border IsHitTestVisible="False"
                Background="#CC3B82F6"
                BorderBrush="#1D4ED8" BorderThickness="3"
                CornerRadius="6">
            <Border.Style>
                <Style TargetType="Border">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding IsDragOver}" Value="True">
                            <Setter Property="Visibility" Value="Visible" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Border.Style>
            <TextBlock Text="拖入文件 / 文件夹 / 驱动器以自动识别作用域"
                       Foreground="White" FontSize="18" FontWeight="SemiBold"
                       HorizontalAlignment="Center" VerticalAlignment="Center" />
        </Border>
`

> Grid 默认最后一行的 Row 索引是未指定（默认 0）；为让 overlay 真正覆盖全部 3 行，需要加 Grid.RowSpan="3"。如不加，WPF 默认放第 0 行，不会盖住下面。
> 修正：把 Grid.RowSpan="3" 加到 overlay Border 上。

### Step 2: 构建验证

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

### Step 3: 提交

`ash
git -c core.autocrlf=false add MainWindow.xaml
git -c core.autocrlf=false commit -m "feat: add drag drop overlay to MainWindow"
`

---

## Task 5: MainWindow.xaml.cs 拖拽事件

**Files:**
- Modify: MainWindow.xaml.cs

### Step 1: 改 ctor + 加事件处理器

MainWindow.xaml.cs 整体替换：

`csharp
using System.Windows;
using RCMenuManager.ViewModels;

namespace RCMenuManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        var ok = HasFilePayload(e);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        _vm.IsDragOver = ok;
        e.Handled = true;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFilePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        _ = _vm.OnFileDroppedAsync(paths);
    }

    private static bool HasFilePayload(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);
}
`

### Step 2: 构建 + 跑全部测试

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

Run: dotnet test RCMenuManager.sln --nologo -v:m
Expected: 旧 50 + 新 9 = 59 测试通过

### Step 3: 提交

`ash
git -c core.autocrlf=false add MainWindow.xaml.cs
git -c core.autocrlf=false commit -m "feat: wire MainWindow drag drop events to MainViewModel"
`

---

## Task 6: App.xaml.cs DI

**Files:**
- Modify: App.xaml.cs

### Step 1: 注册服务

在 services.AddSingleton<IWin11MenuService, Win11MenuService>(); 之后追加：

`csharp
services.AddSingleton<IFileTypeService, FileTypeService>();
`

### Step 2: 构建 + 跑全部测试

Run: dotnet build RCMenuManager.sln --nologo -v:m
Expected: 0 errors

Run: dotnet test RCMenuManager.sln --nologo -v:m
Expected: 59 tests pass

### Step 3: 提交

`ash
git -c core.autocrlf=false add App.xaml.cs
git -c core.autocrlf=false commit -m "feat: register FileTypeService in DI container"
`

---

## Task 7: 手动 smoke 清单

**Files:**
- Create: docs/superpowers/smoke/2026-06-17-phase8-smoke.md

### Step 1: 写 smoke

docs/superpowers/smoke/2026-06-17-phase8-smoke.md 创建：

`markdown
# Phase 8: 拖拽识别 手动 Smoke 检查

## [拖入文件]
- [ ] 从资源管理器拖一个 .txt 文件进 RCMenuManager 窗口
- [ ] 拖动时主窗口显示蓝色 overlay + 提示文字
- [ ] 释放后作用域切到 .txt，菜单项自动加载，状态栏："已切换到 .txt 文件"
- [ ] 再次拖另一个 .txt 文件进窗口：作用域已存在，SelectedScope 复用

## [拖入文件夹]
- [ ] 拖一个文件夹（任意路径）进窗口
- [ ] 释放后作用域切到 文件夹，菜单项加载，状态栏："已切换到文件夹 <path>"

## [拖入驱动器]
- [ ] 拖驱动器根（C:\）进窗口
- [ ] 释放后作用域切到 驱动器，状态栏："已切换到驱动器 C:\"

## [多文件]
- [ ] 多选 .png + .jpg，拖入
- [ ] 释放后切到 .png（第一个），状态栏反馈 .png 名称

## [无扩展名]
- [ ] 创建一个 README（无扩展名）拖入
- [ ] 释放后切到 *（通用文件），状态栏："无扩展名，已切换到通用文件"

## [拖动中移开]
- [ ] 拖动到窗口上 → overlay 显示
- [ ] 拖回资源管理器（移开窗口）→ overlay 消失

## [拖非文件]
- [ ] 从浏览器拖一段文字到窗口
- [ ] overlay 不显示，窗口不接受

## [拖入不存在路径]
- [ ] （通过 PowerShell 模拟）—— 不易手动测；靠单测覆盖
`

### Step 2: 提交

`ash
git -c core.autocrlf=false add docs/superpowers/smoke/2026-06-17-phase8-smoke.md
git -c core.autocrlf=false commit -m "docs: phase 8 manual smoke checklist"
`

---

## Self-Review

**Spec coverage：**
- 4.1 DragDropInfo → Task 1 ✓
- 4.2 IFileTypeService → Task 2 Step 1 ✓
- 4.3 FileTypeService → Task 2 Step 2 ✓
- 4.4 MainViewModel OnFileDroppedAsync → Task 3 Step 2 ✓
- 4.5 SwitchToExtensionScopeAsync 重构 → Task 3 Step 2 ✓
- 4.6 MainWindow.xaml overlay → Task 4 ✓
- 4.7 MainWindow.xaml.cs 事件 → Task 5 ✓
- 4.8 App.xaml.cs DI → Task 6 ✓
- Section 8 testing → Task 2 Step 3 (9 tests) + Task 5/6 跑全部 ✓
- Section 6-7 行为/错误 → Task 7 smoke ✓

**Placeholder scan：** 无 TBD / 实现细节 / 类似 Task N。

**Type consistency：**
- DragDropInfo(string Path, DragDropKind Kind)（Task 1）→ FileTypeService.Identify 返回（Task 2 Step 2）✓
- DragDropKind 枚举（Task 1）→ MainViewModel.OnFileDroppedAsync switch case（Task 3）✓
- IFileTypeService.Identify(string path)（Task 2）→ ctor 注入（Task 3 Step 1）✓
- MainViewModel.OnFileDroppedAsync(string[] paths)（Task 3）→ MainWindow.OnWindowDrop 调（Task 5）✓
- MainViewModel.IsDragOver（Task 3）→ XAML DataTrigger 绑（Task 4）✓
- MainWindow 新 ctor (MainViewModel vm)（Task 5）→ App.xaml.cs DI 自动注入（已有 services.AddSingleton<MainWindow>();）✓