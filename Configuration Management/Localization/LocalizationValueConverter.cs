#if WINDOWS
using System;
using System.Globalization;
using System.Windows.Data;

namespace Configuration_Management.Localization
{
    /// <summary>
    /// WPF-конвертер для локализации: получает ключ через
    /// <see cref="IValueConverter.Convert"/> и возвращает перевод текущего языка.
    ///
    /// Используется расширением <see cref="LocExtension"/>. Поскольку конвертер
    /// читает текущий язык напрямую через <see cref="LocalizationManager.Instance"/>,
    /// а привязка пересчитывается по уведомлению <c>PropertyChanged</c> источника,
    /// текст автоматически обновляется при смене языка.
    /// </summary>
    public sealed class LocalizationValueConverter : IValueConverter
    {
        public static readonly LocalizationValueConverter Instance = new();

        private LocalizationValueConverter() { }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = parameter as string;
            return LocalizationManager.Instance.Translate(key ?? string.Empty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
#endif