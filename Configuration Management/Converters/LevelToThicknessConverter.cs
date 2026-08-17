using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует уровень вложенности дерева (int) в отступ слева (Thickness).
/// <list type="bullet">
/// <item>Без parameter (или не "base"): Level * IndentStep — для Expander групп.</item>
/// <item>parameter = "base": (Level - 1) * IndentStep + ExpanderWidth —
/// для блока ★/📌/название, чтобы ★ совпадала по горизонтали с иконкой папки
/// родительской группы (иконка идёт сразу после кнопки разворота шириной ExpanderWidth).</item>
/// </list>
/// </summary>
public class LevelToThicknessConverter : IMultiValueConverter
{
    /// <summary>Шаг отступа на один уровень вложенности (px). Синхронизирован с GroupTreeIndentStep.</summary>
    public const double IndentStep = 18.0;

    /// <summary>Ширина кнопки разворота группы (px), см. Expander Width в MainWindow.xaml.</summary>
    public const double ExpanderWidth = 26.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var level = values.Length > 0 && values[0] is int i ? i : 0;
        var forBase = parameter is string s &&
                      s.Equals("base", StringComparison.OrdinalIgnoreCase);

        double offset;
        if (forBase)
        {
            // База на уровне L — внутри группы уровня L-1.
            // Expander группы: margin (L-1)*step, ширина ExpanderWidth → иконка папки начинается
            // на (L-1)*step + ExpanderWidth. Тот же отступ у блока ★/📌/название.
            var parentLevel = level > 0 ? level - 1 : 0;
            offset = parentLevel * IndentStep + ExpanderWidth;
        }
        else
        {
            offset = level * IndentStep;
        }

        return new Thickness(offset, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
