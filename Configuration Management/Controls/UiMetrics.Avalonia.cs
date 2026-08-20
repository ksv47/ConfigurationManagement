#if LINUX
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Единые метрики UI и вспомогательные методы полировки (Avalonia/Linux).
    /// Элементы, собираемые в коде, используют эти константы, чтобы отступы и скругления
    /// выглядели цельно, а тени и плавные переходы брались из ресурсов темы (без жёстких цветов).
    /// Всё ограничено #if LINUX и не влияет на Windows (WPF) сборку.
    /// </summary>
    public static class UiMetrics
    {
        // ---- Скругления ----
        /// <summary>Крупные карточки-секции (правый экран, empty-state).</summary>
        public const double RadiusXl = 12;
        /// <summary>Управляющие элементы (кнопки, поле поиска, сегмент-контейнер).</summary>
        public const double RadiusLg = 10;
        /// <summary>Карточки строк и иконки-«аватары».</summary>
        public const double RadiusMd = 8;
        /// <summary>Мелкие элементы (заголовки групп, сегменты).</summary>
        public const double RadiusSm = 6;

        // ---- Отступы ----
        /// <summary>Внутренний отступ секций-карточек правой панели.</summary>
        public const double PaddingSection = 14;
        /// <summary>Внутренний отступ управляющих элементов.</summary>
        public const double PaddingControl = 10;
        /// <summary>Стандартный вертикальный промежуток между строками внутри секции.</summary>
        public const double Gap = 8;

        // ---- Анимации ----
        /// <summary>Длительность плавного перехода цвета/прозрачности.</summary>
        public static readonly TimeSpan TransitionFast = TimeSpan.FromMilliseconds(110);

        /// <summary>
        /// Добавляет мягкую тень (BoxShadow) к элементу. Цвет тени выводится из ресурса
        /// темы «BorderColorBrush» (перекрашивается при смене схемы) — без жёстких цветов.
        /// </summary>
        public static void AddSoftShadow(Border target)
        {
            if (Application.Current is not { } app)
                return;
            app.GetResourceObservable("BorderColorBrush").Subscribe(new ShadowObserver(target));
        }

        /// <summary>Добавляет плавный переход цвета фона и/или границы элемента.</summary>
        public static void AddBrushTransition(Border target, bool background = true, bool border = true)
        {
            if (background)
                target.Transitions.Add(new BrushTransition
                {
                    Property = Border.BackgroundProperty,
                    Duration = TransitionFast
                });
            if (border)
                target.Transitions.Add(new BrushTransition
                {
                    Property = Border.BorderBrushProperty,
                    Duration = TransitionFast
                });
        }

        /// <summary>Добавляет плавное появление/исчезание по прозрачности.</summary>
        public static void AddOpacityTransition(Visual target, double durationMs = 180)
        {
            target.Transitions.Add(new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            });
        }

        /// <summary>
        /// Наблюдатель, который по значению ресурса-кисти строит мягкую полупрозрачную тень
        /// и применяет её к целевому Border.
        /// </summary>
        private sealed class ShadowObserver : IObserver<object?>
        {
            private readonly Border _target;

            public ShadowObserver(Border target) => _target = target;

            public void OnCompleted() { }
            public void OnError(Exception error) { }

            public void OnNext(object? value)
            {
                if (value is not ISolidColorBrush solid)
                    return;
                var c = solid.Color;
                // Полупрозрачный вариант цвета границы — мягкая тень для обеих тем.
                var shadowColor = new Color((byte)(c.A * 0.26), c.R, c.G, c.B);
                _target.BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = 3,
                    Blur = 14,
                    Spread = 0,
                    Color = shadowColor
                });
            }
        }
    }
}
#endif