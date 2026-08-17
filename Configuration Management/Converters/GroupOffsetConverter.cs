using System;
using System.Globalization;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Возвращает горизонтальный сдвиг (double) для заголовка узла дерева в зависимости от уровня
/// вложенности и наличия дочерних элементов. Сдвиг применяется только к группам (HasItems == true),
/// поэтому строки баз (листья) остаются на месте, и их колонки данных остаются строго под заголовками.
/// Шаг сдвига синхронизирован с GroupTreeIndentStep (18 px на уровень).
/// </summary>
public class GroupOffsetConverter : IMultiValueConverter
{
    /// <summary>Шаг отступа на один уровень вложенности (px).</summary>
    public const double IndentStep = 18.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var level = values.Length > 0 && values[0] is int i ? i : 0;
        var hasItems = values.Length > 1 && values[1] is bool b && b;
        return hasItems ? level * IndentStep : 0d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}