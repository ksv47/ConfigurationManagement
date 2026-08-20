#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ширина колонки списка баз из (bool видимость, double сохранённая ширина).
    /// </summary>
    public class ColumnVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var show = values is { Count: > 0 } && values[0] is bool b && b;
            if (!show)
                return new GridLength(0);

            var width = values is { Count: > 1 } && values[1] is double d ? d : 0d;
            if (width > 0)
                return new GridLength(width);

            if (parameter is string p && double.TryParse(p, NumberStyles.Any, culture, out var fallback) && fallback > 0)
                return new GridLength(fallback);

            return new GridLength(1, GridUnitType.Auto);
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => new object?[] { false, 0d };
    }
}
#endif