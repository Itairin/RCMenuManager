using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

/// <summary>
/// Two-way bool/string converter used to bind a RadioButton group to a single
/// string property. ConvertBack returns the parameter when checked, otherwise
/// Binding.DoNothing so the deselected button does not clobber the value.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? parameter : Binding.DoNothing;
}
