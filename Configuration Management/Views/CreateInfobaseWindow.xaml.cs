#if WINDOWS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
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
        private readonly IInfobaseRepository _repository =
            AppServices.GetRequiredService<IInfobaseRepository>();

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
                ? LocalizationManager.T("CreateInfobase.TitleFromTemplate")
                : LocalizationManager.T("CreateInfobase.TitleEmpty");

            TemplatePanel.Visibility = fromTemplate ? Visibility.Visible : Visibility.Collapsed;
            GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            HintText.Text = fromTemplate
                ? LocalizationManager.T("CreateInfobase.HintTemplate")
                : LocalizationManager.T("CreateInfobase.HintEmpty");

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
                noneLabel: LocalizationManager.T("Connection.NoGroup"),
                kind: GroupPickerObjectKind.Infobase)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                GroupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? LocalizationManager.T("Connection.NoGroup")
                    : _selectedGroupPath;
            }
        }

        private void RefreshPlatformList()
        {
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (_platforms.Count == 0)
                _platforms = _platformVersions.ToList();

            if (_platforms.Count == 0)
                return;

            // По умолчанию подставляем последнюю успешно использованную версию для текущего
            // типа базы (файловая/клиент-серверная), если она всё ещё установлена. Иначе — самую новую.
            string selected = _platforms[0];
            var saved = GetSavedPlatformVersion();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                foreach (var p in _platforms)
                {
                    PlatformVersionService.ParseVariant(p, out var clean, out _);
                    var candidate = string.IsNullOrWhiteSpace(clean) ? p : clean;
                    if (string.Equals(candidate, saved, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = p;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(PlatformBox.Text))
                PlatformBox.Text = selected;
        }

        /// <summary>
        /// Возвращает последнюю успешно использованную версию платформы для текущего типа базы.
        /// Тип базы определяется по TypeBox; по умолчанию (до инициализации) — файловая.
        /// </summary>
        private string GetSavedPlatformVersion()
        {
            var settings = _repository.LoadSettings();
            var isFile = TypeBox.SelectedIndex != 1;
            return isFile
                ? settings.LastFileCreatePlatformVersion ?? ""
                : settings.LastClientServerCreatePlatformVersion ?? "";
        }

        /// <summary>
        /// Запоминает последнюю успешно использованную версию платформы отдельно для
        /// файловых и клиент-серверных баз. Ошибки сохранения не должны ломать создание ИБ.
        /// </summary>
        private void SaveLastPlatformVersion(bool isFile, string platform)
        {
            try
            {
                var settings = _repository.LoadSettings();
                PlatformVersionService.ParseVariant(platform, out var cleanPlatform, out _);
                var clean = string.IsNullOrWhiteSpace(cleanPlatform) ? platform : cleanPlatform;
                if (isFile)
                    settings.LastFileCreatePlatformVersion = clean;
                else
                    settings.LastClientServerCreatePlatformVersion = clean;
                _repository.SaveSettings(settings);
            }
            catch
            {
                // Несохранение последней версии не должно прерывать создание ИБ.
            }
        }

        /// <summary>
        /// Разбирает строку версии на числовые компоненты (major, minor).
        /// Суффиксы вроде « (64)» снимаются через <see cref="PlatformVersionService.ParseVariant"/>.
        /// </summary>
        private static (int Major, int Minor) GetMajorMinor(string version)
        {
            PlatformVersionService.ParseVariant(version, out var clean, out _);
            var v = string.IsNullOrWhiteSpace(clean) ? version : clean;
            var parts = (v ?? "").Split('.');
            int.TryParse(parts.Length >= 1 ? parts[0] : "", out var major);
            int.TryParse(parts.Length >= 2 ? parts[1] : "", out var minor);
            return (major, minor);
        }

        /// <summary>
        /// Эвристика Варианта 2 (#91): ищет среди уже существующих клиент-серверных баз на том же
        /// сервере базу, версия платформы которой отличается от выбранной по первым двум числам
        /// (major.minor). Возвращает версию такой базы или null, если расхождений нет.
        /// Ошибки чтения списка баз не блокируют создание — возвращаем null.
        /// </summary>
        private string? GetIncompatibleExistingVersion(string platform, string server)
        {
            var (selectedMajor, selectedMinor) = GetMajorMinor(platform);

            List<Infobase> infobases;
            try
            {
                infobases = _repository.Load();
            }
            catch
            {
                return null;
            }

            var targetServer = (server ?? "").Trim();
            foreach (var ib in infobases)
            {
                var conn = ib.Connection;
                if (conn == null || conn.Type != ConnectionType.ClientServer)
                    continue;
                if (!string.Equals((conn.Server ?? "").Trim(), targetServer, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(ib.PlatformVersion))
                    continue;

                var (major, minor) = GetMajorMinor(ib.PlatformVersion);
                if (major != selectedMajor || minor != selectedMinor)
                    return ib.PlatformVersion;
            }

            return null;
        }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            var isFile = TypeBox.SelectedIndex != 1;
            // Обработчик может сработать в ходе InitializeComponent(), когда элементы,
            // объявленные ниже ComboBox в XAML, ещё не созданы, поэтому защищаемся от null.
            if (FilePanel != null)
                FilePanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            if (ServerPanel != null)
                ServerPanel.Visibility = isFile ? Visibility.Collapsed : Visibility.Visible;
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
                    LocalizationManager.T("CreateInfobase.NoPlatformsPathsMsg"),
                    LocalizationManager.T("CreateInfobase.PlatformPathsTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void LoadInstalledTemplates()
        {
            _templateLoadingCts?.Cancel();
            var cts = new CancellationTokenSource();
            _templateLoadingCts = cts;

            // Показываем окно сразу, а тяжёлое сканирование каталогов шаблонов
            // выполняем в фоне; список дособерётся, когда будет готов.
            ShowTemplateLoading(true);
            TemplateTree.ItemsSource = null;
            _flatTemplates = new List<OneCTemplateService.TemplateInfo>();

            Task.Run(() =>
            {
                // Основной источник — первый фактически существующий корень.
                // GetTemplateRootFolders() ставит настроенные пользователем каталоги
                // первыми, поэтому подсказка отражает реально используемый каталог,
                // а не дефолтный tmplts.
                var roots = OneCTemplateService.GetTemplateRootFolders().ToList();
                var primary = roots.Count > 0
                    ? roots[0]
                    : OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
                var primaryExists = Directory.Exists(primary);

                var templates = OneCTemplateService.FindInstalledTemplates().ToList();
                var tree = OneCTemplateService.BuildTemplateTree(templates);
                return new TemplateLoadResult(primary, roots, primaryExists, templates, tree);
            }).ContinueWith(t =>
            {
                if (cts.IsCancellationRequested)
                    return;

                ShowTemplateLoading(false);

                if (t.IsFaulted)
                {
                    _flatTemplates = new List<OneCTemplateService.TemplateInfo>();
                    TemplateTree.ItemsSource = null;
                    TemplateRootsHint.Text +=
                        LocalizationManager.T("CreateInfobase.LoadingFailed");
                    return;
                }

                var r = t.Result;
                _flatTemplates = r.Templates;
                TemplateTree.ItemsSource = r.Tree;
                UpdateTemplateRootsHint(r.Primary, r.Roots, r.PrimaryExists);

                if (r.Templates.Count == 0)
                    TemplateRootsHint.Text +=
                        LocalizationManager.T("CreateInfobase.NoTemplates");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void UpdateTemplateRootsHint(string primary, IReadOnlyList<string> roots, bool primaryExists)
        {
            TemplateRootsHint.Text =
                string.Format(LocalizationManager.T("CreateInfobase.TemplateRootsDefault"), primary) +
                (primaryExists ? "" : LocalizationManager.T("CreateInfobase.FolderNotCreated")) +
                (roots.Count > 1
                    ? string.Format(LocalizationManager.T("CreateInfobase.AlsoChecked"),
                        string.Join("; ", roots.Where(r =>
                            !r.Equals(primary, StringComparison.OrdinalIgnoreCase))))
                    : "");
        }

        private void ShowTemplateLoading(bool loading)
        {
            if (TemplateLoadingPanel != null)
                TemplateLoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (TemplateTree != null)
                TemplateTree.IsEnabled = !loading;
        }

        private sealed class TemplateLoadResult
        {
            public TemplateLoadResult(
                string primary,
                IReadOnlyList<string> roots,
                bool primaryExists,
                List<OneCTemplateService.TemplateInfo> templates,
                IReadOnlyList<OneCTemplateService.TemplateTreeNode> tree)
            {
                Primary = primary;
                Roots = roots;
                PrimaryExists = primaryExists;
                Templates = templates;
                Tree = tree;
            }

            public string Primary { get; }
            public IReadOnlyList<string> Roots { get; }
            public bool PrimaryExists { get; }
            public List<OneCTemplateService.TemplateInfo> Templates { get; }
            public IReadOnlyList<OneCTemplateService.TemplateTreeNode> Tree { get; }
        }

        private List<OneCTemplateService.TemplateInfo> _flatTemplates = new();
        private CancellationTokenSource? _templateLoadingCts;

        private static string SuggestNameFromTemplate(OneCTemplateService.TemplateInfo t)
        {
            // Как в стартере 1С: последний сегмент Catalog (без суффиксов демо/пустая)
            var segs = t.CatalogSegments;
            if (segs.Length > 0)
            {
                var leaf = segs[^1]
                    .Replace(LocalizationManager.T("Template.SuffixDemo"), "", StringComparison.OrdinalIgnoreCase)
                    .Replace(LocalizationManager.T("Template.SuffixEmpty"), "", StringComparison.OrdinalIgnoreCase)
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

        private void OnRefreshTemplates_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledTemplates();
        }

        private void OnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("CreateInfobase.ChooseFolderDescription"),
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                FilePathBox.Text = dlg.SelectedPath;
        }

        private void OnBrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationManager.T("CreateInfobase.TemplateDialogTitle"),
                Filter = $"{LocalizationManager.T("CreateInfobase.FilterTemplates")}|*.cf;*.dt|{LocalizationManager.T("CreateInfobase.FilterConfig")}|*.cf|{LocalizationManager.T("CreateInfobase.FilterDump")}|*.dt|{LocalizationManager.T("Common.AllFiles")}|*.*"
            };
            if (dlg.ShowDialog() == true)
                TemplateBox.Text = dlg.FileName;
        }

        private void OnCreate_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(LocalizationManager.T("CreateInfobase.EnterName"), LocalizationManager.T("CreateInfobase.CreateTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isFile = TypeBox.SelectedIndex != 1;

            string? templatePath = null;
            if (_fromTemplate)
            {
                templatePath = TemplateBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    MessageBox.Show(LocalizationManager.T("CreateInfobase.EnterTemplateFile"), LocalizationManager.T("CreateInfobase.CreateTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var platform = PlatformBox.Text?.Trim() ?? "";
            // Убираем суффикс « (64)» для хранения в Infobase.PlatformVersion при необходимости.
            if (string.IsNullOrWhiteSpace(platform))
            {
                MessageBox.Show(
                    LocalizationManager.T("CreateInfobase.NoPlatform"),
                    LocalizationManager.T("CreateInfobase.CreateTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool ok;
            string? error;
            ConnectionSettings connection;
            if (isFile)
            {
                var filePath = FilePathBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    MessageBox.Show(LocalizationManager.T("CreateInfobase.EnterFilePath"), LocalizationManager.T("CreateInfobase.CreateTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                (ok, error) = OneCLauncher.CreateInfoBase(
                    platformVersion: platform,
                    isFile: true,
                    filePath: filePath,
                    server: null,
                    databaseName: null,
                    templatePath: templatePath);

                if (!ok)
                {
                    MessageBox.Show(
                        string.Format(LocalizationManager.T("CreateInfobase.CreateFailed"), error ?? ""),
                        LocalizationManager.T("CreateInfobase.CreateTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                connection = new ConnectionSettings
                {
                    Type = ConnectionType.File,
                    FilePath = filePath ?? ""
                };
            }
            else
            {
                var server = ServerBox.Text?.Trim() ?? "";
                var refName = RefBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(refName))
                {
                    MessageBox.Show(LocalizationManager.T("CreateInfobase.EnterServerAndDb"), LocalizationManager.T("CreateInfobase.CreateTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dbms = DbmsBox.Text?.Trim() ?? "";
                var dbServer = DbServerBox.Text?.Trim() ?? "";
                var dbName = DbNameBox.Text?.Trim() ?? "";
                var dbUser = DbUserBox.Text?.Trim() ?? "";
                var dbPwd = DbPwdBox.Password ?? "";
                var createSqlDatabase = CreateDbCheck.IsChecked == true;

                // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
                // отличается (по major.minor) от версий, которыми уже работают
                // клиент-серверные базы на этом же сервере. «Нет» — прерывает создание.
                var existingVersion = GetIncompatibleExistingVersion(platform, server);
                if (existingVersion != null)
                {
                    var result = MessageBox.Show(
                        string.Format(
                            LocalizationManager.T("CreateInfobase.VersionMismatchMsg"),
                            platform, existingVersion, server),
                        LocalizationManager.T("CreateInfobase.VersionMismatchTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                (ok, error) = OneCLauncher.CreateInfoBase(
                    platformVersion: platform,
                    isFile: false,
                    filePath: null,
                    server: server,
                    databaseName: refName,
                    templatePath: templatePath,
                    dbms: dbms,
                    dbServer: dbServer,
                    dbName: dbName,
                    dbUser: dbUser,
                    dbPassword: dbPwd,
                    createSqlDatabase: createSqlDatabase,
                    blockScheduledJobs: BlockJobsCheck.IsChecked == true);

                if (!ok)
                {
                    MessageBox.Show(
                        string.Format(LocalizationManager.T("CreateInfobase.CreateFailed"), error ?? ""),
                        LocalizationManager.T("CreateInfobase.CreateTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                connection = new ConnectionSettings
                {
                    Type = ConnectionType.ClientServer,
                    Server = server,
                    DatabaseName = refName,
                    BlockScheduledJobs = BlockJobsCheck.IsChecked == true
                };
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
                Connection = connection
            };

            // Создание прошло успешно — запоминаем версию для подстановки по умолчанию.
            SaveLastPlatformVersion(isFile, platform);

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
#endif
