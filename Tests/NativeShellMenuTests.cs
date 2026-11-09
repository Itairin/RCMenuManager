using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using RCMenuManager.Views.Controls;
using Xunit;
using Xunit.Abstractions;

namespace RCMenuManager.Tests;

public class NativeShellMenuTests
{
    private readonly ITestOutputHelper _out;
    public NativeShellMenuTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Exe_scope_reads_the_real_shell_menu()
    {
        var scope = MenuScope.ForExtension(".exe", "exefile");
        var nodes = ShellContextMenuReader.Instance.TryRead(scope, extendedVerbs: false);

        Assert.NotNull(nodes);
        var texts = nodes!.Where(n => !n.IsSeparator).Select(n => n.Text).ToList();
        foreach (var t in texts) _out.WriteLine(t);

        // Explorer's own ordering: default verb first, properties last.
        Assert.StartsWith("打开", texts[0]);
        Assert.Contains(texts, t => t.StartsWith("以管理员身份运行", StringComparison.Ordinal));
        Assert.Equal("属性(&R)", texts[^1]);
        Assert.Contains(nodes, n => n.IsSeparator);
        Assert.True(texts.Count > 6, "菜单项太少，可能没有真正读到 shell 菜单");
    }

    /// <summary>
    /// The reader must work for every kind of target, not just .exe: text gets
    /// 打开/编辑 from SystemFileAssociations\text, images get 打印/设为桌面背景,
    /// and an unassociated extension must fall back to a bare 打开方式 menu.
    /// </summary>
    [Theory]
    [InlineData(".txt", "txtfile", "打开")]
    [InlineData(".png", "pngfile", "打开")]
    [InlineData(".jpg", "jpegfile", "打开")]
    [InlineData(".zip", "CompressedFolder", "打开")]
    [InlineData(".pdf", null, "打开")]
    [InlineData(".html", "htmlfile", "打开")]
    [InlineData(".ps1", null, "打开")]
    [InlineData(".xyz", null, "打开方式")]
    public void Other_file_types_read_the_real_shell_menu(string extension, string? progId, string firstRow)
    {
        var scope = MenuScope.ForExtension(extension, progId);
        var nodes = ShellContextMenuReader.Instance.TryRead(scope, extendedVerbs: false);

        Assert.NotNull(nodes);
        var texts = nodes!.Where(n => !n.IsSeparator).Select(n => n.Text).ToList();
        foreach (var t in texts) _out.WriteLine($"{extension}: {t}");

        Assert.True(texts.Count >= 10, $"{extension} 只读到 {texts.Count} 项，菜单不完整");
        Assert.StartsWith(firstRow, texts[0]);
        Assert.Equal("属性(&R)", texts[^1]);
        Assert.Contains(nodes, n => n.IsSeparator);
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Fact]
    public void Second_read_of_the_same_scope_comes_from_cache()
    {
        var scope = MenuScope.ForExtension(".png", "pngfile");
        ShellContextMenuReader.Instance.TryRead(scope, false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var again = ShellContextMenuReader.Instance.TryRead(scope, false);
        sw.Stop();

        Assert.NotNull(again);
        Assert.True(sw.ElapsedMilliseconds < 100, $"缓存未命中，二次读取耗时 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Native_rows_link_back_to_editable_verbs()
    {
        var scope = MenuScope.ForExtension(".exe", "exefile");
        var reg = new RegistryService();
        var parser = new MenuParserService(reg);
        var icons = new IconService();
        var items = parser.GetMenuItems(scope).Select(m => new MenuItemViewModel(m, icons)).ToList();
        var nodes = ShellContextMenuReader.Instance.TryRead(scope, false);

        Assert.NotNull(nodes);
        var composed = MenuPreviewComposer.ComposeNative(nodes!, items);

        // "以管理员身份运行" is the runas verb; it must stay clickable/editable.
        var runas = composed.FirstOrDefault(n => n.Header.StartsWith("以管理员身份运行", StringComparison.Ordinal));
        Assert.NotNull(runas);
        Assert.NotNull(runas!.Item);
        Assert.Equal("runas", runas.Item!.VerbName);

        // Rows the app cannot own (third-party handlers) stay read-only.
        Assert.Contains(composed, n => n.IsBuiltIn);
    }

    [Fact]
    public void Accelerators_and_ampersands_are_stripped_when_matching()
    {
        var item = MakeVm("以管理员身份运行(&A)");
        var nodes = new[]
        {
            new ShellMenuNode { Text = "以管理员身份运行(&A)" },
            new ShellMenuNode { Text = "&Open WezTerm here" },
        };

        var composed = MenuPreviewComposer.ComposeNative(nodes, new[] { item });

        Assert.Same(item, composed[0].Item);
        Assert.Null(composed[1].Item);
    }

    [Fact]
    public void Renders_the_native_menu_for_visual_check()
    {
        var scope = MenuScope.ForExtension(".exe", "exefile");
        var reg = new RegistryService();
        var parser = new MenuParserService(reg);
        var icons = new IconService();
        var items = parser.GetMenuItems(scope).Select(m => new MenuItemViewModel(m, icons)).ToList();
        var native = ShellContextMenuReader.Instance.TryRead(scope, false);
        Assert.NotNull(native);

        var nodes = MenuPreviewComposer.ComposeNative(native!, items);
        var (width, height) = Render(nodes, $"native-{scope.Extension?.TrimStart('.')}");
        Assert.True(width > 200, $"菜单太窄: {width}");
        Assert.True(height > 300, $"菜单太矮: {height}");
    }

    private static MenuItemViewModel MakeVm(string displayName)
    {
        var item = new ContextMenuItem { VerbName = "runas", DisplayName = displayName };
        return new MenuItemViewModel(item, new IconService());
    }

    /// <summary>
    /// Renders through the same factory the popup uses, on an STA thread, so the
    /// image reflects the shipped rendering (icons, access keys, separators).
    /// </summary>
    private static (int Width, int Height) Render(
        System.Collections.Generic.IReadOnlyList<PreviewNode> nodes, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rcm-preview-{name}.png");
        var size = (0, 0);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var stack = new StackPanel { Orientation = Orientation.Vertical, Background = Brushes.White };
                TextElement.SetFontFamily(stack, PreviewMenuFactory.MenuFont);
                TextElement.SetFontSize(stack, PreviewMenuFactory.MenuFontSize);
                foreach (var node in nodes)
                    stack.Children.Add((UIElement)PreviewMenuFactory.BuildNode(node, null));

                var host = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3),
                    MinWidth = 220,
                    Child = stack,
                };

                host.Measure(new Size(420, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                host.UpdateLayout();

                var bmp = new RenderTargetBitmap(
                    (int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(path)) encoder.Save(fs);
                size = (bmp.PixelWidth, bmp.PixelHeight);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(20));
        if (failure is not null) throw failure;
        return size;
    }

}
