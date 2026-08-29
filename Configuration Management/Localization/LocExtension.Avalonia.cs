#if LINUX
using System;

namespace Configuration_Management.Localization
{
    /// <summary>
    /// Расширение разметки Avalonia для локализации: <c>{loc:Loc Ключ}</c>.
    /// Отдаёт перевод текущего языка один раз, при построении разметки.
    ///
    /// Этого достаточно, потому что окна в Linux-версии строятся заново:
    /// диалоги создаются на каждое открытие, а главное окно пересобирается
    /// при смене языка (MainWindow.RebuildAfterLanguageChange). Живая привязка,
    /// как у WPF-версии LocExtension, понадобится, только если появится окно,
    /// которое переживает смену языка без пересборки.
    ///
    /// Использование:
    /// <code>
    /// xmlns:loc="clr-namespace:Configuration_Management.Localization"
    /// Title="{loc:Loc Settings.Title}"
    /// </code>
    /// </summary>
    public sealed class LocExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        /// <summary>Ключ перевода из файлов Localization/Languages.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Отдаёт перевод по ключу.</summary>
        public object ProvideValue(IServiceProvider serviceProvider) => LocalizationManager.T(Key);
    }
}
#endif
