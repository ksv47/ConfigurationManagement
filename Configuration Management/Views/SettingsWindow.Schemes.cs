#if WINDOWS
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class SettingsWindow
    {
        // ===================== Цветовое оформление =====================

        /// <summary>
        /// Инициализирует вкладку «Цветовое оформление»: активная схема загружается
        /// моделью представления (<see cref="SettingsViewModel"/>), здесь заполняется
        /// список тем и редактор цветов, а также режим редактируемой палитры.
        /// </summary>
        private void InitializeColorSchemes()
        {
            ColorItemsControl.ItemsSource = _colorItems;
            RefreshSchemeComboBox();
            RefreshColorItems();

            // Начальный режим палитры — по текущему варианту темы.
            var dark = Themes.ThemeManager.CurrentTheme == Themes.ThemeManager.DarkThemeName;
            _settings.SetPaletteMode(dark);
            UpdatePaletteButton();
            RefreshSchemePreview();
        }

        /// <summary>
        /// Обработчик кнопки-переключателя палитры «светлая/тёмная»: переключает
        /// редактируемую палитру на противоположную и обновляет список цветов,
        /// живой предпросмотр темы и состояние самой кнопки.
        /// </summary>
        private void OnPaletteSwitch_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;

            var dark = !_settings.EditingDarkPalette;
            _settings.SetPaletteMode(dark);
            RefreshColorItems();
            RefreshSchemePreview();
            UpdatePaletteButton();
        }

        /// <summary>
        /// Обновляет состояние кнопки-переключателя палитры: иконку (в тёмной палитре — солнце,
        /// в светлой — луна, как у кнопки смены темы главного окна), подсказку и подпись.
        /// </summary>
        private void UpdatePaletteButton()
        {
            if (PaletteToggleButton is null)
                return;

            // В тёмной палитре кнопка предлагает перейти на светлую (иконка солнца), и наоборот.
            var dark = _settings?.EditingDarkPalette == true;
            PaletteToggleButton.ToolTip = dark
                ? LocalizationManager.T("Theme.Light")
                : LocalizationManager.T("Theme.Dark");

            if (PaletteToggleIcon is not null)
            {
                PaletteToggleIcon.Data = dark
                    ? (System.Windows.Media.Geometry)FindResource("IconSun")
                    : (System.Windows.Media.Geometry)FindResource("IconMoon");
            }

            if (PaletteStateText is not null)
                PaletteStateText.Text = dark
                    ? LocalizationManager.T("Theme.Dark")
                    : LocalizationManager.T("Theme.Light");
        }

        /// <summary>Строит список доступных тем (встроенные + пользовательские) и выбирает текущую.</summary>
        private void RefreshSchemeComboBox()
        {
            _suppressSchemeEvent = true;
            try
            {
                SchemeComboBox.Items.Clear();
                var items = BuildSchemeItems();
                // Гарантируем, что рабочая схема присутствует в списке.
                if (!items.Any(i => string.Equals(i.Name, _settings.CurrentColorScheme.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new SchemeComboItem { Name = _settings.CurrentColorScheme.Name, IsBuiltIn = false });
                }
                foreach (var it in items)
                    SchemeComboBox.Items.Add(it);

                var selected = items.FirstOrDefault(i =>
                    string.Equals(i.Name, _settings.CurrentColorScheme.Name, StringComparison.OrdinalIgnoreCase));
                SchemeComboBox.SelectedItem = selected;
                UpdateSchemeButtons();
            }
            finally
            {
                _suppressSchemeEvent = false;
            }
        }

        private List<SchemeComboItem> BuildSchemeItems()
        {
            var items = new List<SchemeComboItem>();
            var all = _settings.AvailableColorSchemes();
            for (int i = 0; i < all.Count; i++)
            {
                items.Add(new SchemeComboItem { Name = all[i].Name, IsBuiltIn = i < 2, DisplayName = SettingsViewModel.LocalizedBuiltInName(all[i].Name) });
            }
            return items;
        }

        /// <summary>Обновляет список редактируемых цветов из текущей схемы и подписывается на их live-изменение.</summary>
        private void RefreshColorItems()
        {
            _colorItems.Clear();
            foreach (var (key, label, hex) in _settings.GetEditableColors())
            {
                var item = new ColorItem { Key = key, Label = label, Hex = hex };
                item.PropertyChanged += OnColorItemPropertyChanged;
                _colorItems.Add(item);
            }
        }

        /// <summary>
        /// Подписка на изменение любого редактируемого цвета: при изменении <see cref="ColorItem.Hex"/>
        /// предпросмотр темы перерисовывается актуальными цветами (live-обновление без сохранения).
        /// </summary>
        private void OnColorItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ColorItem.Hex))
                RefreshSchemePreview();
        }

        /// <summary>Обновляет доступность кнопок «Переименовать»/«Удалить» для встроенных тем.</summary>
        private void UpdateSchemeButtons()
        {
            var selected = SchemeComboBox.SelectedItem as SchemeComboItem;
            var isBuiltIn = selected?.IsBuiltIn ?? false;
            if (RenameSchemeButton != null) RenameSchemeButton.IsEnabled = !isBuiltIn;
            if (DeleteSchemeButton != null) DeleteSchemeButton.IsEnabled = !isBuiltIn;
        }

        private void OnSchemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSchemeEvent)
                return;
            if (SchemeComboBox.SelectedItem is SchemeComboItem item)
            {
                // Берём рабочую копию темы (с уже внесёнными правками, если они есть),
                // чтобы переключение между темами не сбрасывало редактирование.
                // Встроенные «Светлая»/«Тёмная» используют слот своей базовой темы,
                // пользовательские — свой JSON-файл. Логика загрузки — в модели представления.
                _settings.SetCurrentScheme(item.Name);
                ThemeDebug($"SchemeCombo select '{item.Name}' -> '{_settings.CurrentColorScheme.Name}' (colors light={_settings.CurrentColorScheme.LightColors.Count}, dark={_settings.CurrentColorScheme.DarkColors.Count})");
                RefreshColorItems();
                UpdateSchemeButtons();
                RefreshSchemePreview();
            }
        }

        /// <summary>Диагностика переключения/применения темы (пишет во временный файл).</summary>
        private static void ThemeDebug(string message)
        {
#if DEBUG
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_theme_debug.log"),
                    "[settings] " + message + System.Environment.NewLine);
            }
            catch { /* не критично */ }
#endif
        }

        /// <summary>Применяет выбранную тему и цвета сразу (предпросмотр, без сохранения настроек).</summary>
        private void OnApplyColorScheme_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PreviewColorScheme(_settings.CurrentColorScheme);
        }

        /// <summary>Открывает диалог выбора цвета для отдельного элемента схемы.</summary>
        private void OnColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string key })
                return;
            var item = _colorItems.FirstOrDefault(ci => string.Equals(ci.Key, key, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            var picker = new ColorPickerWindow(item.Hex) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                // Сначала обновляем схему, затем элемент (сеттер Hex поднимает PropertyChanged,
                // по которому предпросмотр перечитывает актуальное значение из схемы).
                _settings.SetColor(item.Key, picker.Result);
                item.Hex = picker.Result;
                RefreshSchemePreview();
            }
        }

        /// <summary>
        /// Перерисовывает единый живой предпросмотр темы цветами редактируемой схемы.
        /// Рисует ОДНУ палитру — ту, что сейчас редактируется (светлую или тёмную,
        /// в зависимости от переключателя палитры <c>_settings.EditingDarkPalette</c>) —
        /// читая значения через <see cref="ColorScheme.PaletteValue"/>. Поэтому отражает
        /// незаконченные правки редактора до сохранения.
        /// </summary>
        private void RefreshSchemePreview()
        {
            if (_settings is null || PreviewShell is null)
                return;

            PaintSchemePreview(_settings.CurrentColorScheme, _settings.EditingDarkPalette);
        }

        /// <summary>Рисует единый миниатюрный предпросмотр темы для заданной палитры.</summary>
        private void PaintSchemePreview(ColorScheme scheme, bool dark)
        {
            string V(string key) => scheme.PaletteValue(dark, key);

            // Окно: подложка-карточка с рамкой и акцентная шапка.
            PaintBorder(PreviewShell, V("CardBackgroundColor"), V("BorderColor"));
            Paint(PreviewTitleBar, V("AccentColor"));
            PaintText(PreviewTitleText, V("TextOnAccentColor"));

            // Боковая панель: тёмный фон, контрастный текст, акцентная/фоновая подсветка пунктов.
            var sidebar = ParseColor(V("SidebarColor"));
            var sidebarText = Contrast(sidebar);
            Paint(PreviewSidebar, V("SidebarColor"));
            Paint(PreviewNavSelected, V("SidebarSelectedColor"));
            Paint(PreviewNavItem1, V("SidebarHoverColor"));
            Paint(PreviewNavItem2, V("SidebarHoverColor"));
            PaintText(PreviewNavSelectedText, sidebarText);
            PaintText(PreviewNavItem1Text, sidebarText);
            PaintText(PreviewNavItem2Text, sidebarText);

            // Контент.
            Paint(PreviewMain, V("ContentBackgroundColor"));
            PaintText(PreviewContentTitle, V("TextPrimaryColor"));
            PaintText(PreviewContentSubtitle, V("TextSecondaryColor"));

            // Карточка.
            PaintBorder(PreviewCard, V("CardBackgroundColor"), V("BorderColor"));
            PaintText(PreviewCardTitle, V("TextPrimaryColor"));
            PaintText(PreviewCardText, V("TextSecondaryColor"));

            // Поле ввода.
            PaintTextBox(PreviewTextField,
                V("CardBackgroundColor"), V("BorderColor"), V("TextPrimaryColor"));

            // Кнопки: акцентная и вторичная.
            Paint(PreviewPrimaryButton, V("AccentColor"));
            PaintText(PreviewPrimaryButtonText, V("ButtonTextColor"));
            Paint(PreviewSecondaryButton, V("SecondaryButtonBackgroundColor"));
            PaintText(PreviewSecondaryButtonText, V("ButtonTextColor"));

            // Список.
            PaintBorder(PreviewListBox, V("CardBackgroundColor"), V("BorderColor"));
            Paint(PreviewListSelected, V("ItemSelectedColor"));
            Paint(PreviewListItem1, V("ItemHoverColor"));
            Paint(PreviewListItem2, V("ItemHoverColor"));
            PaintText(PreviewListSelectedText, V("TextPrimaryColor"));
            PaintText(PreviewListItem1Text, V("TextPrimaryColor"));
            PaintText(PreviewListItem2Text, V("TextPrimaryColor"));
        }

        // ---- Вспомогательные методы для миниатюрного предпросмотра ----

        private static SolidColorBrush Parse(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Transparent; }
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Transparent; }
        }

        /// <summary>Кисть с максимальным контрастом к заданному цвету (чёрный/белый).</summary>
        private static SolidColorBrush Contrast(Color c)
        {
            var lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            return new SolidColorBrush(lum > 0.5 ? Colors.Black : Colors.White);
        }

        private static void Paint(Border? b, string hex)
        {
            if (b is not null) b.Background = Parse(hex);
        }

        private static void PaintBorder(Border? b, string bgHex, string borderHex)
        {
            if (b is null) return;
            b.Background = Parse(bgHex);
            b.BorderBrush = Parse(borderHex);
        }

        private static void PaintText(TextBlock? t, string hex)
        {
            if (t is not null) t.Foreground = Parse(hex);
        }

        private static void PaintText(TextBlock? t, SolidColorBrush? brush)
        {
            if (t is not null && brush is not null) t.Foreground = brush;
        }

        private static void PaintText(Control? c, string hex)
        {
            if (c is not null) c.Foreground = Parse(hex);
        }

        private static void PaintTextBox(TextBox? t, string bgHex, string borderHex, string fgHex)
        {
            if (t is null) return;
            t.Background = Parse(bgHex);
            t.BorderBrush = Parse(borderHex);
            t.Foreground = Parse(fgHex);
        }

        /// <summary>Создаёт собственную тему на основе текущих цветов.</summary>
        private void OnCreateScheme_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptForName(LocalizationManager.T("Settings.CreateTheme"), string.Format(LocalizationManager.T("Settings.CopyOf"), _settings.CurrentColorScheme.Name));
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            if (SettingsViewModel.IsReservedName(name))
            {
                MessageBox.Show(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.CreateTheme"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.CreateCustomScheme(name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Переименовывает выбранную пользовательскую тему.</summary>
        private void OnRenameScheme_Click(object sender, RoutedEventArgs e)
        {
            if (SchemeComboBox.SelectedItem is not SchemeComboItem item || item.IsBuiltIn)
            {
                MessageBox.Show(LocalizationManager.T("Settings.CannotRenameBuiltIn"),
                    LocalizationManager.T("Settings.RenameThemeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = PromptForName(LocalizationManager.T("Settings.Rename"), item.Name);
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), item.Name, StringComparison.OrdinalIgnoreCase))
                return;
            name = name.Trim();
            if (SettingsViewModel.IsReservedName(name))
            {
                MessageBox.Show(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.RenameThemeTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.RenameCustomScheme(item.Name, name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Удаляет выбранную пользовательскую тему.</summary>
        private void OnDeleteScheme_Click(object sender, RoutedEventArgs e)
        {
            if (SchemeComboBox.SelectedItem is not SchemeComboItem item || item.IsBuiltIn)
            {
                MessageBox.Show(LocalizationManager.T("Settings.CannotDeleteBuiltIn"),
                    LocalizationManager.T("Settings.DeleteThemeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(string.Format(LocalizationManager.T("Settings.DeleteThemeConfirm"), item.Name),
                LocalizationManager.T("Settings.DeleteThemeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            _settings.DeleteCustomScheme(item.Name);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Сбрасывает цвета выбранной темы на значения по умолчанию.</summary>
        private void OnResetSchemeColors_Click(object sender, RoutedEventArgs e)
        {
            _settings.ResetCurrentSchemeColors();
            RefreshColorItems();
            RefreshSchemePreview();
        }

        /// <summary>Выгружает текущую тему в JSON-файл.</summary>
        private void OnExportScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.T("Settings.ExportSchemeTitle"),
                Filter = $"{LocalizationManager.T("Settings.ColorSchemeFilter")}|*.json|{LocalizationManager.T("Common.AllFiles")}|*.*",
                FileName = _settings.CurrentColorScheme.Name + ".json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                _viewModel.ExportColorScheme(_settings.CurrentColorScheme, dialog.FileName);
                MessageBox.Show(string.Format(LocalizationManager.T("Settings.ExportedOk"), dialog.FileName),
                    LocalizationManager.T("Settings.ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(LocalizationManager.T("Settings.ExportFailed"), ex.Message),
                    LocalizationManager.T("Settings.ExportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Загружает тему из JSON-файла и добавляет её в список пользовательских тем.</summary>
        private void OnImportScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("Settings.ImportSchemeTitle"),
                Filter = $"{LocalizationManager.T("Settings.ColorSchemeFilter")}|*.json|{LocalizationManager.T("Common.AllFiles")}|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
                return;

            var scheme = _viewModel.ImportColorScheme(dialog.FileName);
            if (scheme is null || (scheme.LightColors.Count == 0 && scheme.DarkColors.Count == 0))
            {
                MessageBox.Show(LocalizationManager.T("Settings.ImportFailed"),
                    LocalizationManager.T("Settings.ImportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _settings.AdoptImportedScheme(scheme);
            RefreshSchemeComboBox();
            RefreshColorItems();
            RefreshSchemePreview();
            MessageBox.Show(string.Format(LocalizationManager.T("Settings.ImportedOk"), scheme.Name),
                LocalizationManager.T("Settings.ImportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Показывает модальное окно ввода названия темы (стиль Material Design,
        /// как остальные окна приложения). Возвращает null при отмене.
        /// </summary>
        private string? PromptForName(string title, string initial)
        {
            var dialog = new NameInputWindow(title, LocalizationManager.T("NameInput.Prompt"), LocalizationManager.T("Common.Ok"), initial) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Result : null;
        }
    }
}
#endif