#if WINDOWS
using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Configuration_Management.Localization
{
    /// <summary>
    /// WPF-расширение разметки для локализации XAML: <c>{loc:Loc Key}</c>.
    ///
    /// Создаёт привязку к источнику локализации <see cref="LocalizationManager.Source"/>
    /// с конвертером <see cref="LocalizationValueConverter"/> (ключ передаётся через
    /// <see cref="Binding.ConverterParameter"/>). Такой способ не зависит от парсинга
    /// пути индексера и корректно работает с ключами, содержащими точки
    /// (например <c>Column.Name</c>). При смене языка источник уведомляет привязку,
    /// и текст обновляется автоматически.
    ///
    /// Примеры:
    /// <code>
    /// xmlns:loc="clr-namespace:Configuration_Management.Localization"
    /// Title="{loc:Loc Settings.Title}"
    /// <Button Content="{loc:Loc Common.Save}"/>
    /// </code>
    ///
    /// Внимание: на Linux (Avalonia) этот класс не компилируется (обёрнут в #if WINDOWS);
    /// там используется <c>{Binding Loc[Key]}</c> или <c>LocalizationManager.T("Key")</c>.
    /// </summary>
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            var binding = new Binding
            {
                // Привязываемся к самому источнику (без индексерного пути),
                // значение получаем через конвертер по ConverterParameter=Key.
                Source = LocalizationManager.Instance.Source,
                Mode = BindingMode.OneWay,
                Converter = LocalizationValueConverter.Instance,
                ConverterParameter = Key
            };

            // Возвращаем результат ProvideValue у Binding (BindingExpression),
            // а не сам объект Binding.
            return binding.ProvideValue(serviceProvider);
        }
    }
}
#endif