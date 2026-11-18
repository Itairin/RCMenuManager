using System;
using System.ComponentModel;
using System.Windows;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;

namespace RCMenuManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;

    public MainWindow(MainViewModel vm, ISettingsService settings)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        DataContext = vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>
    /// Restores the geometry and pane split saved on the previous run. Steps are
    /// clamped to the current work area: a size saved on a bigger monitor (or a
    /// window saved while docked) must not open off screen.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = _settings.Current;
        var area = SystemParameters.WorkArea;

        if (s.WindowWidth >= 480) Width = Math.Min(s.WindowWidth, area.Width);
        if (s.WindowHeight >= 320) Height = Math.Min(s.WindowHeight, area.Height);

        if (s.WindowLeft is double left && s.WindowTop is double top)
        {
            // Keep a grab-able slice visible instead of trusting the saved point.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Min(Math.Max(left, area.Left), Math.Max(area.Left, area.Right - 240));
            Top = Math.Min(Math.Max(top, area.Top), Math.Max(area.Top, area.Bottom - 120));
        }

        ApplyListRatio(s.ListColumnRatio);
        if (s.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var s = _settings.Current;

        // RestoreBounds holds the normal geometry while the window is maximised or
        // minimised, which is the size worth remembering.
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.Width >= 480 && bounds.Height >= 320)
        {
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
        }
        s.IsMaximized = WindowState == WindowState.Maximized;
        s.ListColumnRatio = CurrentListRatio();
        _settings.Save();
    }

    private void ApplyListRatio(double ratio)
    {
        var r = Math.Clamp(ratio, 0.2, 0.85);
        ListColumn.Width = new GridLength(r, GridUnitType.Star);
        DetailsColumn.Width = new GridLength(1 - r, GridUnitType.Star);
    }

    private double CurrentListRatio()
    {
        var total = ListColumn.ActualWidth + DetailsColumn.ActualWidth;
        if (total <= 0) return _settings.Current.ListColumnRatio;
        return Math.Clamp(ListColumn.ActualWidth / total, 0.2, 0.85);
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        var ok = HasFilePayload(e);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        _vm.IsDragOver = ok;
        e.Handled = true;
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFilePayload(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        _ = _vm.OnFileDroppedAsync(paths);
    }

    private static bool HasFilePayload(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop);
}
