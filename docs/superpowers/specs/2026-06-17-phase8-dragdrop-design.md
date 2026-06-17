# Phase 8: 拖拽识别 设计文档

## 1. 目标

让用户把文件 / 文件夹 / 驱动器从资源管理器拖到 RCMenuManager 主窗口任意位置，自动识别类型并切换到对应作用域，同时给出视觉反馈。

- 拖文件（含多选） → 用第一个文件的扩展名切到 .xxx 作用域（无扩展名 → *）
- 拖文件夹 → 切到 HKCR\Directory\shell
- 拖驱动器 → 切到 HKCR\Drive\shell
- 拖非文件系统对象（文本、URL 等）→ 忽略
- 拖动中显示主窗口高亮覆盖层
- 释放后状态栏反馈结果，调用现有 LoadAsync 拉菜单

## 2. 范围

**In scope：**
- Models/DragDropInfo.cs（新）：sealed record DragDropInfo(string Path, DragDropKind Kind) + enum DragDropKind { File, Folder, Drive, Unknown }
- Services/IFileTypeService.cs + Services/FileTypeService.cs（新）：纯函数 Identify(string path) → DragDropInfo，纯文件 IO（File.Exists / Directory.Exists / drive 判定）
- ViewModels/MainViewModel.cs + MainViewModel.Commands.cs（改）：
  - 加 [ObservableProperty] private bool _isDragOver;
  - 加 OnFileDroppedAsync(string[] paths) 私有方法：调用 IFileTypeService.Identify 拿第一项 → 走统一 helper SwitchToScopeForDrop(kind, path) → 切作用域 → StatusText 反馈
  - 重构现有 LoadCustomExtensionAsync：把"扩展名 → ScopeOption + SelectedScope"逻辑抽成 SwitchToExtensionScopeAsync(string ext) 复用
- MainWindow.xaml（改）：
  - Window 加 AllowDrop=True + 一个 Border overlay 覆盖整个客户端区，Visibility 绑 IsDragOver（用 DataTrigger 切 Vis）
  - overlay 内容：居中一个半透明方块 + 提示文字 "拖入文件 / 文件夹 / 驱动器以自动识别"
- MainWindow.xaml.cs（改）：加 OnDragEnter / OnDragOver / OnDragLeave / OnDrop 四个事件，桥接到 MainViewModel
- App.xaml.cs（改）：DI 注册 IFileTypeService → FileTypeService
- 单元测试：FileTypeServiceTests 用真实文件系统 + 临时目录覆盖各类场景

**Out of scope（YAGNI）：**
- 拖动到具体子元素（如 TreeView 行）触发不同行为 → 一律全窗口接受
- 拖动时实时预判类型（仅在 DragEnter 显示 overlay，不预切作用域）
- 从本应用拖出到资源管理器
- .lnk 快捷方式的目标解析
- 网络共享路径特殊处理（依赖 Directory.Exists 自然兼容）
- 拖入项目到非作用域操作（Phase 9+ 再考虑）

## 3. 架构

`
+---------------------------------------------------+
|  MainWindow (AllowDrop=True)                      |
|  +---------------------------------------------+  |
|  | ScopeBar | List/Preview | DetailsPanel      |  |
|  +---------------------------------------------+  |
|                                                   |
|  ┌─ DragDropOverlay (Border) ─────────────────┐   |
|  │ Visibility 绑 MainViewModel.IsDragOver    │   |
|  │  ┌─────────────────────────────────────┐   │   |
|  │  │  拖入文件 / 文件夹 / 驱动器以自动识别 │   │   |
|  │  └─────────────────────────────────────┘   │   |
|  └────────────────────────────────────────────┘   |
+---------------------------------------------------+
        ↑ DragEnter/DragOver/DragLeave/Drop 事件
        ↓
MainViewModel
  ├─ IsDragOver (bool, ObservableProperty)
  └─ OnFileDroppedAsync(paths) → 调 IFileTypeService.Identify → 切作用域

Services
  IFileTypeService ─→ FileTypeService
    .Identify(path) → DragDropInfo
       ├─ if 路径是驱动器根 (C:\, D:\) → Drive
       ├─ else if Directory.Exists → Folder
       ├─ else if File.Exists → File
       └─ else → Unknown
`

## 4. 组件

### 4.1 Models/DragDropInfo.cs（新）

`csharp
namespace RCMenuManager.Models;

public enum DragDropKind { Unknown, File, Folder, Drive }

public sealed record DragDropInfo(string Path, DragDropKind Kind);
`

### 4.2 Services/IFileTypeService.cs（新）

`csharp
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IFileTypeService
{
    DragDropInfo Identify(string path);
}
`

### 4.3 Services/FileTypeService.cs（新）

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

        // 驱动器根: "C:\", "D:\" 这种
        if (IsDriveRoot(path)) return new DragDropInfo(path, DragDropKind.Drive);
        if (Directory.Exists(path)) return new DragDropInfo(path, DragDropKind.Folder);
        if (File.Exists(path)) return new DragDropInfo(path, DragDropKind.File);
        return new DragDropInfo(path, DragDropKind.Unknown);
    }

    private static bool IsDriveRoot(string path)
    {
        // 形如 "C:\" 或 "C:" 且 length <= 3
        if (path.Length < 2 || path.Length > 3) return false;
        if (!char.IsLetter(path[0]) || path[1] != ':') return false;
        return path.Length == 2 || path[2] == '\\' || path[2] == '/';
    }
}
`

### 4.4 ViewModels/MainViewModel.cs（改）

加属性：
`csharp
[ObservableProperty] private bool _isDragOver;
public IFileTypeService FileTypes { get; }   // ctor 注入
`

加方法（放 MainViewModel.Commands.cs 里更合适）：
`csharp
public async Task OnFileDroppedAsync(string[] paths)
{
    IsDragOver = false;
    if (paths is null || paths.Length == 0)
    {
        StatusText = "未识别拖入内容";
        return;
    }
    var first = paths[0];
    var info = FileTypes.Identify(first);
    switch (info.Kind)
    {
        case DragDropKind.Drive:
            SwitchToScope(MenuScope.Drive, $"已切换到驱动器 {first}");
            break;
        case DragDropKind.Folder:
            SwitchToScope(MenuScope.Folder, $"已切换到文件夹 {first}");
            break;
        case DragDropKind.File:
            var ext = Path.GetExtension(first);
            if (string.IsNullOrEmpty(ext))
                SwitchToScope(MenuScope.AllFiles, $"无扩展名，已切换到通用文件");
            else
                await SwitchToExtensionScopeAsync(ext);
            break;
        default:
            StatusText = "不支持的拖入内容：" + first;
            return;
    }
}
`

### 4.5 ViewModels/MainViewModel.cs（改）— 重构 helper

把现有 LoadCustomExtensionAsync 拆出 helper：

`csharp
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

private void SwitchToScope(MenuScope scope, string statusMessage)
{
    var existing = Scopes.FirstOrDefault(s => s.Scope.Equals(scope));
    if (existing is not null) { SelectedScope = existing; }
    else
    {
        var opt = new ScopeOption(scope.DisplayName, scope);
        Scopes.Add(opt);
        SelectedScope = opt;
    }
    StatusText = statusMessage;
}

[RelayCommand]
private async Task LoadCustomExtensionAsync()
{
    var ext = (CustomExtensionInput ?? string.Empty).Trim();
    if (string.IsNullOrEmpty(ext)) return;
    await SwitchToExtensionScopeAsync(ext);
}
`

### 4.6 Views/MainWindow.xaml（改）

Window 属性加 AllowDrop="True"。在主 Grid 上覆盖一个 overlay Border：

`xml
<Window ... AllowDrop="True" DragEnter="OnWindowDragEnter"
        DragOver="OnWindowDragOver" DragLeave="OnWindowDragLeave"
        Drop="OnWindowDrop">
    <Grid Margin="12">
        <!-- 原有内容 -->
        <Border Grid.Row="0" Style="{StaticResource Card}">...</Border>
        <Grid Grid.Row="1">...</Grid>
        <Border Grid.Row="2">...</Border>

        <!-- 拖拽高亮 overlay：覆盖整个客户端区 -->
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
    </Grid>
</Window>
`

> IsHitTestVisible="False" 保证 overlay 不会拦截鼠标事件；窗口的拖拽事件能正常冒泡。

### 4.7 MainWindow.xaml.cs（改）

`csharp
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
        e.Effects = HasFilePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        _vm.IsDragOver = HasFilePayload(e);
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

> MainWindow 现有 ctor 是无参 → 加 ctor 参数需要 DI 注入 MainViewModel。App.xaml.cs 当前是 Services.GetRequiredService<MainWindow>() 自动注入，会找到新 ctor。

### 4.8 App.xaml.cs（改）

DI 注册追加：
`csharp
services.AddSingleton<IFileTypeService, FileTypeService>();
`

## 5. 数据流

1. 用户在资源管理器选中文件 / 文件夹 / 驱动器，拖到 RCMenuManager 主窗口。
2. DragEnter 触发 → 检查 DataFormats.FileDrop 存在 → m.IsDragOver = true → overlay 显示。
3. 用户释放鼠标 → Drop 触发 → 提取 paths 数组 → 调 m.OnFileDroppedAsync(paths)。
4. OnFileDroppedAsync 调 FileTypeService.Identify(firstPath) 拿 kind。
5. 根据 kind 走 SwitchToScope / SwitchToExtensionScopeAsync：
   - SwitchToScope：复用已存在的 ScopeOption 或新建，SelectedScope = option → 触发现有 OnSelectedScopeChanged → LoadAsync(scope) 自动跑。
6. StatusText 更新；菜单项 DataGrid 自动刷新。
7. 拖动中途移出窗口 → DragLeave → IsDragOver = false → overlay 隐藏。

## 6. UI 行为

| 场景 | 行为 |
|---|---|
| 拖入 .txt 文件 | 切到 .txt 作用域，状态栏："已切换到 .txt 文件" |
| 拖入多个 .png | 用第一个（.png）切到 .png 作用域 |
| 拖入文件夹 | 切到 Folder 作用域，状态栏："已切换到文件夹 <path>" |
| 拖入驱动器 (C:\) | 切到 Drive 作用域，状态栏："已切换到驱动器 C:\" |
| 拖入无扩展名文件 (README) | 切到 *（AllFiles），状态栏："无扩展名，已切换到通用文件" |
| 拖入不存在路径 | 状态栏："不支持的拖入内容：<path>"，作用域不变 |
| 拖入非文件（文本、URL） | overlay 不显示（DataFormats.FileDrop 不匹配），不接受 |
| 拖动中 | overlay 蓝色高亮 + 居中提示文字 |
| 拖动到非客户区（标题栏） | Windows 默认不接受，DragLeave 触发，overlay 隐藏 |

## 7. 错误处理

| 异常 | 处理 |
|---|---|
| paths 为 null / 空 | 状态栏："未识别拖入内容"；overlay 关闭 |
| Identify 返 Unknown | 状态栏："不支持的拖入内容：<path>" |
| 拖动过程中 MainViewModel 抛 | 状态栏："切换失败：<ex.Message>"（OnFileDroppedAsync 内 try-catch） |
| ResolveProgId 抛（极少见，注册表权限） | 沿用现有 LoadCustomExtensionAsync 行为（无 catch，靠 OnFileDroppedAsync 外层 try 兜住） |

## 8. 测试

新增 Tests/FileTypeServiceTests.cs：
- Identify_驱动器根_C盘 → DragDropKind.Drive
- Identify_驱动器根_D盘_带反斜杠 → DragDropKind.Drive
- Identify_小写_c_冒号_无反斜杠 → DragDropKind.Drive（边界）
- Identify_临时文件夹 → DragDropKind.Folder（用 Path.GetTempPath() 真实路径）
- Identify_临时文件 → DragDropKind.File（在临时目录写一个 	est.txt）
- Identify_不存在的路径 → DragDropKind.Unknown
- Identify_空字符串 → DragDropKind.Unknown
- Identify_null → DragDropKind.Unknown
- Identify_相对路径不存在 → DragDropKind.Unknown

不测 OnFileDroppedAsync（依赖 MainViewModel + WPF DataContext）和 MainWindow.xaml.cs（WPF UI 事件）— 端到端靠手动 smoke。

## 9. 风险

| 风险 | 缓解 |
|---|---|
| 拖动到 overlay 区域时事件不冒泡 | IsHitTestVisible="False" 让 overlay 不拦截，事件到 Window |
| 多个文件拖入，状态栏只显示第一个的反馈 | 设计如此（用第一个），状态栏文本清晰 |
| 拖动跨进程时 DataFormats.FileDrop 性能差 | 接受（拖动只发生一次） |
| Path.GetExtension 对没有 . 的路径返空 | 已处理：string.IsNullOrEmpty(ext) → 走 AllFiles |
| Drive root 判定漏掉 C:/ 风格 | IsDriveRoot 接受 \ 和 / |
| 用户在拖动过程中应用崩溃 | 现有 AppDomain.UnhandledException + DispatcherUnhandledException 已捕获 |
| 注入 MainViewModel 到 MainWindow ctor 破坏现有 App.xaml.cs 调用 | 现有 Services.GetRequiredService<MainWindow>() 自动支持新 ctor |