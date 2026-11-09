using System;
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

    /// <summary>
    /// Builds the menu on demand so it always reflects the current scope. The
    /// built-in entries Explorer contributes (Open / View / Sort by / New /
    /// Properties ...) are reproduced by <see cref="MenuPreviewComposer"/>; the
    /// verbs parsed from the registry are spliced into the same slot Explorer
    /// uses, which makes the popup a faithful model instead of a flat list.
    /// </summary>
    private void OnPreviewSurfaceRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedScope is null) return;

        var native = vm.ShellMenu.TryRead(vm.SelectedScope.Scope, vm.ShowExtended);
        var nodes = native is { Count: > 0 }
            // Preferred: the menu Windows itself produced for this kind of
            // object, so labels/order/icons are exactly what Explorer shows.
            ? MenuPreviewComposer.ComposeNative(native, vm.MenuItems.ToList())
            // Fallback when the shell cannot build one (locked-down machine,
            // unknown class): the hand-built catalog layout.
            : MenuPreviewComposer.Compose(
                vm.SelectedScope.Scope,
                vm.MenuItems.ToList(),
                vm.ShellExtensions,
                vm.ShowExtended,
                clipboardHasFiles: HasClipboardFiles());

        if (nodes.Count == 0) return;

        var menu = PreviewMenuFactory.Create(nodes, vm);
        menu.PlacementTarget = PreviewSurface;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Paste entries are greyed out in a real menu when the clipboard has no
    /// files; reproducing that avoids showing a clickable action that Explorer
    /// would not offer.
    /// </summary>
    private static bool HasClipboardFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch
        {
            // Clipboard can be locked by another process; assume "has files"
            // rather than lying about the menu being empty.
            return true;
        }
    }
}
