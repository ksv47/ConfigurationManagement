using System;
using System.Windows;
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
            var dialog = new PlatformVersionPickerWindow(_viewModel.InstalledPlatformVersions, _viewModel.PlatformVersion)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                PlatformVersionService.ParseVariant(dialog.Result, out var version, out var architecture);
                _viewModel.PlatformVersion = version;
                _viewModel.Architecture = architecture;
            }
        }
    }
}