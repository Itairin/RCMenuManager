using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class BackupDialog : Window
{
    public BackupDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
