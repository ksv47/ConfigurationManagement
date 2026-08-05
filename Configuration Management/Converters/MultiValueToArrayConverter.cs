using System.Globalization;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер, возвращающий массив значений MultiBinding как единый объект.
/// Используется для передачи нескольких значений в CommandParameter
/// (например, базы и тега в команду удаления тега).
/// </summary>
public class MultiValueToArrayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}