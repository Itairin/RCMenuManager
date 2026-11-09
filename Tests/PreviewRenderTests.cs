using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using RCMenuManager.Views.Controls;
using Xunit;

namespace RCMenuManager.Tests;

/// <summary>
/// Renders the preview menu off-screen so the visual result can be inspected
/// without a desktop session. This is the check that catches styling
/// regressions the structural tests cannot see (missing check marks, lost
/// separators, everything collapsing into one flat column of text).
/// </summary>
public class PreviewRenderTests
{
    private static (int Width, int Height, int InkPixels, string Path) RenderNodes(
        IReadOnlyList<PreviewNode> nodes, string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        Exception? failure = null;
        var result = (Width: 0, Height: 0, Ink: 0, Path: path);

        var thread = new Thread(() =>
        {
            try
            {
                // Mirror what a ContextMenu lays out: MenuItem children stacked
                // vertically. A Menu control would use the top-level (menu bar)
                // template, which has no check column or submenu arrows and so
                // would not show what the real popup looks like.
                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Background = Brushes.White,
                };
                System.Windows.Documents.TextElement.SetFontFamily(stack, PreviewMenuFactory.MenuFont);
                System.Windows.Documents.TextElement.SetFontSize(stack, PreviewMenuFactory.MenuFontSize);
                foreach (var node in nodes)
                    stack.Children.Add((UIElement)PreviewMenuFactory.BuildNode(node, null));

                var host = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3),
                    MinWidth = 220,   // Explorer's menus are never narrower than this
                    Child = stack,
                };

                host.Measure(new Size(420, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                host.UpdateLayout();

                var w = Math.Max(1, (int)Math.Ceiling(host.ActualWidth));
                var h = Math.Max(1, (int)Math.Ceiling(host.ActualHeight));
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(host);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(path))
                    encoder.Save(fs);

                // Count non-white pixels: a menu that failed to lay out renders
                // as a blank rectangle and would otherwise pass a size check.
                var stride = w * 4;
                var pixels = new byte[stride * h];
                rtb.CopyPixels(pixels, stride, 0);
                var ink = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] < 0xF0 || pixels[i + 1] < 0xF0 || pixels[i + 2] < 0xF0)
                        ink++;
                }

                result = (w, h, ink, path);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }

    private static (int Width, int Height, int InkPixels, string Path) Render(
        MenuScope scope, IReadOnlyList<MenuItemViewModel>? items = null, bool showExtended = false)
    {
        var nodes = MenuPreviewComposer.Compose(
            scope,
            items ?? Array.Empty<MenuItemViewModel>(),
            Array.Empty<ShellExtensionInfo>(),
            showExtended,
            clipboardHasFiles: true);
        return RenderNodes(nodes, $"rcm-preview-{scope.ScopeId.Replace(':', '-')}.png");
    }

    [Fact]
    public void Folder_menu_renders_with_visible_items_and_separators()
    {
        var shot = Render(MenuScope.Folder);

        Assert.True(shot.Width > 120, $"菜单太窄: {shot.Width}");
        Assert.True(shot.Height > 200, $"菜单太矮: {shot.Height}");
        Assert.True(shot.InkPixels > 3000, $"几乎没渲染出内容: {shot.InkPixels}");
        Assert.True(File.Exists(shot.Path));
    }

    [Fact]
    public void File_menu_with_a_registry_verb_renders_taller_than_without()
    {
        var bare = Render(MenuScope.AllFiles);
        var withVerb = Render(MenuScope.AllFiles, new[]
        {
            new MenuItemViewModel(new ContextMenuItem
            {
                VerbName = "vscode",
                DisplayName = "Open with Code",
                RegistryPath = @"HKEY_CLASSES_ROOT\*\shell\vscode",
            }, new IconService()),
        });

        Assert.True(withVerb.Height > bare.Height, $"{withVerb.Height} 应大于 {bare.Height}");
    }

    [Fact]
    public void Sort_submenu_renders_all_four_keys_and_both_directions()
    {
        var nodes = SystemMenuCatalog.SortBySubmenu().Children
            .Select(MenuPreviewComposer.FromEntry)
            .ToArray();
        var shot = RenderNodes(nodes, "rcm-preview-sortby.png");

        Assert.Equal(7, nodes.Length);
        Assert.True(shot.Height > 120, $"排序子菜单太矮: {shot.Height}");
        Assert.True(shot.InkPixels > 1500, $"几乎没渲染出内容: {shot.InkPixels}");
    }

    [Fact]
    public void View_submenu_renders_all_eight_modes()
    {
        var nodes = SystemMenuCatalog.ViewSubmenu().Children
            .Select(MenuPreviewComposer.FromEntry)
            .ToArray();
        var shot = RenderNodes(nodes, "rcm-preview-view.png");

        Assert.Equal(8, nodes.Length);
        Assert.True(shot.Height > 140, $"查看子菜单太矮: {shot.Height}");
    }
}
