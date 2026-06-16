# Windows 右键菜单管理器 - 开发计划

## 项目概述

开发一款 Windows 右键菜单管理软件，支持可视化管理 Win10/Win11 系统右键菜单，包括一级菜单和二级菜单的查看、编辑、删除操作。

**开源协议**: MIT License  
**目标系统**: Windows 10 / Windows 11  
**项目名称**: RCMenuManager  
**项目目录**: D:\Itair\RCMenuManager

## 技术选型

| 组件 | 选择 | 理由 |
|------|------|------|
| **框架** | WPF (.NET 9) | 原生 Windows 集成、直接 Registry API、Fluent UI |
| **语言** | C# | Registry 操作最成熟、社区资源丰富 |
| **UI 风格** | Fluent Design / Modern WPF | 符合 Win11 设计语言 |
| **打包** | Single-file publish | 无需安装，便携运行 |

## 项目初始化

```bash
# 创建 WPF 项目
dotnet new wpf -n RCMenuManager -f net9.0-windows

# 添加必要的 NuGet 包
dotnet add package CommunityToolkit.Mvvm
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package System.Drawing.Common

# 创建目录结构
mkdir Models, Services, ViewModels, Views\Controls, Views\Dialogs, Converters, Resources, Helpers
```

### .csproj 配置

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>RCMenuManager</AssemblyName>
    <RootNamespace>RCMenuManager</RootNamespace>
    <Version>1.0.0</Version>
    <Authors>YourName</Authors>
    <Description>A Windows context menu manager for Win10/Win11</Description>
  </PropertyGroup>
</Project>
```

### app.manifest (UAC 提权)

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

## 核心架构

### 1. 右键菜单作用域 (Scope)

```
Scope 类型                    Registry 路径
-------------------------------------------------------
文件 (*.*)                   HKCR\*\shell
所有文件系统对象              HKCR\AllFilesystemObjects\shell
文件夹                       HKCR\Directory\shell
文件夹背景                   HKCR\Directory\Background\shell
驱动器                       HKCR\Drive\shell
桌面                         HKCR\DesktopBackground\Shell
特定文件类型                 HKCR\<扩展名>\shell
```

### 2. 菜单项数据模型

```csharp
class ContextMenuItem
{
    string Name;              // 菜单项名称
    string Command;           // 执行命令
    string IconPath;          // 图标路径
    string Description;       // 描述
    bool IsEnabled;           // 启用/禁用状态
    bool IsExtended;          // 仅 Shift+右键显示
    bool IsProgrammaticOnly;  // 仅程序调用
    string Position;          // Top/Bottom/默认
    List<ContextMenuItem> SubItems;  // 二级菜单
    RegistryKey RegistryKey;  // 对应的注册表键
}
```

### 3. 系统架构图

```
+---------------------------------------------------------+
|                    UI Layer (WPF)                        |
|  +-------------+  +-------------+  +-------------+      |
|  |  Scope选择器 |  |  菜单预览区  |  |  编辑面板   |      |
|  |  (ComboBox)  |  |  (TreeView) |  |  (Detail)   |      |
|  +-------------+  +-------------+  +-------------+      |
+---------------------------------------------------------+
|                  Service Layer                           |
|  +-----------------+  +-----------------+                |
|  | RegistryService  |  | MenuParserService|               |
|  | - Read/Write     |  | - Parse commands |               |
|  | - Backup/Restore |  | - Resolve icons  |               |
|  +-----------------+  +-----------------+                |
+---------------------------------------------------------+
|                  Windows Registry                        |
|  HKEY_CLASSES_ROOT\*\shell\...\command                  |
|  HKEY_CURRENT_USER\Software\Classes\...\command         |
+---------------------------------------------------------+
```

## 功能模块

### Module 1: Scope 管理

| 功能 | 描述 |
|------|------|
| 下拉选择 | 文件、文件夹、文件夹背景、驱动器、桌面等 |
| 自定义类型 | 支持输入特定文件扩展名（如 .txt, .py） |
| 快速切换 | 选中后立即加载对应菜单项 |

### Module 2: 菜单预览与交互

| 功能 | 描述 |
|------|------|
| 右键弹出 | 在预览区域右键，模拟系统右键菜单效果 |
| 一级菜单 | 完整显示所有顶层菜单项 |
| 二级菜单 | 支持展开级联子菜单 (SubCommands) |
| 选中高亮 | 鼠标悬停高亮，点击选中 |
| Shift 支持 | 可切换显示 Extended 菜单项 |

### Module 3: 菜单项编辑

| 操作 | 实现方式 |
|------|----------|
| **删除** | 删除注册表键 HKCR\...\shell\<verb> |
| **禁用** | 添加 ProgrammaticAccessOnly 值，或移动到备份键 |
| **重命名** | 修改 (Default) 值 |
| **修改命令** | 修改 command\(Default) 值 |
| **修改图标** | 添加/修改 Icon 值 |
| **调整顺序** | 修改 Position 值或重新排序注册表键 |
| **添加新项** | 创建新的 \shell\<verb>\command 结构 |

### Module 4: 二级菜单支持

```
注册表结构示例:
HKCR\*\shell\MyMenu
    (Default) = "我的菜单"
    SubCommands = 
        0: "SubCmd1"
        1: "SubCmd2"
    
HKCR\*\shell\MyMenu\shell\SubCmd1
    (Default) = "子菜单项1"
    command
        (Default) = "cmd.exe /c ..."
```

### Module 5: 安全与备份

| 功能 | 描述 |
|------|------|
| 自动备份 | 修改前自动导出 .reg 文件 |
| 还原点 | 修改前创建系统还原点（可选） |
| 一键还原 | 导入备份的 .reg 文件 |
| 系统项保护 | 标记系统关键菜单项，删除前二次确认 |
| UAC 提权 | 修改 HKCR 需要管理员权限 |
| 操作日志 | 记录所有修改操作，便于追溯 |

### Module 6: 推荐设置（预设配置）

提供一键应用的常用右键菜单配置，用户可选择启用/禁用。

详细预设配置见 PRESETS.md

### Module 7: 拖拽识别

支持从文件夹拖拽文件或文件夹到软件中，自动识别类型并加载对应的右键菜单。

| 功能 | 描述 |
|------|------|
| 拖拽文件 | 拖入文件后，自动识别扩展名，切换到对应作用域（如 .txt 切换到 HKCR\.txt\shell） |
| 拖拽文件夹 | 拖入文件夹后，自动切换到文件夹作用域 (HKCR\Directory\shell) |
| 拖拽多个文件 | 多个文件时，取第一个文件的扩展名 |
| 拖拽区域 | 主窗口任意位置均可拖入，或设置专用拖拽区域 |
| 视觉反馈 | 拖入时显示高亮边框和提示文字 |
| 自动刷新 | 拖入后立即加载该类型的右键菜单项 |

#### 拖拽识别逻辑

```
拖入文件/文件夹
    ↓
判断类型（文件/文件夹/驱动器）
    ↓
文件 → 获取扩展名 → 查找 HKCR\<.扩展名>\shell
    ↓                   如果不存在，使用 HKCR\*\shell（通用文件）
文件夹 → 使用 HKCR\Directory\shell
    ↓
驱动器 → 使用 HKCR\Drive\shell
    ↓
自动切换作用域并加载菜单项
```

#### 注册表查找顺序

```
文件类型:
1. HKCU\Software\Classes\<.扩展名>\shell  (用户级，优先)
2. HKLM\SOFTWARE\Classes\<.扩展名>\shell  (系统级)
3. HKCR\<.扩展名>\shell                   (合并视图)
4. HKCR\*\shell                           (通用文件，兜底)

文件夹:
1. HKCU\Software\Classes\Directory\shell
2. HKCR\Directory\shell
```

#### 拖拽区域 UI

```
+---------------------------------------------------------+
|                    RCMenuManager                         |
+---------------------------------------------------------+
|  作用域: [自动识别 ▼]  拖入文件: test.txt  [.txt]       |
+---------------------------------------------------------+
|  +---------------------------------------------------+  |
|  |                                                   |  |
|  |              [拖拽文件或文件夹到此处]              |  |
|  |              自动识别类型并加载菜单                |  |
|  |                                                   |  |
|  +---------------------------------------------------+  |
+---------------------------------------------------------+
|  菜单项:                                                |
|  - 用记事本打开                                         |
|  - 用 VS Code 打开                                      |
|  - ...                                                  |
+---------------------------------------------------------+
```

## Win11 专项支持

### Win11 新菜单 vs 经典菜单

```
Win11 默认行为:
  右键 -> 新菜单（精简）-> "显示更多选项" -> 经典菜单

我们的软件需要:
1. 管理经典菜单项（HKCR\*\shell 等）
2. 管理新菜单的 Block 列表（控制哪些项显示在新菜单）
3. 提供一键切换：禁用/启用 Win11 新菜单
```

### Win11 新菜单控制键

```
禁用新菜单（恢复经典菜单）:
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
    (Default) = "" (空字符串)

启用新菜单（默认）:
删除上述键值
```

## 项目结构

```
RCMenuManager/
├── App.xaml / App.xaml.cs
├── Models/
│   ├── ContextMenuItem.cs
│   ├── MenuScope.cs
│   ├── RegistryBackup.cs
│   └── DragDropInfo.cs           # 拖拽信息模型
├── Services/
│   ├── RegistryService.cs
│   ├── MenuParserService.cs
│   ├── IconService.cs
│   ├── BackupService.cs
│   ├── PresetService.cs
│   └── FileTypeService.cs        # 文件类型识别服务
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── MenuItemViewModel.cs
│   ├── EditPanelViewModel.cs
│   ├── PresetViewModel.cs
│   └── DragDropViewModel.cs      # 拖拽识别 ViewModel
├── Views/
│   ├── MainWindow.xaml
│   ├── Controls/
│   │   ├── ScopeSelector.xaml
│   │   ├── MenuTreeView.xaml
│   │   ├── EditPanel.xaml
│   │   ├── ContextMenuPreview.xaml
│   │   ├── PresetPanel.xaml
│   │   └── DragDropZone.xaml      # 拖拽区域控件
│   └── Dialogs/
│       ├── BackupDialog.xaml
│       └── ConfirmDialog.xaml
├── Converters/
├── Resources/
└── Helpers/
    ├── RegistryHelper.cs
    ├── UacHelper.cs
    ├── ShellNotifyHelper.cs
    └── DragDropHelper.cs          # 拖拽处理工具
```

## 开发阶段

### Phase 1: 基础框架（1-2天）

目标: 搭建项目骨架，实现基础数据模型和服务

任务清单:
- [ ] 配置 app.manifest 启用 requireAdministrator
- [ ] 实现 Models/ContextMenuItem.cs
- [ ] 实现 Models/MenuScope.cs
- [ ] 实现 Services/RegistryService.cs
- [ ] 实现 Helpers/UacHelper.cs

### Phase 2: 核心功能（2-3天）

目标: 实现菜单项读取和 TreeView 展示

任务清单:
- [ ] 实现 Services/MenuParserService.cs
- [ ] 实现 Services/IconService.cs
- [ ] 实现 ViewModels/MainViewModel.cs
- [ ] 实现 ViewModels/MenuItemViewModel.cs
- [ ] 实现 Views/Controls/ScopeSelector.xaml
- [ ] 实现 Views/Controls/MenuTreeView.xaml
- [ ] 支持级联菜单（SubCommands）解析

### Phase 3: 编辑功能（2-3天）

目标: 实现菜单项的增删改

任务清单:
- [ ] 实现 ViewModels/EditPanelViewModel.cs
- [ ] 实现 Views/Controls/EditPanel.xaml
- [ ] 实现菜单项启用/禁用功能
- [ ] 实现菜单项删除功能
- [ ] 实现菜单项属性编辑
- [ ] 实现菜单项添加功能

### Phase 4: 右键预览（1-2天）

目标: 在预览区域模拟系统右键菜单

任务清单:
- [ ] 实现 Views/Controls/ContextMenuPreview.xaml
- [ ] 实现右键弹出菜单功能
- [ ] 支持一级/二级菜单级联显示
- [ ] 菜单项点击选中反馈

### Phase 5: Win11 专项（1-2天）

目标: 支持 Win11 新菜单管理

任务清单:
- [ ] 检测 Windows 版本（Win10/Win11）
- [ ] 实现 Win11 新菜单/经典菜单切换
- [ ] 管理新菜单 Block 列表
- [ ] Win11 特有菜单项识别

### Phase 6: 安全与完善（1-2天）

目标: 备份还原和用户体验优化

任务清单:
- [ ] 实现 Services/BackupService.cs
- [ ] 实现 Views/Dialogs/BackupDialog.xaml
- [ ] 实现 Views/Dialogs/ConfirmDialog.xaml
- [ ] 自动备份机制
- [ ] 一键还原功能
- [ ] 系统项保护机制

### Phase 7: 推荐设置（1-2天）

目标: 实现预设配置一键应用

任务清单:
- [ ] 实现 Services/PresetService.cs
- [ ] 实现 ViewModels/PresetViewModel.cs
- [ ] 实现 Views/Controls/PresetPanel.xaml
- [ ] 加载预设配置
- [ ] 一键应用/批量应用功能
- [ ] 导入/导出自定义配置

### Phase 8: 拖拽识别（1-2天）

目标: 支持拖入文件/文件夹自动识别类型

任务清单:
- [ ] 实现 Models/DragDropInfo.cs - 拖拽信息模型
- [ ] 实现 Services/FileTypeService.cs - 文件类型识别服务
- [ ] 实现 ViewModels/DragDropViewModel.cs - 拖拽识别 ViewModel
- [ ] 实现 Views/Controls/DragDropZone.xaml - 拖拽区域 UI
- [ ] 实现 Helpers/DragDropHelper.cs - 拖拽处理工具
- [ ] 支持文件拖入（识别扩展名）
- [ ] 支持文件夹拖入（识别 Directory 类型）
- [ ] 支持驱动器拖入（识别 Drive 类型）
- [ ] 拖入后自动切换作用域并加载菜单
- [ ] 拖入时视觉反馈（高亮边框、提示文字）

## 关键技术点

### 1. 注册表读取示例

```csharp
using Microsoft.Win32;

public List<ContextMenuItem> GetMenuItems(string scope)
{
    var items = new List<ContextMenuItem>();
    string basePath = scope switch
    {
        "File" => @"HKEY_CLASSES_ROOT\*\shell",
        "Folder" => @"HKEY_CLASSES_ROOT\Directory\shell",
        "FolderBackground" => @"HKEY_CLASSES_ROOT\Directory\Background\shell",
        "Drive" => @"HKEY_CLASSES_ROOT\Drive\shell",
        "Desktop" => @"HKEY_CLASSES_ROOT\DesktopBackground\Shell",
        _ => @"HKEY_CLASSES_ROOT\*\shell"
    };
    
    using var key = Registry.ClassesRoot.OpenSubKey(basePath.Replace("HKEY_CLASSES_ROOT\\", ""));
    if (key != null)
    {
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var item = ParseMenuItem(subKey, subKeyName);
            items.Add(item);
        }
    }
    return items;
}
```

### 2. 右键菜单模拟

```csharp
private void PreviewArea_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
{
    var contextMenu = new ContextMenu();
    foreach (var item in MenuItems)
    {
        var menuItem = new MenuItem { Header = item.Name, Tag = item };
        menuItem.Click += MenuItem_Click;
        
        if (item.SubItems?.Any() == true)
        {
            foreach (var sub in item.SubItems)
            {
                var subMenuItem = new MenuItem { Header = sub.Name, Tag = sub };
                subMenuItem.Click += MenuItem_Click;
                menuItem.Items.Add(subMenuItem);
            }
        }
        contextMenu.Items.Add(menuItem);
    }
    contextMenu.IsOpen = true;
}
```

### 3. UAC 提权

```csharp
public static void RunAsAdmin(Action action)
{
    if (!IsAdmin())
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Assembly.GetExecutingAssembly().Location,
                Verb = "runas",
                Arguments = "--elevated"
            }
        };
        process.Start();
        return;
    }
    action();
}

private static bool IsAdmin()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
```

### 4. 系统项识别

```csharp
private static readonly string[] SystemItems = {
    "open", "edit", "print", "explore", "find",
    "opennewwindow", "opennewprocess", "copyaspath"
};

public bool IsSystemItem(string verb)
{
    return SystemItems.Contains(verb.ToLower());
}
```

### 5. 推荐设置应用示例

```csharp
public void ApplyPreset(PresetItem preset, string scope)
{
    string basePath = GetScopePath(scope);
    string verbName = preset.VerbName;
    
    using var key = Registry.ClassesRoot.CreateSubKey($@"{basePath}\{verbName}");
    key.SetValue("", preset.DisplayName);
    
    if (!string.IsNullOrEmpty(preset.Icon))
        key.SetValue("Icon", preset.Icon);
    
    if (preset.Extended)
        key.SetValue("Extended", "");
    
    using var cmdKey = Registry.ClassesRoot.CreateSubKey($@"{basePath}\{verbName}\command");
    cmdKey.SetValue("", preset.Command);
    
    ShellNotifyHelper.NotifyChange();
}
```

### 6. 拖拽识别示例

```csharp
// DragDropZone.xaml.cs
private void OnDragEnter(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        e.Handled = true;
        DragDropBorder.BorderBrush = Brushes.DodgerBlue;
        DragDropBorder.Background = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255));
        DropHintText.Text = "松开以加载该类型的右键菜单";
    }
}

private void OnDrop(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
        {
            string path = files[0];
            DetectAndLoadScope(path);
        }
    }
    ResetDragUI();
}

private void DetectAndLoadScope(string path)
{
    if (File.Exists(path))
    {
        // 文件：获取扩展名
        string ext = Path.GetExtension(path).ToLower(); // ".txt"
        string progid = GetProgIdForExtension(ext);     // "txtfile"
        
        CurrentScope = new MenuScope
        {
            Type = ScopeType.FileType,
            Extension = ext,
            ProgID = progid,
            DisplayName = $"{ext} 文件"
        };
    }
    else if (Directory.Exists(path))
    {
        // 文件夹
        CurrentScope = new MenuScope
        {
            Type = ScopeType.Folder,
            DisplayName = "文件夹"
        };
    }
    else if (Regex.IsMatch(path, @"^[A-Z]:\\?$", RegexOptions.IgnoreCase))
    {
        // 驱动器
        CurrentScope = new MenuScope
        {
            Type = ScopeType.Drive,
            DisplayName = $"驱动器 {path[0]}:"
        };
    }
    
    LoadMenuItems();
}
```

```csharp
// FileTypeService.cs
public string GetProgIdForExtension(string extension)
{
    // 1. 查找 HKCR\.txt → (Default) = "txtfile"
    using var extKey = Registry.ClassesRoot.OpenSubKey(extension);
    if (extKey != null)
    {
        string progId = extKey.GetValue("") as string;
        if (!string.IsNullOrEmpty(progId))
            return progId;
    }
    
    // 2. 查找 HKLM\SOFTWARE\Classes\.txt
    using var lmKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\{extension}");
    if (lmKey != null)
    {
        string progId = lmKey.GetValue("") as string;
        if (!string.IsNullOrEmpty(progId))
            return progId;
    }
    
    return "*"; // 兜底：通用文件
}

public List<ContextMenuItem> GetMenuItemsForType(string progid)
{
    // 优先用户级，再系统级
    string userPath = $@"Software\Classes\{progid}\shell";
    string systemPath = $@"{progid}\shell";
    
    var items = ReadFromRegistry(Registry.CurrentUser, userPath);
    items.AddRange(ReadFromRegistry(Registry.ClassesRoot, systemPath));
    
    return items;
}
```

```csharp
// WPF XAML 拖拽区域
<Border x:Name="DragDropBorder" 
        AllowDrop="True"
        DragEnter="OnDragEnter"
        DragLeave="OnDragLeave"
        Drop="OnDrop"
        BorderBrush="Gray" BorderThickness="2" CornerRadius="8">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <TextBlock Text="+" FontSize="48" Foreground="Gray"/>
        <TextBlock x:Name="DropHintText" Text="拖拽文件或文件夹到此处" 
                   FontSize="16" Foreground="Gray"/>
    </StackPanel>
</Border>
```

## 验证方案

### 测试清单

1. 作用域切换测试
2. 菜单操作测试（添加、删除、禁用、修改）
3. 二级菜单测试
4. Win11 兼容性测试
5. 备份还原测试
6. 推荐设置测试
7. 拖拽识别测试（文件、文件夹、驱动器、多文件）

### 手动验证步骤

```powershell
# 验证注册表修改
reg query "HKCR\*\shell" /s

# 验证菜单生效（需重启资源管理器）
Stop-Process -Name explorer -Force
Start-Process explorer
```

## 风险与注意事项

| 风险 | 应对措施 |
|------|----------|
| 误删系统关键菜单项 | 系统项标记 + 二次确认 + 自动备份 |
| 权限不足 | UAC 提权 + 明确提示 |
| 注册表修改后需刷新 | 调用 SHChangeNotify 或提示重启资源管理器 |
| Win11 版本差异 | 版本检测 + 功能降级 |

## 参考资源

- [Microsoft: Creating Shortcut Menu Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
- [ContextMenuManager (GitHub)](https://github.com/BluePointLilac/ContextMenuManager)
- [Nilesoft Shell](https://nilesoft.org/)
