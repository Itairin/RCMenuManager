# Contributing

欢迎一起改进 RCMenuManager。下面是基本约定，方便协作。

## 准备开发环境

1. 安装 [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)。
2. Windows 10 1809+ / Windows 11。
3. 推荐 Visual Studio 2022 17.12+、JetBrains Rider 或 VS Code + C# Dev Kit。

## 构建与测试

```powershell
dotnet restore
dotnet build -c Debug
dotnet test Tests/RCMenuManager.Tests.csproj -c Debug
```

在提交 PR 前请确保 `dotnet build` 0 警告 0 错误，且单元测试全部通过。

## 代码风格

- 遵循根目录 [.editorconfig](.editorconfig)：UTF-8 / CRLF / 4 空格缩进，C# 使用 file-scoped namespace。
- 私有字段使用 `_camelCase`，公开成员 `PascalCase`，本地变量 `camelCase`。
- WPF XAML 优先使用 `Resources/Styles.xaml` 中已定义的 token 与样式，避免引入新的硬编码颜色。
- 新增逻辑配套写测试，至少覆盖关键分支。

## 提交规范

使用 Conventional Commits 风格的中英文均可：

```
feat: 添加 Win11 Block 列表批量清空命令
fix: ContextMenuPreview 在空作用域下崩溃
docs: 更新 PRESETS 清单
refactor: 把 BackupService 拆分到独立目录
test: 为 PresetService 增加冲突路径覆盖
chore: 升级 CommunityToolkit.Mvvm 到 8.3.2
```

## Pull Request

1. 从 `master` 拉取最新代码，基于一个特性分支提交（例如 `feat/win11-block-clear`）。
2. PR 描述里说明目的、改动范围、对注册表写入的影响（如有）。
3. 涉及注册表写入或破坏性行为时，请附上手工冒烟步骤。
4. UI 改动需附前后截图或动图。

## 报告问题

提 Issue 时请提供：

- 系统版本（Win10/Win11 build）。
- 复现步骤，最好附上 `%LocalAppData%\RCMenuManager\crash.log` 与备份目录下的 `.reg`。
- 期望行为与实际行为。

谢谢你的贡献！
