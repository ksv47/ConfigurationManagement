using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// true → Collapsed, false → Visible (обратный BooleanToVisibility).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v != Visibility.Visible;
    }
}
