using System;
using System.Collections.Generic;
using System.Threading;
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
/// Covers the row builder itself: which icon reaches the icon column, and how
/// the "this menu is only an approximation" notice is laid out. Both are things
/// the structural composer tests cannot see because they never build a control.
/// </summary>
public class PreviewMenuFactoryTests
{
    /// <summary>
    /// WPF controls are DispatcherObjects: they may only be built *and read* on
    /// one STA thread, so the assertions run inside it and any failure is
    /// rethrown on the test thread.
    /// </summary>
    private static void OnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA 线程超时");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static BitmapSource Swatch(byte b = 0x00)
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new[] { b, b, b, b }, 4);
        source.Freeze();
        return source;
    }

    private static MenuItemViewModel Verb(BitmapSource icon)
    {
        var vm = new MenuItemViewModel(
            new ContextMenuItem { VerbName = "demo", DisplayName = "演示项", Command = "demo.exe" },
            new IconService());
        // Whatever the icon service produced is irrelevant here; the point is a
        // verb icon that differs from the bitmap the shell attached to the row.
        vm.Icon = icon;
        return vm;
    }

    /// <summary>
    /// The shell's own bitmap is what Explorer shows. It used to be overwritten
    /// further down by the verb's registry icon, so a row whose icon the shell
    /// had already resolved (third-party handlers, "Compress to ZIP") came out
    /// looking different from the real menu.
    /// </summary>
    [Fact]
    public void Shell_bitmap_wins_over_the_verb_icon()
    {
        var shellIcon = Swatch(0xFF);
        var verb = Verb(Swatch(0x20));
        var node = new PreviewNode { Header = "演示项", Item = verb, IsBuiltIn = false, Icon = shellIcon };

        OnSta(() =>
        {
            var mi = (MenuItem)PreviewMenuFactory.BuildNode(node, null);
            var image = Assert.IsType<Image>(mi.Icon);
            Assert.Same(shellIcon, image.Source);
        });
    }

    /// <summary>With no shell bitmap the verb's own icon is still the fallback.</summary>
    [Fact]
    public void Verb_icon_is_used_when_the_shell_has_none()
    {
        var verbIcon = Swatch(0x40);
        var node = new PreviewNode { Header = "演示项", Item = Verb(verbIcon), IsBuiltIn = false };

        OnSta(() =>
        {
            var mi = (MenuItem)PreviewMenuFactory.BuildNode(node, null);
            var image = Assert.IsType<Image>(mi.Icon);
            Assert.Same(verbIcon, image.Source);
        });
    }

    /// <summary>
    /// A checked view/sort row draws a glyph in the icon column; that glyph must
    /// survive too, otherwise the user cannot see which mode is active.
    /// </summary>
    [Fact]
    public void Check_glyph_wins_over_the_verb_icon()
    {
        var node = new PreviewNode
        {
            Header = "详细信息",
            Item = Verb(Swatch(0x60)),
            IsBuiltIn = false,
            IsCheckable = true,
            IsChecked = true,
        };

        OnSta(() =>
        {
            var mi = (MenuItem)PreviewMenuFactory.BuildNode(node, null);
            var glyph = Assert.IsType<TextBlock>(mi.Icon);
            Assert.Equal("\u2713", glyph.Text);
        });
    }

    /// <summary>
    /// When the shell cannot build the menu the rows are a catalog guess. The
    /// popup says so instead of silently looking authentic.
    /// </summary>
    [Fact]
    public void Simplified_notice_is_the_first_row_and_is_not_clickable()
    {
        IReadOnlyList<PreviewNode> nodes = new[] { new PreviewNode { Header = "打开", IsBuiltIn = true } };

        OnSta(() =>
        {
            var menu = PreviewMenuFactory.Create(nodes, null, simplified: true);

            var notice = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal(PreviewMenuFactory.SimplifiedNotice, notice.Header);
            Assert.False(notice.IsEnabled);
            Assert.IsType<Separator>(menu.Items[1]);
            // Ordinary rows carry an AccessText header (that is what draws the
            // mnemonic underline), not a plain string.
            var first = Assert.IsType<MenuItem>(menu.Items[2]);
            Assert.Equal("打开", Assert.IsType<AccessText>(first.Header).Text);
        });
    }

    [Fact]
    public void Normal_menu_has_no_notice()
    {
        IReadOnlyList<PreviewNode> nodes = new[] { new PreviewNode { Header = "打开", IsBuiltIn = true } };

        OnSta(() =>
        {
            var menu = PreviewMenuFactory.Create(nodes, null);
            Assert.Single(menu.Items);
            Assert.Equal("打开", Assert.IsType<AccessText>(Assert.IsType<MenuItem>(menu.Items[0]).Header).Text);
        });
    }
}
