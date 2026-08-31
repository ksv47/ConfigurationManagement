#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: горизонтальный отступ (Thickness) для заголовка узла дерева.
    /// </summary>
    public class GroupOffsetConverter : IMultiValueConverter
    {
        /// <summary>Шаг отступа на один уровень вложенности (px).</summary>
        public const double IndentStep = 18.0;

        /// <summary>Ширина кнопки разворота группы (px).</summary>
        public const double ExpanderWidth = LevelToThicknessConverter.ExpanderWidth;

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var level = values is { Count: > 0 } && values[0] is int i ? i : 0;
            var hasItems = values is { Count: > 1 } && values[1] is bool b && b;
            // Заголовок группы выравнивается в начало строки (отступ уровня даёт имя через
            // LevelToThickness "group"), чтобы колонка «Действия» не сдвигалась с вложенностью.
            // Группа с дочерними: расширитель дерева занимает col0 (ExpanderWidth + level*IndentStep),
            // компенсируем его отрицательным отступом. Пустая (листовая): расширителя нет, отступ 0.
            var offset = hasItems ? -(level * IndentStep + ExpanderWidth) : 0d;
            return new Thickness(offset, 1, 0, 1);
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif