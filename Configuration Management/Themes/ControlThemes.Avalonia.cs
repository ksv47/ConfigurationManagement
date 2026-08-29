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

        /// <summary>Выпадающий список: карточный фон, скругление 8, акцентный контур.</summary>
        public const string ModernComboBox = "ModernComboBox";

        /// <summary>Флажок в стиле Material Design: квадрат 18 со скруглением 3.</summary>
        public const string ModernMaterialCheckBox = "ModernMaterialCheckBox";

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
