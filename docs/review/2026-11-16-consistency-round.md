# 四项一致性修复 + 整体审查（待发布）

- **时间**：2026-11-16
- **验证**：Debug / Release 均 0 警告 0 错误；测试 **173 → 196 全通过**；另用临时探针在本机真实注册表上跑通全部结论（探针已删除）

## 1. 桌面作用域名称与内容不一致

**实测证据**（本机 zh-CN Win11）：`HKLM\SOFTWARE\Classes\DesktopBackground\Shell` 下 5 项，其中

| 键 | 默认值 | MUIVerb | 注册表里是否有可读名称 |
|---|---|---|---|
| `.SpotlightLearnMore` | 空 | 空 | 无，名字只在 CLSID `{5C6B744C…}` 的注册名里 |
| `.SpotlightNextImage` | 空 | 空 | 同上（`{2ECAB2B4…}`） |
| `EditStickers` | 空 | 空 | 同上（`{3C0E1E28…}`） |
| `Display` / `Personalize` | `@display.dll,-4` | — | 有（MUI 可解析） |

旧逻辑走到"没有名字就用键名"，于是列表里出现 `.SpotlightLearnMore`。

**修复**
- 解析 `ExplorerCommandHandler` 并读其 CLSID 注册名（`MenuParserService.ResolveHandlerLabel`）
- 对注册名仍是技术串的，用 `StandardVerbLabels.HandlerLabels` 人工对照表给出人话名称
- MUI 解析失败时不再把 `@"windows.storage.dll,-8519"` 这类原始引用当标题（`opennewtab` / `opennewwindow` 之前就是这样显示的）
- 新增 `HasNoStoredLabel` 标记，界面显式标「名称推断」，不再把推断名冒充官方名

**其他作用域排查结果**：文件夹 / 驱动器 / 所有文件 / 文件与文件夹均无同类问题；`Open WezTerm here` 这类"键名即显示名"的项保留原名并标「推断」，属正常。

## 2. 预览里「共享」「在记事本中打开」点不动

**根因**：这两项来自 `shellex\ContextMenuHandlers`，没有注册表 verb；`ComposeNative` 只按显示文本匹配 verb，匹配不到就 `Item=null`，行画出来但点不动。

**修复**
- `ComposeNative` 接受 shell 扩展列表，按 CLSID → 本地化名做别名匹配（共享/Share、打开方式/在记事本中打开/Copy as path…）
- `PreviewNode.Extension` + `CanSelect`：可选中 = 有 verb 或有已注册扩展
- 新增 `SelectPreviewNodeCommand`，扩展行选中后详情面板显示 CLSID / DLL / 注册名，并说明"不能在此编辑"
- 真正属于 Explorer 内部的行（刷新/查看/属性）仍然不可选——它们确实没有注册信息，假装可选才是错的

## 3. 菜单项来源识别

新增 `MenuItemOriginService`，按证据强弱依次判定，六类来源：
**本工具 / 命令库 / 系统处理器 / 应用处理器 / COM 处理器 / 系统内置 / 应用注册**。
界面上：列表行直接显示来源徽章，详情面板给出完整解释与证据（CLSID、DLL、命令）。

判定只认证据，不猜：操作日志里的**创建**记录（`CreateRoot/CreateChild/CreateParent`）或"该作用域的预设 + 键在 HKCU"。编辑前的备份**不算**创建证据。

## 4. 推荐设置自动勾选已存在项

- `IsApplied` 改为复用列表的查询路径（`RegistryService.FindVerbInScope`），不再自己拼 HKCU 路径
- 加载时已存在的项 → 自动勾选 + 状态"已应用"，摘要显示"共 N 项 · 已存在 M 项（已自动勾选）"
- `Apply` 写入前先判定：作用域里已通过其他路径（AllFilesystemObjects / SystemFileAssociations / HKLM）提供该项时，报"已存在"而不是再写一条 HKCU 副本（否则菜单出现两行同名）

**实测**：31 条内置推荐与真实菜单状态**不一致 0 条**；默认勾选 1 条（vscode，本机确有）。

## 5. 整体审查（两位并行审阅 + 本人复核），本轮已修

| 严重度 | 问题 | 修复 |
|---|---|---|
| P0 | 嵌套 @BT\shell\` 子项 RegistryPath 少一层父键 → 保存写垃圾键、删除假成功 | 改用键自身完整路径 `verbKey.Name` |
| P0 | `ExtendedSubCommandsKey` 子项路径二选一必错 | 同上，路径不再由探测入口拼 |
| P0 | `ScopeId` 不含 ProgId → 提权重启后扩展名作用域少一半项 | `FromScopeId(id, progId)` + `RehydrateScope` |
| P0 | 合并顺序按子键交错，HKLM 抢先于 HKCU → 改完菜单没变化、还要求提权 | 全部 HKCU 路径先于 HKLM |
| P1 | 改显示名对 MUIVerb 项无效；只改图标会把推断名写进注册表 | 写 MUIVerb；推断名未改动则不落盘 |
| P1 | 来源徽章按 verb 名匹配、不校验 hive | 按 hive+subKey 精确匹配 |
| P1 | 选中双真相导致两行同时高亮、旧行点不动 | 显式复位上一项 `IsSelected` |
| P1 | 推荐面板数字不刷新；应用后主列表不刷新 | 重算并有条件刷新（`ShowPresetsAsync`） |
| P1 | 简化预览把"菜单中隐藏"的项画成可用项 | 置灰 + 「隐藏」徽章 |
| P2 | 隐藏项在列表里与正常项无区别 | 「菜单中隐藏」徽章 + 详情黄条 + 频率型可见性提示 |

## 仍未做（下一轮候选）

1. 预览按显示名匹配身份，同名项仍可能选错（需改为按 verb+路径建身份）
2. 恢复备份不写操作日志、恢复前不做快照
3. 刷新/切作用域会静默丢弃编辑草稿
4. 无单实例；崩溃只写日志不提示
5. 暗色模式；图标按 DPI；installer 仍强制管理员安装
