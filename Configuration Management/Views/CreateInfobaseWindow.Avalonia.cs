#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Configuration_Management.Controls;
using Configuration_Management.Themes;
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
        private readonly IInfobaseRepository _repository =
            AppServices.GetRequiredService<IInfobaseRepository>();

        private readonly ComboBox _typeBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBox _nameBox = new() { Padding = new Thickness(8, 5) };
        private readonly TextBlock _groupPathBox = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _platformBox = new() { Padding = new Thickness(8, 5), IsReadOnly = true };
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

        private readonly TreeView _templateTree = new() { SelectionMode = SelectionMode.Single, Height = 260 };
        private readonly TextBlock _templateRootsHint = new()
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        private readonly StackPanel _templateLoadingPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
            IsVisible = false
        };
        private int _templateLoadGeneration;
        private bool _closed;
        private List<OneCTemplateService.TemplateInfo> _flatTemplates = new();

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
            if (fromTemplate)
            {
                // С деревом шаблонов окно высокое, поэтому размеры берутся
                // из разметки WPF, вместе с возможностью его уменьшить.
                Height = 640;
                MinHeight = 420;
                MaxHeight = 800;
                CanResize = true;
            }
            else
            {
                SizeToContent = SizeToContent.Height;
                CanResize = false;
            }
            SystemDecorations = SystemDecorations.Full;

            _groupPathBox.Text = string.IsNullOrWhiteSpace(_selectedGroupPath)
                ? LocalizationManager.T("Connection.NoGroup")
                : _selectedGroupPath;

            Closed += (_, _) => _closed = true;

            Content = BuildRoot();
            RefreshPlatformList();
            if (_fromTemplate)
                LoadInstalledTemplates();
        }

        private string HintText => _fromTemplate
            ? LocalizationManager.T("CreateInfobase.HintTemplate")
            : LocalizationManager.T("CreateInfobase.HintEmpty");

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            // Область полей растягивается, когда окно фиксированной высоты
            // с деревом шаблонов, и сжимается по содержимому в пустом режиме.
            grid.RowDefinitions.Add(_fromTemplate
                ? new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 200 }
                : new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 4)
            };
            header.Children.Add(new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new HelpLink
            {
                HelpText = LocalizationManager.T("CreateInfobase.HelpText"),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

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
            fields.Children.Add(Field(LocalizationManager.T("CreateInfobase.NameLabel"), _nameBox));

            // Группа
            var groupRow = new Grid();
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            groupRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var gl = new TextBlock { Text = LocalizationManager.T("CreateInfobase.GroupLabel"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(gl, 0);
            groupRow.Children.Add(gl);
            ToolTip.SetTip(_groupPathBox, LocalizationManager.T("CreateInfobase.GroupTooltip"));
            Grid.SetColumn(_groupPathBox, 1);
            groupRow.Children.Add(_groupPathBox);
            var pickGroup = new Button { Content = LocalizationManager.T("CreateInfobase.ChooseGroup"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(pickGroup, LocalizationManager.T("CreateInfobase.ChooseGroupTooltip"));
            pickGroup.Click += (_, _) => OnPickGroup_Click();
            Grid.SetColumn(pickGroup, 2);
            groupRow.Children.Add(pickGroup);
            fields.Children.Add(groupRow);

            // Платформа
            var platRow = new Grid();
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            platRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var pl = new TextBlock { Text = LocalizationManager.T("CreateInfobase.PlatformVersionLabel"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pl, 0);
            platRow.Children.Add(pl);
            ToolTip.SetTip(_platformBox, LocalizationManager.T("CreateInfobase.SelectedPlatformTooltip"));
            Grid.SetColumn(_platformBox, 1);
            platRow.Children.Add(_platformBox);
            var platButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
            var pickPlatform = new Button { Content = LocalizationManager.T("CreateInfobase.List"), MinWidth = 90 };
            ToolTip.SetTip(pickPlatform, LocalizationManager.T("CreateInfobase.ListTooltip"));
            pickPlatform.Click += (_, _) => OnPickPlatform_Click();
            platButtons.Children.Add(pickPlatform);
            var editPaths = new Button { Content = LocalizationManager.T("CreateInfobase.Paths"), MinWidth = 70 };
            ToolTip.SetTip(editPaths, LocalizationManager.T("CreateInfobase.PathsTooltip"));
            editPaths.Click += (_, _) => OnEditPlatformPaths_Click();
            platButtons.Children.Add(editPaths);
            Grid.SetColumn(platButtons, 2);
            platRow.Children.Add(platButtons);
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

            // Шаблон: дерево установленных поставок из манифестов 1cv8.mft плюс ручной выбор файла.
            if (_fromTemplate)
            {
                var tplPanel = new StackPanel();
                tplPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("CreateInfobase.TemplateManifestsLabel"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                tplPanel.Children.Add(_templateRootsHint);

                _templateLoadingPanel.Children.Add(new ProgressBar
                {
                    Width = 140,
                    Height = 14,
                    IsIndeterminate = true,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _templateLoadingPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationManager.T("CreateInfobase.Loading"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                tplPanel.Children.Add(_templateLoadingPanel);

                _templateTree.ItemTemplate = new FuncTreeDataTemplate(
                    typeof(object),
                    (item, _) => BuildTemplateRow(item),
                    item => item is OneCTemplateService.TemplateTreeNode n && n.Children.Count > 0
                        ? n.Children
                        : (System.Collections.IEnumerable)System.Array.Empty<object>());
                // В разметке WPF узлы дерева раскрыты по умолчанию (ItemContainerStyle),
                // здесь то же самое стилем на TreeViewItem.
                _templateTree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
                {
                    Setters =
                    {
                        new Setter(TreeViewItem.IsExpandedProperty, true),
                        new Setter(TreeViewItem.PaddingProperty, new Thickness(2, 1))
                    }
                });
                _templateTree.SelectionChanged += (_, _) => OnTemplateSelected();
                _templateTree.Margin = new Thickness(0, 0, 0, 6);
                _templateTree.BorderThickness = new Thickness(1);
                ThemeBrushes.Bind(_templateTree, TemplatedControl.BorderBrushProperty, "BorderColorBrush");
                ThemeBrushes.Bind(_templateTree, TemplatedControl.BackgroundProperty, "CardBackgroundColorBrush");
                // Как в разметке WPF: горизонтальной прокрутки нет, иначе строке дерева
                // достаётся бесконечная ширина и длинный путь не переносится.
                ScrollViewer.SetHorizontalScrollBarVisibility(_templateTree, ScrollBarVisibility.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(_templateTree, ScrollBarVisibility.Auto);
                tplPanel.Children.Add(_templateTree);

                var tplRow = new Grid();
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                tplRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                ToolTip.SetTip(_templateBox, LocalizationManager.T("CreateInfobase.TemplatePathTooltip"));
                Grid.SetColumn(_templateBox, 0);
                tplRow.Children.Add(_templateBox);
                var tplButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
                var refreshTemplates = new Button { Content = LocalizationManager.T("CreateInfobase.Refresh"), MinWidth = 90 };
                ToolTip.SetTip(refreshTemplates, LocalizationManager.T("CreateInfobase.RefreshTooltip"));
                refreshTemplates.Click += (_, _) => LoadInstalledTemplates();
                tplButtons.Children.Add(refreshTemplates);
                var browseTemplate = new Button { Content = LocalizationManager.T("CreateInfobase.File"), MinWidth = 90 };
                ToolTip.SetTip(browseTemplate, LocalizationManager.T("CreateInfobase.FileTooltip"));
                browseTemplate.Click += (_, _) => OnBrowseTemplate_Click();
                tplButtons.Children.Add(browseTemplate);
                Grid.SetColumn(tplButtons, 1);
                tplRow.Children.Add(tplButtons);
                tplPanel.Children.Add(tplRow);

                fields.Children.Add(tplPanel);
            }

            var fieldsHost = new ScrollViewer
            {
                Content = fields,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 4, 0)
            };
            Grid.SetRow(fieldsHost, 2);
            grid.Children.Add(fieldsHost);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 16, 0, 0)
            };
            // Оформление и порядок по разметке (CreateInfobaseWindow.xaml:184):
            // зелёное создание шириной 140 слева, красная отмена шириной 130 справа.
            // Кнопка создания закрывает окно сама, только если проверки прошли,
            // поэтому она собирается здесь, а не общим методом базового класса.
            var create = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        IconHelper.MakeIcon("IconDatabase", 16, Brushes.White),
                        new TextBlock
                        {
                            Text = LocalizationManager.T("CreateInfobase.Create"),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Width = 140,
                Height = 36,
                IsDefault = true
            };
            create.Styled(ControlThemes.DialogConfirmButton);
            create.Click += (_, _) => OnCreate_Click();
            buttons.Children.Add(create);
            buttons.Children.Add(BuildCancelActionButton(130));

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

        private static Control BuildTemplateRow(object? item)
        {
            var node = item as OneCTemplateService.TemplateTreeNode;
            // Ширину не фиксируем: после отступов дерева фиксированная не влезает
            // и длинный путь уходит под полосу прокрутки.
            var panel = new StackPanel { Margin = new Thickness(2, 3) };
            panel.Children.Add(new TextBlock
            {
                Text = node?.Title ?? item?.ToString() ?? "",
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(node?.Subtitle))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = node!.Subtitle,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 36
                });
            }
            return panel;
        }

        private void OnTemplateSelected()
        {
            if (_templateTree.SelectedItem is OneCTemplateService.TemplateTreeNode { Template: { } t })
            {
                _templateBox.Text = t.FilePath;
                if (string.IsNullOrWhiteSpace(_nameBox.Text) || NameWasSuggested())
                    _nameBox.Text = SuggestNameFromTemplate(t);
            }
        }

        private bool NameWasSuggested()
        {
            var name = _nameBox.Text?.Trim() ?? "";
            return _flatTemplates.Any(t =>
                SuggestNameFromTemplate(t).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

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

        private void LoadInstalledTemplates()
        {
            // Общий сервис токена отмены не принимает, поэтому уже запущенный обход
            // каталогов доводится до конца, а его результат отбрасывается по номеру
            // поколения. Так же устроено в версии для Windows, только там для этого
            // держится CancellationTokenSource, который ничего не отменяет.
            var generation = ++_templateLoadGeneration;

            // Окно показываем сразу, а сканирование каталогов шаблонов идёт в фоне:
            // на настоящей поставке манифестов около тысячи.
            ShowTemplateLoading(true);
            _templateTree.ItemsSource = null;
            _flatTemplates = new List<OneCTemplateService.TemplateInfo>();

            Task.Run(() =>
            {
                var roots = OneCTemplateService.GetTemplateRootFolders().ToList();
                var primary = roots.Count > 0
                    ? roots[0]
                    : OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
                var primaryExists = Directory.Exists(primary);

                var templates = OneCTemplateService.FindInstalledTemplates().ToList();
                var tree = OneCTemplateService.BuildTemplateTree(templates);
                return (Primary: primary, Roots: (IReadOnlyList<string>)roots, PrimaryExists: primaryExists,
                        Templates: templates, Tree: tree);
            }).ContinueWith(t =>
            {
                // Исключение фоновой задачи читаем всегда, иначе оно остаётся
                // ненаблюдённым и всплывает как UnobservedTaskException.
                var error = t.Exception;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_closed || generation != _templateLoadGeneration)
                        return;

                    ShowTemplateLoading(false);

                    if (error is not null)
                    {
                        _flatTemplates = new List<OneCTemplateService.TemplateInfo>();
                        _templateTree.ItemsSource = null;
                        _templateRootsHint.Text += LocalizationManager.T("CreateInfobase.LoadingFailed");
                        return;
                    }

                    var r = t.Result;
                    _flatTemplates = r.Templates;
                    _templateTree.ItemsSource = r.Tree;
                    UpdateTemplateRootsHint(r.Primary, r.Roots, r.PrimaryExists);

                    if (r.Templates.Count == 0)
                        _templateRootsHint.Text += LocalizationManager.T("CreateInfobase.NoTemplates");
                });
            });
        }

        private void UpdateTemplateRootsHint(string primary, IReadOnlyList<string> roots, bool primaryExists)
        {
            _templateRootsHint.Text =
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
            _templateLoadingPanel.IsVisible = loading;
            _templateTree.IsEnabled = !loading;
        }

        private void OnEditPlatformPaths_Click()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.DataContext is ViewModels.MainViewModel vm)
            {
                // Список версий обновляется после закрытия настроек, как в версии
                // для Windows. Команда главной модели показывает окно без ожидания,
                // поэтому окно открывается здесь напрямую и модально.
                new SettingsWindow(vm).ShowDialogSync(this);
                RefreshPlatformList();
            }
            else
            {
                _dialogs.ShowInfo(
                    LocalizationManager.T("CreateInfobase.NoPlatformsPathsMsg"),
                    LocalizationManager.T("CreateInfobase.PlatformPathsTitle"));
            }
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

            if (platforms.Count == 0)
                return;

            // По умолчанию подставляем последнюю успешно использованную версию для текущего
            // типа базы (файловая/клиент-серверная), если она всё ещё установлена. Иначе — самую новую.
            string selected = platforms[0];
            var saved = GetSavedPlatformVersion();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                foreach (var p in platforms)
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

            if (string.IsNullOrWhiteSpace(_platformBox.Text))
                _platformBox.Text = selected;
        }

        /// <summary>
        /// Возвращает последнюю успешно использованную версию платформы для текущего типа базы.
        /// Тип базы определяется по <see cref="_typeBox"/>; по умолчанию (до инициализации) — файловая.
        /// </summary>
        private string GetSavedPlatformVersion()
        {
            var settings = _repository.LoadSettings();
            var isFile = _typeBox.SelectedIndex != 1;
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

                // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
                // отличается (по major.minor) от версий, которыми уже работают
                // клиент-серверные базы на этом же сервере. Создание можно продолжить.
                var existingVersion = GetIncompatibleExistingVersion(platform, server);
                if (existingVersion != null)
                {
                    var proceed = _dialogs.Confirm(
                        string.Format(
                            LocalizationManager.T("CreateInfobase.VersionMismatchMsg"),
                            platform, existingVersion, server),
                        LocalizationManager.T("CreateInfobase.VersionMismatchTitle"));
                    if (!proceed)
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
                    blockScheduledJobs: _blockJobsCheck.IsChecked == true);
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

            // Создание прошло успешно — запоминаем версию для подстановки по умолчанию.
            SaveLastPlatformVersion(isFile, platform);

            DialogResult = true;
            Close();
        }
    }
}
#endif