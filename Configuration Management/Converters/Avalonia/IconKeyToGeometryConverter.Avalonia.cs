#if LINUX
using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ключ иконки (имя Geometry в Icons.axaml) → Geometry для Path.Data.
    /// </summary>
    public class IconKeyToGeometryConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, Geometry> Cache = new(StringComparer.Ordinal);
        private static readonly Geometry Empty = StreamGeometry.Parse("M0,0H1V1H0Z");

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var key = value as string;
            if (string.IsNullOrWhiteSpace(key))
                key = "IconFolder";

            return Cache.GetOrAdd(key, Resolve);
        }

        private static Geometry Resolve(string key)
        {
            if (Application.Current is { } app &&
                app.TryGetResource(key, null, out var res) && res is Geometry g)
                return g;

            if (Application.Current is { } app2 &&
                app2.TryGetResource("IconFolder", null, out var fallback) && fallback is Geometry fg)
                return fg;

            return Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif