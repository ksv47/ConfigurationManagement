using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер ширины колонки «Название» из числа (double) в тип GridLength.
/// В отличие от DoubleToGridLengthConverter, при нулевой ширине возвращает гибкую
/// колонку (*), чтобы «Название» растягивалось на всё свободное место до тех пор,
/// пока пользователь не задал фиксированную ширину перетаскиванием разделителя.
/// </summary>
public class NameColumnWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width && width > 0)
        {
            return new GridLength(width);
        }

        // Гибкая колонка: растягивается на всё свободное место.
        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is GridLength length ? length.Value : 0d;
    }
}