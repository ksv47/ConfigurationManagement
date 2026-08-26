#if WINDOWS
using System;
using System.Windows;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора цвета (WPF). Оборачивает переиспользуемый элемент
    /// <see cref="Configuration_Management.Controls.ColorPickerControl"/>
    /// в модальное окно с кнопками «Отмена»/«ОК».
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        /// <summary>
        /// Создаёт диалог выбора цвета.
        /// </summary>
        /// <param name="initialColor">Начальный цвет в формате #RRGGBB.</param>
        public ColorPickerWindow(string? initialColor = null)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(initialColor))
                Picker.SelectedColor = initialColor;
        }

        /// <summary>
        /// Возвращает выбранный цвет в формате #RRGGBB.
        /// </summary>
        public string Result { get; private set; } = "#2D6CDF";

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = Picker.SelectedColor ?? "#2D6CDF";
            DialogResult = true;
        }
    }
}
#endif