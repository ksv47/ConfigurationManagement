#if LINUX
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: возвращает массив значений MultiBinding как единый объект.
    /// </summary>
    public class MultiValueToArrayConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
            => values;

        public IList<object?>? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
#endif