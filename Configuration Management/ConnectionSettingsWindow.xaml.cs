using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настройки подключения к информационной базе.
    /// </summary>
    public partial class ConnectionSettingsWindow : Window
    {
        private readonly ConnectionSettingsViewModel _viewModel;

        /// <summary>
        /// Создаёт диалог настройки подключения.
        /// </summary>
        /// <param name="infobase">База для редактирования. Если null — создаётся новая база.</param>
        /// <param name="groups">Список доступных групп для выбора.</param>
        /// <param name="installedPlatformVersions">Список установленных версий платформы 1С.</param>
        /// <param name="defaultGroupPath">Путь группы по умолчанию для новой базы.</param>
        /// <param name="availableServers">Список серверов 1С из других баз списка для выпадающего списка.</param>
        /// <param name="availablePorts">Список портов серверов 1С из других баз списка для выпадающего списка.</param>
        public ConnectionSettingsWindow(Infobase? infobase = null, IEnumerable<Group>? groups = null,
            IEnumerable<string>? installedPlatformVersions = null, string? defaultGroupPath = null,
            IEnumerable<string>? availableServers = null, IEnumerable<int>? availablePorts = null)
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                SyncPasswordBoxFromViewModel();
                SyncRepositoryPasswordBoxFromViewModel();
                SyncConfiguratorPasswordBoxFromViewModel();
            };
            _viewModel = new ConnectionSettingsViewModel(groups);
            _viewModel.SetInstalledPlatformVersions(installedPlatformVersions ?? new List<string>());
            _viewModel.SetAvailableServers(availableServers);
            _viewModel.SetAvailablePorts(availablePorts);
            if (infobase != null)
            {
                _viewModel.LoadFrom(infobase);

                // Сохраняем служебные поля существующей базы, чтобы они не сбрасывались
                // при редактировании (избранное, закрепление, теги, дата последнего запуска, метаданные).
                Result.IsFavorite = infobase.IsFavorite;
                Result.IsPinned = infobase.IsPinned;
                Result.Tags = new List<string>(infobase.Tags);
                Result.LastLaunchDate = infobase.LastLaunchDate;
                Result.MetadataRoot = infobase.MetadataRoot;
            }
            else if (!string.IsNullOrWhiteSpace(defaultGroupPath))
            {
                // Новая база: подставляем группу, в которой сейчас находится курсор.
                _viewModel.Group = defaultGroupPath;
                _viewModel.SelectedGroup = GroupHierarchyHelper.FindByFullPath(defaultGroupPath, _viewModel.Groups);
            }
            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectionSettingsViewModel.Password))
                    SyncPasswordBoxFromViewModel();
                else if (e.PropertyName == nameof(ConnectionSettingsViewModel.RepositoryPassword))
                    SyncRepositoryPasswordBoxFromViewModel();
                else if (e.PropertyName == nameof(ConnectionSettingsViewModel.ConfiguratorPassword))
                    SyncConfiguratorPasswordBoxFromViewModel();
            };
        }

        /// <summary>
        /// Открывает дерево групп для выбора.
        /// </summary>
        private void OnSelectGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupPickerWindow(
                _viewModel.Groups,
                currentGroupId: _viewModel.SelectedGroup?.Id,
                allowNone: true,
                noneLabel: "— Без группы —")
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _viewModel.SelectedGroup = dialog.ResultGroup;
                if (dialog.ResultGroup is null)
                    _viewModel.Group = string.Empty;
            }
        }

        /// <summary>
        /// Открывает диалог выбора каталога для файловой базы и подставляет путь в поле.
        /// </summary>
        private void OnBrowseFilePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Выберите каталог информационной базы 1С",
                Multiselect = false
            };
            var current = _viewModel.FilePath;
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) == true)
                _viewModel.FilePath = dialog.FolderName;
        }

        /// <summary>
        /// Копирует ID базы в буфер обмена.
        /// </summary>
        private void OnCopyId_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.Id))
            {
                Clipboard.SetText(_viewModel.Id);
            }
        }

        /// <summary>
        /// Открывает окно ввода строки подключения 1С. Если в буфере обмена лежит
        /// строка, удовлетворяющая критериям ссылки на информационную базу, она
        /// сразу подставляется в поле ввода. После подтверждения строка разбивается
        /// по полям настроек базы.
        /// </summary>
        private void OnPasteConnectionString_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConnectionStringInputWindow(_viewModel.ConnectionString)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            // Применяем разобранную строку к полям ViewModel.
            _viewModel.ApplyConnectionString(dialog.Result);

            // Обновляем значение поля строки подключения во ViewModel,
            // чтобы оно совпадало с применённым значением.
            _viewModel.ConnectionString = dialog.Result ?? string.Empty;

            MessageBox.Show("Строка подключения успешно разобрана и заполнена по полям.",
                "Вставка строки подключения", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Генерирует новый идентификатор базы в формате, совместимом с ibases.v8i (GUID без скобок).
        /// </summary>
        private void OnGenerateId_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Id = Guid.NewGuid().ToString("D");
        }

        /// <summary>
        /// Возвращает отредактированную информационную базу.
        /// </summary>
        public Infobase Result { get; private set; } = new();

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            // Применяем значения из ViewModel к результату.
            _viewModel.ApplyTo(Result);

            // Если ID базы 1С не задан — пытаемся найти его в файле ibases.v8i
            // по имени базы или строке подключения. Это позволяет корректно
            // очищать кеш 1С точечно по ID даже для баз, созданных вручную.
            if (string.IsNullOrWhiteSpace(Result.Id))
            {
                var id = IbasesV8iImporter.FindId(Result.Name, Result.Connection.ToConnectionString());
                if (!string.IsNullOrWhiteSpace(id))
                {
                    Result.Id = id;
                }
                else
                {
                    // ID не найден ни во ViewModel, ни в ibases.v8i —
                    // назначаем новый идентификатор, чтобы у базы он был всегда
                    // (нужен для точечной очистки кеша и экспорта в ibases.v8i).
                    Result.Id = Guid.NewGuid().ToString("D");
                }
            }

            DialogResult = true;
        }

        private void OnLaunchParameters_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LaunchParametersWindow(_viewModel.LaunchParameters)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _viewModel.LaunchParameters = dialog.Result;
            }
        }

        /// <summary>
        /// Открывает окно выбора версии платформы 1С со сгруппированными версиями.
        /// Выбранный вариант вида «8.3.25.1234 (64)» разбирается на чистую версию
        /// и разрядность, которые сохраняются в соответствующие свойства.
        /// </summary>
        private void OnPlatformSettings_Click(object sender, RoutedEventArgs e)
        {
            var current = _viewModel.PlatformVersion ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_viewModel.Architecture)
                && _viewModel.Architecture is "32" or "64"
                && !current.Contains('('))
            {
                current = $"{current} ({_viewModel.Architecture})".Trim();
            }

            var dialog = new PlatformVersionPickerWindow(_viewModel.InstalledPlatformVersions, current)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;

            var result = (dialog.Result ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(result))
                return;

            PlatformVersionService.ParseVariant(result, out var version, out var architecture);
            // Подставляем чистую версию; если разбор не удался — всю строку выбора.
            _viewModel.PlatformVersion = string.IsNullOrWhiteSpace(version) ? result : version;
            // Разрядность меняем только если в выбранной строке она явно указана «(32)/(64)».
            if (result.Contains('(') && (architecture == "32" || architecture == "64"))
                _viewModel.Architecture = architecture;
        }

        /// <summary>Синхронизация PasswordBox → ViewModel (пароль не биндится напрямую).</summary>
        private void OnPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not PasswordBox pb) return;
            if (_isSyncingPassword) return;
            _viewModel.Password = pb.Password;
        }

        private bool _isSyncingPassword;
        private bool _isSyncingRepositoryPassword;
        private bool _isSyncingConfiguratorPassword;

        /// <summary>Заполняет PasswordBox из ViewModel без рекурсии событий.</summary>
        private void SyncPasswordBoxFromViewModel()
        {
            if (PasswordBox is null || _viewModel is null) return;
            _isSyncingPassword = true;
            try
            {
                if (PasswordBox.Password != (_viewModel.Password ?? string.Empty))
                    PasswordBox.Password = _viewModel.Password ?? string.Empty;
            }
            finally
            {
                _isSyncingPassword = false;
            }
        }

        /// <summary>Синхронизация PasswordBox хранилища → ViewModel (пароль не биндится напрямую).</summary>
        private void OnRepositoryPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not PasswordBox pb) return;
            if (_isSyncingRepositoryPassword) return;
            _viewModel.RepositoryPassword = pb.Password;
        }

        /// <summary>Заполняет PasswordBox хранилища из ViewModel без рекурсии событий.</summary>
        private void SyncRepositoryPasswordBoxFromViewModel()
        {
            if (RepositoryPasswordBox is null || _viewModel is null) return;
            _isSyncingRepositoryPassword = true;
            try
            {
                if (RepositoryPasswordBox.Password != (_viewModel.RepositoryPassword ?? string.Empty))
                    RepositoryPasswordBox.Password = _viewModel.RepositoryPassword ?? string.Empty;
            }
            finally
            {
                _isSyncingRepositoryPassword = false;
            }
        }

        /// <summary>Синхронизация PasswordBox Конфигуратора → ViewModel (пароль не биндится напрямую).</summary>
        private void OnConfiguratorPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null || sender is not PasswordBox pb) return;
            if (_isSyncingConfiguratorPassword) return;
            _viewModel.ConfiguratorPassword = pb.Password;
        }

        /// <summary>Заполняет PasswordBox Конфигуратора из ViewModel без рекурсии событий.</summary>
        private void SyncConfiguratorPasswordBoxFromViewModel()
        {
            if (ConfiguratorPasswordBox is null || _viewModel is null) return;
            _isSyncingConfiguratorPassword = true;
            try
            {
                if (ConfiguratorPasswordBox.Password != (_viewModel.ConfiguratorPassword ?? string.Empty))
                    ConfiguratorPasswordBox.Password = _viewModel.ConfiguratorPassword ?? string.Empty;
            }
            finally
            {
                _isSyncingConfiguratorPassword = false;
            }
        }
    }
}