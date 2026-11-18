# 用户视角体验修复（待发布）

- **时间**：2026-11-10
- **范围**：从「真实用户连续操作」出发评审 0.7.4，落地 5 项高频摩擦 + 1 个顺手抓到的既有缺陷
- **验证**：`dotnet build` 0 警告 0 错误；测试 **141 → 161 全通过**（新增 20 个用例）

## 落地内容

| # | 问题（用户感知） | 改法 | 位置 |
|---|---|---|---|
| 1 | 保存 / 启停 / 删除后列表重建，详情面板还停在原来那一项；删掉的项还能再点「删除」 | 选中项按注册表路径恢复，路径不存在则清空；`TreeViewItem.IsSelected` 双向绑定到 `MenuItemViewModel.IsSelected` | `MainViewModel.LoadAsync` / `MenuTreeView.xaml(.cs)` / `MenuItemViewModel` |
| 2 | 删一个系统项要连点 3~4 个确认框 | 合并为一次确认，风险以清单列出 | `MainViewModel.Commands.DeleteAsync` |
| 3 | 备份列表「删除」直接删掉唯一的回滚文件 | 删除前确认（窗口级 owner，危险按钮不设为默认） | `BackupDialogViewModel.DeleteBackup` |
| 4 | 日志 5 MB 轮转后，旧备份显示「没有关联的操作日志记录」，备份在却恢复不了 | ① 读取全部轮转代（`.2/.1/当前`）；② 日志缺失时从 `.reg` 首行 `[HKEY_...]` 反推注册表路径（兼容 UTF-16/UTF-8） | `OperationLogService.ReadAll` / `BackupRecord` |
| 5 | 关掉再开：窗口回到 1180×720、作用域回到第一项、分栏比例与「显示 Shift 项」全丢 | 新增 `ISettingsService`，存 `%LocalAppData%\RCMenuManager\settings.json`；几何尺寸按当前工作区钳制，避免换显示器后跑到屏幕外 | `Services/AppSettings.cs` 等 / `MainWindow` / `MainViewModel` |
| 6 | 一个作用域 20~40 项，找一项只能肉眼滚 | 列表加筛选框（Ctrl+F 聚焦、Esc 清空），匹配显示名 / Verb / 命令 / 注册表路径；父项在子孙命中时保持可见；顶部徽章显示「筛选 M / 共 N 项」 | `MenuTreeView.xaml` / `MainViewModel.Matches` |

## 顺带修掉的既有缺陷

`BackupRecord` 用 `RegistryHive.ToString()` 拼路径，得到 `LocalMachine\...`（而非 `HKLM\...`）。`ParseRegistryPath` 认不出时**默认按 HKCU 处理**，于是恢复一条 HKLM 备份时不会请求提权，直接以「恢复失败」告终。改用短名映射（HKCU / HKLM / HKCR / HKU / HKCC），由新测试覆盖。

## 测试补强

- `SettingsServiceTests`：往返、缺失、损坏、越界钳制、目录不可写时静默
- `MenuSearchTests`：显示名 / Verb / 命令 / 注册表路径，父项因子孙命中而可见，不相关查询不误命中
- `XamlLoadTests`：离屏解析 `MenuTreeView`（可抓 StaticResource 漏键）+ 绑定目标反射校验
- `BackupRecordTests` / `OperationLogServiceTests`：UTF-16 与 UTF-8 的 `.reg` 首行解析、日志优先于文件头、轮转后仍可读且顺序正确
- `TestS/AssemblyInfo.cs`：关闭程序集内并行——WPF 线程亲和，且有一个用例会创建共享 `Application`，并行即为竞态

## 仍未做（按优先级）

1. **提权重启会丢弃用户正在做的操作**（只带 `--scope=`，不带 pending action）——最影响连续编辑体验
2. 「新增」写死 HKCU，界面上不说明作用范围，与 README 的「自动提权」表述不符
3. Win11 开关绕过「备份 + 日志」漏斗、无确认、未标「实验性」
4. 无单实例；崩溃只写日志、窗口直接消失
5. 预览页需要用户自己悟出「右键空白处」；暗色模式；图标 DPI
