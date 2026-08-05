using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора произвольного цвета с RGB-слайдерами и HEX-полем.
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        private bool _isUpdating;

        /// <summary>
        /// Создаёт диалог выбора цвета.
        /// </summary>
        /// <param name="initialColor">Начальный цвет в формате #RRGGBB.</param>
        public ColorPickerWindow(string? initialColor = null)
        {
            InitializeComponent();
            ApplyPaletteColors();
            SetColor(ParseColor(initialColor));
        }

        /// <summary>
        /// Задаёт фон каждой кнопке палитры из её Tag (HEX-цвет).
        /// </summary>
        private void ApplyPaletteColors()
        {
            foreach (var child in PaletteGrid.Children)
            {
                if (child is Button button && button.Tag is string hex)
                {
                    button.Background = new SolidColorBrush(ParseColor(hex));
                }
            }
        }

        /// <summary>
        /// Возвращает выбранный цвет в формате #RRGGBB.
        /// </summary>
        public string Result { get; private set; } = "#2D6CDF";

        private void SetColor(Color color)
        {
            _isUpdating = true;
            try
            {
                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;
                RedValue.Text = color.R.ToString();
                GreenValue.Text = color.G.ToString();
                BlueValue.Text = color.B.ToString();
                HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                ColorPreview.Background = new SolidColorBrush(color);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OnRgb_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating)
                return;

            var color = Color.FromRgb(
                (byte)RedSlider.Value,
                (byte)GreenSlider.Value,
                (byte)BlueSlider.Value);

            _isUpdating = true;
            try
            {
                RedValue.Text = color.R.ToString();
                GreenValue.Text = color.G.ToString();
                BlueValue.Text = color.B.ToString();
                HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                ColorPreview.Background = new SolidColorBrush(color);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OnHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            var text = HexBox.Text?.Trim() ?? string.Empty;
            if (text.Length != 7 || !text.StartsWith("#"))
                return;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(text);
                SetColor(color);
            }
            catch
            {
                // Игнорируем некорректный ввод HEX.
            }
        }

        private void OnPaletteColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                SetColor(ParseColor(hex));
            }
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = HexBox.Text?.Trim() ?? "#2D6CDF";
            DialogResult = true;
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex ?? "#2D6CDF");
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString("#2D6CDF");
            }
        }
    }
}