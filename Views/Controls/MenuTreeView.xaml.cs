using System.Windows;
using System.Windows.Controls;
using RCMenuManager.ViewModels;

namespace RCMenuManager.Views.Controls;

public partial class MenuTreeView : UserControl
{
    public MenuTreeView()
    {
        InitializeComponent();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is MenuItemViewModel item)
        {
            vm.SelectedItem = item;
        }
    }
}
