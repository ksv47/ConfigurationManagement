using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Configuration_Management.Converters;

/// <summary>
/// Кисть фона заголовка группы. Предпочитает готовое значение Brush из values[0];
/// иначе берёт цвет из строки (hex). Кисти кэшируются и Freeze().
/// </summary>
public class GroupColorConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is SolidColorBrush ready)
            return ready;

        // values[0] — hex-цвет группы (или FullPath в старых привязках — тогда fallback).
        var hex = values.Length > 0 ? values[0]?.ToString() : null;
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
            hex = "#2D6CDF";

        return GetBrush(hex);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static SolidColorBrush GetBrush(string hex)
    {
        return Cache.GetOrAdd(hex ?? "#2D6CDF", key =>
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));
                if (brush.CanFreeze)
                    brush.Freeze();
                return brush;
            }
            catch
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D6CDF"));
                if (brush.CanFreeze)
                    brush.Freeze();
                return brush;
            }
        });
    }
}
