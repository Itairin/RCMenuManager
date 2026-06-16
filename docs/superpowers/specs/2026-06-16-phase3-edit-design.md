# Phase 3 设计：右键菜单条目的编辑闭环

创建日期：2026-06-16

## 目标

在 RCMenuManager 中实现"对单条 verb 执行 CRUD"的完整编辑闭环：启用、禁用、删除、改显示名、改命令、改图标、切换 Extended、修改 Position、新增一级菜单、新增二级菜单（嵌套 \shell 形式）、图标选择器。所有写操作前自动备份目标 verb 子树为 .reg 文件。写入需要管理员的 hive 时通过整进程重启进行按需提权。

## 非目标

以下显式不在本期范围内：

- 拖拽排序。Position 仅以 Top / Default / Bottom 三档单选呈现。
- 通过 SubCommands + HKLM CommandStore 创建共享二级菜单。继续保留只读解析。
- 通过 ExtendedSubCommandsKey 创建二级菜单。继续保留只读解析。
- MUIVerb 间接资源（@dll,-id）的写入。显示名永远写到 (Default)。
- 推荐预设、批量操作（Phase 7）。
- 完整的备份还原 UI（Phase 6）。本期只产出可被 Phase 6 直接消费的备份文件和操作日志。

## 服务层

新增三个服务，旧服务 RegistryService / MenuParserService / IconService 不动：

- RegistryWriteService（Services/RegistryWriteService.cs）：所有 verb 写入操作的唯一入口，决定目标 hive，调备份服务，调 IRegistryWriter 执行写入，写完调 SHChangeNotify，最后调日志服务记录。
- BackupService（Services/BackupService.cs）：调用 reg.exe export 把指定 hive\sub-key 子树导出为 .reg。文件路径 %LOCALAPPDATA%\RCMenuManager\backups\<时间戳>-<scope>-<verb>.reg。返回备份文件绝对路径供日志使用。导出失败必须阻塞后续写入。
- OperationLogService（Services/OperationLogService.cs）：把每一次成功/失败的写入操作以单行 JSON 写入 %LOCALAPPDATA%\RCMenuManager\operations.log。字段：timestamp, scope, verb, op, hive, backupPath, success, error?。Phase 6 还原 UI 直接读这个日志。

RegistryWriteService 不直接调 Microsoft.Win32.Registry，而是通过一个内部抽象 IRegistryWriter，提供 KeyExists / CreateSubKey / DeleteSubKeyTree / SetStringValue / DeleteValue / ValueExists 六个基本动作。生产实现 Win32RegistryWriter 基于 Microsoft.Win32.RegistryKey；测试实现 InMemoryRegistryWriter 用字典模拟，方便给 RegistryWriteService 写单元测试。

## 注册表语义

每个 UI 操作到注册表写入的对照：

- 禁用：在 verb 键上写空字符串 ProgrammaticAccessOnly 值。
- 启用：删除 ProgrammaticAccessOnly 值。
- 删除：删除整个 verb 子树。
- 改显示名：在 verb 键上写 (Default) 为新名。
- 改命令：在 verb\command 键上写 (Default) 为新命令。
- 改图标：非空时写 Icon 值；空字符串时删 Icon 值。
- Extended：加/删 Extended 值（REG_SZ 空字符串）。
- Position：Top/Bottom 时写 Position 值；Default 时删除。
- 新增一级：建 verb 键 -> 写 (Default) / Icon / Extended / Position -> 建 verb\command 键 -> 写 (Default)。
- 新增二级（父）：建 verb 键 -> 写 (Default) 等 -> 不建 command 子键 -> 建 verb\shell 子键。
- 新增二级（子）：在父 verb 的 \shell 下按"新增一级"流程创建。

## hive 选择

读取时 Origin 字段记录 verb 实际所在 hive。写入规则：

- 编辑/删除：写回该 verb 的 Origin hive。
- 新增：默认写 HKCU（Software\Classes\<scope-path>）以避开 UAC。Phase 3 不暴露"写到 HKLM"的开关，需要时通过编辑该 verb 后再修改。
- HKCR 上的条目实际对应 HKLM。

## 写入流程

1. RegistryWriteService 算出目标 hive + subKey。
2. 权限检查：写 HKLM 而当前不是管理员 -> 抛 ElevationRequiredException，由 ViewModel 捕获、提示重启。
3. 备份：BackupService.Export(hive, subKey) -> 得到绝对路径。导出失败抛异常，中止流程。
4. 写入：通过 IRegistryWriter 执行具体动作。新增 verb 时先用 KeyExists 做冲突检测，已存在抛 RegistryConflictException。
5. ShellNotifyHelper.NotifyAssociationsChanged()。
6. OperationLogService.Append(...) 记录成功条目，含备份路径。
7. 异常路径：捕获后写一条 success=false 日志，重抛给 ViewModel。

## 提权策略

1. app.manifest 保持 asInvoker。
2. 写入 HKLM 时由 RegistryWriteService 抛 ElevationRequiredException。
3. ViewModel 捕获后弹 ConfirmDialog：标题"需要管理员权限"，正文"该操作需要以管理员身份重启程序，是否继续？"。
4. 用户同意 -> UacHelper.RelaunchAsAdmin("--scope=" + scopeId)，然后 Application.Current.Shutdown()。
5. 启动时 App.xaml.cs 解析 --scope= 参数，把对应 ScopeOption 赋给 MainViewModel.SelectedScope，自动恢复上下文。
6. --scope= 协议：例如 --scope=Folder、--scope=Drive、--scope=FileExt:.txt。

## UI / ViewModel

### DetailsPanel

面板有"查看"与"编辑/新增"两态。

查看态在现有字段上方加一行按钮：

- 编辑（始终可见）
- 删除（始终可见）
- 新增子项（仅当条目支持嵌套时显示，即条目本身没有 command 或者已有 \shell 子键）
- 启用/禁用（按当前 IsProgrammaticOnly 切换文案）

作用域级别的"新增一级菜单"按钮放在树视图顶部。

编辑/新增态字段：verb 名（编辑时灰掉）、显示名、命令、图标路径（带"浏览…"按钮）、Extended（CheckBox）、Position（三档单选 Default/Top/Bottom）。底部按钮：保存 / 取消。

图标资源选择器：选中 .dll/.exe 后弹一个 Modal，显示 ExtractIconExW 枚举出来的前 N=64 个图标缩略图，单选确认后回填路径形如 C:\Windows\System32\shell32.dll,42。选 .ico 直接回填路径。

### 新增二级菜单

在某个父级 verb 上点"新增子项" -> 弹 Modal，表单与"新增一级菜单"一样，verb 名可编辑。父 verb 如果当前还没有 \shell 子键，由 RegistryWriteService 自动建。

### 系统 verb 保护

目标 verb 的 IsSystemVerb = true 时，编辑/删除/禁用前弹 ConfirmDialog，正文"该项是系统关键项 (open/edit/...)，确认继续？"，默认按钮为"取消"。新增不受此影响：如果用户输入了系统 verb 名，冲突检测会拦下。

### MainViewModel 新命令

- EditCommand(MenuItemViewModel)
- DeleteCommand(MenuItemViewModel)
- ToggleEnabledCommand(MenuItemViewModel)
- AddRootCommand
- AddChildCommand(MenuItemViewModel parent)
- 公共方法 EnsureAdministratorAsync(targetHive) 实现重启提权

### 启动参数解析

App.xaml.cs.OnStartup 扫描 e.Args，识别 --scope=...，结果存到 MainViewModel 的字段，构造完后赋给 SelectedScope。

## 错误处理

所有写入异常分三类：

- ElevationRequiredException：UI 用提权重启对话框处理。
- RegistryConflictException：UI 显示红色 inline 错误，"目标键已存在：…"。
- 其他：UI 显示红色 inline 错误，"操作失败：<message>。已备份到 <path>。"

成功路径：状态栏短暂显示绿色"已保存"，并自动 RefreshAsync() 当前作用域刷新树视图。

## 测试

RegistryWriteService 通过 IRegistryWriter 抽象隔离真实注册表，写一个 InMemoryRegistryWriter 实现，覆盖以下场景：

- 启用/禁用/删除/改显示名/改命令/改图标/Extended/Position 的写入路径
- 新增一级 / 新增二级父 / 新增二级子
- HKCU 优先选择
- 备份调用顺序在写入之前
- 冲突检测：目标 key 已存在时抛 RegistryConflictException，不覆盖
- 系统 verb 检测的辅助方法（保护逻辑在 ViewModel 层，但 Service 层提供 IsSystemVerb 接口）
- ElevationRequiredException 的判定：写 HKLM 且非管理员

UI 没有自动化测试，用手动 smoke 验证：在 HKCU 下创建一个测试 verb -> 启停 -> 改命令 -> 删除 -> 验证清理干净。

## 文件清单

本期会新增/修改的文件：

新增：

- Services/IRegistryWriter.cs
- Services/Win32RegistryWriter.cs
- Services/RegistryWriteService.cs
- Services/BackupService.cs
- Services/OperationLogService.cs
- Models/RegistryWriteExceptions.cs（ElevationRequiredException、RegistryConflictException）
- Models/EditableVerbDraft.cs（编辑/新增表单的 ViewModel 数据载体）
- ViewModels/EditPanelViewModel.cs
- ViewModels/IconPickerViewModel.cs
- Views/Dialogs/ConfirmDialog.xaml(.cs)
- Views/Dialogs/IconPickerDialog.xaml(.cs)
- Views/Dialogs/AddVerbDialog.xaml(.cs)
- Tests/RCMenuManager.Tests.csproj（xUnit + 覆盖 RegistryWriteService）

修改：

- Models/MenuScope.cs（加 ScopeId / WritePath，用于 --scope= 协议）
- ViewModels/MainViewModel.cs（命令 + 重启提权 + 启动参数）
- Views/Controls/DetailsPanel.xaml(.cs)（查看/编辑两态）
- Views/Controls/MenuTreeView.xaml(.cs)（顶部加"新增一级菜单"）
- App.xaml.cs（注册新服务、命令行解析）
- RCMenuManager.csproj（不变；测试项目通过新解决方案文件聚合）

## 风险与决定

- HKLM 写入的二次确认靠 OS 的 UAC 提示。我们额外在 ViewModel 加一次确认对话框，给用户机会取消。
- 备份失败（reg.exe 不在 PATH 或 LOCALAPPDATA 不可写）会阻塞写入。我们认为这是正确的——失去安全网应当让用户立即知晓。
- 显示名是 (Default) 时，对应解析路径 ResolveDisplayName 优先取 MUIVerb；如果原 verb 写了 MUIVerb，编辑后用户改的"显示名"会被 MUIVerb 覆盖。本期不处理这个边缘情况，编辑面板提示"该项使用 MUIVerb 显示，修改 (Default) 不生效；如需改名请先清除 MUIVerb"。
