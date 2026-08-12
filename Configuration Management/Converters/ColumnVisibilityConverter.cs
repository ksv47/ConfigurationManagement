using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Конвертер видимости колонки списка баз. Принимает массив из двух значений:
/// <list type="bullet">
/// <item>признак «показывать колонку» (bool);</item>
/// <item>сохранённая ширина колонки (double, 0 — по умолчанию).</item>
/// </list>
/// Параметр конвертера — ширина колонки по умолчанию (например «120»).
/// Если колонку нужно скрыть, возвращает нулевую ширину; иначе — сохранённую
/// ширину, либо ширину по умолчанию, если сохранённая не задана.
/// </summary>
public class ColumnVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Первое значение — признак видимости колонки.
        var show = values is { Length: > 0 } && values[0] is bool b && b;
        if (!show)
        {
            return new GridLength(0);
        }

        // Второе значение — сохранённая ширина (0 — по умолчанию).
        var width = values is { Length: > 1 } && values[1] is double d ? d : 0d;
        if (width > 0)
        {
            return new GridLength(width);
        }

        // Ширина по умолчанию передаётся параметром (например «120»).
        if (double.TryParse(parameter as string, NumberStyles.Any, culture, out var fallback) && fallback > 0)
        {
            return new GridLength(fallback);
        }

        return new GridLength(1, GridUnitType.Auto);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return new object[] { false, 0d };
    }
}