#if LINUX
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: true → Collapsed, false → Visible (обратный BooleanToVisibility).
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is Visibility v && v != Visibility.Visible;
    }
}
#endif