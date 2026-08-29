#if LINUX
using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// База модальных диалоговых окон (Avalonia/Linux). Предоставляет синхронный показ
    /// модального окна с эмуляцией <c>DialogResult</c>, как в <see cref="AvaloniaDialogService"/>:
    /// модальный показ крутит вложенный цикл сообщений <see cref="Dispatcher.PushFrame"/>,
    /// пока окно открыто. Это позволяет вызывать диалоги синхронно из команд ViewModel,
    /// не блокируя UI-поток.
    /// </summary>
    public abstract class ModalWindowBase : Window
    {
        /// <summary>
        /// Задаёт окну фон темы. В разметке WPF его ставит каждое окно
        /// (<c>Background="{DynamicResource ContentBackgroundBrush}"</c>),
        /// здесь это одно место на все диалоги. Окну, которому нужен свой фон,
        /// присвоения мало: привязка держит то же местное значение и вернёт своё
        /// при следующей смене темы или схемы, поэтому её надо снимать
        /// (<c>ClearValue(BackgroundProperty)</c>) до присвоения.
        /// </summary>
        protected ModalWindowBase()
        {
            Themes.ThemeBrushes.Bind(this, BackgroundProperty, "ContentBackgroundBrush");
        }

        /// <summary>
        /// Источник локализации для привязок XAML: <c>{Binding Loc[Key]}</c>.
        /// При смене языка открытые окна автоматически обновляют текст.
        /// </summary>
        public LocalizationSource Loc => LocalizationManager.Instance.Source;

        // Поля для живого обновления текста кнопок «Отмена»/«ОК» при смене языка.
        private TextBlock? _lastCancelText;
        private TextBlock? _lastOkText;
        private string _lastOkRaw = "";
        // Ключ подписи подтверждения, когда кнопку строили по ключу, а не по
        // готовой строке: только так подпись переводится при смене языка.
        private string? _lastOkKey;
        private bool _languageSubscribed;

        /// <summary>Результат диалога: true — подтверждён (ОК), false — отменён.</summary>
        public bool DialogResult { get; protected set; }

        protected override void OnClosed(EventArgs e)
        {
            // Событие живёт у синглтона локализации, а обработчик это метод
            // экземпляра: без отписки закрытое окно оставалось бы достижимым.
            // Кисти темы отписывать не нужно, их привязку держит сам элемент.
            if (_languageSubscribed)
            {
                LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
                _languageSubscribed = false;
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// Показывает окно модально (синхронно) относительно владельца и блокирует
        /// вызывающий поток до закрытия окна.
        /// </summary>
        /// <param name="owner">Окно-владелец (например, главное). Может быть null.</param>
        /// <returns>True, если пользователь подтвердил действие (DialogResult == true).</returns>
        public bool ShowDialogSync(Window? owner = null)
        {
            var frame = new DispatcherFrame();
            Closed += (_, _) => frame.Continue = false;

            if (owner is not null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _ = ShowDialog(owner);
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Show();
            }

            Dispatcher.UIThread.PushFrame(frame);

            return DialogResult;
        }

        /// <summary>
        /// Показывает окно модально (синхронно) без владельца.
        /// </summary>
        public bool ShowDialogSync() => ShowDialogSync(null);

        /// <summary>
        /// Строит стандартный ряд кнопок «Отмена»/«ОК» с иконками и обработчиками.
        /// При нажатии «ОК» сначала выполняется <paramref name="onOk"/> (если задан),
        /// затем устанавливается <see cref="DialogResult"/> и окно закрывается.
        /// </summary>
        /// <param name="okText">Текст кнопки подтверждения. Если пуст/null — используется локализованный текст <c>Common.Ok</c>.</param>
        /// <param name="okWidth">Ширина кнопки подтверждения.</param>
        /// <param name="onOk">Необязательный обратный вызов при подтверждении (например, сохранить результат).</param>

        /// <summary>
        /// Строит правую панель кнопок «Отмена» и подтверждение, как в разметке WPF:
        /// отмена мягкой заливкой, подтверждение акцентом, зазор 8.
        /// </summary>
        /// <param name="okText">Подпись кнопки подтверждения; пусто означает «ОК».</param>
        /// <param name="okWidth">Ширина кнопки подтверждения.</param>
        /// <param name="onOk">Что выполнить перед закрытием с положительным результатом.</param>
        /// <param name="cancelWidth">Ширина кнопки отмены.</param>
        /// <param name="okIconKey">Ключ значка кнопки подтверждения.</param>
        /// <param name="okTextKey">
        /// Ключ подписи подтверждения вместо готовой строки: только так подпись
        /// переводится при смене языка.
        /// </param>
        protected StackPanel BuildButtons(string? okText = null, double okWidth = 130, Action? onOk = null,
            double cancelWidth = 110, string okIconKey = "IconOk", string? okTextKey = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            panel.Children.Add(BuildCancelButton(cancelWidth));
            panel.Children.Add(BuildConfirmButton(okText, okWidth, onOk, okIconKey: okIconKey, okTextKey: okTextKey));
            return panel;
        }

        /// <summary>
        /// Кнопка отмены. По умолчанию это основная кнопка с мягкой заливкой и
        /// основным цветом текста, как её задаёт разметка WPF в диалогах ввода;
        /// <paramref name="secondary"/> переключает её на стиль вторичной кнопки.
        /// </summary>
        /// <param name="width">Ширина кнопки.</param>
        /// <param name="secondary">Оформлять стилем вторичной кнопки.</param>
        /// <param name="minimumWidth">Ширина задаётся как минимальная, а не жёсткая.</param>
        protected Button BuildCancelButton(double width = 110, bool secondary = false, bool minimumWidth = false)
        {
            var caption = new TextBlock
            {
                Text = LocalizationManager.T("Common.Cancel"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        IconHelper.MakeIcon("IconClose", 14, secondary ? "SecondaryButtonTextBrush" : "TextPrimaryBrush"),
                        caption
                    }
                },
                IsCancel = true
            };

            button.Styled(secondary ? ControlThemes.SecondaryButton : ControlThemes.ModernButton);
            if (!secondary)
            {
                Themes.ThemeBrushes.Bind(button, BackgroundProperty, "ItemHoverBrush");
                Themes.ThemeBrushes.Bind(button, ForegroundProperty, "TextPrimaryBrush");
            }

            SetButtonWidth(button, width, minimumWidth);
            button.Click += (_, _) => Close();

            _lastCancelText = caption;
            EnsureLanguageSubscription();
            return button;
        }

        /// <summary>
        /// Кнопка подтверждения: акцентная заливка, значок и подпись, закрытие
        /// с положительным результатом.
        /// </summary>
        /// <param name="okText">Подпись; пусто означает «ОК».</param>
        /// <param name="width">Ширина кнопки.</param>
        /// <param name="onOk">Что выполнить перед закрытием.</param>
        /// <param name="minimumWidth">Ширина задаётся как минимальная, а не жёсткая.</param>
        /// <param name="okIconKey">Ключ значка.</param>
        /// <param name="okTextKey">Ключ подписи вместо готовой строки.</param>
        protected Button BuildConfirmButton(string? okText, double width = 130, Action? onOk = null,
            bool minimumWidth = false, string okIconKey = "IconOk", string? okTextKey = null)
        {
            var caption = new TextBlock
            {
                Text = okTextKey is { Length: > 0 } captionKey
                    ? LocalizationManager.T(captionKey)
                    : ResolveOkText(okText),
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        IconHelper.MakeIcon(okIconKey, 14, "ButtonTextBrush"),
                        caption
                    }
                },
                IsDefault = true
            };

            button.Styled(ControlThemes.ModernButton);
            SetButtonWidth(button, width, minimumWidth);
            button.Click += (_, _) =>
            {
                onOk?.Invoke();
                DialogResult = true;
                Close();
            };

            _lastOkText = caption;
            _lastOkRaw = okText ?? "";
            _lastOkKey = okTextKey;
            EnsureLanguageSubscription();
            return button;
        }

        /// <summary>
        /// Кнопка подтверждения нижней панели диалога: зелёная заливка, белые
        /// значок и подпись. Так эта кнопка задана в разметке WPF у десяти окон.
        /// </summary>
        /// <param name="textKey">Ключ локализации подписи.</param>
        /// <param name="iconKey">Ключ значка.</param>
        /// <param name="width">Ширина кнопки.</param>
        /// <param name="onOk">Что выполнить перед закрытием с положительным результатом.</param>
        /// <param name="height">Высота кнопки.</param>
        /// <param name="iconSize">Размер значка.</param>
        /// <param name="iconGap">Зазор между значком и подписью.</param>
        /// <param name="closeOnClick">
        /// Закрывать окно по нажатию. Ложь нужна окнам, где кнопка сначала
        /// проверяет введённое и закрывает окно сама.
        /// </param>
        protected Button BuildConfirmActionButton(string textKey, string iconKey, double width,
            Action? onOk = null, double height = 36, double iconSize = 16, double iconGap = 6,
            bool closeOnClick = true)
        {
            var caption = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = iconGap,
                    Children =
                    {
                        IconHelper.MakeIcon(iconKey, iconSize, Brushes.White),
                        caption
                    }
                },
                Width = width,
                Height = height,
                IsDefault = true
            };

            button.Styled(ControlThemes.DialogConfirmButton);
            if (closeOnClick)
            {
                button.Click += (_, _) =>
                {
                    onOk?.Invoke();
                    DialogResult = true;
                    Close();
                };
            }
            else if (onOk is not null)
            {
                button.Click += (_, _) => onOk();
            }

            _lastOkText = caption;
            _lastOkKey = textKey;
            EnsureLanguageSubscription();
            return button;
        }

        /// <summary>
        /// Кнопка отмены нижней панели диалога: прозрачная заливка, красный контур,
        /// красные значок и подпись, как в разметке WPF.
        /// </summary>
        /// <param name="width">Ширина кнопки.</param>
        /// <param name="height">Высота кнопки.</param>
        /// <param name="iconSize">Размер значка.</param>
        /// <param name="iconGap">Зазор между значком и подписью.</param>
        protected Button BuildCancelActionButton(double width = 140, double height = 36,
            double iconSize = 16, double iconGap = 6)
        {
            var caption = new TextBlock
            {
                Text = LocalizationManager.T("Common.Cancel"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = iconGap,
                    Children =
                    {
                        IconHelper.MakeIcon("IconClose", iconSize, DangerBrush),
                        caption
                    }
                },
                Width = width,
                Height = height,
                IsCancel = true
            };

            button.Styled(ControlThemes.DialogCancelButton);
            button.Click += (_, _) => Close();

            _lastCancelText = caption;
            EnsureLanguageSubscription();
            return button;
        }

        /// <summary>Красный цвет отмены и удаления: в разметке WPF он задан числом.</summary>
        protected static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#EF4444"));

        /// <summary>
        /// Берёт на себя перевод подписи кнопки, которую окно собрало само.
        /// Без этого при смене языка переводится только «Отмена», и панель
        /// остаётся двуязычной.
        /// </summary>
        /// <param name="caption">Подпись кнопки подтверждения.</param>
        /// <param name="textKey">Ключ локализации этой подписи.</param>
        protected void RegisterConfirmCaption(TextBlock caption, string textKey)
        {
            _lastOkText = caption;
            _lastOkKey = textKey;
            EnsureLanguageSubscription();
        }

        private static void SetButtonWidth(Button button, double width, bool minimumWidth)
        {
            if (minimumWidth)
                button.MinWidth = width;
            else
                button.Width = width;
        }

        /// <summary>
        /// Возвращает локализованный текст кнопки подтверждения.
        /// Пустое значение (null/пустая строка) интерпретируется как <c>Common.Ok</c>.
        /// </summary>
        private static string ResolveOkText(string? okText) =>
            string.IsNullOrEmpty(okText) ? LocalizationManager.T("Common.Ok") : okText;

        /// <summary>
        /// Включает подписку на смену языка. Вызывается сама для окон
        /// со стандартной панелью кнопок; окну, которое строит кнопки само,
        /// нужно вызвать её явно.
        /// </summary>
        protected void EnsureLanguageSubscription()
        {
            if (_languageSubscribed)
                return;
            _languageSubscribed = true;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        protected virtual void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (_lastCancelText is not null)
                _lastCancelText.Text = LocalizationManager.T("Common.Cancel");
            if (_lastOkText is not null)
                _lastOkText.Text = _lastOkKey is { Length: > 0 } key
                    ? LocalizationManager.T(key)
                    : ResolveOkText(_lastOkRaw);
        }
    }
}
#endif