using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания ИБ через CREATEINFOBASE (пустая или из шаблона .cf/.dt).
    /// </summary>
    public partial class CreateInfobaseWindow : Window
    {
        private readonly bool _fromTemplate;
        private readonly IReadOnlyList<string> _platformVersions;
        private readonly IReadOnlyList<Group> _groups;
        private string _selectedGroupPath;

        public Infobase? Result { get; private set; }

        public CreateInfobaseWindow(
            bool fromTemplate,
            IEnumerable<string> platformVersions,
            string defaultGroupPath = "",
            IEnumerable<Group>? groups = null)
        {
            InitializeComponent();
            _fromTemplate = fromTemplate;
            _platformVersions = platformVersions?.ToList() ?? new List<string>();
            _groups = groups?.ToList() ?? new List<Group>();
            _selectedGroupPath = defaultGroupPath ?? string.Empty;

            Title = fromTemplate
                ? "Создание информационной базы из шаблона"
                : "Создание пустой информационной базы";

            TemplatePanel.Visibility = fromTemplate ? Visibility.Visible : Visibility.Collapsed;
            GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? "— Без группы —"
                : _selectedGroupPath;

            HintText.Text = fromTemplate
                ? "Выберите шаблон из списка установленных (каталоги tmplts) или укажите файл .cf/.dt вручную. Создание — через CREATEINFOBASE /UseTemplate."
                : "Будет выполнена команда CREATEINFOBASE без шаблона. После создания база добавится в список программы.";

            RefreshPlatformList();

            if (fromTemplate)
                LoadInstalledTemplates();
        }

        private List<string> _platforms = new();

        
        private void OnPickGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: null,
                allowNone: true,
                noneLabel: "— Без группы —")
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? "— Без группы —"
                    : _selectedGroupPath;
            }
        }

        private void RefreshPlatformList()
        {
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (_platforms.Count == 0)
                _platforms = _platformVersions.ToList();

            if (_platforms.Count > 0 && string.IsNullOrWhiteSpace(PlatformBox.Text))
                PlatformBox.Text = _platforms[0];
        }

        private void OnPickPlatform_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlatformList();
            var dlg = new PlatformVersionPickerWindow(_platforms, PlatformBox.Text ?? "")
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
                PlatformBox.Text = dlg.Result;
        }

        private void OnEditPlatformPaths_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                vm.OpenSettingsCommand.Execute(null);
                RefreshPlatformList();
            }
            else
            {
                MessageBox.Show(
                    "Добавьте каталоги платформ в Настройки → дополнительные пути поиска (например E:\\1cPlatform),\n" +
                    "затем обновите список версий.",
                    "Пути к платформам",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void LoadInstalledTemplates()
        {
            var primary = OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
            var roots = OneCTemplateService.GetTemplateRootFolders();
            var primaryExists = Directory.Exists(primary);

            TemplateRootsHint.Text =
                $"Каталог шаблонов 1С (по умолчанию): {primary}" +
                (primaryExists ? "" : " — папка ещё не создана") +
                (roots.Count > 1
                    ? "\nТакже проверены: " + string.Join("; ", roots.Where(r =>
                        !r.Equals(primary, StringComparison.OrdinalIgnoreCase)))
                    : "");

            var templates = OneCTemplateService.FindInstalledTemplates();
            _flatTemplates = templates.ToList();
            var tree = OneCTemplateService.BuildTemplateTree(templates);
            TemplateTree.ItemsSource = tree;

            var first = templates.FirstOrDefault();
            if (first is not null)
            {
                TemplateBox.Text = first.FilePath;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = SuggestNameFromTemplate(first);
                Dispatcher.BeginInvoke(new Action(() => SelectFirstLeaf(TemplateTree)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                TemplateBox.Text = "";
                TemplateRootsHint.Text +=
                    "\nШаблоны не найдены. Установите конфигурации через стартер 1С или укажите файл .cf/.dt вручную (кнопка «Файл…»).";
            }
        }

        private List<OneCTemplateService.TemplateInfo> _flatTemplates = new();

        private static string SuggestNameFromTemplate(OneCTemplateService.TemplateInfo t)
        {
            // Как в стартере 1С: последний сегмент Catalog (без суффиксов демо/пустая)
            var segs = t.CatalogSegments;
            if (segs.Length > 0)
            {
                var leaf = segs[^1]
                    .Replace(" (демо)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" (demo)", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" (пустая)", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(leaf))
                    return leaf;
            }
            if (!string.IsNullOrWhiteSpace(t.ConfigurationName) && t.ConfigurationName != "—")
                return t.ConfigurationName;
            var parts = t.RelativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return parts[^2];
            if (parts.Length == 1)
                return Path.GetFileNameWithoutExtension(parts[0]);
            return Path.GetFileNameWithoutExtension(t.FilePath);
        }

        private void OnTemplateTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is OneCTemplateService.TemplateTreeNode { Template: { } t })
            {
                TemplateBox.Text = t.FilePath;
                if (string.IsNullOrWhiteSpace(NameBox.Text) || NameWasSuggested())
                    NameBox.Text = SuggestNameFromTemplate(t);
            }
        }

        private bool NameWasSuggested()
        {
            var name = NameBox.Text?.Trim() ?? "";
            return _flatTemplates.Any(t =>
                SuggestNameFromTemplate(t).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Выделяет первый листовой шаблон в дереве.</summary>
        private static void SelectFirstLeaf(ItemsControl parent)
        {
            foreach (var item in parent.Items)
            {
                if (item is not OneCTemplateService.TemplateTreeNode node) continue;
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (node.Template is not null)
                {
                    if (container is not null)
                        container.IsSelected = true;
                    return;
                }
                if (container is not null)
                {
                    container.IsExpanded = true;
                    container.UpdateLayout();
                    SelectFirstLeaf(container);
                    return;
                }
            }
        }

        private void OnRefreshTemplates_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledTemplates();
        }

        private void OnType_Changed(object sender, RoutedEventArgs e)
        {
            if (FilePanel is null || ServerPanel is null) return;
            var isFile = FileTypeRadio.IsChecked == true;
            FilePanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            ServerPanel.Visibility = isFile ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Каталог для новой файловой базы 1С",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                FilePathBox.Text = dlg.SelectedPath;
        }

        private void OnBrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Шаблон конфигурации или выгрузки",
                Filter = "Шаблоны 1С (*.cf;*.dt)|*.cf;*.dt|Конфигурация (*.cf)|*.cf|Выгрузка (*.dt)|*.dt|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TemplateBox.Text = dlg.FileName;
        }

        private void OnCreate_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Укажите наименование базы.", "Создание ИБ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isFile = FileTypeRadio.IsChecked == true;
            string? filePath = null;
            string? server = null;
            string? refName = null;

            if (isFile)
            {
                filePath = FilePathBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    MessageBox.Show("Укажите каталог файловой базы.", "Создание ИБ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                server = ServerBox.Text?.Trim() ?? "";
                refName = RefBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(refName))
                {
                    MessageBox.Show("Укажите сервер и имя базы (Ref).", "Создание ИБ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            string? templatePath = null;
            if (_fromTemplate)
            {
                templatePath = TemplateBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    MessageBox.Show("Укажите существующий файл шаблона (.cf или .dt).", "Создание ИБ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var platform = PlatformBox.Text?.Trim() ?? "";
            // Убираем суффикс « (64)» для хранения в Infobase.PlatformVersion при необходимости —
            // OneCLauncher.ParseVariant умеет оба формата; оставляем полный display.
            if (string.IsNullOrWhiteSpace(platform))
            {
                MessageBox.Show(
                    "Не найдена установленная платформа 1С.\nУкажите версию через «Список…» или пути в настройках.",
                    "Создание ИБ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Для файловой: если указан путь без имени — создаём подкаталог по имени базы.
            if (isFile && !string.IsNullOrEmpty(filePath))
            {
                if (!Directory.Exists(filePath) && !filePath.EndsWith(Path.DirectorySeparatorChar) &&
                    !filePath.EndsWith(Path.AltDirectorySeparatorChar) &&
                    string.IsNullOrEmpty(Path.GetExtension(filePath)))
                {
                    // path may be intended as new folder
                }
            }

            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: isFile,
                filePath: filePath,
                server: server,
                databaseName: refName,
                templatePath: templatePath);

            if (!ok)
            {
                MessageBox.Show(
                    "Не удалось создать информационную базу.\n" + (error ?? ""),
                    "Создание ИБ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Разрядность, выбранная суффиксом версии «(32)/(64)», сохраняется
            // в отдельное поле Architecture, а PlatformVersion — чистая версия
            // (без встроенной разрядности, чтобы она не попадала в ibases.v8i).
            PlatformVersionService.ParseVariant(platform, out var cleanPlatform, out var platformArch);
            var storedPlatform = string.IsNullOrWhiteSpace(cleanPlatform) ? platform : cleanPlatform;
            var storedArchitecture = platformArch == "32" || platformArch == "64"
                ? platformArch
                : "32-priority";

            Result = new Infobase
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Group = string.IsNullOrWhiteSpace(_selectedGroupPath) ? string.Empty : _selectedGroupPath,
                PlatformVersion = storedPlatform,
                Architecture = storedArchitecture,
                Connection = isFile
                    ? new ConnectionSettings
                    {
                        Type = ConnectionType.File,
                        FilePath = filePath ?? ""
                    }
                    : new ConnectionSettings
                    {
                        Type = ConnectionType.ClientServer,
                        Server = server ?? "",
                        DatabaseName = refName ?? ""
                    }
            };

            DialogResult = true;
        }

        private sealed class PlatformItem
        {
            public PlatformItem(string version) => Version = version;
            public string Version { get; }
            public string DisplayName => Version;
        }
    }
}
