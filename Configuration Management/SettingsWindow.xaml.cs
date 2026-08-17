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
    /// «Платформы», «Отображение», «Клавиши», «Настройки», «ibases.v8i», «Базы» и «О программе».
    /// Управление группами — в основном окне (добавление/редактирование через список баз).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private List<string> _installedPlatformVersions;
        private readonly ObservableCollection<string> _additionalPlatformPaths = new();
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
        private readonly ObservableCollection<FavoriteHotkeyItem> _favoriteHotkeyItems = new();

        /// <summary>
        /// Создаёт диалог настроек приложения.
        /// </summary>
        /// <param name="viewModel">Главная модель представления приложения.</param>
        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _installedPlatformVersions = new List<string>(viewModel.InstalledPlatformVersions);
            foreach (var path in viewModel.AdditionalPlatformSearchPaths)
                _additionalPlatformPaths.Add(path);
            if (AdditionalPathsList != null)
                AdditionalPathsList.ItemsSource = _additionalPlatformPaths;
            UpdatePlatformsDisplay();
            InitializeSyncSettings();
            InitializeDisplaySettings();
            InitializeFavoriteHotkeys();
        }

        private void InitializeFavoriteHotkeys()
        {
            _favoriteHotkeyItems.Clear();
            int n = 1;
            foreach (var key in _viewModel.FavoriteHotkeyIds)
            {
                var ib = _viewModel.FindByFavoriteKey(key);
                _favoriteHotkeyItems.Add(new FavoriteHotkeyItem
                {
                    Key = key,
                    Number = n,
                    Name = ib?.Name ?? key
                });
                n++;
            }
            if (FavoriteHotkeysList != null)
                FavoriteHotkeysList.ItemsSource = _favoriteHotkeyItems;
        }

        private void RefreshFavoriteHotkeyNumbers()
        {
            var snapshot = _favoriteHotkeyItems.ToList();
            _favoriteHotkeyItems.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                snapshot[i].Number = i + 1;
                _favoriteHotkeyItems.Add(snapshot[i]);
            }
        }

        private void OnFavoriteHotkeyUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = FavoriteHotkeysList.SelectedIndex;
            if (idx <= 0) return;
            var item = _favoriteHotkeyItems[idx];
            _favoriteHotkeyItems.RemoveAt(idx);
            _favoriteHotkeyItems.Insert(idx - 1, item);
            RefreshFavoriteHotkeyNumbers();
            FavoriteHotkeysList.SelectedIndex = idx - 1;
        }

        private void OnFavoriteHotkeyDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = FavoriteHotkeysList.SelectedIndex;
            if (idx < 0 || idx >= _favoriteHotkeyItems.Count - 1) return;
            var item = _favoriteHotkeyItems[idx];
            _favoriteHotkeyItems.RemoveAt(idx);
            _favoriteHotkeyItems.Insert(idx + 1, item);
            RefreshFavoriteHotkeyNumbers();
            FavoriteHotkeysList.SelectedIndex = idx + 1;
        }

        private sealed class FavoriteHotkeyItem
        {
            public string Key { get; set; } = string.Empty;
            public int Number { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Display => $"Alt+{Number}: {Name}";
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
            if (ShowSizeColumnCheck != null)
                ShowSizeColumnCheck.IsChecked = _viewModel.ShowSizeColumn;

            ShowFavoritesButtonCheck.IsChecked = _showFavoritesButton;
            ShowPinnedButtonCheck.IsChecked = _showPinnedButton;
            ShowTagsCheck.IsChecked = _showTags;
            if (ShowTagFilterPanelCheck != null)
                ShowTagFilterPanelCheck.IsChecked = _viewModel.ShowTagFilterPanel;
            if (AllowMultipleInstancesCheck != null)
                AllowMultipleInstancesCheck.IsChecked = _viewModel.AllowMultipleInstances;
            if (ShowTrayIconCheck != null)
                ShowTrayIconCheck.IsChecked = _viewModel.ShowTrayIcon;
            if (CloseToTrayCheck != null)
                CloseToTrayCheck.IsChecked = _viewModel.CloseToTray;
            if (EscapeToTrayCheck != null)
                EscapeToTrayCheck.IsChecked = _viewModel.EscapeToTray;

            GroupByGroupCheck.IsChecked = _viewModel.GroupByGroup;
            ShowFavoritesOnlyCheck.IsChecked = _viewModel.ShowFavoritesOnly;

            if (ShowRightPanelDetailsCheck != null)
                ShowRightPanelDetailsCheck.IsChecked = _viewModel.ShowRightPanelDetails;
            if (ShowSessionLaunchPanelCheck != null)
                ShowSessionLaunchPanelCheck.IsChecked = _viewModel.ShowSessionLaunchPanel;
            if (StatusShowConnectionPathCheck != null)
                StatusShowConnectionPathCheck.IsChecked = _viewModel.StatusShowConnectionPath;
            if (StatusShowArchitectureCheck != null)
                StatusShowArchitectureCheck.IsChecked = _viewModel.StatusShowArchitecture;
            if (StatusShowLaunchModeCheck != null)
                StatusShowLaunchModeCheck.IsChecked = _viewModel.StatusShowLaunchMode;
            if (StatusShowPortCheck != null)
                StatusShowPortCheck.IsChecked = _viewModel.StatusShowPort;
            if (StatusShowPlatformVersionCheck != null)
                StatusShowPlatformVersionCheck.IsChecked = _viewModel.StatusShowPlatformVersion;
            if (StatusShowClientTypeCheck != null)
                StatusShowClientTypeCheck.IsChecked = _viewModel.StatusShowClientType;
            if (StatusShowConnectionTypeCheck != null)
                StatusShowConnectionTypeCheck.IsChecked = _viewModel.StatusShowConnectionType;
            if (StatusShowUserCheck != null)
                StatusShowUserCheck.IsChecked = _viewModel.StatusShowUser;
            if (StatusShowIdCheck != null)
                StatusShowIdCheck.IsChecked = _viewModel.StatusShowId;

            InitHotkeyCombos();
        }

        /// <summary>Доступные жесты для выпадающих списков («Нет» = не назначено).</summary>
        private static readonly string[] HotkeyChoices =
        {
            "Нет",
            "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Delete", "Insert",
            "Ctrl+F2", "Ctrl+F3", "Ctrl+F4",
            "Shift+Insert", "Ctrl+Insert",
            "Ctrl+Delete"
        };

        private void InitHotkeyCombos()
        {
            BindHotkeyCombo(HotkeyEnterpriseCombo, _viewModel.HotkeyEnterprise, "F3");
            BindHotkeyCombo(HotkeyConfiguratorCombo, _viewModel.HotkeyConfigurator, "F4");
            BindHotkeyCombo(HotkeyFavoriteCombo, _viewModel.HotkeyFavorite, "F8");
            BindHotkeyCombo(HotkeyEditCombo, _viewModel.HotkeyEdit, "F2");
            BindHotkeyCombo(HotkeyDeleteCombo, _viewModel.HotkeyDelete, "Delete");
            BindHotkeyCombo(HotkeyClearCacheCombo, _viewModel.HotkeyClearCache, "Нет");
            BindHotkeyCombo(HotkeyAddCombo, _viewModel.HotkeyAdd, "Insert");
            BindHotkeyCombo(HotkeyPinCombo, _viewModel.HotkeyPin, "Нет");
        }

        private static void BindHotkeyCombo(System.Windows.Controls.ComboBox? combo, string current, string fallback)
        {
            if (combo is null) return;
            combo.ItemsSource = null;
            combo.ItemsSource = HotkeyChoices;
            var value = string.IsNullOrWhiteSpace(current) ? "Нет" : current.Trim();
            if (!HotkeyChoices.Contains(value, StringComparer.OrdinalIgnoreCase))
                value = fallback;
            var idx = Array.FindIndex(HotkeyChoices, c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
            combo.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private static string ReadHotkeyCombo(System.Windows.Controls.ComboBox? combo, string fallback)
        {
            var s = combo?.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(s) || s == "Нет")
                return "";
            return s.Trim();
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
        /// Обновляет список установленных версий платформы, сканируя стандартные
        /// и дополнительные каталоги 1С.
        /// </summary>
        private void OnRefreshPlatforms_Click(object sender, RoutedEventArgs e)
        {
            PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformPaths);
            UpdatePlatformsDisplay();
            _viewModel.SetInstalledPlatformVersions(_installedPlatformVersions);
        }

        private void OnAddPlatformPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку с установкой платформы 1С (корень, 1cv8 или папка версии)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var path = dialog.SelectedPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (_additionalPlatformPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Этот путь уже добавлен в список.",
                    "Дополнительные пути", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _additionalPlatformPaths.Add(path);
            PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformPaths);
            UpdatePlatformsDisplay();
        }

        private void OnEditPlatformPath_Click(object sender, RoutedEventArgs e)
        {
            var selected = AdditionalPathsList?.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                MessageBox.Show("Выберите путь для изменения.",
                    "Дополнительные пути", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите новый путь (корень, 1cv8 или папка версии)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
                SelectedPath = selected
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var path = dialog.SelectedPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (_additionalPlatformPaths.Any(p =>
                    !string.Equals(p, selected, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Этот путь уже добавлен в список.",
                    "Дополнительные пути", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var index = _additionalPlatformPaths.IndexOf(selected);
            if (index < 0) return;
            _additionalPlatformPaths[index] = path;
            if (AdditionalPathsList != null)
                AdditionalPathsList.SelectedItem = path;
            PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformPaths);
            UpdatePlatformsDisplay();
        }

        private void OnRemovePlatformPath_Click(object sender, RoutedEventArgs e)
        {
            var selected = AdditionalPathsList?.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
            {
                MessageBox.Show("Выберите путь для удаления.",
                    "Дополнительные пути", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _additionalPlatformPaths.Remove(selected);
            PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformPaths);
            UpdatePlatformsDisplay();
        }

        /// <summary>
        /// Обновляет список платформ: линия (8.3) → разрядность → сборка с путём
        /// (тот же принцип, что в диалоге выбора версии).
        /// </summary>
        private void UpdatePlatformsDisplay()
        {
            PlatformsTree.Items.Clear();

            var infos = PlatformVersionService.FindInstalledVersionInfos(_additionalPlatformPaths);
            _installedPlatformVersions = infos.Select(i => i.Display).ToList();

            if (infos.Count == 0)
            {
                StatusText.Text = "Версии платформы 1С не найдены. Нажмите «Обновить список».";
                return;
            }

            var tree = PlatformVersionService.BuildGroupedTree(infos);
            foreach (var node in tree)
                PlatformsTree.Items.Add(node);

            StatusText.Text = $"Найдено версий: {infos.Count} (группировка: 8.3 → 8.3.27 → сборка)";

            // Разворачиваем линии 8.x, чтобы группировка была видна сразу
            Dispatcher.BeginInvoke(new Action(() => ExpandPlatformTreeGroups(PlatformsTree)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static void ExpandPlatformTreeGroups(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (item is not Models.PlatformVersionGroup node || node.IsLeaf)
                    continue;
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                    continue;
                container.IsExpanded = true;
                container.UpdateLayout();
                ExpandPlatformTreeGroups(container);
            }
        }

        private void OnExportInfobases_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ExportInfobasesCommand.Execute(null);
        }

        private void OnRemoveMissingFileBases_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.RemoveMissingFileBasesCommand.Execute(null);
            RefreshGroupsAfterDataChange();
        }

        private void OnKillOneCProcesses_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.KillOneCProcessesCommand.Execute(null);
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
            // Сохраняем версии платформы и дополнительные пути поиска.
            _viewModel.SetAdditionalPlatformSearchPaths(_additionalPlatformPaths);
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
                ShowFavoritesOnlyCheck.IsChecked ?? false,
                ShowSizeColumnCheck?.IsChecked ?? true);

            _viewModel.ShowRightPanelDetails = ShowRightPanelDetailsCheck?.IsChecked ?? true;
            _viewModel.ShowSessionLaunchPanel = ShowSessionLaunchPanelCheck?.IsChecked ?? true;
            _viewModel.ApplyStatusBarSettings(
                StatusShowConnectionPathCheck?.IsChecked ?? true,
                StatusShowArchitectureCheck?.IsChecked ?? true,
                StatusShowLaunchModeCheck?.IsChecked ?? true,
                StatusShowPortCheck?.IsChecked ?? true,
                StatusShowPlatformVersionCheck?.IsChecked ?? true,
                StatusShowClientTypeCheck?.IsChecked ?? false,
                StatusShowConnectionTypeCheck?.IsChecked ?? false,
                StatusShowUserCheck?.IsChecked ?? false,
                StatusShowIdCheck?.IsChecked ?? false);
            var hkEnterprise = ReadHotkeyCombo(HotkeyEnterpriseCombo, "F3");
            var hkConfigurator = ReadHotkeyCombo(HotkeyConfiguratorCombo, "F4");
            var hkFavorite = ReadHotkeyCombo(HotkeyFavoriteCombo, "");
            var hkEdit = ReadHotkeyCombo(HotkeyEditCombo, "");
            var hkDelete = ReadHotkeyCombo(HotkeyDeleteCombo, "");
            var hkClearCache = ReadHotkeyCombo(HotkeyClearCacheCombo, "");
            var hkAdd = ReadHotkeyCombo(HotkeyAddCombo, "");
            var hkPin = ReadHotkeyCombo(HotkeyPinCombo, "");

            // Проверка: одна клавиша — одно действие (пустые «Нет» не учитываются).
            var assigned = new (string Name, string Key)[]
            {
                ("1С:Предприятие", hkEnterprise),
                ("Конфигуратор", hkConfigurator),
                ("Избранное", hkFavorite),
                ("Изменить", hkEdit),
                ("Удалить", hkDelete),
                ("Очистить кэш", hkClearCache),
                ("Добавить базу", hkAdd),
                ("Закрепить", hkPin)
            };
            var duplicates = assigned
                .Where(a => !string.IsNullOrEmpty(a.Key))
                .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            if (duplicates.Count > 0)
            {
                var msg = string.Join("\n", duplicates.Select(g =>
                    $"«{g.Key}» назначена для: {string.Join(", ", g.Select(x => x.Name))}"));
                MessageBox.Show(
                    "Одна и та же клавиша не может быть назначена разным действиям:\n\n" + msg,
                    "Горячие клавиши",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _viewModel.ApplyAppBehaviorSettings(
                AllowMultipleInstancesCheck.IsChecked ?? false,
                ShowTagFilterPanelCheck.IsChecked ?? true,
                CloseToTrayCheck.IsChecked ?? false,
                ShowTrayIconCheck.IsChecked ?? true,
                hkEnterprise,
                hkConfigurator,
                hkFavorite,
                hkEdit,
                hkDelete,
                hkClearCache,
                hkAdd,
                hkPin,
                EscapeToTrayCheck.IsChecked ?? true);

            var templatePaths = TemplatePathsList?.Items.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                ?? new System.Collections.Generic.List<string>();
            _viewModel.SetTemplateCatalogPaths(templatePaths);


            // Порядок горячих клавиш избранного.
            _viewModel.SetFavoriteHotkeyOrder(_favoriteHotkeyItems.Select(i => i.Key));

            DialogResult = true;
        }


        private void OnAddTemplatePath_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Каталог шаблонов конфигураций 1С",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var path = dlg.SelectedPath;
            if (TemplatePathsList.Items.Cast<string>().Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                return;
            TemplatePathsList.Items.Add(path);
        }

        private void OnRemoveTemplatePath_Click(object sender, RoutedEventArgs e)
        {
            if (TemplatePathsList.SelectedItem is string path)
                TemplatePathsList.Items.Remove(path);
        }

        private void OnEditTemplatePath_Click(object sender, RoutedEventArgs e)
        {
            if (TemplatePathsList.SelectedItem is not string currentPath)
                return;

            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Изменить каталог шаблонов конфигураций 1С",
                UseDescriptionForTitle = true,
                SelectedPath = currentPath
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var path = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (TemplatePathsList.Items.Cast<string>().Any(x =>
                    !string.Equals(x, currentPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                return;

            var index = TemplatePathsList.Items.IndexOf(currentPath);
            if (index < 0) return;
            TemplatePathsList.Items[index] = path;
            TemplatePathsList.SelectedItem = path;
        }

        private void OnLoadDefaultTemplatePaths_Click(object sender, RoutedEventArgs e)
        {
            TemplatePathsList.Items.Clear();
            foreach (var p in Configuration_Management.Services.OneCTemplateService.GetTemplateRootFolders())
                TemplatePathsList.Items.Add(p);
            var def = Configuration_Management.Services.OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
            if (!string.IsNullOrEmpty(def) && !TemplatePathsList.Items.Cast<string>().Any(x => string.Equals(x, def, StringComparison.OrdinalIgnoreCase)))
                TemplatePathsList.Items.Insert(0, def);
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnAboutLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { /* ignore */ }
            }
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