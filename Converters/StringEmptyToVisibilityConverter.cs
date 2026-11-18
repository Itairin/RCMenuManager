using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

/// <summary>
/// Visible only when the bound string is empty — used for the search box
/// placeholder, which WPF has no built-in support for.
/// </summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
