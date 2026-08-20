#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: значок в зависимости от типа клиента 1С.
    /// </summary>
    public class ClientTypeToIconConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string clientType)
            {
                return clientType switch
                {
                    "Тонкий" => "📱",
                    "Толстый" => "💻",
                    _ => "❓"
                };
            }
            return "❓";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
#endif