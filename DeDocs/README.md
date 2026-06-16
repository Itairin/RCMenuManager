# RCMenuManager

Windows 右键菜单管理器 - 可视化管理 Win10/Win11 右键菜单

## 功能特性

- 支持文件、文件夹、驱动器、桌面等作用域
- 一级/二级菜单管理
- Win11 新菜单/经典菜单切换
- 推荐设置一键应用
- 自动备份/一键还原
- 拖拽排序、批量操作
- 搜索框快速定位
- 系统项保护机制
- 操作日志记录

## 截图

(添加截图)

## 下载

(添加下载链接)

## 使用方法

(添加使用说明)

## 开发

### 环境要求

- .NET 9 SDK
- Windows 10/11
- Visual Studio 2022 或 VS Code

### 构建

```bash
git clone https://github.com/yourname/RCMenuManager.git
cd RCMenuManager
dotnet restore
dotnet build
dotnet run
```

### 发布

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 项目结构

```
RCMenuManager/
├── Models/              # 数据模型
├── Services/            # 业务服务
├── ViewModels/          # MVVM ViewModel
├── Views/               # UI 界面
│   ├── Controls/        # 自定义控件
│   └── Dialogs/         # 对话框
├── Converters/          # 值转换器
├── Resources/           # 资源文件
└── Helpers/             # 工具类
```

## 技术栈

- WPF (.NET 9)
- C#
- CommunityToolkit.Mvvm
- Microsoft.Win32.Registry

## 许可证

MIT License

## 参考

- [ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager)
- [Nilesoft Shell](https://nilesoft.org/)
- [Microsoft: Creating Shortcut Menu Handlers](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
