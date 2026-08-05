using System;
using System.Globalization;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер, возвращающий true, если значение не равно null.
/// Используется для управления доступностью элементов управления
/// в зависимости от наличия выбранного объекта.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Если передан параметр "invert" — инвертируем результат.
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var isNotNull = value != null;
        return invert ? !isNotNull : isNotNull;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}