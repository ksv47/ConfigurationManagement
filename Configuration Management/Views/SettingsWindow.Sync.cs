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
using Microsoft.Win32;
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
        /// <summary>
        /// Инициализирует блок синхронизации с файлом ibases.v8i: заполняет список
        /// режимов, подставляет сохранённый путь и обновляет состояние элементов управления.
        /// </summary>
        private void InitializeSyncSettings()
        {
            var s = _settings.Sync;
            s.Mode = _viewModel.IbasesSyncMode;
            s.FilePath = _viewModel.IbasesSyncFilePath;
            s.Trigger = _viewModel.IbasesSyncTrigger;
            s.IntervalMinutes = _viewModel.IbasesSyncIntervalMinutes;
            s.ScheduleTime = _viewModel.IbasesSyncScheduleTime;

            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeDisabled"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeImport"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeExport"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeBoth"));
            SyncModeComboBox.SelectedIndex = (int)s.Mode;

            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerStartup"));
            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerInterval"));
            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerSchedule"));
            SyncTriggerComboBox.SelectedIndex = (int)s.Trigger;

            SyncFilePathTextBox.Text = s.FilePath;
            SyncIntervalTextBox.Text = s.IntervalMinutes.ToString();
            SyncScheduleTimePicker.Text = s.ScheduleTime;
            IbasesBackupEnabledCheck.IsChecked = _viewModel.IbasesBackupEnabled;
            IbasesBackupKeepCountBox.Text = _viewModel.IbasesBackupKeepCount.ToString();

            UpdateSyncControls();
        }

        /// <summary>
        /// Обновляет видимость/доступность элементов управления блока синхронизации
        /// в зависимости от выбранного режима и пути к файлу.
        /// </summary>
        private void UpdateSyncControls()
        {
            var s = _settings.Sync;
            var enabled = s.IsEnabled;
            SyncFilePathTextBox.IsEnabled = enabled;
            BrowseSyncFileButton.IsEnabled = enabled;

            // Элементы настройки момента автоматической синхронизации.
            SyncTriggerComboBox.IsEnabled = enabled;
            var trigger = SyncTriggerComboBox.SelectedIndex;
            var isInterval = enabled && trigger == (int)IbasesSyncTrigger.Interval;
            var isSchedule = enabled && trigger == (int)IbasesSyncTrigger.Schedule;
            SyncIntervalTextBox.IsEnabled = isInterval;
            SyncIntervalLabel.IsEnabled = isInterval;
            SyncScheduleTimePicker.IsEnabled = isSchedule;
            SyncScheduleLabel.IsEnabled = isSchedule;

            // Кнопка «Загрузить» доступна в режимах с импортом, «Выгрузить» — с экспортом.
            SyncImportButton.IsEnabled = enabled &&
                (s.Mode == IbasesSyncMode.Import || s.Mode == IbasesSyncMode.Both);
            SyncExportButton.IsEnabled = enabled &&
                (s.Mode == IbasesSyncMode.Export || s.Mode == IbasesSyncMode.Both);

            // Текстовый статус строится бизнес-логикой модели.
            SyncStatusText.Text = s.BuildStatusText();
        }

        private void OnSyncMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SyncModeComboBox.SelectedIndex < 0)
                return;

            _settings.Sync.Mode = (IbasesSyncMode)SyncModeComboBox.SelectedIndex;
            UpdateSyncControls();
        }

        private void OnSyncTrigger_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SyncTriggerComboBox.SelectedIndex < 0)
                return;

            _settings.Sync.Trigger = (IbasesSyncTrigger)SyncTriggerComboBox.SelectedIndex;
            UpdateSyncControls();
        }

        private void OnSyncInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SyncIntervalTextBox.Text, out var minutes) && minutes > 0)
            {
                _settings.Sync.IntervalMinutes = minutes;
                UpdateSyncControls();
            }
        }

        private void OnSyncScheduleTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            var value = SyncScheduleTimePicker.Text?.Trim() ?? string.Empty;
            if (TimeSpan.TryParse(value, out _))
            {
                _settings.Sync.ScheduleTime = value;
                UpdateSyncControls();
            }
        }

        private void OnBrowseSyncFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.T("Settings.Ibases.FileDialogTitle"),
                Filter = $"{LocalizationManager.T("Settings.Ibases.FileFilter")}|*.v8i|{LocalizationManager.T("Common.AllFiles")}|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                _settings.Sync.FilePath = dialog.FileName;
                SyncFilePathTextBox.Text = _settings.Sync.FilePath;
                UpdateSyncControls();
            }
        }

        private void OnSyncImport_Click(object sender, RoutedEventArgs e)
        {
            var filePath = _settings.Sync.ResolveDisplayPath();
            if (filePath is null || !System.IO.File.Exists(filePath))
            {
                MessageBox.Show(
                    LocalizationManager.T("Settings.Ibases.ImportFileNotFound"),
                    LocalizationManager.T("Settings.Ibases.ImportTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Используем готовый метод ViewModel, который выполняет импорт,
            // обновляет представление и сохраняет данные.
            var ok = _viewModel.ImportFromIbases();
            if (ok)
            {
                MessageBox.Show(LocalizationManager.T("Settings.Ibases.ImportOk"),
                    LocalizationManager.T("Settings.Ibases.ImportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(LocalizationManager.T("Settings.Ibases.ImportFailed"),
                    LocalizationManager.T("Settings.Ibases.ImportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshGroupsAfterDataChange();
        }

        private void OnSyncExport_Click(object sender, RoutedEventArgs e)
        {
            var filePath = _settings.Sync.ResolveDisplayPath();
            if (filePath is null)
            {
                MessageBox.Show(
                    LocalizationManager.T("Settings.Ibases.ExportNoPath"),
                    LocalizationManager.T("Settings.Ibases.ExportTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                IbasesV8iExporter.Export(filePath, _viewModel.Infobases, _viewModel.Groups);
                MessageBox.Show(LocalizationManager.T("Settings.Ibases.ExportOk"),
                    LocalizationManager.T("Settings.Ibases.ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(LocalizationManager.T("Settings.Ibases.ExportFailed"), ex.Message),
                    LocalizationManager.T("Settings.Ibases.ExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
#endif