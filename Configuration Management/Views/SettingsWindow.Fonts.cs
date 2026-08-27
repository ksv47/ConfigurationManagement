#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class SettingsWindow
    {
        // ===================== Шрифт интерфейса =====================

        /// <summary>Элемент списка начертаний шрифта.</summary>
        private sealed class FontFaceItem
        {
            public string Key { get; init; } = string.Empty;
            public string Weight { get; init; } = "Normal";
            public string Style { get; init; } = "Normal";
            public override string ToString() => LocalizationManager.T(Key);
        }

        /// <summary>Инициализирует подвкладку «Шрифт»: области, семейства, размеры и начертания.</summary>
        private void InitializeFontSettings()
        {
            // Рабочие копии настроек элементов загружает SettingsViewModel.
            _settings.LoadElementFontWorkingCopies(_viewModel);

            // Список областей.
            ElementComboBox.Items.Clear();
            foreach (var key in Themes.ThemeManager.AllFontScopes)
                ElementComboBox.Items.Add(new ElementScopeItem(key));
            ElementComboBox.SelectedItem = ElementComboBox.Items.Cast<ElementScopeItem>()
                .FirstOrDefault(s => s.Key == Themes.ThemeManager.FontDefault);

            // Списки шрифтов.
            FontFamilyComboBox.Items.Clear();
            foreach (var family in new[]
            {
                "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana",
                "Trebuchet MS", "Georgia", "Times New Roman", "Courier New", "Consolas"
            })
                FontFamilyComboBox.Items.Add(family);

            // Диапазон размеров шрифта как в Microsoft Word: от 8 до 72.
            FontSizeComboBox.Items.Clear();
            foreach (var size in new double[]
            {
                8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24,
                26, 28, 32, 36, 40, 48, 56, 64, 72
            })
                FontSizeComboBox.Items.Add(size.ToString());

            FontStyleComboBox.Items.Clear();
            foreach (var face in FontFaces)
                FontStyleComboBox.Items.Add(face);

            LoadCurrentElementFont();
        }

        /// <summary>При смене области загружает её настройки в поля.</summary>
        private void OnElement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ElementComboBox.SelectedItem is ElementScopeItem item)
            {
                _currentElement = item.Key;
                LoadCurrentElementFont();
            }
        }

        /// <summary>Загружает настройки шрифта текущей области в поля ввода.</summary>
        private void LoadCurrentElementFont()
        {
            if (FontFamilyComboBox is null || FontSizeComboBox is null || FontStyleComboBox is null)
                return;

            var fs = _settings.ElementFonts.TryGetValue(_currentElement, out var f) && f is not null
                ? f : new Models.ElementFontSettings();

            FontFamilyComboBox.SelectedItem = FontFamilyComboBox.Items.Cast<string>()
                .FirstOrDefault(x => string.Equals(x, fs.FontFamily, StringComparison.OrdinalIgnoreCase))
                ?? "Segoe UI";
            FontSizeComboBox.Text = fs.FontSize.ToString("0.#");
            FontStyleComboBox.SelectedItem = FontFaces.FirstOrDefault(x =>
                string.Equals(x.Weight, fs.FontWeight, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Style, fs.FontStyle, StringComparison.OrdinalIgnoreCase)) ?? FontFaces[0];

            UpdateFontPreview();
        }

        /// <summary>Обновляет предпросмотр при изменении выбора шрифта.</summary>
        private void OnFontCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateFontPreview();
        }

        /// <summary>Обновляет предпросмотр при ручном вводе размера шрифта (по нажатию клавиши).</summary>
        private void OnFontSize_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            UpdateFontPreview();
        }

        /// <summary>Возвращает введённый/выбранный размер шрифта (по умолчанию 13).</summary>
        private double ReadFontSize()
        {
            var text = FontSizeComboBox?.Text;
            return double.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 13;
        }

        /// <summary>Обновляет текстовый предпросмотр выбранного шрифта.</summary>
        private void UpdateFontPreview()
        {
            if (FontPreviewText is null || FontFamilyComboBox is null)
                return;

            var family = FontFamilyComboBox.SelectedItem as string ?? "Segoe UI";
            double size = ReadFontSize();
            var face = FontStyleComboBox.SelectedItem as FontFaceItem;

            FontPreviewText.FontFamily = new System.Windows.Media.FontFamily(family);
            FontPreviewText.FontSize = size;
            FontPreviewText.FontWeight = string.Equals(face?.Weight, "Bold", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            FontPreviewText.FontStyle = string.Equals(face?.Style, "Italic", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
        }

        /// <summary>Сохраняет текущий выбор шрифта в настройки текущей области.</summary>
        private void ReadFontSelection()
        {
            var fs = _settings.EnsureElementFont(_currentElement);
            fs.FontFamily = FontFamilyComboBox.SelectedItem as string ?? "Segoe UI";
            fs.FontSize = ReadFontSize();
            var face = FontStyleComboBox.SelectedItem as FontFaceItem;
            fs.FontWeight = face?.Weight ?? "Normal";
            fs.FontStyle = face?.Style ?? "Normal";
        }

        /// <summary>Применяет настройки шрифта областей к программе сразу (предпросмотр, без сохранения).</summary>
        private void OnFontApply_Click(object sender, RoutedEventArgs e)
        {
            ReadFontSelection();
            _viewModel.PreviewElementFonts(_settings.ElementFonts);
        }
    }
}
#endif