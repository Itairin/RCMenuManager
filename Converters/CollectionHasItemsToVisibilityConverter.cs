using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RCMenuManager.Converters;

/// <summary>
/// Returns Visible when the source is a non-null IEnumerable with at least one
/// element. Used to switch the preview between "menu rendered" and
/// "no items" placeholder. Reads the live count when the source is an
/// ICollectionView (WPF feeds us the view, not the underlying collection).
/// </summary>
public sealed class CollectionHasItemsToVisibilityConverter : IValueConverter
{
    public Visibility TrueValue { get; set; } = Visibility.Visible;
    public Visibility FalseValue { get; set; } = Visibility.Collapsed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasItems = value switch
        {
            ICollectionView cv => cv is not null && !cv.IsEmpty,
            IEnumerable e => HasAny(e),
            _ => false,
        };
        return hasItems ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool HasAny(IEnumerable e)
    {
        foreach (var _ in e) return true;
        return false;
    }
}
