# Phase 4: 右键菜单预览 设计文档

## 1. 目标

在主窗口左侧提供一个"预览"视图，把当前作用域下的所有右键菜单项渲染成接近 Windows 真实右键菜单的样式。点选预览项 = 选中该项进入编辑流程（行为与 TreeView 一致），不改注册表、不真执行命令。

## 2. 范围

**In scope：**
- 新增 `ContextMenuPreview` UserControl，用 WPF 原生 `Menu` 控件渲染 `MenuItemViewModel` 树。
- 主窗口左侧顶部加 `TabControl`，两个 Tab：列表（即现有 `MenuTreeView`）/ 预览。
- 预览视图支持"显示 Shift+右键 项"开关，默认隐藏 Extended 项。
- 点击预览项 = 同步 `MainViewModel.SelectedItem`，详情面板自动联动。
- 一级、二级（SubCommands / 嵌套 shell）菜单递归渲染。

**Out of scope：**
- Win32 原生弹窗（不调 TrackPopupMenu，留给后续可选增强）。
- 实际执行菜单项命令（仅选中，不调用 `Command`）。
- 拖入文件自动切换作用域（属于 Phase 8）。
- 预设/推荐设置（属于 Phase 7）。

## 3. 架构

```
MainWindow.xaml
  └─ 左栏 (Border Card)
       └─ TabControl (新增)
            ├─ TabItem "列表" → MenuTreeView  (现有，未动)
            └─ TabItem "预览" → ContextMenuPreview  (新增)

ContextMenuPreview.xaml/cs
  ├─ 顶部工具条：ShowExtended CheckBox + "目标" 提示文本
  └─ Body: Menu 控件 (用 MenuItem 容器 + HierarchicalDataTemplate 递归)
       └─ ItemsSource ← CollectionViewSource(Filter=ShowExtended) ← MainViewModel.MenuItems

MainViewModel (扩展)
  └─ + ShowExtended 属性
  └─ + PreviewItems 视图（CollectionViewSource，在 XAML 声明）

MenuItemViewModel (无改动)
  └─ Children、DisplayName、Icon 等字段已够用
```

## 4. 组件

### 4.1 `Views/Controls/ContextMenuPreview.xaml` + code-behind

- 顶部一条工具栏：左侧 `CheckBox`"显示 Shift+右键 项"绑 `MainViewModel.ShowExtended`；右侧 `TextBlock` 显示"目标：{当前作用域 DisplayName}"。
- Body 区域：`Menu` 控件（`IsMainMenu="False"`，避免被当成窗口主菜单抢 Alt 键）。
  - 根节点放一个不可见的 `MenuItem` 作为容器，`ItemsSource` 绑 `CollectionViewSource`，`CollectionViewSource` 绑 `MainViewModel.MenuItems`。
  - `HierarchicalDataTemplate DataType="{x:Type vm:MenuItemViewModel}"`：`ItemsSource="{Binding Children}"`，Header 是 `StackPanel`（16x16 Icon + DisplayName）。
  - `MenuItem.ItemContainerStyle`：绑 `Command` = `RelayCommand<MenuItemViewModel>`（MainViewModel 新增 `SelectPreviewItemCommand`），设置 `Click` 事件也行但 command 更 MVVM。
- Code-behind：仅 `InitializeComponent`。

### 4.2 `MainViewModel` 扩展

新增：
- `[ObservableProperty] private bool _showExtended;`
- `partial void OnShowExtendedChanged(bool value)`：调用 `PreviewView?.Refresh()`（`ICollectionView` 引用），让过滤实时生效。
- `[RelayCommand] private void SelectPreviewItem(MenuItemViewModel? item)`：若 item 非空，设 `SelectedItem = item`。
- `ICollectionView? PreviewView { get; }`：在构造函数里通过 `CollectionViewSource.GetDefaultView(MenuItems)` 初始化，并注册 `Filter` 回调（`item => ShowExtended || !((MenuItemViewModel)item).IsExtended`）。

> 注意：默认 CollectionView 的 `Filter` 不能直接在 XAML 里写，所以用代码注册。`ShowExtended` 变化时 `view.Refresh()`。

### 4.3 `MainWindow.xaml` 改造

把现有：
```xml
<Border Grid.Column="0" Style="{StaticResource Card}">
    <c:MenuTreeView />
</Border>
```
改成：
```xml
<Border Grid.Column="0" Style="{StaticResource Card}">
    <TabControl Background="Transparent" BorderThickness="0">
        <TabItem Header="列表"><c:MenuTreeView /></TabItem>
        <TabItem Header="预览"><c:ContextMenuPreview /></TabItem>
    </TabControl>
</Border>
```
`Card` 样式 + TabControl 内嵌要协调内边距，可去掉 TabControl 头部的内凹边线。

## 5. 数据流

1. 用户切换作用域 -> `MainViewModel.LoadAsync` 重新填充 `MenuItems` -> `CollectionViewSource` 收到 `MenuItems` 变化通知，预览自动刷新。
2. 用户勾选/取消"显示 Shift+右键 项" -> `MainViewModel.ShowExtended` 变化 -> `OnShowExtendedChanged` 调 `PreviewView.Refresh()` -> 视图重新过滤。
3. 用户点击预览中的某项 -> 触发 `MenuItem.Command` = `SelectPreviewItemCommand` -> `MainViewModel.SelectedItem = item` -> 详情面板 `DetailsPanel` 通过绑定自动更新（已有 `EditPanel.CancelEdit` 联动）。
4. 用户在 TreeView 里点 -> `MainViewModel.SelectedItem` 变化 -> 详情面板更新 -> 预览视图里的对应 `MenuItem.IsHighlighted` 由 WPF 选择状态自动维持（虽然切到预览 Tab 看不见，但保持一致）。

## 6. UI 行为

| 场景 | 行为 |
|---|---|
| 进入预览 Tab | 立即显示当前 `MenuItems` 渲染的菜单树 |
| Hover 二级菜单 | 标准的 WPF `MenuItem` 行为：自动展开子项（300ms 延迟沿用系统默认） |
| 一级菜单项无 `Children` | 渲染成普通项，无右侧箭头 |
| 隐藏项（ProgrammaticAccessOnly / LegacyDisable） | 仍显示但视觉上灰化（透明度 0.5），与系统行为一致 |
| Extended 项 | 默认隐藏；勾选"显示 Shift+右键 项"才出现，并在标签右侧补一个 `Shift` 灰字提示 |
| 点击预览项 | 选中并切到详情面板编辑流程（不切回列表 Tab，用户可继续浏览预览） |
| 选中项不存在（被删/筛选掉） | 不报错；预览里所有项都不高亮 |
| 作用域为空 | 预览区显示灰色提示"该作用域无菜单项" |

## 7. 错误处理

| 异常 | 处理 |
|---|---|
| `MenuItemViewModel.Icon` 解码失败 | `IconService` 已 swallow 异常返回 `null`，UI 自然无图，不需额外处理 |
| `CollectionViewSource.Filter` 抛 | 过滤逻辑只读 bool 属性 + 简单 null 检查，不会抛 |
| 用户连续切换作用域 | 旧 `MenuItems` 被 `Clear()`，CollectionView 自动同步；`ShowExtended` 状态保持 |
| 预览 Tab 切回列表 Tab 再切回 | 状态完全保留（CollectionView 一直存在） |

## 8. 测试

不需要新单元测试（纯 View 渲染层 + ViewModel 单一 bool 属性 + RelayCommand）。沿用 `dotnet test` 26 个 case 确保未回归即可。

手动 smoke（追加到 Phase 3 清单或新建 Phase 4 清单）：
1. 启动应用，作用域选"文件夹"，切到"预览"Tab -> 应看到真实菜单样式的渲染。
2. Hover 任意有子菜单的项 -> 子菜单弹出。
3. 勾选"显示 Shift+右键 项" -> 之前隐藏的 Extended 项出现。
4. 点击某个菜单项 -> 详情面板同步显示该 verb 信息，"编辑"按钮可点。
5. 切换作用域 -> 预览内容刷新。
6. 切回"列表"Tab -> 行为与之前一致。

## 9. 风险

| 风险 | 缓解 |
|---|---|
| WPF `Menu` 在非主菜单场景下样式与系统不一致 | 用 `ItemContainerStyle` 覆盖 `MenuItem` 的背景/边框/Icon 尺寸；不做 100% 像素贴合，目标是"接近系统" |
| TabControl 嵌套 `Card` 样式导致双重内边距 | 给 TabControl `Padding="0"`，`TabItem` 用轻量样式 |
| `CollectionViewSource.Filter` 走代码而非 XAML | 在 `MainViewModel` 构造函数里初始化 `PreviewView` 即可，对调用方零侵入 |
| 预览点击命令与 TreeView 选中冲突 | 详情面板只读 `MainViewModel.SelectedItem`，两端写入同一属性，框架自动处理 |