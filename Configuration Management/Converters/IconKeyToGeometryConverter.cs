using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует ключ иконки (имя Geometry в Icons.xaml) в Geometry для Path.Data.
/// Пустой ключ → IconFolder.
/// </summary>
public class IconKeyToGeometryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        if (Application.Current?.TryFindResource(key) is Geometry geometry)
            return geometry;

        // Запасной вариант, если ключ неизвестен
        if (Application.Current?.TryFindResource("IconFolder") is Geometry fallback)
            return fallback;

        return Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
