using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;

internal static class Program
{
    private static string OutDir = @"D:\Itair\RCMenuManager\docs\review\preview\deliverables";

    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(OutDir);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/RCMenuManager;component/Resources/Styles.xaml", UriKind.Absolute),
        });

        app.Startup += (_, _) =>
        {
            MainViewModel vm;
            try { vm = Build(); }
            catch (Exception ex) { Console.WriteLine("VM FAILED: " + ex); app.Shutdown(); return; }

            // Let the async scope load finish on the UI thread before rendering.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { Render(vm); }
                catch (Exception ex) { Console.WriteLine("RENDER FAILED: " + ex); }
                app.Shutdown();
            };
            timer.Start();
        };

        app.Run();
    }

    private static MainViewModel Build()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rcm-render-data");
        var registry = new RegistryService();
        var parser = new MenuParserService(registry);
        var icons = new IconService();
        var writer = new Win32RegistryWriter();
        var backup = new BackupService(Path.Combine(tmp, "backups"));
        var log = new OperationLogService(Path.Combine(tmp, "logs", "op.log"));
        var regWrite = new RegistryWriteService(writer, backup, log, () => false, null, backup.Import);
        var win11 = new Win11MenuService(regWrite);
        var presets = new PresetService(writer, regWrite, registry);
        var settings = new SettingsService(Path.Combine(tmp, "settings.json"));
        var pending = new PendingActionStore(Path.Combine(tmp, "pending.json"));
        var origins = new MenuItemOriginService(log, presets);

        var vm = new MainViewModel(registry, parser, icons, regWrite, backup, log, win11,
            new WinVersionService(), new FileTypeService(), presets,
            ShellContextMenuReader.Instance, settings, pending, origins);

        // Desktop is the scope this round was about.
        vm.SelectedScope = vm.Scopes.FirstOrDefault(s => s.Scope.Type == ScopeType.Desktop) ?? vm.Scopes[0];
        return vm;
    }

    private static void Render(MainViewModel vm)
    {
        var tree = new RCMenuManager.Views.Controls.MenuTreeView { DataContext = vm };
        Save(tree, 820, 460, "01-list-source-badges.png");

        var first = vm.MenuItems.FirstOrDefault();
        if (first is not null)
        {
            vm.SelectedItem = first;
            var details = new RCMenuManager.Views.Controls.DetailsPanel { DataContext = vm };
            Save(details, 460, 620, "02-details-source-block.png");
        }

        // A shell-extension row (共享 / 在记事本中打开 …) selected from the preview.
        var parser = new MenuParserService(new RegistryService());
        var ext = parser.GetShellExtensionHandlers(vm.SelectedScope!.Scope).FirstOrDefault();
        if (ext is not null)
        {
            vm.SelectPreviewNodeCommand.Execute(new PreviewNode { Header = ext.DisplayName, Extension = ext });
            var details = new RCMenuManager.Views.Controls.DetailsPanel { DataContext = vm };
            Save(details, 460, 520, "03-details-shell-extension.png");
        }

        // The preview popup built from the real shell menu.
        var native = ShellContextMenuReader.Instance.TryRead(vm.SelectedScope!.Scope, false);
        var nodes = native is { Count: > 0 }
            ? MenuPreviewComposer.ComposeNative(native, vm.MenuItems.ToList(), vm.ShellExtensions)
            : MenuPreviewComposer.Compose(vm.SelectedScope!.Scope, vm.MenuItems.ToList(), vm.ShellExtensions, false, true);
        var stack = new StackPanel { Background = Brushes.White };
        System.Windows.Documents.TextElement.SetFontFamily(stack, new FontFamily("Segoe UI"));
        System.Windows.Documents.TextElement.SetFontSize(stack, 12);
        foreach (var node in nodes)
            stack.Children.Add((UIElement)RCMenuManager.Views.Controls.PreviewMenuFactory.BuildNode(node, vm));
        var host = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = stack,
        };
        Save(host, 460, 700, "04-preview-popup.png");

        // Dropped-item state: banner + list read from a real folder.
        var demoDir = Path.Combine(Path.GetTempPath(), "rcm-render-drop", "demo");
        Directory.CreateDirectory(demoDir);
        File.WriteAllText(Path.Combine(demoDir, "note.txt"), "x");
        // Mirror the real drop flow: dropping a folder also switches the scope, so the
        // demo state is one the app can actually be in.
        vm.SelectedScope = vm.Scopes.FirstOrDefault(s => s.Scope.Type == ScopeType.Folder) ?? vm.SelectedScope;
        vm.DropTarget = new RCMenuManager.Models.DropTarget(demoDir, RCMenuManager.Models.DropTargetKind.Folder);
        // Sleep() on the dispatcher thread would block the awaits inside LoadAsync
        // forever; pump instead so the reload actually finishes.
        PumpFor(TimeSpan.FromSeconds(4));
        var dropped = new RCMenuManager.Views.Controls.MenuTreeView { DataContext = vm };
        Save(dropped, 820, 520, "06-drop-target-list.png");
        // The full window cannot be built here: its XAML references the app assembly's
        // pack:// icon, which does not resolve in a separate executable. The list view
        // above is what this feature changes.
        vm.DropTarget = null;

        File.WriteAllText(Path.Combine(OutDir, "metrics.txt"),
            $"scope={vm.SelectedScope!.Scope.ScopeId}{Environment.NewLine}" +
            $"items={vm.MenuItems.Count}{Environment.NewLine}" +
            $"withSourceBadge={vm.MenuItems.Count(i => i.HasSourceBadge)}{Environment.NewLine}" +
            $"nameInferred={vm.MenuItems.Count(i => i.IsNameInferred)}{Environment.NewLine}" +
            $"hiddenFromMenu={vm.MenuItems.Count(i => i.IsHiddenFromMenu)}{Environment.NewLine}" +
            $"shellExtensions={vm.ShellExtensions.Count}{Environment.NewLine}");
    }


    /// <summary>Runs the dispatcher for a while so pending async continuations complete.</summary>
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Save(FrameworkElement element, int width, int height, string name)
    {
        var host = new Border { Background = Brushes.White, Child = element };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var w = Math.Max(1, (int)Math.Ceiling(host.ActualWidth == 0 ? width : host.ActualWidth));
        var h = Math.Max(1, (int)Math.Ceiling(host.ActualHeight == 0 ? height : host.ActualHeight));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(Path.Combine(OutDir, name));
        encoder.Save(stream);
        // Proof the render is not blank, plus an exact dump of the drawn text, so the
        // content can be checked without opening the PNG.
        var pixels = new byte[w * h * 4];
        rtb.CopyPixels(pixels, w * 4, 0);
        var ink = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] < 240 || pixels[i + 1] < 240 || pixels[i + 2] < 240) ink++;

        var lines = new List<string>();
        CollectText(host, lines);
        File.WriteAllText(Path.Combine(OutDir, Path.ChangeExtension(name, ".txt")),
            "image: " + name + Environment.NewLine
            + "size: " + w + "x" + h + Environment.NewLine
            + "nonWhite: " + Math.Round(100.0 * ink / (w * h), 1) + "%" + Environment.NewLine
            + "--- visible text ---" + Environment.NewLine
            + string.Join(Environment.NewLine, lines));

        Console.WriteLine("saved " + name + " " + w + "x" + h + " ink=" + Math.Round(100.0 * ink / (w * h), 1) + "% texts=" + lines.Count);
    }

    private static void CollectText(DependencyObject node, List<string> lines)
    {
        // Collapsed / hidden subtrees stay in the visual tree, so walking without this
        // check reported text the user never sees (the details panel showed both the
        // verb view and the extension view, the list showed every badge).
        if (node is UIElement el && el.Visibility != Visibility.Visible) return;

        if (node is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            lines.Add("  " + tb.Text);
        if (node is System.Windows.Controls.AccessText at && !string.IsNullOrWhiteSpace(at.Text))
            lines.Add("  " + at.Text.Replace('_', ' '));
        if (node is ContentControl cc && cc.Content is string s && !string.IsNullOrWhiteSpace(s))
            lines.Add("  [btn] " + s);

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            CollectText(VisualTreeHelper.GetChild(node, i), lines);
    }
}
