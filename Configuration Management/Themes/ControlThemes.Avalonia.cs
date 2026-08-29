#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Применение именованных тем контролов из Themes/Controls.axaml к элементам,
    /// которые собираются в коде. Аналог <c>Style="{DynamicResource ИмяСтиля}"</c>
    /// из разметки WPF: ключи те же самые.
    /// </summary>
    public static class ControlThemes
    {
        /// <summary>Основная кнопка: акцентная заливка, скругление 6, высота от 36.</summary>
        public const string ModernButton = "ModernButton";

        /// <summary>Вторичная кнопка: в светлой теме мягкая заливка, в тёмной карточка в контуре.</summary>
        public const string SecondaryButton = "SecondaryButton";

        /// <summary>Кнопка подтверждения нижней панели диалога: зелёная заливка.</summary>
        public const string DialogConfirmButton = "DialogConfirmButton";

        /// <summary>Кнопка отмены нижней панели диалога: красный контур.</summary>
        public const string DialogCancelButton = "DialogCancelButton";

        /// <summary>Набор вертикальных вкладок окна подключения.</summary>
        public const string ConnTabControl = "ConnTabControl";

        /// <summary>Вертикальная вкладка: полоса акцента справа у выбранной.</summary>
        public const string ConnTabItem = "ConnTabItem";

        /// <summary>Карточка варианта выбора: рамка вокруг переключателя с подписью и пояснением.</summary>
        public const string OptionCard = "OptionCard";

        /// <summary>Поле ввода: карточный фон, скругление 6, акцентный контур при наведении и фокусе.</summary>
        public const string ModernTextBox = "ModernTextBox";

        /// <summary>Колонка вертикальных вкладок окна настроек.</summary>
        public const string SettingsTabControl = "SettingsTabControl";

        /// <summary>Вертикальная вкладка окна настроек: та же, что в окне подключения, шириной 235.</summary>
        public const string SettingsTabItem = "SettingsTabItem";

        /// <summary>Переключатель настроек: дорожка 40 на 22 с кружком 16.</summary>
        public const string SettingsToggle = "SettingsToggle";

        /// <summary>Плитка выбора значка группы: тёмная в обеих темах, как у автора.</summary>
        public const string IconPickButton = "IconPickButton";

        /// <summary>Пилюля видимости колонки: та же дорожка без подписи.</summary>
        public const string ColumnVisibilitySwitch = "ColumnVisibilitySwitch";

        /// <summary>Полоса горизонтальных вкладок раздела: равная ширина, общая линия снизу.</summary>
        public const string SettingsSubTabControl = "SettingsSubTabControl";

        /// <summary>Горизонтальная вкладка раздела: акцентное подчёркивание у выбранной.</summary>
        public const string SettingsSubTabItem = "SettingsSubTabItem";

        /// <summary>Поле пароля: то же поле, но с контуром 1.5, скруглением 8 и отступом 10 на 6.</summary>
        public const string ModernPasswordBox = "ModernPasswordBox";

        /// <summary>
        /// Ставит элементу тему контрола по ключу словаря и возвращает сам элемент,
        /// чтобы вызов можно было встроить в инициализацию.
        /// </summary>
        /// <param name="control">Элемент, которому назначается тема.</param>
        /// <param name="themeKey">Ключ ресурса, например <see cref="ModernButton"/>.</param>
        /// <remarks>
        /// Неизвестный ключ оставляет элемент со штатной темой Fluent и ничего не сообщает:
        /// ресурсы ищутся по значению, а не по типу, и промах виден только глазами.
        /// Поэтому ключи заданы здесь константами, а не строками по месту вызова.
        /// </remarks>
        public static T Styled<T>(this T control, string themeKey) where T : StyledElement
        {
            if (Application.Current is { } app
                && app.TryFindResource(themeKey, out var found)
                && found is ControlTheme theme)
            {
                control.Theme = theme;
            }

            return control;
        }
    }
}
#endif
