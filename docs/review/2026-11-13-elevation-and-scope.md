# 第二批：提权续做 / 写路径收敛 / 作用范围（待发布）

- **时间**：2026-11-13
- **前置**：`docs/review/2026-11-10-usability-round.md` 已完成
- **验证**：`dotnet build` Debug+Release 均 0 警告 0 错误；测试 **161 → 173 全通过**

## 1. 提权重启不再吞掉用户操作

**问题**：`EnsureAdministratorAsync` 只带 `--scope=` 重新拉起进程，正在编辑的草稿、刚填的新增对话框、待执行的删除全部丢失。用户体感是「填了半天，点确定，弹UAC，回来啥也没有」。

**改法**：
- 新增 `PendingAction` / `PendingActionStore`，记录 kind / hive / subKey / scopeId / verb / draft / backupPath
- 覆盖 6 类操作：`SaveEdit`、`Toggle`、`Delete`、`AddRoot`、`AddChild`、`Restore`
- 提权成功后以 `--resume` 重启，启动后自动重放，状态栏说明「已继续上次的操作」
- 三道保险：**只在带 `--resume` 时消费**（崩溃残留不会误触发）、**10 分钟过期**（过久重放已不是用户当时的意图）、**UAC 被拒立即清除**

## 2. Win11 开关并入「备份 + 日志」漏斗

**问题**：`Win11MenuService` 直接 `DeleteSubKeyTree` 删掉整个 CLSID 树——无确认、无 .reg 备份、不写操作日志，正好击穿产品首页承诺的三件事。

**改法**：
- 新增 `IRegistryTransactor`，`RegistryWriteService` 实现它并暴露 `Run(...)`
- 开关切换与 Block 移除全部改走漏斗（scopeId 记为 `Win11`）
- 切换前加确认对话框，文案标注「依赖未公开的注册表行为，系统更新后可能失效」
- 新增 `Tests/Win11TransactorTests.cs` 钉住路由：每次变更必须恰好经过一次漏斗，且带正确的 key 与 op

## 3. 「新增」支持选择作用范围

**问题**：`AddRootAsync` 写死 `RegistryHive.CurrentUser`，但顶栏作用域显示的是 `HKCR\Directory\shell`、README 又写「修改系统级菜单会自动提权」。用户以为给全机器加的，其实只对自己生效。

**改法**：
- 新增对话框加入「作用范围」：仅当前用户 (HKCU) / 所有用户 (HKCR)
- 选机器范围时用 `MenuScope.ShellSubKey`（ClassesRoot 下的相对路径），用户范围仍用 `HkcuShellSubKey`
- 默认仍是「仅当前用户」，机器范围是显式选择，不是意外
- 子项对话框（`AddChildAsync`）不显示该选项——父项已确定 hive，给选择就是误导

## 测试补强

- `PendingActionStoreTests`：往返、缺失、损坏 JSON、**过期边界**（`MaxAge` 内外的两侧）、`Clear` 后不可重放、`Peek` 不消费
- `Win11TransactorTests`：开关与 Block 移除各走一次漏斗且带正确参数、空 verb 不产生事务、无 transactor 时测试路径仍可用
- `XamlLoadTests`：新增对话框默认用户范围且隐藏选项、`AllowMachineScope` 可开启

## 仍未做

1. 无单实例——两个窗口能同时改同一键，后写覆盖先写
2. 崩溃只写日志、窗口直接消失（`e.Handled` 未设，无提示）
3. 预览页要用户自己悟出「右键空白处」
4. 暗色模式；图标按 DPI 取图；`installer` 仍要求管理员安装
