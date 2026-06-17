using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class Win11Dialog : Window
{
    public Win11Dialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}