#if WINDOWS
using System;
using System.Collections.Generic;
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
        /// список тем и редактор цветов.
        /// </summary>
        private void InitializeColorSchemes()
        {
            ColorItemsControl.ItemsSource = _colorItems;
            RefreshSchemeComboBox();
            RefreshColorItems();
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

        /// <summary>Обновляет список редактируемых цветов из текущей схемы.</summary>
        private void RefreshColorItems()
        {
            _colorItems.Clear();
            foreach (var (key, label, hex) in _settings.GetEditableColors())
            {
                _colorItems.Add(new ColorItem { Key = key, Label = label, Hex = hex });
            }
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
                ThemeDebug($"SchemeCombo select '{item.Name}' -> '{_settings.CurrentColorScheme.Name}' (isDark={_settings.CurrentColorScheme.IsDark}, colors={_settings.CurrentColorScheme.Colors.Count})");
                RefreshColorItems();
                UpdateSchemeButtons();
            }
        }

        /// <summary>Диагностика переключения/применения темы (пишет во временный файл).</summary>
        private static void ThemeDebug(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_theme_debug.log"),
                    "[settings] " + message + System.Environment.NewLine);
            }
            catch { /* не критично */ }
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
                item.Hex = picker.Result;
                _settings.SetColor(item.Key, picker.Result);
            }
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
        }

        /// <summary>Сбрасывает цвета выбранной темы на значения по умолчанию.</summary>
        private void OnResetSchemeColors_Click(object sender, RoutedEventArgs e)
        {
            _settings.ResetCurrentSchemeColors();
            RefreshColorItems();
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
            if (scheme is null || scheme.Colors.Count == 0)
            {
                MessageBox.Show(LocalizationManager.T("Settings.ImportFailed"),
                    LocalizationManager.T("Settings.ImportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _settings.AdoptImportedScheme(scheme);
            RefreshSchemeComboBox();
            RefreshColorItems();
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