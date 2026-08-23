#if LINUX
using System.Collections.Generic;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Вспомогательный класс для построения векторных иконок (Path + StreamGeometry)
    /// из ресурсов Icons.axaml. Используется вместо эмодзи в UI, собираемом в коде.
    /// Цвет иконки подписывается на ресурс-кисть темы, поэтому иконки автоматически
    /// перекрашиваются при переключении светлой/тёмной схемы.
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Разрешает StreamGeometry по ключу из Icons.axaml. При отсутствии ключа
        /// возвращает геометрию «папки» как запасной вариант (как в IconKeyToGeometryConverter).
        /// </summary>
        public static Geometry? Geometry(string key)
        {
            if (Application.Current is { } app &&
                app.TryGetResource(key, null, out var res) && res is Geometry g)
                return g;

            if (Application.Current is { } app2 &&
                app2.TryGetResource("IconFolder", null, out var fallback) && fallback is Geometry fg)
                return fg;

            return null;
        }

        /// <summary>
        /// Создаёт Path-иконку по ключу геометрии. Кисть Fill подписывается на ресурс
        /// <paramref name="brushKey"/> (например "TextPrimaryColorBrush"), поэтому цвет
        /// следует теме. По умолчанию используется кисть основного текста.
        /// </summary>
        /// <param name="subscriptions">
        /// Необязательный приёмник подписки на ресурс-кисть. Нужен там, где иконка
        /// живёт меньше приложения и пересоздаётся: без освобождения подписки
        /// накапливались бы на каждую пересборку.
        /// </param>
        public static Avalonia.Controls.Shapes.Path MakeIcon(string key, double size = 16,
            string brushKey = "TextPrimaryColorBrush", ICollection<IDisposable>? subscriptions = null)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Width = size,
                Height = size,
                Data = Geometry(key),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (Application.Current is not null)
            {
                // Динамический ресурс вместо ручной подписки: его отслеживает сам
                // элемент, и при выходе из дерева отслеживание снимается. Прежняя
                // подписка жила у приложения и держала элемент сильной ссылкой,
                // то есть укореняла всё окно, пока её не освободят вручную.
                var binding = path.Bind(Avalonia.Controls.Shapes.Path.FillProperty,
                    new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey));
                subscriptions?.Add(binding);
            }
            else
            {
                path.Fill = Brushes.Black;
            }

            return path;
        }

        /// <summary>
        /// Строит содержимое кнопки «иконка + подпись» (горизонтальная панель).
        /// </summary>
        public static Control IconAndText(string iconKey, string text, double iconSize = 16,
            string brushKey = "TextPrimaryColorBrush", ICollection<IDisposable>? subscriptions = null)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MakeIcon(iconKey, iconSize, brushKey, subscriptions),
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }

    }
}
#endif