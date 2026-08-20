#if LINUX
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: видимость колонки через ширину (из параметра) или нулевую ширину.
    /// </summary>
    public class BooleanToGridLengthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var show = value is bool b && b;
            if (show)
            {
                if (parameter is string p && double.TryParse(p, NumberStyles.Any, culture, out var width) && width > 0)
                    return new GridLength(width);
                return new GridLength(1, GridUnitType.Auto);
            }
            return new GridLength(0);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is GridLength length && length.Value > 0;
    }
}
#endif