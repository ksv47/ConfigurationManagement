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

        /// <summary>
        /// Радиус скругления верхних углов полосы заголовка (в DIP), совпадает с радиусом
        /// скругления углов окна, который DWM применяет через DwmWindowCornerPreference
        /// на Windows 11. Если не совпадать с ним, прямоугольная акцентная полоса выходит
        /// за скруглённый клип окна, и в углах шапки просвечивает стеклянная подложка.
        /// </summary>
        private const double DwmCornerRadius = 8;

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

        // GWL_STYLE и флаги стилей окна для удаления системных кнопок заголовка.
        private const int GwlStyle = -16;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // Флаги SetWindowPos для принудительной перерисовки рамки после смены стиля.
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

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
                // Сначала отсоединяем текущее содержимое окна, иначе WPF бросает
                // InvalidOperationException «элемент уже является логическим дочерним для
                // другого элемента» при добавлении inner в новую сетку BuildChrome.
                window.Content = null;
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
            // Убираем системные кнопки заголовка (свернуть/развернуть/закрыть), которые DWM
            // может рисовать «призрачными» поверх расширенной стеклянной рамки.
            RemoveSystemCaptionButtons(window);

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
        /// Снимает флаги стиля окна WS_SYSMENU / WS_MINIMIZEBOX / WS_MAXIMIZEBOX, чтобы
        /// системные кнопки заголовка (свернуть/развернуть/закрыть) не рисовались DWM
        /// «призрачными» поверх расширенной стеклянной рамки (GlassFrameThickness=-1).
        /// WindowChrome с UseAeroCaptionButtons=false сам их скрывает, но на части систем
        /// фон остаётся, поэтому продублируем на уровне Win32.
        /// </summary>
        private static void RemoveSystemCaptionButtons(Window window)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int style = GetWindowLong(hwnd, GwlStyle);
                style &= ~(WsSysMenu | WsMinimizeBox | WsMaximizeBox);
                SetWindowLong(hwnd, GwlStyle, style);

                // Принудительно перерисовываем рамку, чтобы изменения стиля применились сразу.
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
            catch
            {
                // Не критично: останется штатное поведение WindowChrome.
            }
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
                // Полоса заголовка диалога заливается акцентным цветом темы на всю ширину.
                // DynamicResource через SetResourceReference: при смене темы/схемы цвет обновляется сам.
                VerticalAlignment = VerticalAlignment.Top,
                // Скругляем два верхних угла с тем же радиусом, что и окно (DWM, Windows 11).
                // Иначе прямоугольная полоса не доходит до скруглённых углов окна и в углах
                // шапки просвечивает стеклянная подложка/рабочий стол — «недозалитые» углы.
                CornerRadius = new CornerRadius(DwmCornerRadius, DwmCornerRadius, 0, 0)
            };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
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
            // Текст заголовка читаемым цветом «на акценте» (ButtonTextBrush) поверх акцентной полосы.
            title.SetResourceReference(TextBlock.ForegroundProperty, "ButtonTextBrush");

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            // В диалоговых окнах показываем только кнопку «закрыть» (без «свернуть»
            // и «развернуть») — это стандартная схема диалогов. Свои кнопки
            // «свернуть/развернуть/закрыть» есть только у главного окна.
            buttons.Children.Add(BuildButton(window));

            Grid.SetColumn(title, 0);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(title);
            grid.Children.Add(buttons);

            bar.Child = grid;
            return bar;
        }

        /// <summary>
        /// Кнопка «закрыть» окна. Стиль WindowControlCloseButton (красное выделение)
        /// лежит в общих ресурсах приложения (App.xaml). В диалоговых окнах показывается
        /// только эта кнопка — без «свернуть» и «развернуть».
        /// </summary>
        private static Button BuildButton(Window window)
        {
            // Стиль «на акценте»: базовый значок ButtonTextBrush (читается на акцентной
            // полосе заголовка), красное hover-выделение и белый значок наследуются из
            // базового шаблона. Задаём цвет сеттером стиля, а не локальным значением —
            // иначе локальное значение (приоритет выше шаблонного триггера) «ломало» бы
            // белое выделение значка при наведении.
            var style = (Style?)window.TryFindResource("WindowControlCloseButtonOnAccent")
                        ?? (Style?)window.TryFindResource("WindowControlCloseButton");

            var path = new Path
            {
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.2,
                Data = Geometry.Parse("M1,1 L12,12 M12,1 L1,12")
            };
            var button = new Button
            {
                Style = style,
                Content = path,
                ToolTip = LocalizationManager.T("Common.Close")
            };
            // Значок следует за цветом Foreground кнопки (тема + состояние hover/pressed).
            path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground)) { Source = button });
            button.Click += (_, _) => window.Close();
            return button;
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