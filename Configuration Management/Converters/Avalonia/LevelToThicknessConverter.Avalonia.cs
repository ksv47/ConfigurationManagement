#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: уровень вложенности дерева (int) → отступ слева (Thickness).
    /// </summary>
    public class LevelToThicknessConverter : IMultiValueConverter
    {
        /// <summary>Шаг отступа на один уровень вложенности (px).</summary>
        public const double IndentStep = 18.0;

        /// <summary>Ширина кнопки разворота группы (px).</summary>
        public const double ExpanderWidth = 26.0;

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var level = values is { Count: > 0 } && values[0] is int i ? i : 0;
            var param = parameter as string;

            double offset;
            if (string.Equals(param, "base", StringComparison.OrdinalIgnoreCase))
            {
                var parentLevel = level > 0 ? level - 1 : 0;
                offset = parentLevel * IndentStep + ExpanderWidth;
            }
            else if (string.Equals(param, "group", StringComparison.OrdinalIgnoreCase))
            {
                // Заголовок группы: сдвиг названия по уровню + ширина расширителя,
                // чтобы название начиналось сразу после кнопки разворота.
                offset = level * IndentStep + ExpanderWidth;
            }
            else
            {
                offset = level * IndentStep;
            }

            return new Thickness(offset, 0, 0, 0);
        }

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif