using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RCMenuManager.ViewModels;

namespace RCMenuManager.Converters;

public sealed class PresetStateToVisibilityConverter : IValueConverter
{
    public PresetApplyState TargetState { get; set; } = PresetApplyState.Exists;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PresetApplyState s && s == TargetState ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
