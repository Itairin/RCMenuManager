# RCMenuManager 5 个月后技术复核

- **复核时间**：2026-11-09
- **复核对象**：`HEAD 98b555d`（2026-06-21），即 2026-06-16 ~ 06-21 六天内完成的 59 个提交
- **规模**：应用代码 4,055 行 C#（不含测试），12 个测试文件 / 72 个用例，8 个 phase 的 spec + plan + smoke 文档，31 条内置预设
- **范围**：设计思路、实现方式、依赖与工具体系

> 说明：复核基于已提交状态（`HEAD 98b555d`），当时未修改仓库任何文件，验证在临时副本 `%TEMP%\rcm-review` 中进行。
> 复核完成后已按本文结论实施修复，落地情况见文末「附录 A：修复记录（0.7.0）」。

---

## 0. 一句话结论

**架构选型没有错判，实现细节有一处阻塞级缺陷。**

WPF + MVVM + `IRegistryWriter` 抽象 + 写入单一漏斗，到今天仍然是最适合这类 Windows shell 工具的组合，不需要换栈。但"备份先行"被无条件塞进写入漏斗，与"新增 verb 时键还不存在"的语义直接冲突——导致 **新增菜单项、新增子项、推荐预设应用这三个核心功能在当前代码上 100% 失败**；而测试用的替身比真实实现宽松，恰好把这个缺陷遮盖了。

叠加两件到期事项：.NET 9 的官方支持**明天（2026-11-10）结束**，`NuGet.config` 让任何其他机器无法还原依赖。

---

## 1. 设计思路复核：哪些判断仍然成立

| 当时的决策 | 5 个月后判断 | 依据 |
|---|---|---|
| WPF (.NET) + 直接 Registry / Win32 调用 | **成立** | 场景是 COM、注册表、Shell 通知，全部 Win32 原生路径。迁 WinUI 3 无收益，Avalonia 反而增加跨平台抽象成本 |
| 分层：Services / ViewModels / Views / Models，VM 不碰注册表 | **成立** | 22 个服务文件职责清晰，VM 只依赖接口，读写分离干净 |
| `IRegistryWriter` + `InMemoryRegistryWriter` 测试替身 | **成立且关键** | 这次能用低成本复现问题，全靠这层抽象 |
| 写入单一漏斗 `RegistryWriteService` | **方向对，粒度错** | 漏斗保证了"备份→写→通知→日志"不被绕过；但一次用户动作被拆成 5 个独立事务 |
| 备份先行（fail-closed） | **原则对，前提错** | 备份要求"键已存在"，而这正是新增操作的例外 |
| 系统 verb 保护 + UAC 感知 + 崩溃日志落地 | **成立** | 这三条是同类工具最常见的翻车点，当时就防住了 |
| 多 phase 的 spec → plan → smoke 文档流程 | **成立，但 smoke 是"清单"不是"记录"** | 8 份 smoke 只写了"应该看到什么"，没有勾选与实测结果，所以没人发现新增流程是坏的 |
| 手写 1,033 行 Fluent 风格主题 | **部分过时** | 无深色模式、强调色硬编码，而 .NET 9 起 WPF 已内置 Fluent 主题能力 |
| "通过注册表 verb 管理右键菜单" | **部分失效** | 注册表 verb 只作用于经典菜单层；Win11 新菜单由 `IExplorerCommand` + 包身份决定 |

---

## 2. 已过时的做法

### 2.1 目标框架：.NET 9 明天到期

| 位置 | 当前 | 状态 | 建议 |
|---|---|---|---|
| `RCMenuManager.csproj` / `Tests/*.csproj` | `net9.0-windows` | STS，**EOL 2026-11-10** | `net10.0-windows`（LTS 至 **2028-11-14**） |
| `.github/workflows/*.yml` | `dotnet-version: 9.0.x` | 同上 | `10.0.x` |
| `README.md`、`docs/manual/DEVELOPMENT.md` | .NET 9 徽章 / `dotnet new -f net9.0-windows` | 文档过期 | 同步更新 |

本次升级在代码层面**没有破坏性 API**（WPF、`Microsoft.Win32.Registry`、CommunityToolkit 均兼容 net10），主要工作量在 CI 与安装脚本的运行时检测；收益是把"支持窗口"从 1 天延长到 2 年。

### 2.2 依赖版本

| 包 | 当前 | 建议 | 理由 |
|---|---|---|---|
| `CommunityToolkit.Mvvm` | 8.3.2 | **8.4.2**（2026-03-25） | 8.4 修了源生成器与 `RelayCommand` 取消相关的若干缺陷 |
| `Microsoft.Extensions.DependencyInjection` | 9.0.0 | **10.0.x** | 与 TFM 对齐 |
| `System.Drawing.Common` | 9.0.0 | **删除** | 全仓库 0 处引用，纯冗余依赖 |
| `Microsoft.Win32.Registry` | 5.0.0 | **删除** | 自 .NET 5 起 Windows 桌面框架已内置；5.0.0 只是 netstandard 转发壳 |
| `xunit` 2.9.2 + `Microsoft.NET.Test.Sdk` 17.11.1 | — | xunit.v3 + Microsoft.Testing.Platform（可选） | 中优先级，可与测试补强一起做 |
| — | 无 | `Directory.Packages.props` + `packages.lock.json` | 依赖集中管理 + 可重现还原 |

### 2.3 工具体系：两处会让别人直接构建失败

1. **`NuGet.config`** 把包源清空并指向 `%USERPROFILE%\.nuget\packages` 作为 fallback 目录。换任何一台机器（包括 CI）还原直接报错——实测 `NU1301` + `MSB4018`。当时大概是为了离线沙箱跑通，但把环境假设提交进了仓库。→ 删除该文件，或只保留 nuget.org。
2. **GitHub Actions** `actions/checkout@v4` / `actions/setup-dotnet@v4` 已落后（两者均发布 v5），且未 pin 到 commit SHA、无包缓存、无漏洞扫描、无覆盖率门禁。
3. **无代码签名**：未签名安装包在现代 Windows 上会被 SmartScreen 直接拦，这是分发层面的硬伤（Azure Trusted Signing 约 10 美元/月）。

### 2.4 Win11 相关假设

- `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` 置空仍是切回经典菜单的通行做法，**25H2 上依然有效**，但它是未公开行为，微软 Q&A 已有"deprecated"表述。→ UI 应标注"实验性"，并在 smoke 里加"系统更新后复查"。
- 更要紧的是产品叙事：这个手段只影响经典菜单层，新菜单项由 `IExplorerCommand` + 稀疏 MSIX 决定，注册表改不了。把能力描述收敛成"管理经典菜单 + 一键切回经典菜单"，比暗示"管理系统右键菜单"更站得住。

---

## 3. 实现层问题（按严重度）

### P0-1｜新增菜单项 / 推荐预设 100% 失败（阻塞级，已实测）

探针测试输出：

```
exception => System.InvalidOperationException
message   => 目标键不存在：HKCU\Software\Classes\Directory\shell\ReviewProbe
keyCreated=> False
```

原因链：

1. `RegistryWriteService.Run()`（`:154`）**无条件**先 `_backup.Export(...)`；
2. `BackupService.Export()`（`:24`）第一行 `EnsureKeyExists(...)`，键不存在就抛异常；
3. `CreateRootVerb()`（`:86`）的前提恰恰是"键还不存在"。

影响面：`ScopeBar` 的"+ 新增菜单项"、详情面板的"新增子项"、推荐预设的"应用选中"（`PresetService.cs:75/82`）全部走 `CreateRootVerb` → 全部失败，用户只看到"操作失败：目标键不存在…"。

**为什么测试没抓到**：`RegistryWriteServiceTests` 用 `RecordingBackup`（永远返回成功，不校验键存在），替身契约比真实实现宽松。这是典型 mock 漂移，不是覆盖率不足。

修法（择一）：

- **A（最小改动）**：`Export` 返回 `string?`，键不存在时返回 `null` 跳过备份，日志 `backupPath=null`；
- **B（更彻底）**：引入 `IWriteTransaction`，`Begin` 时快照父键，创建场景天然没有"前状态"。

验收：非提权 / 提权两种身份各跑一遍真实冒烟；补 2 条"真实 BackupService + 不存在键"的测试。

### P0-2｜备份文件名同秒碰撞，原始快照被覆盖（数据安全）

`BackupService.cs:25` 时间戳精度到秒，文件名 `{时间}-{范围}-{verb}.reg`。同一个 verb 在同一秒内的多次写产生同名文件，`reg export /y` 直接覆盖。叠加 P1-1（一次保存 5 次写），**第一次写入前导出的"原始状态"会被第 2~5 次导出覆盖成"半改完状态"**。

对一个宣称"删除前自动备份、可一键还原"的工具，这是核心承诺被击穿。

修法：毫秒 + 进程内自增序号（或 GUID 后缀）；事务化后每次用户动作只留一份备份。

### P1-1｜一次"保存" = 5 个独立写事务

`MainViewModel.Commands.cs` 的 `SaveEditAsync` 连续调用 `UpdateDisplayName` / `UpdateCommand` / `UpdateIcon` / `SetExtended` / `SetPosition` → 5 次 `reg.exe` 导出 + 5 次 `SHChangeNotify` + 5 条日志 + 5 个 `.reg` 文件，且全部同步跑在 UI 线程。

修法：`BeginTransaction(scopeId)` 收集变更，`Commit` 时一次备份、一次通知、一条日志；`await Task.Run(commit)`。预期：外部进程调用 5→1，UI 卡顿消失，日志噪声降到 1/5。

### P1-2｜无取消、无版本号，快速切 scope 会串台

`MainViewModel.cs:128` `_ = LoadAsync(...)` 即发即忘；`LoadAsync` 无 `CancellationToken`、无"最后一次请求"判定 → A 的加载结果可能覆盖 B 的列表（状态栏显示 B，列表是 A 的项）。

修法：`IAsyncRelayCommand` + `AsyncRelayCommandOptions.AllowConcurrentExecutions=false`；内部 CTS + `_loadVersion` 比对。顺带清掉 `await Task.CompletedTask` 这类空等待。

### P1-3｜HKCR 探测是纯重复，且 HKCR 写回路径映射错误（潜在）

`RegistryService` 为每个 scope 额外查 `HKCR\*\shell` 等，而 HKCR 正是 `HKCU\Software\Classes` + `HKLM\SOFTWARE\Classes` 的合并视图，前两者已查过 → 结果在去重时全部丢弃，只增加每 scope 3~6 次键枚举。

更危险的是 `MenuItemViewModel.cs:84` 把 `ClassesRoot` 映射为 `LocalMachine`，而 `SubKey` 是从 `RegistryPath` 字符串切出来的 `*\shell\xxx`（**不带 `SOFTWARE\Classes\`**）→ 一旦真有 HKCR-origin 条目，写操作会落到 `HKLM\*\shell\xxx`，而 `SetStringValue` 会**静默创建**这个错误键。

修法：删掉 HKCR 探测；或在映射时补前缀。同时把 hive + subkey 作为结构化字段保存在 `ContextMenuItem` 上，不要再用字符串切。

### P1-4｜CommandStore 里的共享 verb 被当成本地子项

`MenuParserService.OpenCommandStore()` 把 `CommandStore\shell\<verb>` 解析成 child，origin 记为 HKLM，UI 与自己菜单的子项**看不出区别**。对它执行删除/禁用会改动全局（所有引用该 `SubCommands` 的菜单），而且无法从界面判断。

修法：模型加 `SharedDefinition` 标记 + 来源说明（"该子项来自系统命令库，修改会影响其他菜单"）；删除前二次确认，或改为"从父菜单移除"（只摘 `SubCommands` 列表项，不删定义）。

### P1-5｜`DelegateExecute` 类 verb 未识别，编辑会破坏行为

`ParseVerb` 只读 `\command` 默认值。现代 verb 大量使用 `DelegateExecute`（如"压缩为 ZIP"及部分系统项），这类项在 UI 里命令为空、`IsParentOnly=false`，用户编辑"命令"会新建 `command` 子键并覆盖原有语义。

同类未处理值：`AppliesTo`（条件显示）、`CommandFlags`、`HasLUAShield`、`MultiSelectModel`、`SeparatorBefore/After`。

修法：模型补字段；详情面板对 `DelegateExecute` 显示"由系统代理执行"并禁用命令编辑；`AppliesTo` 展示为"仅对匹配项显示"。

### P1-6｜`reg.exe` 依赖 + 无超时

`BackupService` 的导出/导入都是启动外部进程 + `WaitForExit()`（无超时）。任何环境禁掉 `reg.exe`（AV、受限策略、CI 沙箱），**所有写入 fail-closed**——本次跑测试就有 1 条因这个原因失败。

修法：用 `RegistryKey` 递归遍历自实现 `.reg` 导出（约 120 行，可单测），或保留 `reg.exe` 但加超时与降级路径；把"备份失败"和"写入失败"在 UI 上区分为两种提示。

### P1-7｜错误通道错位

所有写失败都写进 `EditPanel.ErrorMessage`，但新增/删除/预设应用时编辑面板根本不在编辑态 → 错误不可见，只剩状态栏一句话。

修法：统一通知服务（状态栏 + 可展开错误区/Toast），失败时附带 `backupPath` 与"打开日志"入口。

### P2｜体验、维护与工程化

| 项 | 现状 | 建议 |
|---|---|---|
| 图标清晰度 | `ExtractIconExW` 取 large（32×32），150%/200% DPI 下发虚 | `SHDefExtractIconW` 按 DPI 取 20/24/32/48/64，或 `IShellItemImageFactory` |
| 图标枚举阻塞 | `EnumerateIconsFromFile` 在 UI 线程同步循环 64 次 | 异步 + 可取消 |
| MUI 解析无缓存 | 每次加载对每条调 `SHLoadIndirectString` | 进程内字典缓存（key = 原始串） |
| 加载串行 | `LoadAsync` 顺序跑两次 `Task.Run` | 合并为一次并行 |
| 重启 Explorer | `taskkill /f` + 3 秒阻塞，无 try/catch、无警告 | 提示"将关闭所有资源管理器窗口"、非阻塞、失败反馈 |
| 操作日志 | 永不轮转（注释自认），无大小上限，跨进程 append 可能 IO 异常 | 5 MB 轮转保留 N 份；吞掉 IO 错误并降级 |
| 单实例 | 无保护，两个进程可同时改同一键 | Mutex + 激活已有窗口 |
| 主题 | 1,033 行硬编码色板，无深色、不跟随系统强调色 | 颜色 token 化，支持 `ThemeMode="System"` 与深色变体 |
| 本地化 | XAML/VM/异常消息全硬编码中文 | 抽 `.resx`，`NeutralLanguage` 已有基础 |
| 安装语义 | installer `PrivilegesRequired=admin`，manifest `asInvoker` | 改 per-user 安装（`{userappdata}` + `lowest`），或明确"HKLM 编辑需重启提权" |
| 测试缺口 | `MenuParserService`（476 行，3 种级联 + MUI 解析）**0 测试**；`IconService` 0 测试；无真实注册表契约测试；无覆盖率 | 用 `HKCU\Software\RCMenuManagerTests` 一次性键做 real-writer 契约测试 |

---

## 4. 优先级路线图

| 优先级 | 动作 | 主要文件 | 预期效果 |
|---|---|---|---|
| **P0-1** | 备份对"不存在的键"降级（返回 `null`） | `BackupService`、`RegistryWriteService`、`IBackupService` | 新增/预设应用从 **0% → 100%** 可用 |
| **P0-2** | 备份文件名唯一化（毫秒 + 序号） | `BackupService.cs:25` | 恢复语义从"可能恢复到半改完状态"变为"恢复到操作前" |
| **P0-3** | TFM → `net10.0-windows`，同步 CI / README / 安装脚本 | `*.csproj`、`.github/workflows/*` | 支持窗口 1 天 → 2 年（2028-11-14） |
| **P0-4** | 修 `NuGet.config`（删本地 fallback 源） | `NuGet.config` | 首次克隆构建从**必然失败**变为可还原；CI 不再首跑即红 |
| **P1-1** | 写操作事务化：单备份 / 单通知 / 单日志 | `RegistryWriteService`、`MainViewModel.Commands` | 单次保存外部进程 5→1，UI 不再卡顿，日志噪声 1/5 |
| **P1-2** | 加载可取消 + 版本号守卫 | `MainViewModel` | 切换 scope 结果确定，消除串台 |
| **P1-3** | 删除 HKCR 冗余探测；hive/subkey 结构化 | `RegistryService`、`MenuItemViewModel`、`ContextMenuItem` | 每 scope 少 3~6 次键枚举，消除错误写回路径 |
| **P1-4** | CommandStore 共享项标记与防误删 | `MenuParserService`、`DetailsPanel` | 避免全局性误改 |
| **P1-5** | 识别 `DelegateExecute` / `AppliesTo` 等值 | `MenuParserService`、详情面板 | 展示准确，不再"编辑即破坏" |
| **P1-6** | 去 `reg.exe` 依赖或加超时降级 | `BackupService` | 可在受限环境运行与测试；恢复链路可单测 |
| **P1-7** | 统一错误通道 | 新增 `INotificationService`、各 Dialog VM | 失败可见、可追溯 |
| **P2** | 依赖清理（删 2 个冗余包）、图标 DPI、MUI 缓存、日志轮转、单实例、主题 token 化、测试补强、签名、i18n | 多处 | 体积/启动/可维护性/分发体验 |

**整体量化预期**：核心功能可用性 0% → 100%；单次保存外部进程调用 5 → 1；每 scope 注册表枚举减少 30%~50%；恢复可靠性从"可能恢复到中间态"变为"恢复到操作前"；平台支持期 1 天 → 2 年。

---

## 5. 不建议做的事（避免过度工程）

- **不迁 WinUI 3 / Avalonia**：注册表、COM、Shell 通知全在 Win32 侧，WPF 已足够；迁移成本远大于收益。
- **不引入 Prism / ReactiveUI / MediatR**：10 个服务的 DI + CommunityToolkit 已覆盖需求，再加一层只会让 phase 文档更难维护。
- **不引入数据库**：`.reg` 文件 + JSONL 日志已满足审计诉求，加 SQLite 反而增加迁移与并发风险。
- **不做 trimming / AOT**：WPF 不支持 AOT，自包含 + 单文件已经够用。
- **不去逆向新菜单的 `IExplorerCommand`**：短期不可行，把精力放在"经典菜单管理 + 一键切换"的可靠性上更划算。

---

## 6. 复核记录（可复现）

```text
# 导出已提交状态到临时副本，不改动仓库
git archive HEAD | tar -x -C %TEMP%\rcm-review

# 原 NuGet.config 在本机还原失败（换了机器一样失败）
error NU1301: 本地源"C:\Users\<user>\.nuget\packages"不存在
error MSB4018: 无法找到回退包文件夹"C:\Users\<user>\.nuget\packages"

# 换回 nuget.org 后跑测试
失败!  失败: 1，通过: 71，总计: 72   (net9.0)
  失败项为 BackupServiceTests.Export_writes_reg_file_with_expected_naming
  原因：执行环境禁用 reg.exe，非代码缺陷（但暴露了 P1-6 的依赖风险）

# 探针测试 1：CreateRootVerb + 真实 BackupService
exception => System.InvalidOperationException
message   => 目标键不存在：HKCU\Software\Classes\Directory\shell\ReviewProbe
keyCreated=> False

# 事实核查
.NET 9   STS  支持至 2026-11-10        → 已到期（明天）
.NET 10  LTS  支持至 2028-11-14        → 建议目标
CommunityToolkit.Mvvm 最新 8.4.2（2026-03-25）
Win11 25H2 仍支持 CLSID {86ca1aa0…} 覆盖（未公开行为，微软 Q&A 有 deprecated 表述）
```

---

## 附录 A：修复记录（0.7.0，2026-11-09）

复核结论已落地，`dotnet test` **80 通过 / 0 失败**（net10.0，Debug + Release 各跑一次），framework-dependent 单文件发布产物启动正常。

### 已修复

| 编号 | 问题 | 处理方式 |
|---|---|---|
| P0-1 | 新增 / 预设应用必定失败 | `IBackupService.Export` 改返回 `string?`，目标键不存在时返回 `null`（创建语义），`RegistryWriteService.Execute` 不再把 null 当失败；补 2 条回归测试（真实 `BackupService` + 不存在键、创建成功且日志 `backupPath=null`） |
| P0-2 | 备份同名覆盖 | 文件名改为 `{时间戳}-{3 位序号}-{scope}-{verb}.reg`，序号落盘前探测，确保同秒多次写各得一份；`BackupRecord` 解析兼容新旧两种命名 |
| P0-3 | .NET 9 到期 | TFM → `net10.0-windows`（包含测试项目），新增 `global.json` 锁 SDK 10.0.100（rollForward latestFeature），CI / release 工作流、README、开发文档、安装脚本版本同步到 0.7.0 / .NET 10 |
| P0-4 | NuGet.config 让还原失败 | 删除本机 fallback 源，改为仅 nuget.org；CI 增加 `dotnet list package --vulnerable` 审计步骤，action 升级到 checkout@v5 / setup-dotnet@v5 |
| P1-1 | 一次保存 = 5 个写事务 | 新增 `RegistryWriteService.SaveEdits`：单次备份、单次 `SHChangeNotify`、单条日志；命令层改为 `await Task.Run(...)`，不再阻塞 UI 线程；各单值方法统一走同一个 `Execute` 内核 |
| P1-2 | 切 scope 串台 | `LoadAsync` 引入 `CancellationTokenSource` + 版本令牌，过期结果丢弃；解析与 shellex 枚举合并到一次后台任务 |
| P1-3 | HKCR 冗余探测 / 写回路径错误 | 删除全部 HKCR 查询（HKCU + HKLM 已覆盖合并视图）；`MenuItemViewModel.SubKey` 对 HKCR 来源补 `SOFTWARE\Classes\` 前缀，杜绝写到 `HKLM\*\shell\...` |
| P1-4 | CommandStore 共享项可被误删 | `ContextMenuItem.IsSharedDefinition` 标记，详情面板显示"共享命令库"徽章，删除前强制二次确认 |
| P1-5 | `DelegateExecute` 未识别 | 模型与解析器新增 `DelegateExecute` / `AppliesTo` / `HasLUAShield`；`CanEditCommand` 在 COM 代理与级联父项上为 false，编辑面板禁用命令框并给出说明 |
| P1-6 | reg.exe 依赖且无超时 | 抽出 `IRegistrySnapshotTool`，默认实现 `RegExeSnapshotTool` 带 20 秒超时与进程树终止；`BackupService` 可注入替身，备份逻辑从此可在任意环境单测 |
| P1-7 | 错误通道错位 | 新增 `ErrorBanner` + 主窗口顶部错误条（可关闭），新增/删除/恢复/预设失败统一走 `ReportError`，不再只写进（隐藏的）编辑面板 |
| P2 | 日志无轮转 | `OperationLogService` 5 MB 上限 + 保留 2 代；跨进程写冲突降级为静默，不再让成功的注册表写入报错 |
| P2 | MUI 解析无缓存 | `MuiStringHelper` 增加进程内缓存，重复的 `MUIVerb` 不再每次进 COM |
| P2 | 冗余依赖 | 移除 `Microsoft.Win32.Registry` 与 `System.Drawing.Common`；`CommunityToolkit.Mvvm` → 8.4.2，`Microsoft.Extensions.DependencyInjection` → 10.0.0 |

### 未做（保留在路线图中）

- 图标按 DPI 提取（`SHDefExtractIconW` / `IShellItemImageFactory`）与异步枚举
- 主题 token 化 + 深色模式（`ThemeMode`）
- 单实例互斥（需与"提权重启"流程一起设计，避免新实例被旧实例挡住）
- `IExplorerCommand` 新菜单支持
- 代码签名、i18n（resx）

---

## 附录 B：预览页还原 Explorer 菜单（0.7.0，2026-11-09）

### 问题

预览页右键只把注册表解析出的 verb 平铺出来，与资源管理器真实菜单相差很远：没有「打开 / 查看 / 排序方式 / 刷新 / 粘贴 / 新建 / 属性」，没有分组分隔，也看不出自定义项实际落在哪个位置。

### 改法

| 新增 | 职责 |
|---|---|
| `Models/SystemMenuEntry.cs` | 系统内置项的数据形状（文本 / 是否默认 / 是否勾选 / 子项 / 是否可用） |
| `Services/SystemMenuCatalog.cs` | 按 scope 生成 Explorer 自带的两段式布局（Before / After），并按 `Position` 留出插入点 |
| `ViewModels/MenuPreviewComposer.cs` | 把系统项与注册表 verb、Shell 扩展按真实顺序合并，清理多余分隔 |
| `Views/Controls/PreviewMenuFactory.cs` | 节点 → WPF 菜单项，唯一渲染出口（预览与测试共用） |

各 scope 的菜单骨架：

| Scope | 菜单开头 | 结尾 |
|---|---|---|
| 文件 / 扩展名 | 打开(粗体)、打开方式▸、──、发送到▸、──、剪切、复制、创建快捷方式、删除、重命名 | 属性 |
| 文件夹 | 查看▸、排序方式▸、刷新、──、粘贴、粘贴快捷方式、──、授予访问权限▸、──、新建▸ | 属性 |
| 文件夹背景 | 查看▸、排序方式▸、刷新、──、粘贴、粘贴快捷方式、──、新建▸ | 属性 |
| 桌面 | 查看▸、排序方式▸、刷新、──、粘贴、粘贴快捷方式、──、新建▸ | 显示设置、个性化 |
| 驱动器 | 打开(粗体)、在终端中打开、──、固定到“快速访问”、──、格式化… | 属性 |

自定义 verb 的插入位置：`Position=Top` → 菜单最顶部；未指定 → 「新建」之后、「属性」之前；`Position=Bottom` → 紧邻「属性」之前；`shellex\ContextMenuHandlers` → 与第三方项同区。

保真细节：

- 「打开」半粗体（Explorer 的默认动词样式）
- 查看 8 个视图模式、排序方式 4 个键 + 递增/递减，勾选画在图标列（不用 WPF 的 CheckBox 主题，避免蓝框）
- 分隔线左缩进 26px，行高 22px
- 「粘贴 / 粘贴快捷方式」随剪贴板是否有文件而灰显
- `Extended` 项默认不出现，顶部「显示 Shift 项」开关可展开，并带 Shift 徽标
- 「新建」「发送到」取自本机真实数据（`ShellNew` 注册项 / `%APPDATA%\Microsoft\Windows\SendTo`），失败时回落到 Windows 默认模板

### 验证

- `dotnet test`：**105 通过 / 0 失败**，其中 25 条为本次新增（20 条结构断言 + 4 条离屏渲染 + 1 条合并规则）
- 离屏渲染（`RenderTargetBitmap`，无需桌面会话）逐张核对：`docs/review/preview/folder-menu.png`、`file-menu.png`、`sortby-submenu.png`、`view-submenu.png`
- 应用启动冒烟：无 XAML 解析错误、无新崩溃日志

### 仍未覆盖（刻意保留）

- 多选对象（多个文件 / 文件+文件夹混选）的菜单差异、各文件类型专属项
- 「打开方式」的程序清单按 `OpenWithProgids` 名称推断，不是完整 MRU
- Win11 新式菜单（`IExplorerCommand`）、图标按 DPI 提取、暗色主题

---

## 附录 C：预览改为「由 Windows 自己出菜单」（0.7.1，2026-11-09）

### 为什么推翻附录 B 的做法

附录 B 是手写目录（catalog）模拟系统项，只能覆盖「打开 / 查看 / 排序方式 / 新建 / 属性」这类固定的壳内建项。真实菜单里还有大量机器相关的项——`使用小米互传发送`、`使用 ToDesk 快传文件`、`通过 Code 打开`、`UltraISO`、`包含到库中`……这些由第三方 shell 扩展在 `QueryContextMenu` 时动态插入，无论如何枚举注册表都拼不出来。对照真实菜单截图即可看到：旧实现少了十来项，还把 `open` / `runas` 当原始 verb 名显示，第三方处理器显示成 `NvAppShExt Class` 这类 CLSID 类名。

### 新做法

| 文件 | 职责 |
|---|---|
| `Services/ShellContextMenuReader.cs` | 用 `SHCreateItemFromParsingName` + `IShellItem::BindToHandler(BHID_SFUIObject)` 拿到 `IContextMenu`，`QueryContextMenu` 后遍历 `HMENU`，逐项读出文本、分隔符、置灰/勾选/默认态与 `HBITMAP` 图标 |
| `Services/ShellMenuNode.cs` | 上面那次遍历的中性结果 |
| `Models/...`→`ViewModels/MenuPreviewComposer.ComposeNative` | 把 shell 的行映射成预览行；能对上本工具 verb 的行仍可点击选中（按去掉 `&` 与 `(O)` 后缀的文本匹配） |
| `Helpers/StandardVerbLabels.cs` | `open/runas/edit/...` 的中文名（注册表里根本没存，只有 Shell 知道） |

要点：

- **作用域 → 具体对象**：shell 要一个真实对象才能出菜单，所以每个作用域用一份固定占位对象（`%LOCALAPPDATA%\RCMenuManager\preview\sample.exe`、`sample-folder`、系统盘根）。
- **STA + 超时**：`QueryContextMenu` 必须在 STA 线程跑，且第三方处理器可能卡住，因此放到独立 STA 线程并限时 10 秒，超时/异常一律回落到附录 B 的 catalog。
- **缓存**：按 `scope + extended` 缓存，`LoadAsync` 时失效并在后台预热，右键时直接命中。
- **Shift 项**：`CMF_EXTENDEDVERBS` 让 shell 自己吐出 Extended 项，「显示 Shift 项」开关直接对应这个标志。
- **图标**：菜单项的 `hbmpItem` 转成冻结的 `BitmapSource`（菜单销毁前完成，不做 `DeleteObject`）。部分程序用自绘回调（`HBMMENU_CALLBACK`）给图标，读不到时回落到该 verb 自己的 `Icon`。

### 与真实菜单的逐项核对

以本机 `.exe` 为例，dump 出的 shell 菜单与资源管理器实际右键完全一致：

```
打开(&O) / 以管理员身份运行(&A) / 在记事本中编辑 / 使用小米互传发送 /
添加到收藏夹(&F) / 发送到 Sunshine 客户端 / 使用 ToDesk 快传文件 /
通过 Code 打开 / 兼容性疑难解答(&Y) / 固定到"开始"(&P) / 通过网易邮箱大师发送 /
授予访问权限(&G) / 复制文件地址(&A) / 共享(&S) / 还原以前的版本(&V)
--- 发送到(&N) ▸ --- 剪切(&T) / 复制(&C) ---
创建快捷方式(&S) / 删除(&D) / 重命名(&M) --- 属性(&R)
```

对照图：`docs/review/preview/exe-scope-live.png`（应用内）、`folder-scope-live.png`、`desktop-scope-live.png`。

### 仍未覆盖

- **桌面 / 文件夹背景**：这两类菜单由 shell 视图（`IShellView::GetItemObject(SVGIO_BACKGROUND)`）产生，`IShellItem` 路径拿不到，仍走 catalog（顺序与分组按真实菜单校准，但第三方背景项只能靠注册表拼接）。
- **菜单行内自绘图标**（`HBMMENU_CALLBACK`）读不到位图。
- 占位对象决定了个别上下文相关项：例如文件放在用户目录会多出「授予访问权限」，「固定到任务栏」则取决于对象是否可固定。

---

## 附录 D：其他文件类型的验证（0.7.2，2026-11-09）

### 顺带修掉的一个真问题

验证 `.txt` 时发现：预览里能看到「打开 / 编辑」，但左侧列表只有 1 项——这两个 verb 存在 `HKCR\SystemFileAssociations\text\shell`，既不在 `txtfile\shell` 也不在 `.txt\shell` 里。Explorer 的合并顺序是：

```
.ext\shell → SystemFileAssociations\.ext\shell → ProgID\shell
  → SystemFileAssociations\<ProgID>\shell → SystemFileAssociations\<PerceivedType>\shell
  → *\shell → AllFilesystemObjects\shell
```

`RegistryService` 只走到第 4 步，于是「看得见但改不了」。现已补齐后三步（`PerceivedType` 从扩展名键读，缺失时回退 ProgID），并新增 `RegistryServiceTests` 锁住这个顺序。效果：`.txt` 的可编辑项从 1 → 13，`.png` → 14，`.zip` 的「打开」也能对回 `Open` verb。

### 逐类型核对

用同一套 `IContextMenu` 读取链路，对本机各类型分别取菜单（占位对象：`%LOCALAPPDATA%\RCMenuManager\preview\sample<ext>`）：

| 类型 | 菜单项数 | 首项 | 可映射回可编辑项（示例） | 备注 |
|---|---|---|---|---|
| `.exe` | 24 | 打开(O) | 以管理员身份运行、固定到"开始" | 无「打开方式」（Explorer 同） |
| `.txt` | 27 | 打开(O) | 打开、编辑、转换为PDF格式、添加到收藏夹 | 打开/编辑来自 `SystemFileAssociations\text` |
| `.png` / `.jpg` | 38 | 打开(O) | 设置为桌面背景、打印、添加到收藏夹 | 含「向右/向左旋转」「播放到设备」 |
| `.pdf` | 30 | 打开(O) | 编辑、打印、邮箱大师 PDF 编辑 | 「打开方式」在中间位置（Explorer 同） |
| `.zip` | 27 | 打开(O) | 打开(Open)、全部解压缩…、转换为PDF格式 | 「全部解压缩」为 shell 扩展项 |
| `.html` | 25 | 打开(O) | 添加到收藏夹、通过 Code 打开 | — |
| `.docx` | 30 | 打开(O) | 编辑、新建、打印 | 「打开方式(H)…」带省略号 |
| `.ps1` | 27 | 打开(O) | 编辑、使用 PowerShell 运行 | 后者是 `Windows.PowerShell.Run` verb |
| `.xyz`（无关联） | 24 | 打开方式(H) | 仅 shell 通用项 | 无关联时菜单只剩「打开方式」 |
| 驱动器 | 17 | 打开(O) | 固定到快速访问、启用 BitLocker、格式化 | 有「重命名」「复制」无「剪切」 |
| 文件与文件夹 | 25 | 打开(O) | 添加到收藏夹、发送到 Sunshine | 占位对象是文件，故有「打开方式」 |

所有类型都以「属性(R)」收尾，分组位置与真实右键一致。

### 性能

冷读要加载第三方 shell 扩展，`.png` 最慢（本机约 2.5s），`.txt` 约 1.2s，其余 0.2–0.4s；命中缓存后 < 1ms。因此预热时机从「解析完成之后」提前到 `LoadAsync` 开头，与注册表解析并行，右键时通常已预热完。

测试：**125 通过 / 0 失败**（新增 11 条：8 个类型的菜单读取断言、缓存命中、查询路径顺序、感知类型解析）。
