using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Configuration_Management.Models;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания/редактирования группы.
    /// </summary>
    public partial class GroupEditWindow : Window
    {
        private string _color = "#2D6CDF";

        /// <summary>
        /// Создаёт диалог для новой группы.
        /// </summary>
        public GroupEditWindow()
        {
            InitializeComponent();
            ApplyPaletteColors();
            UpdateColorPreview();
        }

        /// <summary>
        /// Создаёт диалог для редактирования существующей группы.
        /// </summary>
        /// <param name="group">Группа для редактирования.</param>
        public GroupEditWindow(Group group)
        {
            InitializeComponent();
            Result.Id = group.Id;
            NameBox.Text = group.Name;
            DescriptionBox.Text = group.Description;
            _color = group.Color;
            ApplyPaletteColors();
            UpdateColorPreview();
        }

        /// <summary>
        /// Возвращает отредактированную группу.
        /// </summary>
        public Group Result { get; private set; } = new();

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

        private void OnPaletteColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                _color = hex;
                UpdateColorPreview();
            }
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Укажите наименование группы.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result.Name = NameBox.Text.Trim();
            Result.Description = DescriptionBox.Text.Trim();
            Result.Color = _color;
            DialogResult = true;
        }

        private void UpdateColorPreview()
        {
            ColorPreview.Background = new SolidColorBrush(ParseColor(_color));
            ColorHexText.Text = _color;
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