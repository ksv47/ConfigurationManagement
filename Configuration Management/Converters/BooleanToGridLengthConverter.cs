using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер видимости колонки: возвращает ширину (из параметра), если колонку
/// нужно показывать, и нулевую ширину, если колонка скрыта.
/// Используется для колонок-кнопок «Избранное» и «Закрепить» в списке баз.
/// </summary>
public class BooleanToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var show = value is bool b && b;
        if (show)
        {
            if (double.TryParse(parameter as string, NumberStyles.Any, culture, out var width) && width > 0)
            {
                return new GridLength(width);
            }
            return new GridLength(1, GridUnitType.Auto);
        }

        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is GridLength length && length.Value > 0;
    }
}