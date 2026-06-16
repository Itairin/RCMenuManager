using System.Windows;
using RCMenuManager.Models;
using RCMenuManager.Services;

namespace RCMenuManager.Views.Dialogs;

public partial class AddVerbDialog : Window
{
    private readonly IconService _iconService;

    public EditableVerbDraft Result { get; private set; } = new();

    public AddVerbDialog(IconService iconService, string headline = "新增菜单项", bool allowParentOnly = true)
    {
        InitializeComponent();
        Title = headline;
        _iconService = iconService;
        ParentOnlyBox.Visibility = allowParentOnly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ParentOnlyBox_Changed(object sender, RoutedEventArgs e)
    {
        var isParent = ParentOnlyBox.IsChecked == true;
        CommandBox.IsEnabled = !isParent;
        if (isParent) CommandBox.Text = string.Empty;
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new IconPickerDialog(_iconService, IconBox.Text) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedExpression))
            IconBox.Text = dlg.SelectedExpression!;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var verb = (VerbNameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(verb))
        {
            ErrorText.Text = "Verb 名不能为空";
            return;
        }
        if (verb.Contains('\\'))
        {
            ErrorText.Text = "Verb 名不能包含反斜杠";
            return;
        }
        var isParent = ParentOnlyBox.IsChecked == true;
        if (!isParent && string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            ErrorText.Text = "命令不能为空（非父级菜单必填）";
            return;
        }

        Result = new EditableVerbDraft
        {
            VerbName = verb,
            DisplayName = (DisplayNameBox.Text ?? string.Empty).Trim(),
            Command = (CommandBox.Text ?? string.Empty).Trim(),
            IconExpression = (IconBox.Text ?? string.Empty).Trim(),
            IsExtended = ExtendedBox.IsChecked == true,
            IsParentOnly = isParent,
            Position = PositionTop.IsChecked == true ? "Top" : PositionBottom.IsChecked == true ? "Bottom" : "Default",
        };
        DialogResult = true;
        Close();
    }
}
