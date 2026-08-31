using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// Возвращает горизонтальный отступ (Thickness) для заголовка узла дерева в зависимости от уровня
/// вложенности и наличия дочерних элементов.
/// <para>
/// У группы с дочерними элементами (<see cref="HasItems"/> == true) горизонтальный сдвиг уже даёт
/// кнопка разворота (см. <see cref="LevelToThicknessConverter.ExpanderWidth"/> + level*IndentStep),
/// поэтому дополнительный отступ заголовку не нужен (offset = 0).
/// У пустой (листовой) группы кнопка разворота скрыта — добавляем тот же отступ, чтобы пустые группы
/// сохраняли иерархию, как остальные вложенные группы.
/// </para>
/// Шаг сдвига синхронизирован с GroupTreeIndentStep (18 px на уровень).
/// </summary>
public class GroupOffsetConverter : IMultiValueConverter
{
    /// <summary>Шаг отступа на один уровень вложенности (px).</summary>
    public const double IndentStep = 18.0;

    /// <summary>Ширина кнопки разворота группы (px), см. Expander Width в MainWindow.xaml.</summary>
    public const double ExpanderWidth = LevelToThicknessConverter.ExpanderWidth;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var level = values.Length > 0 && values[0] is int i ? i : 0;
        var hasItems = values.Length > 1 && values[1] is bool b && b;
        // В компактном режиме вертикальный зазор между группами убирается почти полностью,
        // чтобы группы располагались плотнее (сохраняется только горизонтальный отступ уровня).
        var compact = values.Length > 2 && values[2] is bool cb && cb;
        // Заголовок группы выравнивается в начало строки (отступ уровня даёт имя через
        // LevelToThickness "group"), чтобы колонка «Действия» не сдвигалась с вложенностью.
        // Группа с дочерними: расширитель дерева занимает col0 (ExpanderWidth + level*IndentStep),
        // компенсируем его отрицательным отступом. Пустая (листовая): расширителя нет, отступ 0.
        var offset = hasItems ? -(level * IndentStep + ExpanderWidth) : 0d;
        // Вертикальный 1px — как прежний Margin="0,1" у заголовка группы (в компактном — 0).
        var vert = compact ? 0d : 1d;
        return new Thickness(offset, vert, 0, vert);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}