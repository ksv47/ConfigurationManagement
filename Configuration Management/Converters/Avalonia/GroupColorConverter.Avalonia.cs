#if LINUX
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: кисть фона заголовка группы (hex-цвет → SolidColorBrush).
    /// </summary>
    public class GroupColorConverter : IMultiValueConverter
    {
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is { Count: > 0 } && values[0] is ISolidColorBrush ready)
                return ready;

            var hex = values is { Count: > 0 } ? values[0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith('#'))
                hex = "#2D6CDF";

            return GetBrush(hex);
        }

        public static SolidColorBrush GetBrush(string? hex)
            => Cache.GetOrAdd(hex ?? "#2D6CDF", key =>
            {
                try { return new SolidColorBrush(Color.Parse(key)); }
                catch { return new SolidColorBrush(Color.Parse("#2D6CDF")); }
            });

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif