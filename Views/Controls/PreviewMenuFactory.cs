using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RCMenuManager.ViewModels;

namespace RCMenuManager.Views.Controls;

/// <summary>
/// Turns composed <see cref="PreviewNode"/>s into WPF menu items. Kept out of
/// the control so the exact same builder can be exercised (and rendered) by
/// tests instead of a re-implementation drifting away from the real preview.
/// </summary>
internal static class PreviewMenuFactory
{
    internal static readonly FontFamily MenuFont = new("Segoe UI");
    internal const double MenuFontSize = 12;

    internal static ContextMenu Create(IReadOnlyList<PreviewNode> nodes, MainViewModel? vm)
    {
        var menu = new ContextMenu
        {
            FontFamily = MenuFont,
            FontSize = MenuFontSize,
        };
        foreach (var node in nodes)
            menu.Items.Add(BuildNode(node, vm));
        return menu;
    }

    internal static object BuildNode(PreviewNode node, MainViewModel? vm)
    {
        if (node.Kind == PreviewNodeKind.Separator)
        {
            // Explorer indents the rule so it starts at the icon column and
            // stops short of the arrow gutter; the default WPF rule spans the
            // full width and reads as a hard line across the menu.
            return new Separator { Margin = new Thickness(26, 4, 8, 4) };
        }

        var mi = new MenuItem
        {
            Header = BuildHeader(node),
            IsEnabled = node.IsEnabled,
            // Explorer's rows are ~22px; WPF's default template is tighter and
            // makes a 15-entry menu look cramped.
            MinHeight = 22,
        };

        // Explorer draws the check as a plain glyph in the icon column. WPF's
        // IsCheckable path renders a themed check box instead, which does not
        // exist in a context menu, so the glyph is placed as the icon.
        if (node.IsCheckable && node.IsChecked)
        {
            mi.Icon = new TextBlock
            {
                Text = "\u2713",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 13,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        // Explorer renders the default verb (Open) semibold.
        if (node.IsDefault)
            mi.FontWeight = FontWeights.SemiBold;

        if (node.Item is not null && vm is not null)
        {
            mi.Command = vm.SelectPreviewItemCommand;
            mi.CommandParameter = node.Item;
        }

        if (node.Item?.Icon is not null)
        {
            mi.Icon = new Image
            {
                Source = node.Item.Icon,
                Width = 16,
                Height = 16,
                SnapsToDevicePixels = true,
            };
        }

        foreach (var child in node.Children)
            mi.Items.Add(BuildNode(child, vm));

        return mi;
    }

    private static object BuildHeader(PreviewNode node)
    {
        if (node.Badge is null) return node.Header;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = node.Header,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xE7, 0xB6)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = node.Badge,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x4A, 0x00)),
            },
        });
        return panel;
    }
}
