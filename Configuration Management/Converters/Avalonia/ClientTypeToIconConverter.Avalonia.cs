#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ключ иконки (имя Geometry из Icons.axaml) в зависимости
    /// от типа клиента 1С. Возвращаемое значение передаётся в IconKeyToGeometryConverter.
    /// </summary>
    public class ClientTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string clientType)
            {
                return clientType switch
                {
                    "Тонкий" => "IconPhone",
                    "Толстый" => "IconMonitor",
                    _ => "IconUnknown"
                };
            }
            return "IconUnknown";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
#endif