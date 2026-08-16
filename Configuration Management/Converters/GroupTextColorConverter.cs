using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Configuration_Management.Converters;

/// <summary>
/// Контрастный цвет текста для фона группы. Кэширует результат по hex.
/// </summary>
public class GroupTextColorConverter : IMultiValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SolidColorBrush Black = CreateFrozen(Colors.Black);
    private static readonly SolidColorBrush White = CreateFrozen(Colors.White);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is SolidColorBrush ready)
            return ready;

        var hex = values.Length > 0 ? values[0]?.ToString() : null;
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
            hex = "#2D6CDF";

        return GetBrush(hex!);
    }

    public static SolidColorBrush GetBrush(string hex)
    {
        return Cache.GetOrAdd(hex ?? "#2D6CDF", key =>
        {
            var color = ParseColor(key);
            return IsLight(color) ? Black : White;
        });
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var b = new SolidColorBrush(color);
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString("#2D6CDF"); }
    }

    private static bool IsLight(Color color)
    {
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        return luminance > 150;
    }
}
