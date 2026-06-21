using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RCMenuManager.ViewModels;

namespace RCMenuManager.Views.Controls;

public partial class ContextMenuPreview : UserControl
{
    public ContextMenuPreview()
    {
        InitializeComponent();
    }

    // Builds a system-style ContextMenu from MenuItems on demand. We use the
    // unfiltered list so the preview matches what the user sees in the
    // adjacent TreeView, including Extended / system verbs that aren't
    // editable - those just light up the read-only fields in the right panel.
    private void OnPreviewSurfaceRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var items = vm.MenuItems.ToList();
        var hasExt = vm.ShellExtensions.Count > 0;
        if (items.Count == 0 && !hasExt) return;

        var menu = BuildContextMenu(items, vm);
        if (hasExt)
        {
            if (items.Count > 0)
                menu.Items.Add(new Separator());
            var extMenu = new MenuItem { Header = $"Shell 扩展 (只读, {vm.ShellExtensions.Count})" };
            foreach (var ext in vm.ShellExtensions)
            {
                var extItem = new MenuItem
                {
                    Header = string.IsNullOrEmpty(ext.DisplayName) || ext.DisplayName == ext.SourceKey
                        ? $"{ext.SourceKey}  ({ext.Clsid})"
                        : $"{ext.DisplayName}  ({ext.SourceKey})",
                    IsEnabled = false,
                };
                extMenu.Items.Add(extItem);
            }
            menu.Items.Add(extMenu);
        }
        menu.PlacementTarget = PreviewSurface;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static ContextMenu BuildContextMenu(IReadOnlyList<MenuItemViewModel> items, MainViewModel vm)
    {
        var menu = new ContextMenu();
        foreach (var item in items)
            menu.Items.Add(BuildMenuItem(item, vm));
        return menu;
    }

    private static MenuItem BuildMenuItem(MenuItemViewModel item, MainViewModel vm)
    {
        var mi = new MenuItem
        {
            Header = BuildHeader(item),
            Command = vm.SelectPreviewItemCommand,
            CommandParameter = item,
        };
        if (item.Icon is not null)
        {
            mi.Icon = new Image
            {
                Source = item.Icon,
                Width = 16,
                Height = 16,
                SnapsToDevicePixels = true,
            };
        }
        foreach (var child in item.Children)
            mi.Items.Add(BuildMenuItem(child, vm));
        return mi;
    }

    private static object BuildHeader(MenuItemViewModel item)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (item.IsExtended)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xE7, 0xB6)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Shift",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x4A, 0x00)),
                },
            };
            panel.Children.Add(badge);
        }
        return panel;
    }
}
