using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Configuration_Management.Models;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер для отображения значков в зависимости от типа подключения.
/// </summary>
public class ConnectionTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionType connectionType)
        {
            return connectionType switch
            {
                ConnectionType.File => "📁",
                ConnectionType.ClientServer => "🌐",
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