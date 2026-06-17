using System.Windows;
using RCMenuManager.ViewModels;

namespace RCMenuManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
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
