using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class PresetDialog : Window
{
    public PresetDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
