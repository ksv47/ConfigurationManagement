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
        // У групп с дочерними элементами отступ уже обеспечивает кнопка разворота.
        // У пустых (листовых) групп компенсируем отсутствие кнопки, чтобы иерархия сохранялась.
        var offset = hasItems ? 0d : level * IndentStep + ExpanderWidth;
        // Вертикальный 1px — как прежний Margin="0,1" у заголовка группы
        return new Thickness(offset, 1, 0, 1);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}