#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Вспомогательный класс для привязки кистей-ресурсов темы к свойствам элементов,
    /// собираемых в коде (без XAML). Ресурсы-кисти создаются ThemeManager.ApplyScheme как
    /// Application.Resources (ключи вида <c>TextPrimaryColorBrush</c>, <c>CardBackgroundColorBrush</c>
    /// и т.п.). Подписка через GetResourceObservable обеспечивает автоматическую перекраску
    /// элемента при переключении светлой/тёмной схемы (аналогично IconHelper).
    /// </summary>
    public static class ThemeBrushes
    {
        /// <summary>
        /// Подписывает свойство-кисть целевого элемента на ресурс-кисть темы.
        /// При каждом обновлении ресурса (смена темы/схемы) свойство обновляется.
        /// </summary>
        /// <param name="target">Элемент, у которого меняется кисть.</param>
        /// <param name="property">Свойство типа IBrush (Background/Foreground/BorderBrush и т.п.).</param>
        /// <param name="brushKey">Ключ ресурса-кисти темы (например "CardBackgroundColorBrush").</param>
        /// <returns>
        /// Подписка на ресурс. Вызывающий может её освободить, если элемент
        /// живёт меньше приложения: наблюдатель держит сильную ссылку на элемент,
        /// а сам ресурс живёт до конца процесса.
        /// </returns>
        public static IDisposable? Bind(AvaloniaObject target, AvaloniaProperty property, string brushKey)
        {
            var app = Application.Current;
            if (app is null)
                return null;
            return target.Bind(property, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey));
        }

    }
}
#endif