#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Configuration_Management.Controls;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора цвета (Avalonia/Linux). Оборачивает переиспользуемый
    /// элемент <see cref="ColorPickerControl"/> в модальное окно с кнопками
    /// «Отмена»/«ОК». WPF-аналог — <see cref="ColorPickerWindow"/>.
    /// </summary>
    public class ColorPickerWindow : ModalWindowBase
    {
        private readonly ColorPickerControl _picker = new();

        /// <summary>
        /// Создаёт диалог выбора цвета.
        /// </summary>
        /// <param name="initialColor">Начальный цвет в формате #RRGGBB.</param>
        public ColorPickerWindow(string? initialColor = null)
        {
            Title = LocalizationManager.T("ColorPicker.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            Width = 470;
            // Высота из разметки, а не подгонка по содержимому.
            Height = 560;
            CanResize = false;

            if (!string.IsNullOrWhiteSpace(initialColor))
                _picker.SelectedColor = initialColor;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(_picker, 0);
            root.Children.Add(_picker);

            // Порядок и оформление как в разметке: подтверждение слева основной
            // кнопкой, отмена справа вторичной. Общая панель базового класса
            // ставит их наоборот, поэтому кнопки собираются здесь по одной.
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0),
                Children =
                {
                    BuildConfirmButton(LocalizationManager.T("Common.Ok"), 90, OnOk_Click, minimumWidth: true),
                    BuildCancelButton(110, secondary: true, minimumWidth: true)
                }
            };
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            Content = root;
        }

        /// <summary>
        /// Возвращает выбранный цвет в формате #RRGGBB.
        /// </summary>
        public string Result { get; private set; } = "#2D6CDF";

        private void OnOk_Click()
        {
            Result = _picker.SelectedColor ?? "#2D6CDF";
        }
    }
}
#endif