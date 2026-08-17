using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Преобразует уровень вложенности дерева (int) в отступ слева (Thickness) размером
/// Level * <see cref="IndentStep"/>. Используется, чтобы сдвигать только название базы
/// (и теги) вправо при вложенных группах, не трогая остальные колонки данных.
/// Реализует IMultiValueConverter, так как применяется внутри MultiBinding.
/// </summary>
public class LevelToThicknessConverter : IMultiValueConverter
{
    /// <summary>Шаг отступа на один уровень вложенности (px). Синхронизирован с GroupTreeIndentStep.</summary>
    public const double IndentStep = 18.0;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var level = values.Length > 0 && values[0] is int i ? i : 0;
        return new Thickness(level * IndentStep, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}