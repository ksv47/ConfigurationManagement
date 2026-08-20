#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Configuration_Management.Models;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: ключ иконки (имя Geometry из Icons.axaml) в зависимости
    /// от типа подключения базы. Возвращаемое значение передаётся в IconKeyToGeometryConverter.
    /// </summary>
    public class ConnectionTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ConnectionType connectionType)
            {
                return connectionType switch
                {
                    ConnectionType.File => "IconFolder",
                    ConnectionType.ClientServer => "IconNetwork",
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