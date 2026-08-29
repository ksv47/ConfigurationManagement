#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Configuration_Management.Localization;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Рамка с заголовком: повторяет шаблон <c>GroupBox</c> из словарей WPF
    /// (Themes/LightTheme.xaml:928). Заголовок это отдельная полоса во всю ширину
    /// со скруглением 6, содержимое ниже в такой же рамке без верхней стороны.
    /// В Avalonia контрола GroupBox нет, поэтому здесь построитель.
    /// </summary>
    public static class GroupBoxPanel
    {
        /// <summary>
        /// Строит рамку с заголовком.
        /// </summary>
        /// <param name="headerKey">Ключ локализации заголовка.</param>
        /// <param name="content">Содержимое рамки.</param>
        /// <param name="margin">Внешний отступ рамки.</param>
        /// <param name="padding">Внутренний отступ содержимого; по умолчанию 8, как в стиле.</param>
        public static Control Build(string headerKey, Control content,
            Thickness? margin = null, Thickness? padding = null)
        {
            var grid = new Grid { Margin = margin ?? default };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            var header = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4),
                Child = new TextBlock
                {
                    Text = LocalizationManager.T(headerKey),
                    FontWeight = FontWeight.SemiBold
                }
            };
            Themes.ThemeBrushes.Bind(header, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(header, Border.BorderBrushProperty, "BorderBrushColor");
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var body = new Border
            {
                BorderThickness = new Thickness(1, 0, 1, 1),
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Padding = padding ?? new Thickness(8),
                Child = content
            };
            Themes.ThemeBrushes.Bind(body, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(body, Border.BorderBrushProperty, "BorderBrushColor");
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            return grid;
        }
    }
}
#endif
