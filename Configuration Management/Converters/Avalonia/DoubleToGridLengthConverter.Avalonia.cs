#if LINUX
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ширина колонки из double в GridLength.
    /// </summary>
    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
                return new GridLength(width);

            if (parameter is string p && double.TryParse(p, NumberStyles.Any, culture, out var fallback) && fallback > 0)
                return new GridLength(fallback);

            return new GridLength(1, GridUnitType.Auto);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is GridLength length ? length.Value : 0d;
    }
}
#endif