using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RCMenuManager.Services;

namespace RCMenuManager.Views.Dialogs;

public partial class IconPickerDialog : Window
{
    private readonly IconService _iconService;
    private string? _currentFile;

    public string? SelectedExpression { get; private set; }

    public IconPickerDialog(IconService iconService, string? initialExpression = null)
    {
        InitializeComponent();
        _iconService = iconService;
        if (!string.IsNullOrEmpty(initialExpression))
        {
            PathBox.Text = initialExpression;
            TryLoadCurrent();
        }
    }

    private record IconRow(int Index, BitmapSource Bitmap);

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图标资源 (*.dll;*.exe;*.ico)|*.dll;*.exe;*.ico|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            PathBox.Text = dlg.FileName;
            TryLoadCurrent();
        }
    }

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryLoadCurrent();
    }

    private void TryLoadCurrent()
    {
        var raw = (PathBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            IconsList.ItemsSource = null;
            StatusText.Text = string.Empty;
            _currentFile = null;
            return;
        }
        var commaIndex = raw.LastIndexOf(',');
        var filePath = commaIndex > 0 ? raw.Substring(0, commaIndex).Trim() : raw;
        filePath = Environment.ExpandEnvironmentVariables(filePath);
        if (!File.Exists(filePath))
        {
            StatusText.Text = $"文件不存在：{filePath}";
            IconsList.ItemsSource = null;
            _currentFile = null;
            return;
        }
        _currentFile = filePath;
        if (string.Equals(Path.GetExtension(filePath), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            SelectedExpression = filePath;
            StatusText.Text = ".ico 文件，已自动选中";
            IconsList.ItemsSource = null;
            return;
        }
        var icons = _iconService.EnumerateIconsFromFile(filePath, max: 64);
        var rows = new List<IconRow>(icons.Count);
        foreach (var (index, bmp) in icons) rows.Add(new IconRow(index, bmp));
        IconsList.ItemsSource = rows;
        StatusText.Text = $"找到 {rows.Count} 个图标";
    }

    private void IconsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IconsList.SelectedItem is IconRow row && _currentFile is not null)
            SelectedExpression = $"{_currentFile},{row.Index}";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedExpression))
            SelectedExpression = (PathBox.Text ?? string.Empty).Trim();
        DialogResult = !string.IsNullOrEmpty(SelectedExpression);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
