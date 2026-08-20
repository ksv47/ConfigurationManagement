#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Themes;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия гиперссылки в виде вопросительного знака «?». По клику открывает
    /// всплывающую подсказку с описанием поведения элемента. Свойство <see cref="HelpText"/>
    /// содержит текст справки (можно с переносами строк). Закрывается повторным кликом
    /// или кликом вне подсказки.
    /// </summary>
    public class HelpLink : UserControl
    {
        /// <summary>Текст всплывающей подсказки.</summary>
        public static readonly StyledProperty<string> HelpTextProperty =
            AvaloniaProperty.Register<HelpLink, string>(nameof(HelpText), string.Empty);

        public string HelpText
        {
            get => GetValue(HelpTextProperty);
            set => SetValue(HelpTextProperty, value);
        }

        private readonly ToggleButton _helpToggle = new();
        private readonly Popup _helpPopup = new();
        private readonly TextBlock _helpBody;

        public HelpLink()
        {
            // Круглая кнопка «?».
            _helpToggle.Width = 18;
            _helpToggle.Height = 18;
            _helpToggle.HorizontalAlignment = HorizontalAlignment.Center;
            _helpToggle.VerticalAlignment = VerticalAlignment.Center;
            _helpToggle.Content = new TextBlock
            {
                Text = "?",
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(_helpToggle, "Что это? Нажмите, чтобы открыть подсказку");
            _helpToggle.IsCheckedChanged += (_, _) => OnToggleChanged();

            // Всплывающая подсказка.
            _helpBody = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var titleBlock = new TextBlock
            {
                Text = "Подсказка",
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            };
            // Заголовок — акцентный цвет темы.
            ThemeBrushes.Bind(titleBlock, TextBlock.ForegroundProperty, "AccentColorBrush");
            var popupContent = new StackPanel { Children = { titleBlock, _helpBody } };
            var popupBorder = new Border
            {
                Child = popupContent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 6, 0, 0)
            };
            // Фон и граница подсказки — карточка и граница из темы.
            ThemeBrushes.Bind(popupBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(popupBorder, Border.BorderBrushProperty, "BorderColorBrush");
            _helpPopup.Child = popupBorder;
            _helpPopup.PlacementTarget = _helpToggle;
            _helpPopup.Placement = PlacementMode.Bottom;
            _helpPopup.IsLightDismissEnabled = true;
            _helpPopup.Closed += (_, _) => _helpToggle.IsChecked = false;

            Content = new Grid { Children = { _helpToggle, _helpPopup } };
        }

        private void OnToggleChanged()
        {
            if (_helpToggle.IsChecked == true)
            {
                _helpBody.Text = HelpText;
                _helpPopup.IsOpen = true;
            }
            else
            {
                _helpPopup.IsOpen = false;
            }
        }
    }
}
#endif