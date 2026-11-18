using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (DataContext is not MainViewModel vm) return;

        // Clearing matters as much as setting: when the list is rebuilt the containers
        // are recreated and WPF reports "nothing selected". Only assigning on a hit
        // used to leave the details panel showing an item that no longer existed
        // (for example one the user had just deleted).
        vm.SelectedItem = e.NewValue as MenuItemViewModel;
    }

    /// <summary>Ctrl+F puts the caret in the filter box, Escape clears it.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0)
        {
            if (DataContext is MainViewModel vm) vm.SearchText = string.Empty;
            e.Handled = true;
        }
    }
}
