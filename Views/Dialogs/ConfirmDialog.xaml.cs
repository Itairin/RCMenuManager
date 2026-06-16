using System.Windows;

namespace RCMenuManager.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string header, string body, string confirmText = "确定", string cancelText = "取消", bool dangerous = false)
    {
        InitializeComponent();
        HeaderText.Text = header;
        BodyText.Text = body;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        if (dangerous)
        {
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            CancelButton.IsCancel = true;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static bool Ask(Window? owner, string header, string body, string confirmText = "确定", string cancelText = "取消", bool dangerous = false)
    {
        var dlg = new ConfirmDialog(header, body, confirmText, cancelText, dangerous);
        if (owner is not null) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// Convenience overload used by ViewModel commands. Picks the active
    /// MainWindow as owner so the dialog inherits the right lifecycle and is
    /// modal relative to the application.
    /// </summary>
    public static bool Show(string header, string body, string confirmText = "确定", bool isDestructive = false)
        => Ask(Application.Current?.MainWindow, header, body, confirmText, cancelText: "取消", dangerous: isDestructive);
}
