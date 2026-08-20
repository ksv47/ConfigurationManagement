#if LINUX
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Configuration_Management.Converters
{
    /// <summary>
    /// Avalonia-версия: true, если значение не равно null (параметр «invert» инвертирует).
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            var isNotNull = value != null;
            return invert ? !isNotNull : isNotNull;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
#endif