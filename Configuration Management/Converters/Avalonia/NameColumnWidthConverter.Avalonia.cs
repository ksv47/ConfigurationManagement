#if LINUX
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ширина колонки «Название» (double) → GridLength.
    /// При нулевой ширине — гибкая колонка (*), чтобы название растягивалось.
    /// </summary>
    public class NameColumnWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width && width > 0)
                return new GridLength(width);

            return new GridLength(1, GridUnitType.Star);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is GridLength length ? length.Value : 0d;
    }
}
#endif