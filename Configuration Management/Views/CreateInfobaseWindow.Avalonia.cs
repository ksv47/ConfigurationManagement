#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания ИБ через CREATEINFOBASE (пустая или из шаблона .cf/.dt).
    /// Поддерживает файловый и клиент-серверный варианты (с параметрами СУБД).
    /// Avalonia/Linux-версия WPF-окна <see cref="CreateInfobaseWindow"/>.
    /// </summary>
    public class CreateInfobaseWindow : ModalWindowBase
    {
        private readonly bool _fromTemplate;
        private readonly IReadOnlyList<string> _platformVersions;
        private readonly IReadOnlyList<Group> _groups;
        private string _selectedGroupPath;
        private readonly IDialogService _dialogs;

        private readonly ComboBox _typeBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _nameBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBlock _groupPathBox = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _platformBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _filePathBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _templateBox = new() { Padding = new Thickness(8, 5) };
        private readonly StackPanel _filePanel = new() { Spacing = 8 };

        // Поля клиент-серверного варианта.
        private readonly StackPanel _serverPanel = new() { Spacing = 8 };
        private readonly TextBox _serverBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _refBox = new() { Padding = new Thickness(8, 5) };
        private readonly ComboBox _dbmsBox = new() { Padding = new Thickness(8, 5), IsEditable = true };
        private readonly TextBox _dbServerBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _dbNameBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _dbUserBox = new() { Padding = new Thickness(8, 5) };
        private readonly PasswordBox _dbPwdBox = new() { Padding = new Thickness(8, 5) };
        private readonly CheckBox _createDbCheck = new();
        private readonly CheckBox _blockJobsCheck = new();

        /// <summary>Доступные значения СУБД для клиент-серверного создания.</summary>
        private static readonly string[] DbmsValues =
        {
            "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase", "SQLite"
        };

        public Infobase? Result { get; private set; }

        public CreateInfobaseWindow(
            bool fromTemplate,
            IEnumerable<string> platformVersions,
            string defaultGroupPath = "",
            IEnumerable<Group>? groups = null)
        {
            _fromTemplate = fromTemplate;
            _platformVersions = platformVersions?.ToList() ?? new List<string>();
            _groups = groups?.ToList() ?? new List<Group>();
            _selectedGroupPath = defaultGroupPath ?? string.Empty;
            _dialogs = AppServices.GetRequiredService<IDialogService>();

            Title = fromTemplate
                ? LocalizationManager.T("CreateInfobase.TitleFromTemplate")
                : LocalizationManager.T("CreateInfobase.TitleEmpty");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            SystemDecorations = SystemDecorations.Full;

            _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            Content = BuildRoot();
            RefreshPlatformList();
        }

        private string HintText => _fromTemplate
            ? LocalizationManager.T("CreateInfobase.HintTemplate")
            : LocalizationManager.T("CreateInfobase.HintEmpty");

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = _fromTemplate ? LocalizationManager.T("CreateInfobase.HeaderTemplate") : LocalizationManager.T("CreateInfobase.HeaderEmpty"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var hint = new TextBlock
            {
                Text = HintText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);

            var fields = new StackPanel { Spacing = 10 };

            // Тип базы
            _typeBox.Items.Clear();
            _typeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.T("CreateInfobase.TypeFile") });
            _typeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.T("CreateInfobase.TypeClientServer") });
            _typeBox.SelectedIndex = 0;
            _typeBox.SelectionChanged += (_, _) => OnTypeChanged();
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.TypeLabel"), _typeBox));

            // Наименование
            fields.Children.Add(Field(LocalizationManager.T("Connection.NameLabel"), _nameBox));

            // Группа
            var groupRow = new Grid();
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var gl = new TextBlock { Text = LocalizationManager.T("Connection.GroupLabel"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(gl, 0);
            groupRow.Children.Add(gl);
            Grid.SetColumn(_groupPathBox, 1);
            groupRow.Children.Add(_groupPathBox);
            var pickGroup = new Button { Content = LocalizationManager.T("Connection.ChooseGroup"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickGroup.Click += (_, _) => OnPickGroup_Click();
            Grid.SetColumn(pickGroup, 2);
            groupRow.Children.Add(pickGroup);
            fields.Children.Add(groupRow);

            // Платформа
            var platRow = new Grid();
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var pl = new TextBlock { Text = LocalizationManager.T("Connection.VersionLabel"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pl, 0);
            platRow.Children.Add(pl);
            Grid.SetColumn(_platformBox, 1);
            platRow.Children.Add(_platformBox);
            var pickPlatform = new Button { Content = LocalizationManager.T("CreateInfobase.List"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickPlatform.Click += (_, _) => OnPickPlatform_Click();
            Grid.SetColumn(pickPlatform, 2);
            platRow.Children.Add(pickPlatform);
            fields.Children.Add(platRow);

            // Файловая база: путь к каталогу.
            var browseFile = new Button { Content = LocalizationManager.T("Common.Browse"), MinWidth = 90 };
            browseFile.Click += (_, _) => OnBrowseFolder_Click();
            var fileRow = new Grid();
            fileRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            fileRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_filePathBox, 0);
            fileRow.Children.Add(_filePathBox);
            Grid.SetColumn(browseFile, 1);
            browseFile.Margin = new Thickness(8, 0, 0, 0);
            fileRow.Children.Add(browseFile);
            _filePanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DirLabel"), fileRow));

            // Клиент-серверная база: сервер 1С, имя базы и параметры СУБД.
            _dbmsBox.Items.Clear();
            foreach (var v in DbmsValues)
                _dbmsBox.Items.Add(new ComboBoxItem { Content = v });
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.ServerLabel"), _serverBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.RefLabel"), _refBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbmsLabel"), _dbmsBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbServerLabel"), _dbServerBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbNameLabel"), _dbNameBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbUserLabel"), _dbUserBox));
            _serverPanel.Children.Add(Field(LocalizationManager.T("CreateInfobase.DbPasswordLabel"), _dbPwdBox));
            var createDbRow = new Grid();
            createDbRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            createDbRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var cdLabel = new TextBlock { Text = LocalizationManager.T("CreateInfobase.CreateDatabase"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cdLabel, 1);
            createDbRow.Children.Add(cdLabel);
            _createDbCheck.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_createDbCheck, 0);
            createDbRow.Children.Add(_createDbCheck);
            _serverPanel.Children.Add(createDbRow);

            var blockJobsRow = new Grid();
            blockJobsRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            blockJobsRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var bjLabel = new TextBlock { Text = LocalizationManager.T("CreateInfobase.BlockScheduledJobs"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bjLabel, 1);
            blockJobsRow.Children.Add(bjLabel);
            _blockJobsCheck.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_blockJobsCheck, 0);
            blockJobsRow.Children.Add(_blockJobsCheck);
            _serverPanel.Children.Add(blockJobsRow);

            fields.Children.Add(_filePanel);
            fields.Children.Add(_serverPanel);
            _serverPanel.IsVisible = false;

            // Шаблон
            if (_fromTemplate)
            {
                var browseTemplate = new Button { Content = LocalizationManager.T("CreateInfobase.File"), MinWidth = 90 };
                browseTemplate.Click += (_, _) => OnBrowseTemplate_Click();
                var tplRow = new Grid();
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Grid.SetColumn(_templateBox, 0);
                tplRow.Children.Add(_templateBox);
                Grid.SetColumn(browseTemplate, 1);
                browseTemplate.Margin = new Thickness(8, 0, 0, 0);
                tplRow.Children.Add(browseTemplate);
                fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.TemplateLabel"), tplRow));
            }

            Grid.SetRow(fields, 2);
            grid.Children.Add(fields);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new Button { Content = LocalizationManager.T("Common.Cancel"), MinWidth = 100, IsCancel = true };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);
            var create = new Button
            {
                Content = LocalizationManager.T("CreateInfobase.Create"),
                MinWidth = 120,
                IsDefault = true,
                Background = new SolidColorBrush(Color.Parse("#16A34A")),
                Foreground = Brushes.White
            };
            create.Click += (_, _) => OnCreate_Click();
            buttons.Children.Add(create);
            Grid.SetRow(buttons, 5);
            grid.Children.Add(buttons);

            return grid;
        }

        private void OnTypeChanged()
        {
            var isFile = _typeBox.SelectedIndex != 1;
            _filePanel.IsVisible = isFile;
            _serverPanel.IsVisible = !isFile;
        }

        private static Grid Field(string label, Control control)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private void OnPickGroup_Click()
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: null,
                allowNone: true,
                noneLabel: LocalizationManager.T("Connection.NoGroup"));
            if (dialog.ShowDialogSync(this))
            {
                _selectedGroupPath = string.IsNullOrWhiteSpace(dialog.ResultFullPath)
                    ? string.Empty
                    : dialog.ResultFullPath;
                _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                    ? LocalizationManager.T("Connection.NoGroup")
                    : _selectedGroupPath;
            }
        }

        private void RefreshPlatformList()
        {
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            var platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (platforms.Count == 0)
                platforms = _platformVersions.ToList();

            if (platforms.Count > 0 && string.IsNullOrWhiteSpace(_platformBox.Text))
                _platformBox.Text = platforms[0];
        }

        private void OnPickPlatform_Click()
        {
            RefreshPlatformList();
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            var platforms = PlatformVersionService.FindInstalledVersions(extras);
            if (platforms.Count == 0)
                platforms = _platformVersions.ToList();

            var dlg = new PlatformVersionPickerWindow(platforms, _platformBox.Text ?? "");
            if (dlg.ShowDialogSync(this) && !string.IsNullOrWhiteSpace(dlg.Result))
                _platformBox.Text = dlg.Result;
        }

        private void OnBrowseFolder_Click()
        {
            var path = _dialogs.OpenFolderDialog(LocalizationManager.T("CreateInfobase.ChooseFolderDescription"));
            if (!string.IsNullOrWhiteSpace(path))
                _filePathBox.Text = path;
        }

        private void OnBrowseTemplate_Click()
        {
            var path = _dialogs.OpenFileDialog(LocalizationManager.T("CreateInfobase.TemplateDialogTitle"),
                $"{LocalizationManager.T("CreateInfobase.FilterTemplates")}|*.cf;*.dt|{LocalizationManager.T("CreateInfobase.FilterConfig")}|*.cf|{LocalizationManager.T("CreateInfobase.FilterDump")}|*.dt|{LocalizationManager.T("Common.AllFiles")}|*.*");
            if (!string.IsNullOrWhiteSpace(path))
                _templateBox.Text = path;
        }

        private void OnCreate_Click()
        {
            var name = _nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterName"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                return;
            }

            var isFile = _typeBox.SelectedIndex != 1;

            string? templatePath = null;
            if (_fromTemplate)
            {
                templatePath = _templateBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterTemplateFile"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                }
            }

            var platform = _platformBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(platform))
            {
                _dialogs.ShowWarning(
                    LocalizationManager.T("CreateInfobase.NoPlatform"),
                    LocalizationManager.T("CreateInfobase.CreateTitle"));
                return;
            }

            bool ok;
            string? error;
            string server;
            string refName;
            ConnectionSettings connection;

            if (isFile)
            {
                var filePath = _filePathBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterFilePath"), LocalizationManager.T("CreateInfobase.CreateTitle"));
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
                    _dialogs.ShowError(string.Format(LocalizationManager.T("CreateInfobase.CreateFailed"), error ?? ""), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                }

                server = string.Empty;
                refName = string.Empty;
                connection = new ConnectionSettings
                {
                    Type = ConnectionType.File,
                    FilePath = filePath ?? ""
                };
            }
            else
            {
                server = _serverBox.Text?.Trim() ?? "";
                refName = _refBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(refName))
                {
                    _dialogs.ShowWarning(LocalizationManager.T("CreateInfobase.EnterServerAndDb"), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                }

                var dbms = _dbmsBox.Text?.Trim() ?? "";
                var dbServer = _dbServerBox.Text?.Trim() ?? "";
                var dbName = _dbNameBox.Text?.Trim() ?? "";
                var dbUser = _dbUserBox.Text?.Trim() ?? "";
                var dbPwd = _dbPwdBox.Password ?? "";
                var createSqlDatabase = _createDbCheck.IsChecked == true;

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
                    createSqlDatabase: createSqlDatabase);
                if (!ok)
                {
                    _dialogs.ShowError(string.Format(LocalizationManager.T("CreateInfobase.CreateFailed"), error ?? ""), LocalizationManager.T("CreateInfobase.CreateTitle"));
                    return;
                }

                connection = new ConnectionSettings
                {
                    Type = ConnectionType.ClientServer,
                    Server = server,
                    DatabaseName = refName,
                    BlockScheduledJobs = _blockJobsCheck.IsChecked == true
                };
            }

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

            DialogResult = true;
            Close();
        }
    }
}
#endif