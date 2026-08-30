#if WINDOWS
using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Общий «стеклянный» стиль диалоговых окон (Windows/WPF): собственный WindowChrome
    /// без системных кнопок, полупрозрачная подложка цвета темы, системный acrylic/mica
    /// (или blur-behind), скруглённые углы DWM и собственные кнопки «свернуть/закрыть»
    /// (у закрытия — красное выделение). Стиль применяется централизованно ко всем окнам
    /// через <see cref="RegisterGlobalWindowStyling"/>, поэтому отдельные окна править
    /// не нужно: оформление навешивается единым кодом (главное окно, оформленное
    /// собственными средствами, пропускается).
    /// </summary>
    public static class WindowChromeHelper
    {
        /// <summary>Альфа полупрозрачной подложки «стекла» — 0xE8 (~91% непрозрачности).</summary>
        private const byte GlassBackgroundAlpha = 0xE8;

        // DWMWA_SYSTEMBACKDROP_TYPE (38): 2 = Mica, 3 = Acrylic.
        private const int DwmSystemBackdropType = 38;
        private const int DwmBackdropAcrylic = 3;
        private const int DwmBackdropMica = 2;

        // DWMWA_WINDOW_CORNER_PREFERENCE (33): 1 = не скруглять, 2 = скруглять.
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;
        private const int DwmCornerDoNotRound = 1;

        // DWM_BB_ENABLE для DwmEnableBlurBehindWindow.
        private const int DwmBbEnable = 0x00000001;

        // Помечает окно оформленным, чтобы не оборачивать содержимое повторно.
        private static readonly DependencyProperty AppliedProperty =
            DependencyProperty.RegisterAttached(
                "Applied", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));

        private static bool IsApplied(DependencyObject d) => (bool)d.GetValue(AppliedProperty);
        private static void MarkApplied(DependencyObject d) => d.SetValue(AppliedProperty, true);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind pBlurBehind);

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmBlurBehind
        {
            public int dwFlags;
            public int fEnable;
            public IntPtr hRgnBlur;
            public int fTransitionOnMaximized;
        }

        /// <summary>
        /// Регистрирует обработчик класса <c>Window.Loaded</c>, который применяет
        /// «стеклянный» стиль ко всем окнам приложения. Вызывается один раз из
        /// <c>App.OnStartup</c> до показа первого окна.
        /// </summary>
        public static void RegisterGlobalWindowStyling()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                Window.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var window = (Window)sender;
            // Главное окно уже оформлено собственными средствами (WindowChrome, свои кнопки).
            if (window is MainWindow)
                return;
            if (IsApplied(window))
                return;
            try
            {
                Apply(window);
            }
            catch
            {
                // Оформление не должно ломать показ диалога.
            }
        }

        /// <summary>
        /// Применяет «стеклянный» стиль к конкретному окну (идемпотентно): рамка без
        /// системных кнопок, полупрозрачная подложка, acrylic/mica, скруглённые углы
        /// и собственные кнопки окна поверх содержимого.
        /// </summary>
        public static void Apply(Window window)
        {
            if (IsApplied(window))
                return;
            MarkApplied(window);

            // Собственный каркас окна вместо системного (аналог WindowChrome главного окна):
            // UseAeroCaptionButtons=false прячет системные кнопки, CaptionHeight=0 отключает
            // системную зону заголовка, ResizeBorderThickness оставляет невидимую рамку ресайза.
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            // Оборачиваем содержимое в полосу заголовка с кнопками «свернуть/закрыть».
            if (window.Content is UIElement inner)
            {
                window.Content = BuildChrome(window, inner);
            }

            // Полупрозрачная подложка пересчитывается при смене темы (ThemeManager заменяет
            // словари в Application.Resources). Подписку снимаем при закрытии окна.
            var themeChangedHandler = new NotifyCollectionChangedEventHandler((_, _) =>
            {
                try { window.Dispatcher.BeginInvoke(new Action(() => ApplyGlassBackground(window))); }
                catch { /* не блокируем смену темы */ }
            });
            if (Application.Current?.Resources is { } resources)
                ((INotifyCollectionChanged)resources.MergedDictionaries).CollectionChanged += themeChangedHandler;
            window.Closed += (_, _) =>
            {
                if (Application.Current?.Resources is { } res)
                    ((INotifyCollectionChanged)res.MergedDictionaries).CollectionChanged -= themeChangedHandler;
            };

            ApplyGlassBackground(window);
            ApplySystemBackdrop(window);
            ApplyCornerPreference(window);

            // В развёрнутом виде скругление убираем и сбрасываем толщину стеклянной рамки.
            window.StateChanged += (_, _) =>
            {
                try
                {
                    ApplyCornerPreference(window);
                    UpdateGlassFrameForMaximize(window);
                }
                catch { /* некритично */ }
            };
        }

        /// <summary>
        /// Оборачивает содержимое окна в сетку: сверху полоса заголовка (перетаскивание +
        /// кнопки окна), ниже — исходное содержимое диалога.
        /// </summary>
        private static UIElement BuildChrome(Window window, UIElement inner)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = BuildTitleBar(window);
            Grid.SetRow(titleBar, 0);
            Grid.SetRow(inner, 1);

            grid.Children.Add(titleBar);
            grid.Children.Add(inner);
            return grid;
        }

        /// <summary>
        /// Полоса заголовка: слева заголовок окна, справа собственные кнопки управления
        /// (свернуть, закрыть с красным выделением). Пустое место полосы таскает окно.
        /// </summary>
        private static UIElement BuildTitleBar(Window window)
        {
            var bar = new Border
            {
                Height = 34,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top
            };
            bar.MouseLeftButtonDown += (_, _) =>
            {
                if (window.WindowState != WindowState.Maximized)
                {
                    try { window.DragMove(); }
                    catch { /* иногда DWM не даёт начать перетаскивание */ }
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = window.Title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            buttons.Children.Add(BuildButton(window, WindowControlKind.Minimize));
            buttons.Children.Add(BuildButton(window, WindowControlKind.Close));

            Grid.SetColumn(title, 0);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(title);
            grid.Children.Add(buttons);

            bar.Child = grid;
            return bar;
        }

        private enum WindowControlKind { Minimize, MaximizeRestore, Close }

        /// <summary>
        /// Кнопка управления окном. Стили WindowControlButton / WindowControlCloseButton
        /// (красное закрытие) лежат в общих ресурсах приложения (App.xaml). Кнопка
        /// «развернуть/восстановить» переключает значок и подсказку по состоянию окна.
        /// </summary>
        private static Button BuildButton(Window window, WindowControlKind kind)
        {
            bool isClose = kind == WindowControlKind.Close;
            bool isMaxRestore = kind == WindowControlKind.MaximizeRestore;

            var style = (Style?)window.TryFindResource(isClose ? "WindowControlCloseButton" : "WindowControlButton");

            var path = BuildGlyph(kind);
            var button = new Button { Style = style, Content = path };

            if (isClose)
            {
                button.ToolTip = LocalizationManager.T("Common.Close");
                // Значок следует за цветом Foreground кнопки (тема + состояние hover/pressed).
                path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                button.Click += (_, _) => window.Close();
            }
            else if (isMaxRestore)
            {
                path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                UpdateMaximizeRestoreGlyph(window, path, button);
                window.StateChanged += (_, _) => UpdateMaximizeRestoreGlyph(window, path, button);
                button.Click += (_, _) =>
                    window.WindowState = window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
            }
            else
            {
                button.ToolTip = LocalizationManager.T("Window.Minimize");
                path.SetBinding(Shape.FillProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                button.Click += (_, _) => window.WindowState = WindowState.Minimized;
            }
            return button;
        }

        /// <summary>Значок кнопки: минус для «свернуть», крест для «закрыть», контур квадрата для «развернуть».</summary>
        private static Path BuildGlyph(WindowControlKind kind)
        {
            switch (kind)
            {
                case WindowControlKind.Close:
                    return new Path
                    {
                        Width = 13,
                        Height = 13,
                        Stretch = Stretch.Uniform,
                        StrokeThickness = 1.2,
                        Data = Geometry.Parse("M1,1 L12,12 M12,1 L1,12")
                    };
                case WindowControlKind.MaximizeRestore:
                    return new Path
                    {
                        Width = 12,
                        Height = 12,
                        Stretch = Stretch.Uniform,
                        StrokeThickness = 1.2,
                        Data = Geometry.Parse("M1,1 L12,1 L12,12 L1,12 Z")
                    };
                default: // Minimize
                    return new Path
                    {
                        Width = 11,
                        Height = 11,
                        Stretch = Stretch.Uniform,
                        Data = Geometry.Parse("M0,5.5 L11,5.5 L11,6.5 L0,6.5 Z")
                    };
            }
        }

        /// <summary>
        /// Обновляет значок и подсказку кнопки «развернуть/восстановить»: в развёрнутом
        /// состоянии показываются два наложенных прямоугольника и подсказка «Восстановить».
        /// </summary>
        private static void UpdateMaximizeRestoreGlyph(Window window, Path path, Button button)
        {
            bool maximized = window.WindowState == WindowState.Maximized;
            path.Data = Geometry.Parse(maximized
                ? "M4,1 L12,1 L12,9 L4,9 Z M1,4 L9,4 L9,12 L1,12 Z"
                : "M1,1 L12,1 L12,12 L1,12 Z");
            button.ToolTip = maximized
                ? LocalizationManager.T("Window.Restore")
                : LocalizationManager.T("Window.Maximize");
        }

        /// <summary>
        /// Полупрозрачная подложка окна: берём текущий цвет темы (ContentBackgroundBrush,
        /// обновляется для светлой/тёмной темы и любой цветовой схемы) и пересчитываем его
        /// с альфой ~0xE8. Именно эта подложка остаётся основным фоном, а размытие DWM
        /// добавляет эффект «стекла» сквозь прозрачные области.
        /// </summary>
        private static void ApplyGlassBackground(Window window)
        {
            if (window.TryFindResource("ContentBackgroundBrush") is SolidColorBrush brush)
            {
                var c = brush.Color;
                window.Background = new SolidColorBrush(Color.FromArgb(GlassBackgroundAlpha, c.R, c.G, c.B));
            }
        }

        /// <summary>
        /// Включает системный размытый фон: на Windows 11 — acrylic, при недоступности —
        /// mica; на старых Windows — blur-behind. Если ничего не удалось — полупрозрачный
        /// фон без размытия.
        /// </summary>
        private static void ApplySystemBackdrop(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    int backdrop = DwmBackdropAcrylic;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;

                    backdrop = DwmBackdropMica;
                    if (DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                        return;
                }

                var bb = new DwmBlurBehind { dwFlags = DwmBbEnable, fEnable = 1 };
                DwmEnableBlurBehindWindow(hwnd, ref bb);
            }
            catch
            {
                // Не блокируем: останется полупрозрачный фон без размытия.
            }
        }

        /// <summary>
        /// Скруглённые углы окна на уровне DWM (Windows 11). В развёрнутом состоянии углы
        /// обнуляются, чтобы в углах окна не просвечивал рабочий стол.
        /// </summary>
        private static void ApplyCornerPreference(Window window)
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22000)
                    return;

                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int pref = window.WindowState == WindowState.Maximized ? DwmCornerDoNotRound : DwmCornerRound;
                DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref pref, sizeof(int));
            }
            catch
            {
                // Игнорируем: скругление — некритичное улучшение.
            }
        }

        /// <summary>
        /// При максимизации возвращаем толщину стеклянной рамки к 0 (известное обходное
        /// решение для WindowChrome с GlassFrameThickness=-1 и панели задач Windows 11).
        /// </summary>
        private static void UpdateGlassFrameForMaximize(Window window)
        {
            try
            {
                var chrome = WindowChrome.GetWindowChrome(window);
                if (chrome is null)
                    return;
                chrome.GlassFrameThickness = window.WindowState == WindowState.Maximized
                    ? new Thickness(0)
                    : new Thickness(-1);
            }
            catch
            {
                // Игнорируем: если рамку не удалось поправить, окно всё равно работает.
            }
        }
    }
}
#endif