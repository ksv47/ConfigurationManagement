#if WINDOWS
using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Configuration_Management.Localization
{
    /// <summary>
    /// WPF-расширение разметки для локализации XAML: <c>{loc:Loc Key}</c>.
    ///
    /// Создаёт КОНВЕРТЕРНУЮ привязку к <see cref="LocalizationSource"/>:
    /// <c>Source = LocalizationManager.Instance.Source</c>,
    /// <c>Converter = LocalizationValueConverter.Instance</c>,
    /// <c>ConverterParameter = Key</c>. Конвертер читает перевод текущего языка
    /// напрямую через <see cref="LocalizationManager.Instance"/>, поэтому при
    /// загрузке элемент сразу получает корректный текст (а не сырой ключ).
    ///
    /// Привязка WPF без пути НЕ подписывается на INotifyPropertyChanged источника,
    /// поэтому после создания привязка дополнительно регистрируется в источнике
    /// (<see cref="LocalizationSource.RegisterForUpdate"/>). При смене языка
    /// <see cref="LocalizationSource.NotifyAll"/> вызывает UpdateTarget() у всех
    /// зарегистрированных выражений, и текст обновляется без перезапуска.
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
                Source = LocalizationManager.Instance.Source,
                Mode = BindingMode.OneWay,
                Converter = LocalizationValueConverter.Instance,
                ConverterParameter = Key
            };

            // Возвращаем результат ProvideValue у Binding (BindingExpression),
            // а не сам объект Binding.
            var value = binding.ProvideValue(serviceProvider);

            // Динамическое обновление: привязка без пути не реагирует на
            // PropertyChanged источника, поэтому регистрируем выражение в источнике,
            // который вызовет UpdateTarget() при NotifyAll() (смене языка).
            if (value is BindingExpression expression)
            {
                LocalizationManager.Instance.Source.RegisterForUpdate(
                    expression,
                    static target => ((BindingExpression)target).UpdateTarget());
            }

            return value;
        }
    }
}
#endif