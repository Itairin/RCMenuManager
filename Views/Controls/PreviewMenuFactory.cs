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

    internal static ContextMenu Create(IReadOnlyList<PreviewNode> nodes, MainViewModel? vm, bool simplified = false)
    {
        var menu = new ContextMenu
        {
            FontFamily = MenuFont,
            FontSize = MenuFontSize,
        };

        // When the shell could not build the menu, the rows below come from the
        // built-in catalog and are therefore an approximation. Saying so inside
        // the popup is the only place the user is actually looking.
        if (simplified)
        {
            menu.Items.Add(new MenuItem
            {
                Header = SimplifiedNotice,
                IsEnabled = false,
                FontWeight = FontWeights.Normal,
            });
            menu.Items.Add(new Separator { Margin = new Thickness(26, 4, 8, 4) });
        }

        foreach (var node in nodes)
            menu.Items.Add(BuildNode(node, vm));
        return menu;
    }

    internal const string SimplifiedNotice = "系统菜单读取失败，以下为简化预览（可能缺少第三方扩展项）";

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
            // Rows with a keyboard hint need to span the menu so the hint can
            // sit flush right the way Explorer draws it.
            HorizontalContentAlignment = node.Shortcut is null
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch,
        };

        // Icon column priority: the icon the shell itself put on the row wins,
        // then the check glyph of view/sort entries, then the verb's own icon.
        // The verb icon used to be assigned unconditionally, which overwrote a
        // shell-provided bitmap with the (often older) registry icon and made
        // the row differ from the real menu.
        if (node.Icon is not null)
        {
            mi.Icon = new Image
            {
                Source = node.Icon,
                Width = 16,
                Height = 16,
                SnapsToDevicePixels = true,
            };
        }
        else if (node.IsCheckable && node.IsChecked)
        {
            // Explorer draws the check as a plain glyph in the icon column. WPF's
            // IsCheckable path renders a themed check box instead, which does not
            // exist in a context menu, so the glyph is placed as the icon.
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
        else if (node.Item?.Icon is not null)
        {
            mi.Icon = new Image
            {
                Source = node.Item.Icon,
                Width = 16,
                Height = 16,
                SnapsToDevicePixels = true,
            };
        }

        // Explorer renders the default verb (Open) semibold.
        if (node.IsDefault)
            mi.FontWeight = FontWeights.SemiBold;

        // A row is clickable when the app can identify what it is: either an
        // editable verb, or a registered shell-extension handler. Rows that are
        // purely Explorer-internal stay inert (and greying them out would be a
        // lie — Explorer shows them enabled).
        if (vm is not null && node.CanSelect)
        {
            mi.Command = vm.SelectPreviewNodeCommand;
            mi.CommandParameter = node;
        }

        foreach (var child in node.Children)
            mi.Items.Add(BuildNode(child, vm));

        return mi;
    }

    /// <summary>
    /// Shell menu text marks the access key with <c>&amp;</c> (<c>&amp;&amp;</c>
    /// for a literal one); WPF marks it with <c>_</c>. Underscores already in
    /// the text have to be doubled so they are not eaten as markers.
    /// </summary>
    internal static string ToAccessText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&') { sb.Append('&'); i++; continue; }
                if (i + 1 < text.Length) { sb.Append('_').Append(text[i + 1]); i++; continue; }
                sb.Append('&');
                continue;
            }
            if (c == '_') sb.Append("__");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static object BuildHeader(PreviewNode node)
    {
        // AccessText underlines a letter marked with "_", while the shell uses
        // "&" for the same job. Feeding it the raw string shows the "&" itself
        // ("查看(&V)") instead of Explorer's "查看(V)", so the marker is
        // translated first.
        var label = new AccessText
        {
            Text = ToAccessText(node.Header),
            VerticalAlignment = VerticalAlignment.Center,
        };

        object inline = label;
        if (node.Badge is not null)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(label);
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
            inline = panel;
        }

        if (node.Shortcut is null) return inline;

        // The shell stores "<label>\t<shortcut>" and Explorer draws the hint
        // flush right, so the two halves go into separate columns.
        var left = (UIElement)inline;
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var hint = new TextBlock
        {
            Text = node.Shortcut,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0),
        };
        Grid.SetColumn(hint, 1);
        grid.Children.Add(hint);
        return grid;
    }
}
