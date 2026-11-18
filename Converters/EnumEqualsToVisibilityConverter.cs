using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

/// <summary>
/// Visible when the bound enum equals the converter parameter. Used to show the folder /
/// folder-background switch buttons only for the target kind they apply to.
/// </summary>
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string expected) return Visibility.Collapsed;
        return string.Equals(value.ToString(), expected, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
