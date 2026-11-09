# RCMenuManager

[![Build](https://github.com/your-name/RCMenuManager/actions/workflows/ci.yml/badge.svg)](https://github.com/your-name/RCMenuManager/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6.svg)](#)

> Windows 右键菜单可视化管理器，支持 Win10 / Win11 系统级与用户级菜单。

RCMenuManager 是一个基于 WPF (.NET 10) 的桌面工具，让你可以查看、编辑、备份并一键应用 Windows 资源管理器右键菜单的 Shell 扩展项。

## 功能特性

- 多作用域支持：文件 / 所有文件系统对象 / 文件夹 / 文件夹背景 / 驱动器 / 桌面 / 任意扩展名
- 系统级 (HKCR / HKLM) 与用户级 (HKCU) 菜单统一展示
- 一级 / 二级（级联）菜单的可视化树形浏览
- 新增 / 编辑 / 删除 / 启停 / 置顶置底 / Extended (Shift+右键)
- Win11 新菜单与经典菜单一键切换，附 Block 列表管理
- 推荐预设（PRESETS）按分组勾选 + 批量应用
- 写操作自动 `.reg` 备份，可一键还原；操作日志可追溯
- 拖入文件 / 文件夹 / 驱动器自动识别对应作用域
- 系统 Verb 保护、UAC 提权感知、崩溃日志落地


## 快速开始

### 环境要求

- Windows 10 1809+ (x64) 或 Windows 11
- 构建需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 运行：便携单文件版**无需**任何运行时；框架依赖版需要 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)

### 从源码构建

```powershell
git clone https://github.com/your-name/RCMenuManager.git
cd RCMenuManager
dotnet restore
dotnet build -c Debug
dotnet run --project RCMenuManager.csproj
```

### 发布

#### 单文件便携版（推荐分发方式）

```powershell
dotnet publish RCMenuManager.csproj -p:PublishProfile=portable-win-x64
```

产物是**唯一一个文件** `publish\portable\RCMenuManager.exe`（约 66 MB）：

- **自包含**：目标机不需要安装 .NET 10 运行时；
- **零散文件**：预设库 `presets.json` 已嵌入程序集，不再依赖 exe 旁边的文件；
- **免安装**：拷到任意目录双击即可运行，卸载等于删掉这个 exe；
- **数据不丢**：用户配置与备份仍写在 `%LocalAppData%\RCMenuManager\`，替换 exe 升级不受影响。

首次启动时 WPF 的原生 DLL（`wpfgfx_cor3` 等）会解包到 `%TEMP%\.net\`，之后复用——这是 .NET 单文件的标准行为，不影响"单文件"分发。

> 想在**不重新编译**的前提下替换预设库：把新的 `Resources\presets.json` 放到 exe 同目录即可，它优先于内嵌副本；文件损坏时会自动改名为 `.bak` 并回落到内嵌副本。

#### 框架依赖版（体积最小）

```powershell
dotnet publish RCMenuManager.csproj -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true
```

体积约 2 MB，但目标机必须先安装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。

产物默认位于 `bin\Release\net10.0-windows\win-x64\publish\`。

### 运行测试

```powershell
dotnet test Tests/RCMenuManager.Tests.csproj -c Debug
```

## 使用提示

- 启动后顶部「作用域」下拉切换目标位置，「扩展名」可加载任意 `.ext` 的菜单。
- 修改系统级菜单（HKCR/HKLM）需要管理员权限，应用会自动提示提权。
- 每一次写入都会在 `%LocalAppData%\RCMenuManager\Backups\` 留下 `.reg` 备份；右上角「备份」按钮可查看与还原。
- 「推荐」面板包含常用预设（VS Code 打开、复制路径、文件哈希等），勾选后批量应用。
- Win11 用户可在「Win11」面板切换新旧菜单，并管理被隐藏的 Verb Block 列表。

## 项目结构

```
RCMenuManager/
├── Models/            # POCO 数据模型
├── Services/          # 注册表 / 备份 / 预设 / Win11 等业务服务
├── ViewModels/        # MVVM ViewModel（CommunityToolkit.Mvvm）
├── Views/
│   ├── Controls/      # ScopeBar / MenuTreeView / ContextMenuPreview / DetailsPanel
│   └── Dialogs/       # 备份 / 推荐 / Win11 / 新增 / 图标选择 / 确认
├── Converters/        # WPF 值转换器
├── Resources/         # 设计系统 (Styles.xaml)、预设 JSON
├── Helpers/           # 注册表、MUI、UAC、Shell 通知工具
├── Tests/             # xUnit 测试（独立项目）
└── docs/              # 设计文档、注册表参考、预设清单
```

## 文档

- [开发说明](docs/manual/DEVELOPMENT.md)
- [注册表参考](docs/manual/REGISTRY_REFERENCE.md)
- [推荐预设清单](docs/manual/PRESETS.md)
- 阶段性设计与冒烟记录见 `docs/superpowers/`

## 致谢

- [ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager)
- [Nilesoft Shell](https://nilesoft.org/)
- [Microsoft Docs: Shell Extensions](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)

## 许可证

[MIT](LICENSE)
