using System.Globalization;
using System.Windows.Data;

namespace Configuration_Management.Converters;

/// <summary>
/// IMultiValueConverter, форматирующий несколько значений привязки в строку по шаблону,
/// заданному через ConverterParameter (например "{0}: {1}" или "{0} ({1})").
///
/// Используется для Button.ToolTip и других target-свойств типа object, где WPF не
/// применяет MultiBinding.StringFormat без явного IMultiValueConverter — там возникает
/// ошибка "Cannot set MultiBinding because MultiValueConverter must be specified".
/// </summary>
public class MultiFormatConverter : IMultiValueConverter
{
    public static readonly MultiFormatConverter Instance = new();

    private MultiFormatConverter() { }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var format = parameter as string;
        if (string.IsNullOrWhiteSpace(format))
            return values.Length == 1 ? values[0]! : string.Join(" ", values);

        try
        {
            return string.Format(culture, format, values);
        }
        catch (FormatException)
        {
            // Некорректный шаблон — возвращаем его как есть, чтобы не ронять привязку.
            return format;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}