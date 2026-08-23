#if LINUX
using Avalonia;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Привязка кистей-ресурсов темы к свойствам элементов, собираемых в коде
    /// (без XAML). Сами ресурсы кладёт ThemeManager.ApplyScheme в
    /// Application.Resources под ключами вида <c>TextPrimaryColorBrush</c>
    /// и <c>CardBackgroundColorBrush</c>.
    /// </summary>
    public static class ThemeBrushes
    {
        /// <summary>
        /// Привязывает свойство-кисть элемента к ресурсу темы. При смене темы или
        /// цветовой схемы значение обновляется само.
        /// </summary>
        /// <param name="target">Элемент, у которого меняется кисть.</param>
        /// <param name="property">Свойство типа IBrush (Background, Foreground, BorderBrush).</param>
        /// <param name="brushKey">Ключ ресурса-кисти темы, например "CardBackgroundColorBrush".</param>
        /// <remarks>
        /// Привязку отслеживает сам элемент, освобождать её не нужно и нельзя:
        /// освобождение снимает окраску, а не подписку. Ресурс ищется по цепочке
        /// логических родителей до окна, поэтому элемент, который так и не попал
        /// в содержимое окна, останется неокрашенным молча. Ловушки такого рода:
        /// ToolTip.Tip и MenuItem.Icon у неоткрытого меню.
        /// </remarks>
        public static void Bind(StyledElement target, AvaloniaProperty property, string brushKey)
            => target.Bind(property, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey));
    }
}
#endif
