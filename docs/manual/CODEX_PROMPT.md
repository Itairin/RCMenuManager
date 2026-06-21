# Codex 提示词 - Windows 右键菜单管理器

将以下提示词喂给 Codex 即可开始开发：

---

## 完整提示词

```
请按照以下开发计划实现 Windows 右键菜单管理器：

【项目信息】
- 项目名称: RCMenuManager
- 项目目录: D:\Itair\RCMenuManager
- 框架: WPF (.NET 9)
- 目标系统: Windows 10/Windows 11
- 开源协议: MIT

【功能需求】
1. 作用域管理: 支持选择文件、文件夹、文件夹背景、驱动器、桌面、自定义扩展名
2. 菜单读取: 从 Windows 注册表读取右键菜单项（一级和二级）
3. 菜单预览: 在软件内右键弹出模拟菜单，可选中任意项
4. 菜单编辑: 支持添加、删除、禁用、修改菜单项
5. 推荐设置: 提供常用右键菜单预设配置，一键应用
6. Win11 支持: 管理新菜单和经典菜单，支持一键切换
7. 安全机制: 自动备份、一键还原、系统项保护、操作日志
8. 拖拽识别: 拖入文件/文件夹自动识别类型，加载对应右键菜单

【技术要求】
- 使用 MVVM 架构
- 使用 CommunityToolkit.Mvvm 工具包
- 注册表操作使用 Microsoft.Win32.Registry
- 需要管理员权限运行（app.manifest 配置 requireAdministrator）
- 支持单文件发布
- 先实现只读模式，验证读取逻辑后再添加编辑功能

【注册表路径参考】
- 文件: HKCR\*\shell
- 文件夹: HKCR\Directory\shell
- 文件夹背景: HKCR\Directory\Background\shell
- 驱动器: HKCR\Drive\shell
- 桌面: HKCR\DesktopBackground\Shell
- Win11 经典菜单开关: HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32

【推荐设置预设】
文件类:
- 用记事本打开: notepad.exe "%1"
- 用 VS Code 打开: code "%1"
- 复制文件路径: cmd /c echo "%1" | clip
- 在终端中打开: pwsh -NoExit -Command "cd '%~dp1'"
- 文件哈希校验: certutil -hashfile "%1" SHA256

文件夹类:
- 在 VS Code 中打开: code "%V"
- 在终端中打开: pwsh -NoExit -Command "cd '%V'"
- 新建文件: cmd /c echo. > "%V\新建文件.txt"

文件夹背景类:
- 在终端中打开: pwsh -NoExit -Command "cd '%V'"
- 新建文本文档: cmd /c echo. > "%V\新建文本文档.txt"

桌面类:
- 打开系统设置: ms-settings:
- 打开任务管理器: taskmgr.exe
- 打开注册表编辑器: regedit.exe

【开发阶段】
Phase 1: 基础框架 - 创建项目、数据模型、基础服务
Phase 2: 核心功能 - 菜单读取、TreeView 展示
Phase 3: 编辑功能 - 增删改菜单项
Phase 4: 右键预览 - 模拟系统右键菜单
Phase 5: Win11 专项 - 新菜单管理
Phase 6: 安全完善 - 备份还原、系统项保护
Phase 7: 推荐设置 - 预设配置一键应用
Phase 8: 拖拽识别 - 拖入文件/文件夹自动识别类型

请从 Phase 1 开始实现，逐步完成所有功能。
```

---

## 分阶段提示词

### Phase 1: 基础框架

```
请创建 RCMenuManager WPF 项目的基础框架：

1. 创建 WPF 项目 (.NET 9)
2. 添加 NuGet 包: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.Drawing.Common
3. 配置 app.manifest 启用 requireAdministrator
4. 创建 Models/ContextMenuItem.cs - 菜单项数据模型
5. 创建 Models/MenuScope.cs - 作用域枚举
6. 创建 Services/RegistryService.cs - 注册表读写基础
7. 创建 Helpers/UacHelper.cs - UAC 提权工具

项目目录: D:\Itair\RCMenuManager
```

### Phase 2: 核心功能

```
请实现 RCMenuManager 的核心功能：

1. 实现 Services/MenuParserService.cs - 解析注册表为菜单项
2. 实现 Services/IconService.cs - 从 DLL/EXE 提取图标
3. 实现 ViewModels/MainViewModel.cs - 主窗口逻辑
4. 实现 ViewModels/MenuItemViewModel.cs - 菜单项 ViewModel
5. 实现 Views/Controls/ScopeSelector.xaml - 作用域选择器
6. 实现 Views/Controls/MenuTreeView.xaml - 菜单树形视图
7. 支持级联菜单（SubCommands）解析

注册表路径:
- 文件: HKCR\*\shell
- 文件夹: HKCR\Directory\shell
- 文件夹背景: HKCR\Directory\Background\shell
- 驱动器: HKCR\Drive\shell
- 桌面: HKCR\DesktopBackground\Shell
```

### Phase 3: 编辑功能

```
请实现 RCMenuManager 的编辑功能：

1. 实现 ViewModels/EditPanelViewModel.cs - 编辑面板逻辑
2. 实现 Views/Controls/EditPanel.xaml - 编辑面板 UI
3. 实现菜单项启用/禁用功能
4. 实现菜单项删除功能
5. 实现菜单项属性编辑（名称、命令、图标）
6. 实现菜单项添加功能

编辑操作对应注册表:
- 删除: 删除 HKCR\...\shell\<verb> 键
- 禁用: 添加 ProgrammaticAccessOnly 值
- 重命名: 修改 (Default) 值
- 修改命令: 修改 command\(Default) 值
- 添加: 创建新的 \shell\<verb>\command 结构
```

### Phase 4: 右键预览

```
请实现 RCMenuManager 的右键预览功能：

1. 实现 Views/Controls/ContextMenuPreview.xaml - 预览区域
2. 实现右键弹出菜单功能（使用 WPF ContextMenu）
3. 支持一级/二级菜单级联显示
4. 菜单项点击选中反馈

在预览区域右键时，显示解析出的菜单项，可选中任意项进行编辑或删除。
```

### Phase 5: Win11 专项

```
请实现 RCMenuManager 的 Win11 专项支持：

1. 检测 Windows 版本（Win10/Win11）
2. 实现 Win11 新菜单/经典菜单切换
3. 管理新菜单 Block 列表
4. Win11 特有菜单项识别

Win11 新菜单控制键:
禁用新菜单（恢复经典菜单）:
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
    (Default) = "" (空字符串)
```

### Phase 6: 安全完善

```
请实现 RCMenuManager 的安全功能：

1. 实现 Services/BackupService.cs - 备份还原服务
2. 实现 Views/Dialogs/BackupDialog.xaml - 备份对话框
3. 实现 Views/Dialogs/ConfirmDialog.xaml - 确认对话框
4. 自动备份机制（修改前导出 .reg）
5. 一键还原功能
6. 系统项保护机制（标记关键项，二次确认）
7. 操作日志记录

系统项识别词: open, edit, print, explore, find, opennewwindow, opennewprocess, copyaspath
```

### Phase 7: 推荐设置

```
请实现 RCMenuManager 的推荐设置功能：

1. 实现 Services/PresetService.cs - 推荐设置服务
2. 实现 ViewModels/PresetViewModel.cs - 推荐设置 ViewModel
3. 实现 Views/Controls/PresetPanel.xaml - 推荐设置面板 UI
4. 加载预设配置（文件类、文件夹类、文件夹背景类、桌面类）
5. 一键应用/批量应用功能
6. 导入/导出自定义配置（JSON 格式）

预设配置见 PRESETS.md
```

### Phase 8: 拖拽识别

```
请实现 RCMenuManager 的拖拽识别功能：

1. 实现 Models/DragDropInfo.cs - 拖拽信息模型
2. 实现 Services/FileTypeService.cs - 文件类型识别服务
3. 实现 ViewModels/DragDropViewModel.cs - 拖拽识别 ViewModel
4. 实现 Views/Controls/DragDropZone.xaml - 拖拽区域 UI
5. 实现 Helpers/DragDropHelper.cs - 拖拽处理工具

功能要求:
- 拖入文件后，自动识别扩展名，查找 HKCR\<.扩展名>\shell
- 如果该扩展名无专属菜单，使用 HKCR\*\shell（通用文件）
- 拖入文件夹后，切换到 HKCR\Directory\shell
- 拖入驱动器后，切换到 HKCR\Drive\shell
- 拖入多个文件时，取第一个文件的扩展名
- 拖入时显示高亮边框和提示文字
- 松开后自动切换作用域并加载菜单项

注册表查找顺序:
1. HKCU\Software\Classes\<.扩展名>\shell (用户级)
2. HKLM\SOFTWARE\Classes\<.扩展名>\shell (系统级)
3. HKCR\<.扩展名>\shell (合并视图)
4. HKCR\*\shell (通用文件，兜底)
```
