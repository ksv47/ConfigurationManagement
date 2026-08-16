using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует ключ иконки (имя Geometry в Icons.xaml) в Geometry для Path.Data.
/// Результаты кэшируются — TryFindResource не вызывается на каждую строку списка.
/// </summary>
public class IconKeyToGeometryConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            key = "IconFolder";

        return Cache.GetOrAdd(key, Resolve);
    }

    private static Geometry Resolve(string key)
    {
        if (Application.Current?.TryFindResource(key) is Geometry geometry)
            return geometry;

        if (Application.Current?.TryFindResource("IconFolder") is Geometry fallback)
            return fallback;

        return Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
