using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер для отображения значков в зависимости от типа клиента.
/// </summary>
public class ClientTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}