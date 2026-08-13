using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Microsoft.Win32;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения с горизонтальными вкладками:
    /// «Платформы», «Отображение», «ibases.v8i» и «Дополнительные функции».
    /// Управление группами — в основном окне (добавление/редактирование через список баз).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private List<string> _installedPlatformVersions;
        private IbasesSyncMode _syncMode;
        private string _syncFilePath = string.Empty;
        private IbasesSyncTrigger _syncTrigger = IbasesSyncTrigger.OnStartup;
        private int _syncIntervalMinutes = 30;
        private string _syncScheduleTime = "09:00";
        private bool _showFavoritesButton = true;
        private bool _showPinnedButton = true;
        private bool _showTags = true;
        private bool _showVersionColumn = true;
        private bool _showLaunchModeColumn = true;
        private bool _showServerColumn = true;
        private bool _showLastLaunchColumn = true;

        /// <summary>
        /// Создаёт диалог настроек приложения.
        /// </summary>
        /// <param name="viewModel">Главная модель представления приложения.</param>
        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _installedPlatformVersions = new List<string>(viewModel.InstalledPlatformVersions);
            UpdatePlatformsDisplay();
            InitializeSyncSettings();
            InitializeDisplaySettings();
        }

        /// <summary>
        /// Инициализирует вкладку «Отображение»: заполняет флажки текущими
        /// настройками отображения списка баз.
        /// </summary>
        private void InitializeDisplaySettings()
        {
            _showFavoritesButton = _viewModel.ShowFavoritesButton;
            _showPinnedButton = _viewModel.ShowPinnedButton;
            _showTags = _viewModel.ShowTags;
            _showVersionColumn = _viewModel.ShowVersionColumn;
            _showLaunchModeColumn = _viewModel.ShowLaunchModeColumn;
            _showServerColumn = _viewModel.ShowServerColumn;
            _showLastLaunchColumn = _viewModel.ShowLastLaunchColumn;

            ShowNameColumnCheck.IsChecked = true;
            ShowVersionColumnCheck.IsChecked = _showVersionColumn;
            ShowLaunchModeColumnCheck.IsChecked = _showLaunchModeColumn;
            ShowServerColumnCheck.IsChecked = _showServerColumn;
            ShowLastLaunchColumnCheck.IsChecked = _showLastLaunchColumn;

            ShowFavoritesButtonCheck.IsChecked = _showFavoritesButton;
            ShowPinnedButtonCheck.IsChecked = _showPinnedButton;
            ShowTagsCheck.IsChecked = _showTags;
            if (ShowTagFilterPanelCheck != null)
                ShowTagFilterPanelCheck.IsChecked = _viewModel.ShowTagFilterPanel;
            if (AllowMultipleInstancesCheck != null)
                AllowMultipleInstancesCheck.IsChecked = _viewModel.AllowMultipleInstances;

            GroupByGroupCheck.IsChecked = _viewModel.GroupByGroup;
            ShowFavoritesOnlyCheck.IsChecked = _viewModel.ShowFavoritesOnly;
        }

        /// <summary>
        /// Инициализирует блок синхронизации с файлом ibases.v8i: заполняет список
        /// режимов, подставляет сохранённый путь и обновляет состояние элементов управления.
        /// </summary>
        private void InitializeSyncSettings()
        {
            _syncMode = _viewModel.IbasesSyncMode;
            _syncFilePath = _viewModel.IbasesSyncFilePath;
            _syncTrigger = _viewModel.IbasesSyncTrigger;
            _syncIntervalMinutes = _viewModel.IbasesSyncIntervalMinutes;
            _syncScheduleTime = _viewModel.IbasesSyncScheduleTime;

            SyncModeComboBox.Items.Add("Отключена");
            SyncModeComboBox.Items.Add("Только загрузка (из файла в приложение)");
            SyncModeComboBox.Items.Add("Только выгрузка (из приложения в файл)");
            SyncModeComboBox.Items.Add("Двусторонняя (загрузка и выгрузка)");
            SyncModeComboBox.SelectedIndex = (int)_syncMode;

            SyncTriggerComboBox.Items.Add("Только при запуске приложения");
            SyncTriggerComboBox.Items.Add("Через заданный интервал");
            SyncTriggerComboBox.Items.Add("По расписанию");
            SyncTriggerComboBox.SelectedIndex = (int)_syncTrigger;

            SyncFilePathTextBox.Text = _syncFilePath;
            SyncIntervalTextBox.Text = _syncIntervalMinutes.ToString();
            SyncScheduleTimePicker.Text = _syncScheduleTime;
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
            var enabled = _syncMode != IbasesSyncMode.None;
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
                (_syncMode == IbasesSyncMode.Import || _syncMode == IbasesSyncMode.Both);
            SyncExportButton.IsEnabled = enabled &&
                (_syncMode == IbasesSyncMode.Export || _syncMode == IbasesSyncMode.Both);

            // Текстовый статус.
            var path = ResolveDisplayPath();
            if (!enabled)
            {
                SyncStatusText.Text = "Синхронизация отключена.";
            }
            else if (string.IsNullOrWhiteSpace(path))
            {
                SyncStatusText.Text = "Файл ibases.v8i не найден. Укажите путь вручную.";
            }
            else
            {
                var modeText = _syncMode switch
                {
                    IbasesSyncMode.Import => "только загрузка",
                    IbasesSyncMode.Export => "только выгрузка",
                    _ => "двусторонняя"
                };
                var triggerText = _syncTrigger switch
                {
                    IbasesSyncTrigger.Interval => $"автоматически каждые {_syncIntervalMinutes} мин.",
                    IbasesSyncTrigger.Schedule => $"автоматически по расписанию в {_syncScheduleTime}.",
                    _ => "автоматически при запуске."
                };
                SyncStatusText.Text = $"Файл: {path}\nРежим: {modeText}. Запуск: {triggerText}";
            }
        }

        /// <summary>
        /// Возвращает путь к файлу ibases.v8i для отображения: пользовательский путь
        /// или стандартный путь 1С, если пользовательский не задан.
        /// </summary>
        private string? ResolveDisplayPath()
        {
            if (!string.IsNullOrWhiteSpace(_syncFilePath))
                return _syncFilePath;

            return IbasesV8iImporter.FindDefaultPath();
        }

        private void OnSyncMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SyncModeComboBox.SelectedIndex < 0)
                return;

            _syncMode = (IbasesSyncMode)SyncModeComboBox.SelectedIndex;
            UpdateSyncControls();
        }

        private void OnSyncTrigger_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SyncTriggerComboBox.SelectedIndex < 0)
                return;

            _syncTrigger = (IbasesSyncTrigger)SyncTriggerComboBox.SelectedIndex;
            UpdateSyncControls();
        }

        private void OnSyncInterval_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SyncIntervalTextBox.Text, out var minutes) && minutes > 0)
            {
                _syncIntervalMinutes = minutes;
                UpdateSyncControls();
            }
        }

        private void OnSyncScheduleTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            var value = SyncScheduleTimePicker.Text?.Trim() ?? string.Empty;
            if (TimeSpan.TryParse(value, out _))
            {
                _syncScheduleTime = value;
                UpdateSyncControls();
            }
        }

        private void OnBrowseSyncFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл списка баз 1С (ibases.v8i)",
                Filter = "Файл списка баз 1С (*.v8i)|*.v8i|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                _syncFilePath = dialog.FileName;
                SyncFilePathTextBox.Text = _syncFilePath;
                UpdateSyncControls();
            }
        }

        private void OnSyncImport_Click(object sender, RoutedEventArgs e)
        {
            var filePath = ResolveDisplayPath();
            if (filePath is null || !System.IO.File.Exists(filePath))
            {
                MessageBox.Show(
                    "Файл ibases.v8i не найден. Укажите путь к файлу вручную.",
                    "Импорт из ibases.v8i",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Используем готовый метод ViewModel, который выполняет импорт,
            // обновляет представление и сохраняет данные.
            var ok = _viewModel.ImportFromIbases();
            if (ok)
            {
                MessageBox.Show("Импорт из файла ibases.v8i выполнен успешно.",
                    "Импорт из ibases.v8i", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Не удалось выполнить импорт. Проверьте, что файл ibases.v8i существует и доступен.",
                    "Ошибка импорта", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshGroupsAfterDataChange();
        }

        private void OnSyncExport_Click(object sender, RoutedEventArgs e)
        {
            var filePath = ResolveDisplayPath();
            if (filePath is null)
            {
                MessageBox.Show(
                    "Не удалось определить путь к файлу ibases.v8i. Укажите путь вручную.",
                    "Экспорт в ibases.v8i",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                IbasesV8iExporter.Export(filePath, _viewModel.Infobases, _viewModel.Groups);
                MessageBox.Show("Экспорт в файл ibases.v8i выполнен успешно.",
                    "Экспорт в ibases.v8i", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось выполнить экспорт.\n{ex.Message}",
                    "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Список установленных версий платформы 1С.
        /// </summary>
        public List<string> Result => _installedPlatformVersions;

        /// <summary>
        /// Обновляет список установленных версий платформы, сканируя каталоги 1С.
        /// </summary>
        private void OnRefreshPlatforms_Click(object sender, RoutedEventArgs e)
        {
            _installedPlatformVersions = PlatformVersionService.FindInstalledVersions();
            _viewModel.SetInstalledPlatformVersions(_installedPlatformVersions);
            UpdatePlatformsDisplay();
        }

        /// <summary>
        /// Обновляет отображение списка установленных версий платформы,
        /// группируя их по мажорной версии (например, «8.3.27»).
        /// </summary>
        private void UpdatePlatformsDisplay()
        {
            PlatformsTree.Items.Clear();

            if (_installedPlatformVersions.Count == 0)
            {
                StatusText.Text = "Версии платформы 1С не найдены. Нажмите «Обновить список».";
                return;
            }

            // Группируем версии по первым трём компонентам (мажорная версия).
            var groups = _installedPlatformVersions
                .GroupBy(GetMajorVersion)
                .OrderByDescending(g => g.Key, new VersionComparer())
                .Select(g => new PlatformVersionGroup
                {
                    Name = g.Key,
                    Versions = g.OrderByDescending(v => v, new VersionComparer()).ToList()
                })
                .ToList();

            foreach (var group in groups)
            {
                PlatformsTree.Items.Add(group);
            }

            StatusText.Text = $"Найдено версий: {_installedPlatformVersions.Count}";
        }

        /// <summary>
        /// Возвращает мажорную версию (первые три компонента) из варианта платформы.
        /// Например, для «8.3.27.1234 (64)» вернёт «8.3.27».
        /// </summary>
        private static string GetMajorVersion(string variant)
        {
            PlatformVersionService.ParseVariant(variant, out var version, out _);
            var parts = version.Split('.');
            return parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : version;
        }

        private void OnExportInfobases_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ExportInfobasesCommand.Execute(null);
        }

        private void OnImportInfobases_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ImportInfobasesCommand.Execute(null);
            RefreshGroupsAfterDataChange();
        }

        private void OnImportIbasesV8i_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ImportFromIbasesV8iCommand.Execute(null);
            RefreshGroupsAfterDataChange();
        }

        private void OnClearAllInfobases_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearAllInfobasesCommand.Execute(null);
            RefreshGroupsAfterDataChange();
        }

        /// <summary>
        /// Обновляет локальную копию списка групп после изменения данных
        /// командами дополнительных функций.
        /// </summary>
        /// <summary>
        /// После импорта/очистки данные уже в MainViewModel; локальный список групп в настройках не ведётся.
        /// </summary>
        private void RefreshGroupsAfterDataChange()
        {
            // Группы управляются из главного окна.
        }
        private void OnRestoreIbasesBackup_Click(object sender, RoutedEventArgs e)
        {
            var filePath = SyncFilePathTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
                filePath = Services.IbasesV8iImporter.FindDefaultPath() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                MessageBox.Show("Не указан путь к файлу ibases.v8i.", "Восстановление", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var backups = Services.IbasesBackupService.ListBackups(filePath);
            if (backups.Count == 0)
            {
                MessageBox.Show("Резервные копии не найдены рядом с файлом:\n" + filePath,
                    "Восстановление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var latest = backups[0];
            var result = MessageBox.Show(
                $"Восстановить файл ibases.v8i из копии?\n\n{System.IO.Path.GetFileName(latest)}\n\nТекущий файл будет перезаписан.",
                "Восстановление из резервной копии",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Services.IbasesBackupService.RestoreBackup(latest, filePath);
                MessageBox.Show("Файл ibases.v8i успешно восстановлен из резервной копии.",
                    "Восстановление", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось восстановить файл.\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            // Сохраняем версии платформы в модели представления (группы правятся из главного окна).
            _viewModel.SetInstalledPlatformVersions(_installedPlatformVersions);

            // Сохраняем настройки синхронизации с файлом ibases.v8i.
            var filePath = SyncFilePathTextBox.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(SyncIntervalTextBox.Text, out var interval) || interval <= 0)
            {
                interval = 30;
            }
            var scheduleTime = SyncScheduleTimePicker.Text?.Trim() ?? string.Empty;
            _viewModel.ApplyIbasesSyncSettings(_syncMode, filePath, _syncTrigger, interval, scheduleTime,
                IbasesBackupEnabledCheck.IsChecked ?? true,
                int.TryParse(IbasesBackupKeepCountBox.Text, out var keep) && keep > 0 ? keep : 5);

            // Сохраняем настройки отображения списка баз.
            _viewModel.ApplyDisplaySettings(
                ShowFavoritesButtonCheck.IsChecked ?? false,
                ShowPinnedButtonCheck.IsChecked ?? false,
                ShowTagsCheck.IsChecked ?? false,
                ShowVersionColumnCheck.IsChecked ?? false,
                ShowLaunchModeColumnCheck.IsChecked ?? false,
                ShowServerColumnCheck.IsChecked ?? false,
                ShowLastLaunchColumnCheck.IsChecked ?? false,
                GroupByGroupCheck.IsChecked ?? true,
                ShowFavoritesOnlyCheck.IsChecked ?? false);
            _viewModel.ApplyAppBehaviorSettings(
                AllowMultipleInstancesCheck.IsChecked ?? false,
                ShowTagFilterPanelCheck.IsChecked ?? true);

            DialogResult = true;
        }

        /// <summary>
        /// Компаратор для сортировки версий по убыванию.
        /// Учитывает суффикс разрядности «(32)» / «(64)»: в пределах одной версии
        /// 64-битный вариант считается более новым.
        /// </summary>
        private sealed class VersionComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                var result = CompareCore(x, y);
                if (result != 0)
                    return result;

                // Версии совпадают — сравниваем разрядность (64 > 32).
                return GetArch(x).CompareTo(GetArch(y));
            }

            private static int CompareCore(string x, string y)
            {
                PlatformVersionService.ParseVariant(x, out var xv, out _);
                PlatformVersionService.ParseVariant(y, out var yv, out _);

                var xParts = xv.Split('.').Select(int.Parse).ToArray();
                var yParts = yv.Split('.').Select(int.Parse).ToArray();

                var length = Math.Max(xParts.Length, yParts.Length);
                for (var i = 0; i < length; i++)
                {
                    var xVal = i < xParts.Length ? xParts[i] : 0;
                    var yVal = i < yParts.Length ? yParts[i] : 0;
                    if (xVal != yVal)
                        return xVal.CompareTo(yVal);
                }

                return 0;
            }

            private static int GetArch(string variant)
            {
                PlatformVersionService.ParseVariant(variant, out _, out var architecture);
                return architecture == "64" ? 1 : 0;
            }
        }
    }
}