#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Configuration_Management.Localization;
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
        private readonly ObservableCollection<FavoriteHotkeyItem> _favoriteHotkeyItems = new();
        private readonly ObservableCollection<ColumnOrderItem> _columnOrderItems = new();

        // ---- Шрифт интерфейса ----
        private readonly Dictionary<string, Models.ElementFontSettings> _elementFonts = new();
        private string _currentElement = Themes.ThemeManager.FontDefault;

        // ---- Резервное копирование профиля ----
        private System.Windows.Controls.TextBox _profileDirBox = null!;
        private System.Windows.Controls.CheckBox _profileRestoreCheck = null!;

        // ---- Цветовое оформление ----
        private ColorScheme _colorScheme = ColorScheme.CreateLight();
        private readonly ObservableCollection<ColorItem> _colorItems = new();
        /// <summary>
        /// Рабочие копии схем по идентификатору темы (встроенной «Светлая»/«Тёмная»
        /// или пользовательской). Хранят незаконченные правки каждой темы отдельно,
        /// поэтому переключение между темами не сбрасывает внесённые изменения.
        /// </summary>
        private readonly Dictionary<string, ColorScheme> _editingSchemes = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Идентификаторы тем, реально изменённых в ходе редактирования (сохраняются при нажатии «ОК»).</summary>
        private readonly HashSet<string> _dirtySchemes = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSchemeEvent;
        private const string BuiltInLightName = "Светлая";
        private const string BuiltInDarkName = "Тёмная";

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
            InitializeDefaultArchitecture();
            InitializeSyncSettings();
            InitializeDisplaySettings();
            InitializeFontSettings();
            InitializeFavoriteHotkeys();
            InitializeExportTimestampSettings();
            InitializeColorSchemes();
            InitializeLanguage();
            InitializeProfileBackupTab();
        }

        /// <summary>
        /// Строит вкладку «Резервное копирование» профиля: каталог, флажок восстановления
        /// при запуске и кнопки «Сохранить профиль» / «Восстановить профиль».
        /// </summary>
        private void InitializeProfileBackupTab()
        {
            var tab = new TabItem();
            try { tab.Style = (Style)FindResource("SettingsTabItem"); } catch { /* стандартный вид */ }
            var tabIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.BackupRestore,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            tabIcon.SetBinding(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.TabItem), 1)
                });
            var tabHeader = new StackPanel { Orientation = Orientation.Horizontal };
            tabHeader.Children.Add(tabIcon);
            tabHeader.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.TabProfile"),
                VerticalAlignment = VerticalAlignment.Center
            });
            tab.Header = tabHeader;

            var panel = new StackPanel { Margin = new Thickness(4, 12, 4, 0) };

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.Description"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.Includes"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Каталог резервной копии.
            var dirGroup = new GroupBox
            {
                Header = LocalizationManager.T("Settings.Profile.Directory"),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var dirDock = new DockPanel();
            var browse = new Button
            {
                Content = LocalizationManager.T("Settings.Profile.Browse"),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(browse, Dock.Right);
            browse.Click += (_, _) => OnBrowseProfileDir_Click();
            _profileDirBox = new TextBox
            {
                Text = _viewModel.ProfileBackupDirectory,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            dirDock.Children.Add(browse);
            dirDock.Children.Add(_profileDirBox);
            dirGroup.Content = dirDock;
            panel.Children.Add(dirGroup);

            _profileRestoreCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.Profile.RestoreOnStartup"),
                IsChecked = _viewModel.ProfileRestoreOnStartup,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(_profileRestoreCheck);
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.RestoreOnStartupHint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(24, 0, 0, 12)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var backup = new Button { Content = LocalizationManager.T("Settings.Profile.BackupNow"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            backup.Click += (_, _) => OnProfileBackup_Click();
            var restore = new Button { Content = LocalizationManager.T("Settings.Profile.RestoreNow"), Padding = new Thickness(12, 6, 12, 6) };
            restore.Click += (_, _) => OnProfileRestore_Click();
            buttons.Children.Add(backup);
            buttons.Children.Add(restore);
            panel.Children.Add(buttons);

            tab.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // Вставляем перед последней вкладкой («О программе»), чтобы она шла выше.
            SettingsTabs.Items.Insert(Math.Max(0, SettingsTabs.Items.Count - 1), tab);
        }

        /// <summary>Кисть вторичного текста темы (с запасным серым цветом).</summary>
        private Brush SecondaryBrush()
        {
            try { return (Brush)FindResource("TextSecondaryBrush"); }
            catch { return Brushes.Gray; }
        }

        /// <summary>Выбор каталога резервной копии профиля.</summary>
        private void OnBrowseProfileDir_Click()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("Settings.Profile.Directory"),
                SelectedPath = _profileDirBox?.Text ?? string.Empty
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath)
                && _profileDirBox is not null)
                _profileDirBox.Text = dlg.SelectedPath;
        }

        /// <summary>«Сохранить профиль»: копирует настройки в выбранный каталог.</summary>
        private void OnProfileBackup_Click()
        {
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);
            _viewModel.BackupProfile();
        }

        /// <summary>«Восстановить профиль»: копирует настройки из каталога и закрывает окно.</summary>
        private void OnProfileRestore_Click()
        {
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);
            if (_viewModel.RestoreProfile())
            {
                DialogResult = true;
                Close();
            }
        }

        /// <summary>Заполняет список доступных языков интерфейса и выбирает текущий.</summary>
        private void InitializeLanguage()
        {
            if (LanguageComboBox == null)
                return;

            LanguageComboBox.ItemsSource = LocalizationManager.Instance.AvailableLanguages.ToList();
            LanguageComboBox.DisplayMemberPath = "Name";
            LanguageComboBox.SelectedItem = LocalizationManager.Instance.AvailableLanguages
                .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage);
        }

        private void OnLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox?.SelectedItem is LanguageInfo li &&
                !string.Equals(li.Code, LocalizationManager.Instance.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.ApplyLanguage(li.Code);
                // Перестраиваем список тем: отображаемые подписи встроенных тем
                // локализованы и должны обновиться при смене языка. Сохранённое имя
                // (канонический ключ «Светлая»/«Тёмная») не меняется.
                RefreshSchemeComboBox();
            }
        }

        /// <summary>Переключатель компактного режима: применяет изменение сразу и сохраняет.</summary>
        private void OnCompactMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (CompactModeCheck is null)
                return;
            _viewModel.ApplyCompactMode(CompactModeCheck.IsChecked == true);
        }

        // ===================== Цветовое оформление =====================

        /// <summary>
        /// Инициализирует вкладку «Цветовое оформление»: загружает активную схему,
        /// заполняет список тем и редактор цветов.
        /// </summary>
        private void InitializeColorSchemes()
        {
            _colorScheme = _viewModel.ActiveColorScheme.Clone();
            // Регистрируем активную тему в карте правок, чтобы её настройки сохранялись
            // при переключении на другие темы и обратно.
            _editingSchemes[_colorScheme.Name] = _colorScheme;
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
                if (!items.Any(i => string.Equals(i.Name, _colorScheme.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add(new SchemeComboItem { Name = _colorScheme.Name, IsBuiltIn = false });
                }
                foreach (var it in items)
                    SchemeComboBox.Items.Add(it);

                var selected = items.FirstOrDefault(i =>
                    string.Equals(i.Name, _colorScheme.Name, StringComparison.OrdinalIgnoreCase));
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
            var all = _viewModel.AvailableColorSchemes();
            for (int i = 0; i < all.Count; i++)
            {
                items.Add(new SchemeComboItem { Name = all[i].Name, IsBuiltIn = i < 2, DisplayName = LocalizedBuiltInName(all[i].Name) });
            }
            return items;
        }

        /// <summary>
        /// Возвращает локализованное отображаемое имя встроенной темы.
        /// Идентификатор схемы (<c>Name</c>) остаётся неизменным, поэтому сохранение/загрузка и сравнения не ломаются.
        /// </summary>
        private static string LocalizedBuiltInName(string name)
        {
            if (string.Equals(name, BuiltInLightName, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.T("Theme.Light");
            if (string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.T("Theme.Dark");
            return name;
        }

        /// <summary>Обновляет список редактируемых цветов из текущей схемы.</summary>
        private void RefreshColorItems()
        {
            _colorItems.Clear();
            foreach (var (key, label) in ColorScheme.Definitions)
            {
                _colorItems.Add(new ColorItem { Key = key, Label = label, Hex = _colorScheme.Get(key) });
            }
        }

        /// <summary>Возвращает схему по имени из доступных (встроенных или пользовательских).</summary>
        private ColorScheme? ResolveScheme(string name)
        {
            return _viewModel.AvailableColorSchemes()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Clone();
        }

        /// <summary>true, если имя соответствует встроенной теме («Светлая»/«Тёмная»).</summary>
        private static bool IsBuiltInName(string name)
            => string.Equals(name, BuiltInLightName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Возвращает рабочую копию схемы для редактирования по идентификатору темы.
        /// Если тема уже открыта в редакторе (есть незаконченные правки) — возвращает её,
        /// иначе загружает сохранённое состояние (слот базовой темы для встроенных,
        /// JSON-файл для пользовательских). Так каждая тема хранит собственные настройки
        /// во время редактирования, и переключение между темами не теряет правки.
        /// </summary>
        private ColorScheme LoadEditableScheme(string name)
        {
            if (_editingSchemes.TryGetValue(name, out var cached))
                return cached;

            ColorScheme? source;
            if (IsBuiltInName(name))
            {
                var dark = string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase);
                source = _viewModel.GetSchemeForTheme(
                    dark ? Themes.ThemeManager.DarkThemeName : Themes.ThemeManager.LightThemeName).Clone();
                source.Name = dark ? BuiltInDarkName : BuiltInLightName;
            }
            else
            {
                source = ResolveScheme(name);
            }

            if (source is null)
                source = ColorScheme.Create(name, false);

            _editingSchemes[name] = source;
            return source;
        }

        /// <summary>
        /// Сохраняет все темы, изменённые во время редактирования вкладки «Цветовое оформление».
        /// Встроенные темы сохраняются в слот соответствующей базовой темы (светлой/тёмной),
        /// пользовательские — в их JSON-файл. Правки одной темы не затрагивают остальные.
        /// </summary>
        private void PersistEditedSchemes()
        {
            foreach (var name in _dirtySchemes.ToList())
            {
                if (!_editingSchemes.TryGetValue(name, out var scheme))
                    continue;
                if (IsBuiltInName(name))
                    _viewModel.SaveColorSchemeSlot(scheme);
                else
                    _viewModel.SaveCustomColorScheme(scheme);
            }
            _dirtySchemes.Clear();
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
                // пользовательские — свой JSON-файл.
                var scheme = LoadEditableScheme(item.Name);

                if (scheme != null)
                {
                    _colorScheme = scheme;
                    ThemeDebug($"SchemeCombo select '{item.Name}' -> '{_colorScheme.Name}' (isDark={_colorScheme.IsDark}, colors={_colorScheme.Colors.Count})");
                    RefreshColorItems();
                    UpdateSchemeButtons();
                }
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
            _viewModel.PreviewColorScheme(_colorScheme);
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
                _colorScheme.Colors[item.Key] = picker.Result;
                // Фиксируем правку именно этой темы, чтобы она сохранилась независимо.
                _editingSchemes[_colorScheme.Name] = _colorScheme;
                _dirtySchemes.Add(_colorScheme.Name);
            }
        }

        /// <summary>Создаёт собственную тему на основе текущих цветов.</summary>
        private void OnCreateScheme_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptForName(LocalizationManager.T("Settings.CreateTheme"), string.Format(LocalizationManager.T("Settings.CopyOf"), _colorScheme.Name));
            if (string.IsNullOrWhiteSpace(name))
                return;
            name = name.Trim();
            if (IsReservedName(name))
            {
                MessageBox.Show(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.CreateTheme"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var copy = _colorScheme.Clone();
            copy.Name = name;
            _viewModel.SaveCustomColorScheme(copy);
            _colorScheme = copy;
            // Регистрируем новую тему в карте правок для дальнейшего редактирования.
            _editingSchemes[name] = copy;
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
            if (IsReservedName(name))
            {
                MessageBox.Show(LocalizationManager.T("Settings.ReservedName"),
                    LocalizationManager.T("Settings.RenameThemeTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Сохраняем под новым именем и удаляем старый файл. Если тема уже открыта
            // в редакторе с незаконченными правками — переносим её рабочую копию на новое имя.
            var toSave = _editingSchemes.TryGetValue(item.Name, out var working) ? working : ResolveScheme(item.Name);
            if (toSave != null)
            {
                _viewModel.DeleteCustomColorScheme(item.Name);
                toSave.Name = name;
                _viewModel.SaveCustomColorScheme(toSave);
                _editingSchemes.Remove(item.Name);
                _editingSchemes[name] = toSave;
                if (_dirtySchemes.Remove(item.Name))
                    _dirtySchemes.Add(name);
            }

            if (string.Equals(_colorScheme.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                _colorScheme.Name = name;
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

            _viewModel.DeleteCustomColorScheme(item.Name);
            _editingSchemes.Remove(item.Name);
            _dirtySchemes.Remove(item.Name);

            // Если удалили активную — переключаемся на базовую встроенную тему.
            if (string.Equals(_colorScheme.Name, item.Name, StringComparison.OrdinalIgnoreCase))
            {
                _colorScheme = _colorScheme.IsDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
                _editingSchemes[_colorScheme.Name] = _colorScheme;
            }
            RefreshSchemeComboBox();
            RefreshColorItems();
        }

        /// <summary>Сбрасывает цвета выбранной темы на значения по умолчанию.</summary>
        private void OnResetSchemeColors_Click(object sender, RoutedEventArgs e)
        {
            // Сбрасываем цвета ТОЛЬКО выбранной темы: остальные схемы не затрагиваются.
            var wasDark = _colorScheme.IsDark;
            var name = _colorScheme.Name;
            _colorScheme = ColorScheme.Create(name, wasDark);
            _editingSchemes[name] = _colorScheme;
            _dirtySchemes.Add(name);
            RefreshColorItems();
        }

        /// <summary>Выгружает текущую тему в JSON-файл.</summary>
        private void OnExportScheme_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationManager.T("Settings.ExportSchemeTitle"),
                Filter = $"{LocalizationManager.T("Settings.ColorSchemeFilter")}|*.json|{LocalizationManager.T("Common.AllFiles")}|*.*",
                FileName = _colorScheme.Name + ".json",
                DefaultExt = ".json",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                _viewModel.ExportColorScheme(_colorScheme, dialog.FileName);
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

            _viewModel.SaveCustomColorScheme(scheme);
            _colorScheme = scheme;
            _editingSchemes[scheme.Name] = scheme;
            RefreshSchemeComboBox();
            RefreshColorItems();
            MessageBox.Show(string.Format(LocalizationManager.T("Settings.ImportedOk"), scheme.Name),
                LocalizationManager.T("Settings.ImportDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool IsReservedName(string name)
        {
            return string.Equals(name, BuiltInLightName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, BuiltInDarkName, StringComparison.OrdinalIgnoreCase);
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

        /// <summary>Элемент списка тем.</summary>
        private sealed class SchemeComboItem
        {
            public string Name { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public bool IsBuiltIn { get; set; }
            public override string ToString() => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
        }

        /// <summary>Элемент редактора цветов: подпись, ключ, HEX и кисть-образец.</summary>
        private sealed class ColorItem : INotifyPropertyChanged
        {
            public string Key { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;

            private string _hex = "#000000";
            public string Hex
            {
                get => _hex;
                set
                {
                    _hex = value;
                    OnPropertyChanged(nameof(Hex));
                    ColorBrush = ParseBrush(value);
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }

            private SolidColorBrush _colorBrush = new(Colors.Black);
            public SolidColorBrush ColorBrush
            {
                get => _colorBrush;
                private set { _colorBrush = value; OnPropertyChanged(nameof(ColorBrush)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private static SolidColorBrush ParseBrush(string hex)
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                }
                catch
                {
                    return new SolidColorBrush(Colors.Transparent);
                }
            }
        }

        /// <summary>
        /// Инициализирует выпадающий список «Разрядность по умолчанию» во вкладке «Платформы».
        /// </summary>
        private void InitializeDefaultArchitecture()
        {
            DefaultArchComboBox.Items.Clear();
            DefaultArchComboBox.Items.Add(LocalizationManager.T("Settings.Arch64Recommended"));
            DefaultArchComboBox.Items.Add(LocalizationManager.T("Settings.Arch32"));
            DefaultArchComboBox.SelectedIndex =
                string.Equals(_viewModel.DefaultArchitecture, "X64", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
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

        /// <summary>
        /// Инициализирует выбор шаблона (формата) даты и времени для имени файла при выгрузке:
        /// заполняет список популярных шаблонов и показывает предпросмотр текущего.
        /// </summary>
        private void InitializeExportTimestampSettings()
        {
            if (ExportTimestampFormatComboBox == null)
                return;

            ExportTimestampFormatComboBox.Items.Clear();
            string[] templates =
            {
                "yyyyMMdd_HHmmss",
                "yyyy-MM-dd_HH-mm-ss",
                "yyyy-MM-dd_HHmmss",
                "dd.MM.yyyy HH-mm-ss",
                "yyyyMMdd",
                "HHmmss"
            };
            foreach (var t in templates)
                ExportTimestampFormatComboBox.Items.Add(t);

            var current = _viewModel.ExportTimestampFormat;
            ExportTimestampFormatComboBox.Text = string.IsNullOrWhiteSpace(current) ? "yyyyMMdd_HHmmss" : current;
            UpdateExportTimestampPreview();
        }

        /// <summary>Обновляет текст предпросмотра отметки даты и времени по выбранному шаблону.</summary>
        private void UpdateExportTimestampPreview()
        {
            if (ExportTimestampPreview == null)
                return;

            var format = ExportTimestampFormatComboBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(format))
            {
                ExportTimestampPreview.Text = LocalizationManager.T("Settings.TimestampSpecifyHint");
                return;
            }

            try
            {
                var preview = string.Format(LocalizationManager.T("Settings.TimestampBasePrefix"), DateTime.Now.ToString(format));
                ExportTimestampPreview.Text = string.Format(LocalizationManager.T("Settings.TimestampExample"), preview);
            }
            catch (FormatException)
            {
                ExportTimestampPreview.Text = LocalizationManager.T("Settings.TimestampInvalid");
            }
        }

        private void OnExportTimestampFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateExportTimestampPreview();
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

        /// <summary>Элемент списка колонок: хранит ключ, имя и флаг видимости.
        /// Один элемент объединяет порядок и видимость колонки — оба редактируются
        /// в одном списке на вкладке «Отображение».</summary>
        private sealed class ColumnOrderItem
        {
            public string Key { get; init; } = string.Empty;
            public string Display { get; init; } = string.Empty;
            public bool Visible { get; set; } = true;

            public override string ToString() => Display;
        }

        /// <summary>Локализованное название колонки по её ключу.</summary>
        private static string ColumnOrderLabel(string key) => LocalizationManager.T(key switch
        {
            "Version" => "Column.Version",
            "Configuration" => "Column.Configuration",
            "LaunchMode" => "Column.LaunchMode",
            "ServerBase" => "Column.ServerBase",
            "LastLaunch" => "Column.LastLaunch",
            "Size" => "Column.Size",
            "Actions" => "Column.Actions",
            _ => "Column.Name"
        });

        /// <summary>Видимость колонки по её ключу из текущих настроек.</summary>
        private bool ColumnVisible(string key) => key switch
        {
            "Version" => _viewModel.ShowVersionColumn,
            "Configuration" => _viewModel.ShowConfigurationColumn,
            "LaunchMode" => _viewModel.ShowLaunchModeColumn,
            "ServerBase" => _viewModel.ShowServerColumn,
            "LastLaunch" => _viewModel.ShowLastLaunchColumn,
            "Size" => _viewModel.ShowSizeColumn,
            _ => true
        };

        /// <summary>Заполняет список порядка колонок текущим порядком из настроек.</summary>
        private void InitializeColumnOrder()
        {
            _columnOrderItems.Clear();
            foreach (var key in _viewModel.ColumnOrderKeys)
                _columnOrderItems.Add(new ColumnOrderItem { Key = key, Display = ColumnOrderLabel(key), Visible = ColumnVisible(key) });
            if (ColumnOrderList != null)
                ColumnOrderList.ItemsSource = _columnOrderItems;
            UpdateColumnOrderButtons();
        }

        /// <summary>Обновляет доступность кнопок «Вверх»/«Вниз» по выбранной строке.</summary>
        private void UpdateColumnOrderButtons()
        {
            if (ColumnOrderList == null)
                return;
            var idx = ColumnOrderList.SelectedIndex;
            if (ColumnOrderUpButton != null)
                ColumnOrderUpButton.IsEnabled = idx > 0;
            if (ColumnOrderDownButton != null)
                ColumnOrderDownButton.IsEnabled = idx >= 0 && idx < _columnOrderItems.Count - 1;
        }

        private void OnColumnOrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateColumnOrderButtons();

        private void OnColumnOrderUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = ColumnOrderList?.SelectedIndex ?? -1;
            if (idx <= 0 || ColumnOrderList == null)
                return;
            var item = _columnOrderItems[idx];
            _columnOrderItems.Move(idx, idx - 1);
            ColumnOrderList.SelectedIndex = idx - 1;
            UpdateColumnOrderButtons();
        }

        private void OnColumnOrderDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = ColumnOrderList?.SelectedIndex ?? -1;
            if (idx < 0 || idx >= _columnOrderItems.Count - 1 || ColumnOrderList == null)
                return;
            var item = _columnOrderItems[idx];
            _columnOrderItems.Move(idx, idx + 1);
            ColumnOrderList.SelectedIndex = idx + 1;
            UpdateColumnOrderButtons();
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

            // Видимость колонок (кроме закреплённой «Название») задаётся в том же
            // списке, что и порядок: флажки заполняются в InitializeColumnOrder.
            ShowNameColumnCheck.IsChecked = true;

            InitializeColumnOrder();

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
            if (AfterLaunchActionCombo != null)
            {
                AfterLaunchActionCombo.ItemsSource = new[]
                {
                    LocalizationManager.T("Settings.General.AfterLaunchAction.None"),
                    LocalizationManager.T("Settings.General.AfterLaunchAction.MinimizeToTray"),
                    LocalizationManager.T("Settings.General.AfterLaunchAction.Close")
                };
                AfterLaunchActionCombo.SelectedIndex = (int)Models.AfterLaunchActionHelper.Parse(_viewModel.AfterLaunchAction);
            }
            if (RememberWindowLayoutCheck != null)
                RememberWindowLayoutCheck.IsChecked = _viewModel.RememberWindowLayout;
            if (CompactModeCheck != null)
                CompactModeCheck.IsChecked = _viewModel.CompactMode;

            GroupByGroupCheck.IsChecked = _viewModel.GroupByGroup;
            ShowFavoritesOnlyCheck.IsChecked = _viewModel.ShowFavoritesOnly;
            if (ShowEmptyGroupsCheck != null)
                ShowEmptyGroupsCheck.IsChecked = _viewModel.ShowEmptyGroups;
            if (AddTimestampToExportFileNameCheck != null)
                AddTimestampToExportFileNameCheck.IsChecked = _viewModel.AddTimestampToExportFileName;

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

        // ===================== Шрифт интерфейса =====================

        /// <summary>Элемент списка начертаний шрифта.</summary>
        private sealed class FontFaceItem
        {
            public string Key { get; init; } = string.Empty;
            public string Weight { get; init; } = "Normal";
            public string Style { get; init; } = "Normal";
            public override string ToString() => LocalizationManager.T(Key);
        }

        /// <summary>Доступные начертания шрифта. Технические Weight/Style не локализуются.</summary>
        private static readonly FontFaceItem[] FontFaces =
        {
            new() { Key = "Settings.Font.StyleNormal", Weight = "Normal", Style = "Normal" },
            new() { Key = "Settings.Font.StyleBold", Weight = "Bold", Style = "Normal" },
            new() { Key = "Settings.Font.StyleItalic", Weight = "Normal", Style = "Italic" },
            new() { Key = "Settings.Font.StyleBoldItalic", Weight = "Bold", Style = "Italic" }
        };

        /// <summary>Элемент списка областей интерфейса для выбора шрифта.</summary>
        private sealed class ElementScopeItem
        {
            public string Key { get; }
            public ElementScopeItem(string key) { Key = key; }
            public override string ToString() => Themes.ThemeManager.FontScopeDisplayName(Key);
        }

        /// <summary>Инициализирует подвкладку «Шрифт»: области, семейства, размеры и начертания.</summary>
        private void InitializeFontSettings()
        {
            // Загружаем настройки элементов.
            _elementFonts.Clear();
            foreach (var kvp in _viewModel.ElementFonts)
                _elementFonts[kvp.Key] = kvp.Value?.Clone() ?? new Models.ElementFontSettings();
            // «По умолчанию» всегда присутствует — из общих настроек шрифта.
            if (!_elementFonts.ContainsKey(Themes.ThemeManager.FontDefault))
            {
                _elementFonts[Themes.ThemeManager.FontDefault] = new Models.ElementFontSettings
                {
                    FontFamily = _viewModel.FontFamily,
                    FontSize = _viewModel.FontSize,
                    FontWeight = _viewModel.FontWeight,
                    FontStyle = _viewModel.FontStyle
                };
            }

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

            var fs = _elementFonts.TryGetValue(_currentElement, out var f) && f is not null
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
            if (!_elementFonts.TryGetValue(_currentElement, out var fs) || fs is null)
            {
                fs = new Models.ElementFontSettings();
                _elementFonts[_currentElement] = fs;
            }
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
            _viewModel.PreviewElementFonts(_elementFonts);
        }

        /// <summary>Инициализирует поля ввода горячих клавиш (запись комбинаций Ctrl/Shift/Alt).</summary>
        private void InitHotkeyCombos()
        {
            BindHotkeyBox(HotkeyEnterpriseBox, _viewModel.HotkeyEnterprise);
            BindHotkeyBox(HotkeyConfiguratorBox, _viewModel.HotkeyConfigurator);
            BindHotkeyBox(HotkeyFavoriteBox, _viewModel.HotkeyFavorite);
            BindHotkeyBox(HotkeyEditBox, _viewModel.HotkeyEdit);
            BindHotkeyBox(HotkeyDeleteBox, _viewModel.HotkeyDelete);
            BindHotkeyBox(HotkeyClearCacheBox, _viewModel.HotkeyClearCache);
            BindHotkeyBox(HotkeyAddBox, _viewModel.HotkeyAdd);
            BindHotkeyBox(HotkeyPinBox, _viewModel.HotkeyPin);
            BindHotkeyBox(HotkeyShowAllBox, _viewModel.HotkeyShowAll);
            BindHotkeyBox(HotkeyShowFavoritesBox, _viewModel.HotkeyShowFavorites);
            BindHotkeyBox(HotkeyShowRecentBox, _viewModel.HotkeyShowRecent);
        }

        private static void BindHotkeyBox(Controls.HotkeyBox? box, string current)
        {
            if (box is null) return;
            box.Value = current?.Trim() ?? string.Empty;
        }

        private static string ReadHotkeyBox(Controls.HotkeyBox? box)
        {
            var s = box?.Value;
            // Поле «Нет»/«None» показывает локализованный Common.None при пустом назначении.
            if (string.IsNullOrWhiteSpace(s) ||
                string.Equals(s, LocalizationManager.T("Common.None"), StringComparison.Ordinal))
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

            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeDisabled"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeImport"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeExport"));
            SyncModeComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.SyncModeBoth"));
            SyncModeComboBox.SelectedIndex = (int)_syncMode;

            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerStartup"));
            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerInterval"));
            SyncTriggerComboBox.Items.Add(LocalizationManager.T("Settings.Ibases.TriggerSchedule"));
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
                SyncStatusText.Text = LocalizationManager.T("Settings.Ibases.StatusDisabled");
            }
            else if (string.IsNullOrWhiteSpace(path))
            {
                SyncStatusText.Text = LocalizationManager.T("Settings.Ibases.StatusFileNotFound");
            }
            else
            {
                var modeText = _syncMode switch
                {
                    IbasesSyncMode.Import => LocalizationManager.T("Settings.Ibases.ModeImportShort"),
                    IbasesSyncMode.Export => LocalizationManager.T("Settings.Ibases.ModeExportShort"),
                    _ => LocalizationManager.T("Settings.Ibases.ModeBothShort")
                };
                var triggerText = _syncTrigger switch
                {
                    IbasesSyncTrigger.Interval => string.Format(LocalizationManager.T("Settings.Ibases.TriggerIntervalShort"), _syncIntervalMinutes),
                    IbasesSyncTrigger.Schedule => string.Format(LocalizationManager.T("Settings.Ibases.TriggerScheduleShort"), _syncScheduleTime),
                    _ => LocalizationManager.T("Settings.Ibases.TriggerStartupShort")
                };
                SyncStatusText.Text = string.Format(LocalizationManager.T("Settings.Ibases.StatusFormat"), path, modeText, triggerText);
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
                Title = LocalizationManager.T("Settings.Ibases.FileDialogTitle"),
                Filter = $"{LocalizationManager.T("Settings.Ibases.FileFilter")}|*.v8i|{LocalizationManager.T("Common.AllFiles")}|*.*",
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
            var filePath = ResolveDisplayPath();
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
                Description = LocalizationManager.T("Settings.ChoosePlatformFolder"),
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
                MessageBox.Show(LocalizationManager.T("Settings.PathAlreadyAdded"),
                    LocalizationManager.T("Settings.AdditionalPathsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(LocalizationManager.T("Settings.SelectPathToEdit"),
                    LocalizationManager.T("Settings.AdditionalPathsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("Settings.ChooseNewPlatformFolder"),
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
                MessageBox.Show(LocalizationManager.T("Settings.PathAlreadyAdded"),
                    LocalizationManager.T("Settings.AdditionalPathsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(LocalizationManager.T("Settings.SelectPathToRemove"),
                    LocalizationManager.T("Settings.AdditionalPathsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                StatusText.Text = LocalizationManager.T("Settings.PlatformsNotFound");
                return;
            }

            var tree = PlatformVersionService.BuildGroupedTree(infos);
            foreach (var node in tree)
                PlatformsTree.Items.Add(node);

            StatusText.Text = string.Format(LocalizationManager.T("Settings.PlatformsFound"), infos.Count);

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
                MessageBox.Show(LocalizationManager.T("Settings.Ibases.RestoreNoPath"), LocalizationManager.T("Settings.Ibases.RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var backups = Services.IbasesBackupService.ListBackups(filePath);
            if (backups.Count == 0)
            {
                MessageBox.Show(string.Format(LocalizationManager.T("Settings.Ibases.RestoreNoBackups"), filePath),
                    LocalizationManager.T("Settings.Ibases.RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var latest = backups[0];
            var result = MessageBox.Show(
                string.Format(LocalizationManager.T("Settings.Ibases.RestoreConfirm"), System.IO.Path.GetFileName(latest)),
                LocalizationManager.T("Settings.Ibases.RestoreConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Services.IbasesBackupService.RestoreBackup(latest, filePath);
                MessageBox.Show(LocalizationManager.T("Settings.Ibases.RestoreOk"),
                    LocalizationManager.T("Settings.Ibases.RestoreTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(LocalizationManager.T("Settings.Ibases.RestoreFailed"), ex.Message),
                    LocalizationManager.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            // Сохраняем версии платформы и дополнительные пути поиска.
            _viewModel.SetAdditionalPlatformSearchPaths(_additionalPlatformPaths);
            _viewModel.SetInstalledPlatformVersions(_installedPlatformVersions);

            // Разрядность по умолчанию (Настройки → Платформы).
            _viewModel.ApplyDefaultArchitecture(DefaultArchComboBox.SelectedIndex == 0 ? "X64" : "X86");

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

            // Сохраняем настройки резервного копирования профиля.
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);

            // Сохраняем настройки отображения списка баз.
            // Видимость колонок читается из тех же элементов списка, где задаётся
            // и порядок: флажок каждой строки и есть её видимость.
            bool VisibleOf(string key) => _columnOrderItems.FirstOrDefault(i => i.Key == key)?.Visible ?? true;

            _viewModel.ApplyDisplaySettings(
                ShowFavoritesButtonCheck.IsChecked ?? false,
                ShowPinnedButtonCheck.IsChecked ?? false,
                ShowTagsCheck.IsChecked ?? false,
                VisibleOf("Version"),
                VisibleOf("LaunchMode"),
                VisibleOf("ServerBase"),
                VisibleOf("LastLaunch"),
                GroupByGroupCheck.IsChecked ?? true,
                ShowFavoritesOnlyCheck.IsChecked ?? false,
                VisibleOf("Size"),
                VisibleOf("Configuration"),
                ShowEmptyGroupsCheck?.IsChecked ?? false,
                _columnOrderItems.Select(i => i.Key).ToList());

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
            var hkEnterprise = ReadHotkeyBox(HotkeyEnterpriseBox);
            var hkConfigurator = ReadHotkeyBox(HotkeyConfiguratorBox);
            var hkFavorite = ReadHotkeyBox(HotkeyFavoriteBox);
            var hkEdit = ReadHotkeyBox(HotkeyEditBox);
            var hkDelete = ReadHotkeyBox(HotkeyDeleteBox);
            var hkClearCache = ReadHotkeyBox(HotkeyClearCacheBox);
            var hkAdd = ReadHotkeyBox(HotkeyAddBox);
            var hkPin = ReadHotkeyBox(HotkeyPinBox);
            var hkShowAll = ReadHotkeyBox(HotkeyShowAllBox);
            var hkShowFavorites = ReadHotkeyBox(HotkeyShowFavoritesBox);
            var hkShowRecent = ReadHotkeyBox(HotkeyShowRecentBox);

            // Проверка: одна клавиша — одно действие (пустые «Нет» не учитываются).
            var assigned = new (string Name, string Key)[]
            {
                (LocalizationManager.T("Main.Enterprise"), hkEnterprise),
                (LocalizationManager.T("Main.SectionConfigurator"), hkConfigurator),
                (LocalizationManager.T("Main.Favorites"), hkFavorite),
                (LocalizationManager.T("Main.EditShort"), hkEdit),
                (LocalizationManager.T("Common.Delete"), hkDelete),
                (LocalizationManager.T("Main.ClearCache"), hkClearCache),
                (LocalizationManager.T("Main.AddBase"), hkAdd),
                (LocalizationManager.T("Main.Pin"), hkPin),
                (LocalizationManager.T("Main.AllBasesTooltip"), hkShowAll),
                (LocalizationManager.T("Main.FavoritesTooltip"), hkShowFavorites),
                (LocalizationManager.T("Main.RecentTooltip"), hkShowRecent)
            };
            var duplicates = assigned
                .Where(a => !string.IsNullOrEmpty(a.Key))
                .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            if (duplicates.Count > 0)
            {
                var msg = string.Join("\n", duplicates.Select(g =>
                    string.Format(LocalizationManager.T("Settings.Hotkeys.AssignedTo"), g.Key,
                        string.Join(", ", g.Select(x => x.Name)))));
                MessageBox.Show(
                    string.Format(LocalizationManager.T("Settings.Hotkeys.DuplicateMsg"), msg),
                    LocalizationManager.T("Settings.Hotkeys.DuplicateTitle"),
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
                EscapeToTrayCheck.IsChecked ?? true,
                hkShowAll,
                hkShowFavorites,
                hkShowRecent,
                RememberWindowLayoutCheck.IsChecked ?? true,
                ReadAfterLaunchAction());

            var templatePaths = TemplatePathsList?.Items.Cast<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                ?? new System.Collections.Generic.List<string>();
            _viewModel.SetTemplateCatalogPaths(templatePaths);

            // Добавление даты-времени к имени файла при выгрузке (JSON, .dt, .cf).
            _viewModel.ApplyExportFileNameSettings(AddTimestampToExportFileNameCheck?.IsChecked ?? true);

            // Шаблон (формат) отметки даты и времени для имени файла при выгрузке.
            _viewModel.ApplyExportTimestampFormat(ExportTimestampFormatComboBox?.Text ?? "yyyyMMdd_HHmmss");


            // Порядок горячих клавиш избранного.
            _viewModel.SetFavoriteHotkeyOrder(_favoriteHotkeyItems.Select(i => i.Key));

            // Сохраняем все темы, изменённые во вкладке «Цветовое оформление»: каждая тема
            // хранит собственные настройки независимо (встроенные — в своём слоте базовой
            // темы, пользовательские — в своём JSON-файле).
            PersistEditedSchemes();
            ThemeDebug($"Settings OK: applying '{_colorScheme.Name}' (isDark={_colorScheme.IsDark}, colors={_colorScheme.Colors.Count})");
            _viewModel.ApplyColorScheme(_colorScheme);

            // Сохраняем настройки шрифта интерфейса (общий и отдельных областей).
            ReadFontSelection();
            _viewModel.SaveElementFonts(_elementFonts);

            DialogResult = true;
        }

        /// <summary>Читает выбранное в окне настроек действие «после запуска базы/конфигуратора».</summary>
        private string ReadAfterLaunchAction()
        {
            if (AfterLaunchActionCombo?.SelectedIndex is int idx && idx >= 0 && idx <= 2)
                return ((Models.AfterLaunchAction)idx).ToSettingString();
            return _viewModel.AfterLaunchAction;
        }


        private void OnAddTemplatePath_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("Settings.Bases.AddTemplateFolderDesc"),
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
                Description = LocalizationManager.T("Settings.Bases.EditTemplateFolderDesc"),
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

        /// <summary>Открывает окно управления учётными записями (профилями).</summary>
        private void OnManageProfiles_Click(object sender, RoutedEventArgs e)
        {
            var profiles = AppServices.GetRequiredService<IProfileService>();
            new ProfilesWindow(profiles) { Owner = this }.ShowDialog();
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
        /// Копирует обезличенную техническую информацию о системе и приложении в буфер обмена
        /// (для диагностики проблемы разработчику). Работает в Windows и Linux.
        /// </summary>
        private void OnCopyTechInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(TechnicalInfoService.Collect());
                MessageBox.Show(
                    LocalizationManager.T("Settings.About.TechInfoCopied"),
                    LocalizationManager.T("Common.Information"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationManager.T("Settings.About.TechInfoCopyFailed") + "\n" + ex.Message,
                    LocalizationManager.T("Common.Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
#endif