# Phase 4: 右键菜单预览 Implementation Plan

> REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在主窗口左侧新增"预览"Tab，用 WPF Menu 渲染当前作用域下的右键菜单项，结构与 Windows 系统右键菜单接近；点击预览项 = 选中（与 TreeView 行为一致）。

**Architecture:** 左侧加 `TabControl` 容纳两个 Tab（列表 / 预览）。预览用 WPF `Menu` + `HierarchicalDataTemplate` 递归渲染 `MenuItemViewModel` 树。MainViewModel 新增 `ShowExtended` 开关和 `PreviewView` 视图对象，绑定时通过 `CollectionViewSource.Filter` 隐藏 Extended 项。点击通过 `RelayCommand` 同步到 `SelectedItem`。

**Tech Stack:** WPF / .NET 9 / CommunityToolkit.Mvvm

**Spec:** `docs/superpowers/specs/2026-06-16-phase4-preview-design.md`

---

## Task 1: MainViewModel 扩展（ShowExtended + 过滤视图 + 选择命令）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 添加 using 与字段**

在 `ViewModels/MainViewModel.cs` 顶部 `using` 区域加一行：

```csharp
using System.ComponentModel;
using System.Windows.Data;
```

在类的字段区域（`private readonly ...` 之后），`public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();` 这一行之后加一个只读视图属性：

```csharp
    /// <summary>Filtered view used by the Preview tab to hide Extended items by default.</summary>
    public ICollectionView PreviewView { get; }
```

- [ ] **Step 2: 构造函数里初始化 PreviewView**

在构造函数 `public MainViewModel(...)` 内、`Scopes.Add(...)` 之前加入：

```csharp
        PreviewView = CollectionViewSource.GetDefaultView(MenuItems);
        PreviewView.Filter = obj => obj is MenuItemViewModel m && (_showExtended || !m.IsExtended);
```

- [ ] **Step 3: 添加 ShowExtended 属性 + 变化通知**

在 `public bool HasSelectedItem => ...` 后面、`partial void OnSelectedItemChanged(...)` 之前，加：

```csharp
    [ObservableProperty]
    private bool _showExtended;
```

紧接着加：

```csharp
    partial void OnShowExtendedChanged(bool value) => PreviewView.Refresh();
```

- [ ] **Step 4: 添加 SelectPreviewItemCommand**

在 `RefreshAsync` 方法之前添加：

```csharp
    [RelayCommand]
    private void SelectPreviewItem(MenuItemViewModel? item)
    {
        if (item is not null) SelectedItem = item;
    }
```

- [ ] **Step 5: 验证构建**

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" build RCMenuManager.sln --nologo -v:m
```

- [ ] **Step 6: 跑现有测试**

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" test RCMenuManager.sln --nologo -v:m
```

预期：26 个测试全过。

- [ ] **Step 7: 提交**

```powershell
git add ViewModels/MainViewModel.cs
git commit -m "feat: add ShowExtended, PreviewView, and SelectPreviewItemCommand"
```

---

## Task 2: ContextMenuPreview UserControl

**Files:**
- Create: `Views/Controls/ContextMenuPreview.xaml`
- Create: `Views/Controls/ContextMenuPreview.xaml.cs`
- Create: `Converters/CollectionHasItemsToVisibilityConverter.cs`

- [ ] **Step 1: 写 XAML**

创建 `Views/Controls/ContextMenuPreview.xaml`：

```xml
<UserControl x:Class="RCMenuManager.Views.Controls.ContextMenuPreview"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:RCMenuManager.ViewModels"
             xmlns:conv="clr-namespace:RCMenuManager.Converters">
    <UserControl.Resources>
        <conv:BoolToVisibilityConverter x:Key="BoolToVis" />
        <conv:StringNotEmptyToVisibilityConverter x:Key="StringToVis" />
        <conv:CollectionHasItemsToVisibilityConverter x:Key="ListHasItemsToVis" />
        <conv:CollectionHasItemsToVisibilityConverter x:Key="ListEmptyToVis" TrueValue="Visible" FalseValue="Collapsed" />

        <HierarchicalDataTemplate DataType="{x:Type vm:MenuItemViewModel}" ItemsSource="{Binding Children}">
            <StackPanel Orientation="Horizontal" Margin="2,0">
                <Image Source="{Binding Icon}" Width="16" Height="16" Margin="0,0,8,0"
                       VerticalAlignment="Center" />
                <TextBlock Text="{Binding DisplayName}" VerticalAlignment="Center" />
                <TextBlock Text="Shift" Margin="6,0,0,0" Foreground="#9A1F1B" FontSize="11"
                           VerticalAlignment="Center"
                           Visibility="{Binding IsExtended, Converter={StaticResource BoolToVis}}" />
            </StackPanel>
        </HierarchicalDataTemplate>
    </UserControl.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" BorderBrush="#E1E3E8" BorderThickness="0,0,0,1" Padding="12,8">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <CheckBox Content="显示 Shift+右键 项 (Extended)" IsChecked="{Binding ShowExtended}" />
                <TextBlock Margin="14,0,0,0" Foreground="#5F6B7A" FontSize="12" VerticalAlignment="Center">
                    <Run Text="目标：" />
                    <Run Text="{Binding SelectedScope.Label, Mode=OneWay}" />
                </TextBlock>
            </StackPanel>
        </Border>

        <Grid Grid.Row="1">
            <TextBlock Text="该作用域无菜单项"
                       Foreground="#9AA3B0" FontSize="12" Margin="20"
                       Visibility="{Binding PreviewView, Converter={StaticResource ListEmptyToVis}}" />
            <Menu IsMainMenu="False" Background="Transparent" Padding="8,12"
                  Visibility="{Binding PreviewView, Converter={StaticResource ListHasItemsToVis}}">
                <MenuItem ItemsSource="{Binding PreviewView}">
                    <MenuItem.ItemContainerStyle>
                        <Style TargetType="MenuItem">
                            <Setter Property="Command" Value="{Binding DataContext.SelectPreviewItemCommand, RelativeSource={RelativeSource AncestorType=UserControl}}" />
                            <Setter Property="CommandParameter" Value="{Binding}" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding IsHidden}" Value="True">
                                    <Setter Property="Opacity" Value="0.45" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </MenuItem.ItemContainerStyle>
                </MenuItem>
            </Menu>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: 写 code-behind**

创建 `Views/Controls/ContextMenuPreview.xaml.cs`：

```csharp
using System.Windows.Controls;

namespace RCMenuManager.Views.Controls;

public partial class ContextMenuPreview : UserControl
{
    public ContextMenuPreview()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: 创建 CollectionHasItemsToVisibilityConverter**

新建 `Converters/CollectionHasItemsToVisibilityConverter.cs`：

```csharp
using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

public sealed class CollectionHasItemsToVisibilityConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasItems = value switch
        {
            ICollectionView cv => cv is not null && !cv.IsEmpty,
            IEnumerable e => HasAny(e),
            _ => false,
        };
        return hasItems ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool HasAny(IEnumerable e)
    {
        foreach (var _ in e) return true;
        return false;
    }
}
```

- [ ] **Step 4: 验证构建 + 跑测试**

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" build RCMenuManager.sln --nologo -v:m
& "C:\Users\chen7\.dotnet\dotnet.exe" test RCMenuManager.sln --nologo -v:m
```

预期：构建通过，26 个测试仍全过。

- [ ] **Step 5: 提交**

```powershell
git add Views/Controls/ContextMenuPreview.xaml Views/Controls/ContextMenuPreview.xaml.cs Converters/CollectionHasItemsToVisibilityConverter.cs
git commit -m "feat: add ContextMenuPreview user control"
```

---

## Task 3: MainWindow.xaml 加 TabControl 容器

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: 替换左栏 Card 内部**

把 `MainWindow.xaml` 里：

```xml
            <Border Grid.Column="0" Style="{StaticResource Card}">
                <c:MenuTreeView />
            </Border>
```

替换为：

```xml
            <Border Grid.Column="0" Style="{StaticResource Card}" Padding="0">
                <TabControl Background="Transparent" BorderThickness="0" Padding="0">
                    <TabItem Header="列表">
                        <c:MenuTreeView />
                    </TabItem>
                    <TabItem Header="预览">
                        <c:ContextMenuPreview />
                    </TabItem>
                </TabControl>
            </Border>
```

- [ ] **Step 2: 验证构建 + 跑测试**

```powershell
& "C:\Users\chen7\.dotnet\dotnet.exe" build RCMenuManager.sln --nologo -v:m
& "C:\Users\chen7\.dotnet\dotnet.exe" test RCMenuManager.sln --nologo -v:m
```

- [ ] **Step 3: 提交**

```powershell
git add MainWindow.xaml
git commit -m "feat: split left pane into list/preview tab control"
```

---

## Task 4: Phase 4 手动 smoke 清单

**Files:**
- Create: `docs/superpowers/smoke/2026-06-16-phase4-smoke.md`

- [ ] **Step 1: 写 smoke 清单**

创建 `docs/superpowers/smoke/2026-06-16-phase4-smoke.md`：

```markdown
# Phase 4 手动 Smoke 测试

本阶段新增"预览"Tab。下列步骤不写注册表，仅验证渲染与交互。

## 1. 基础渲染
1. 启动 RCMenuManager，作用域选"文件夹 (HKCR\\Directory\\shell)"。
2. 点击左侧"预览"Tab。
3. 期望：看到接近 Windows 系统右键菜单样式的列表（含图标、文本），一级菜单项按注册表顺序排列。

## 2. 二级菜单展开
1. 在预览里 hover 任意有 `Children` 的项。
2. 期望：右侧自动展开二级菜单，hover 行为与系统右键菜单一致。

## 3. Extended 过滤
1. 找一项 `IsExtended=true` 的 verb。
2. 默认预览应**不**显示该项。
3. 勾选顶部"显示 Shift+右键 项 (Extended)"，该项应**出现**，且标签右侧有"Shift"红字提示。
4. 取消勾选，该项应**再次隐藏**。

## 4. 点击预览项 = 选中
1. 在预览中点击任意菜单项。
2. 期望：右侧详情面板（DetailsPanel）立即显示该 verb 的信息，编辑/删除/启用按钮可用。
3. 注意：点击不切回"列表" Tab。
4. 切到"列表" Tab，TreeView 里同一项应处于高亮/展开状态。

## 5. 切换作用域刷新
1. 在作用域下拉框切换到"桌面 (HKCR\\DesktopBackground\\Shell)"。
2. 期望：预览 Tab 里的内容立即换成桌面作用域下的菜单项。
3. Extended 开关状态应**保持**。

## 6. 空作用域占位
1. 选一个当前账号没有 verb 的作用域。
2. 期望：预览区显示灰色文字"该作用域无菜单项"，无空菜单渲染。

## 7. 隐藏项灰化
1. 在预览里找 `IsProgrammaticOnly` 或 `IsLegacyDisabled` 的项。
2. 期望：该项以 0.45 透明度灰化显示（仍可点击预览并查看详情）。
```

- [ ] **Step 2: 提交**

```powershell
git add docs/superpowers/smoke/2026-06-16-phase4-smoke.md
git commit -m "docs: phase 4 manual smoke checklist"
```

---

## 自审

- Spec §4.1 -> Task 2; §4.2 -> Task 1; §4.3 -> Task 3
- Spec §6 UI 行为 -> Task 4 smoke
- Task 1 的 `ShowExtended` / `PreviewView` / `SelectPreviewItemCommand` 在 Task 2/3 全部按此名引用
