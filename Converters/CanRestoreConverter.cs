using System;
using System.Globalization;
using System.Windows.Data;

namespace RCMenuManager.Converters;

public sealed class CanRestoreConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        var success = values[0] is bool b && b;
        var path = values[1] as string;
        return success && !string.IsNullOrEmpty(path);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
