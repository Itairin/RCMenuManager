using System.Windows;
using System.Windows.Controls;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;
using RCMenuManager.Views.Dialogs;

namespace RCMenuManager.Views.Controls;

public partial class DetailsPanel : UserControl
{
    public DetailsPanel()
    {
        InitializeComponent();
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var icons = (Application.Current as App)?.Services.GetService(typeof(IconService)) as IconService;
        if (icons is null) return;
        var dlg = new IconPickerDialog(icons, vm.EditPanel.DraftIcon) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedExpression))
            vm.EditPanel.DraftIcon = dlg.SelectedExpression!;
    }
}
