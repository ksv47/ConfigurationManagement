#if LINUX
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Controls;
using Avalonia.Input;
using Configuration_Management.Localization;
using Configuration_Management.Themes;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настройки подключения к информационной базе. Avalonia/Linux-версия
    /// WPF-окна <see cref="ConnectionSettingsWindow"/>. Привязывается к
    /// <see cref="ConnectionSettingsViewModel"/>.
    /// </summary>
    public class ConnectionSettingsWindow : ModalWindowBase
    {
        private readonly ConnectionSettingsViewModel _viewModel;
        private readonly IDialogService _dialogs;

        private readonly PasswordBox _passwordBox = new();
        private readonly PasswordBox _repositoryPasswordBox = new();
        private readonly PasswordBox _configuratorPasswordBox = new();

        private bool _isSyncingPassword;
        private bool _isSyncingRepositoryPassword;
        private bool _isSyncingConfiguratorPassword;

        /// <summary>
        /// Создаёт диалог настройки подключения.
        /// </summary>
        /// <param name="infobase">База для редактирования. Если null — создаётся новая база.</param>
        /// <param name="groups">Список доступных групп для выбора.</param>
        /// <param name="installedPlatformVersions">Список установленных версий платформы 1С.</param>
        /// <param name="defaultGroupPath">Путь группы по умолчанию для новой базы.</param>
        /// <param name="availableServers">Список серверов 1С из других баз списка.</param>
        /// <param name="availablePorts">Список портов серверов 1С из других баз списка.</param>
        public ConnectionSettingsWindow(Infobase? infobase = null, IEnumerable<Group>? groups = null,
            IEnumerable<string>? installedPlatformVersions = null, string? defaultGroupPath = null,
            IEnumerable<string>? availableServers = null, IEnumerable<int>? availablePorts = null)
        {
            // Размеры и базовый кегль по разметке (ConnectionSettingsWindow.xaml:13).
            Title = LocalizationManager.T("ConnectionSettings.Title");
            Width = 760;
            Height = 780;
            MinWidth = 680;
            MinHeight = 680;
            FontSize = 13;

            _dialogs = AppServices.GetRequiredService<IDialogService>();

            _viewModel = new ConnectionSettingsViewModel(groups);
            _viewModel.SetInstalledPlatformVersions(installedPlatformVersions ?? new List<string>());
            _viewModel.SetAvailableServers(availableServers);
            _viewModel.SetAvailablePorts(availablePorts);
            if (infobase != null)
            {
                _viewModel.LoadFrom(infobase);
                Result.IsFavorite = infobase.IsFavorite;
                Result.IsPinned = infobase.IsPinned;
                Result.Tags = new List<string>(infobase.Tags);
                Result.LastLaunchDate = infobase.LastLaunchDate;
                Result.MetadataRoot = infobase.MetadataRoot;
            }
            else if (!string.IsNullOrWhiteSpace(defaultGroupPath))
            {
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

            Content = BuildRoot();

            Opened += (_, _) =>
            {
                SyncPasswordBoxFromViewModel();
                SyncRepositoryPasswordBoxFromViewModel();
                SyncConfiguratorPasswordBoxFromViewModel();
            };
        }

        /// <summary>Возвращает отредактированную информационную базу.</summary>
        public Infobase Result { get; private set; } = new();

        // ===================== Вспомогательные построители =====================

        /// <summary>Ширина колонки подписей, как в разметке (ConnectionSettingsWindow.xaml:203).</summary>
        private const double LabelColumn = 120;

        private static TextBox Tb(string path, bool readOnly = false)
        {
            var tb = new TextBox
            {
                Padding = new Thickness(6, 4),
                MinHeight = 28,
                FontSize = 12,
                Margin = new Thickness(0, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = readOnly
            };
            tb.Styled(ControlThemes.ModernTextBox);
            // Свойство только для чтения привязывается односторонне, как в разметке
            // (ConnectionSettingsWindow.xaml:219): двусторонняя привязка к свойству
            // без сеттера пишет ошибку в журнал при каждом обновлении.
            tb.Bind(TextBox.TextProperty,
                new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay });
            return tb;
        }

        private static ComboBox EditableCombo(string textPath, string itemsPath)
        {
            var combo = new ComboBox
            {
                IsEditable = true,
                Margin = new Thickness(0, 3),
                MinHeight = 28,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            combo.Bind(ComboBox.TextProperty, new Binding(textPath) { Mode = BindingMode.TwoWay });
            combo.Bind(ComboBox.ItemsSourceProperty, new Binding(itemsPath));
            return combo;
        }

        /// <summary>Сетка «подпись / поле»: колонка подписей и остаток под поле.</summary>
        private static Grid FieldsGrid(int rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(LabelColumn)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            for (var i = 0; i < rows; i++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            return grid;
        }

        /// <summary>
        /// Кладёт в сетку строку «подпись и поле». Путь видимости нужен строкам,
        /// которые в разметке лежат в одной и той же строке и переключаются
        /// по типу подключения.
        /// </summary>
        private static void Place(Grid grid, int row, string labelKey, Control control,
            string? visibilityPath = null, VerticalAlignment labelAlignment = VerticalAlignment.Center)
        {
            var label = new TextBlock
            {
                Text = LocalizationManager.T(labelKey),
                VerticalAlignment = labelAlignment,
                Margin = new Thickness(0, 3)
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);

            if (visibilityPath is null)
                return;

            label.Bind(Visual.IsVisibleProperty, new Binding(visibilityPath));
            control.Bind(Visual.IsVisibleProperty, new Binding(visibilityPath));
        }

        /// <summary>Строка «поле и кнопка справа», как в разметке.</summary>
        private static Grid WithButton(Control field, Button button)
        {
            var grid = new Grid { Margin = new Thickness(0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            field.Margin = new Thickness(0);
            Grid.SetColumn(field, 0);
            grid.Children.Add(field);
            button.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            return grid;
        }

        /// <summary>Вторичная кнопка со значком и подписью, как в разметке окна.</summary>
        private static Button SecondaryButton(string iconKey, string textKey, Action onClick,
            string? tooltipKey = null, double iconSize = 14, IBrush? iconBrush = null)
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        iconBrush is null
                            ? IconHelper.MakeIcon(iconKey, iconSize, "SecondaryButtonTextBrush")
                            : IconHelper.MakeIcon(iconKey, iconSize, iconBrush),
                        new TextBlock
                        {
                            Text = LocalizationManager.T(textKey),
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Padding = new Thickness(10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Styled(ControlThemes.SecondaryButton);
            button.Click += (_, _) => onClick();
            if (tooltipKey is not null)
                ToolTip.SetTip(button, LocalizationManager.T(tooltipKey));
            return button;
        }

        /// <summary>
        /// Группа с заголовком: рамка карточки, а над ней подпись со значком.
        /// Повторяет шаблон GroupBox из разметки (ConnectionSettingsWindow.xaml:42).
        /// </summary>
        private static Control Group(string iconKey, string titleKey, Control content)
        {
            var grid = new Grid { Margin = new Thickness(4, 6, 4, 4), VerticalAlignment = VerticalAlignment.Top };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            Themes.ThemeBrushes.Bind(frame, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(frame, Border.BorderBrushProperty, "BorderBrushColor");
            Grid.SetRow(frame, 0);
            Grid.SetRowSpan(frame, 2);
            grid.Children.Add(frame);

            var header = new Border
            {
                Padding = new Thickness(10, 4, 10, 0),
                Margin = new Thickness(8, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        IconHelper.MakeIcon(iconKey, 16, "AccentBrush"),
                        new TextBlock
                        {
                            Text = LocalizationManager.T(titleKey),
                            FontWeight = FontWeight.SemiBold,
                            FontSize = 13,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };
            Themes.ThemeBrushes.Bind(header, Border.BackgroundProperty, "CardBackgroundBrush");
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            content.Margin = new Thickness(10, 8);
            Grid.SetRow(content, 1);
            grid.Children.Add(content);
            return grid;
        }

        /// <summary>
        /// Карточка варианта выбора: переключатель в рамке с подписью и пояснением.
        /// </summary>
        private static RadioButton OptionCard(string groupName, string path, string titleKey, string hintKey,
            string? enabledPath = null, bool wrapHint = false)
        {
            var card = new RadioButton
            {
                GroupName = groupName,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = LocalizationManager.T(titleKey),
                            FontWeight = FontWeight.SemiBold,
                            FontSize = 12
                        },
                        // Перенос строк и отступ пояснения у автора разные: на
                        // вкладках выбора типа и авторизации это одна строка
                        // с отступом 1 (xaml:279), на запуске и разрядности
                        // перенос и отступ 2 (xaml:657).
                        new TextBlock
                        {
                            Text = LocalizationManager.T(hintKey),
                            FontSize = 11,
                            Opacity = 0.75,
                            TextWrapping = wrapHint ? TextWrapping.Wrap : TextWrapping.NoWrap,
                            Margin = new Thickness(0, wrapHint ? 2 : 1, 0, 0)
                        }
                    }
                }
            };
            card.Styled(ControlThemes.OptionCard);
            card.Bind(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            if (enabledPath is not null)
                card.Bind(InputElement.IsEnabledProperty, new Binding(enabledPath));
            return card;
        }

        /// <summary>Мелкое пояснение под группой: кегль 11, второстепенный цвет.</summary>
        private static TextBlock Hint(string? textKey = null, string? bindingPath = null,
            Thickness? margin = null, double fontSize = 11)
        {
            var block = new TextBlock
            {
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin ?? new Thickness(2, 6, 0, 0)
            };
            if (textKey is not null)
                block.Text = LocalizationManager.T(textKey);
            if (bindingPath is not null)
                block.Bind(TextBlock.TextProperty, new Binding(bindingPath));
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return block;
        }

        private static TabItem Tab(string iconKey, string titleKey, Control content)
        {
            var tab = new TabItem
            {
                Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            tab.Styled(ControlThemes.ConnTabItem);

            // Значок берёт цвет подписи: у выбранной вкладки тема меняет Foreground,
            // а заливка контура сама по себе не наследуется, и значок остался бы
            // светлым на тёмно-оранжевом.
            var icon = IconHelper.MakeIcon(iconKey, 16, out var path);
            path.Bind(Avalonia.Controls.Shapes.Shape.FillProperty,
                new Binding(nameof(TabItem.Foreground)) { Source = tab });

            tab.Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    icon,
                    new TextBlock { Text = LocalizationManager.T(titleKey), VerticalAlignment = VerticalAlignment.Center }
                }
            };
            return tab;
        }

        // ===================== Раскладка окна =====================

        private Control BuildRoot()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var tabs = BuildTabs();
            tabs.Margin = new Thickness(12, 8, 12, 4);
            Grid.SetRow(tabs, 1);
            grid.Children.Add(tabs);

            var bottom = BuildBottomBar();
            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Шапка окна: значок базы, заголовок и подзаголовок.</summary>
        private static Control BuildHeader()
        {
            var iconBox = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = IconHelper.MakeIcon("IconDatabase", 22, Brushes.White)
            };
            Themes.ThemeBrushes.Bind(iconBox, Border.BackgroundProperty, "AccentBrush");

            var title = new TextBlock
            {
                Text = LocalizationManager.T("Connection.HeaderTitle"),
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            };
            var subtitle = new TextBlock
            {
                Text = LocalizationManager.T("Connection.HeaderSubtitle"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Themes.ThemeBrushes.Bind(subtitle, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var band = new Border
            {
                Padding = new Thickness(20, 16),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        iconBox,
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Children = { title, subtitle }
                        }
                    }
                }
            };
            Themes.ThemeBrushes.Bind(band, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(band, Border.BorderBrushProperty, "BorderBrushColor");
            return band;
        }

        /// <summary>Нижняя панель: сохранение доступно, только пока есть изменения.</summary>
        private Control BuildBottomBar()
        {
            // Сохранение доступно, только пока есть несохранённые изменения,
            // и гаснет прозрачностью, как в разметке (ConnectionSettingsWindow.xaml:1040).
            var save = BuildConfirmActionButton("Common.Save", "IconSave", 140, OnSave_Click, closeOnClick: false);
            save.Classes.Add("dimmed");
            save.Bind(InputElement.IsEnabledProperty, new Binding("HasChanges"));

            var bar = new Border
            {
                Padding = new Thickness(16, 12),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { save, BuildCancelActionButton(140) }
                }
            };
            Themes.ThemeBrushes.Bind(bar, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(bar, Border.BorderBrushProperty, "BorderBrushColor");
            return bar;
        }

        private TabControl BuildTabs()
        {
            // Колонку вкладок слева задаёт сам шаблон темы, поэтому
            // TabStripPlacement здесь не нужен: шаблон его не читает.
            var tabs = new TabControl();
            tabs.Styled(ControlThemes.ConnTabControl);

            // Значков MaterialDesign в Linux-сборке нет, поэтому взяты ближайшие
            // из словаря автора: LanConnect это IconNetwork, SourceBranch это
            // IconMerge, PlayCircleOutline это IconPlay, Chip это IconMonitor,
            // CubeOutline это IconPackage, Identifier это IconInfo.
            tabs.Items.Add(Tab("IconDatabase", "Connection.Tab.Base", BuildBaseTab()));
            tabs.Items.Add(Tab("IconNetwork", "Connection.Tab.Connection", BuildConnectionTab()));
            tabs.Items.Add(Tab("IconMerge", "Connection.Tab.Repository", BuildRepositoryTab()));
            tabs.Items.Add(Tab("IconAccountKey", "Connection.Tab.Auth", BuildAuthTab()));
            tabs.Items.Add(Tab("IconPlay", "Connection.Tab.Launch", BuildLaunchTab()));
            tabs.Items.Add(Tab("IconMonitor", "Connection.Tab.Bitness", BuildBitnessTab()));
            tabs.Items.Add(Tab("IconPackage", "Connection.Tab.Platform", BuildPlatformTab()));
            tabs.Items.Add(Tab("IconInfo", "Connection.Tab.Id", BuildIdTab()));
            return tabs;
        }

        private Control BuildBaseTab()
        {
            var fields = FieldsGrid(3);
            Place(fields, 0, "Connection.NameLabel", Tb("Name"));

            var groupPath = Tb("GroupDisplayPath", readOnly: true);
            groupPath.Padding = new Thickness(8, 6);
            Place(fields, 1, "Connection.GroupLabel",
                WithButton(groupPath, SecondaryButton("IconFolderOutline", "Connection.ChooseGroup", OnSelectGroup_Click)));

            Place(fields, 2, "Connection.DescriptionLabel", Tb("Description"));
            return Group("IconDatabase", "Connection.GroupBase", fields);
        }

        private Control BuildConnectionTab()
        {
            var fields = FieldsGrid(4);

            var types = new StackPanel
            {
                Children =
                {
                    OptionCard("ConnType", "IsClientServer", "Connection.TypeServer", "Connection.TypeServerHint"),
                    OptionCard("ConnType", "IsFile", "Connection.TypeFile", "Connection.TypeFileHint"),
                    OptionCard("ConnType", "IsWebServer", "Connection.TypeWeb", "Connection.TypeWebHint")
                }
            };
            Place(fields, 0, "Connection.TypeLabel", types, labelAlignment: VerticalAlignment.Top);

            // Строки серверного, файлового и веб-подключения занимают одни и те же
            // строки сетки и переключаются признаком типа, как в разметке
            // (ConnectionSettingsWindow.xaml:324, :346, :363).
            var server = EditableCombo("Server", "AvailableServers");
            ToolTip.SetTip(server, LocalizationManager.T("Connection.ServerTooltip"));
            Place(fields, 1, "Connection.ServerLabel", server, "IsClientServer");
            Place(fields, 2, "Connection.DatabaseNameLabel", Tb("DatabaseName"), "IsClientServer");
            var port = EditableCombo("PortText", "AvailablePorts");
            ToolTip.SetTip(port, LocalizationManager.T("Connection.PortTooltip"));
            Place(fields, 3, "Connection.PortLabel", port, "IsClientServer");

            Place(fields, 1, "Connection.FilePathLabel",
                WithButton(Tb("FilePath"),
                    SecondaryButton("IconFolderOpen", "Common.Browse", OnBrowseFilePath_Click, "Connection.BrowseFileTooltip")),
                "IsFile");

            Place(fields, 1, "Connection.WebUrlLabel", Tb("WebUrl"), "IsWebServer");

            var paste = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        IconHelper.MakeIcon("IconCopy", 15, "SecondaryButtonTextBrush"),
                        new TextBlock
                        {
                            Text = LocalizationManager.T("Connection.PasteString"),
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Height = 36,
                Padding = new Thickness(12, 0),
                Margin = new Thickness(0, 0, 6, 0)
            };
            paste.Styled(ControlThemes.SecondaryButton);
            paste.Click += (_, _) => OnPasteConnectionString_Click();
            ToolTip.SetTip(paste, LocalizationManager.T("Connection.PasteStringTooltip"));

            return new StackPanel
            {
                Children =
                {
                    Group("IconServer", "Connection.GroupConnectionType", fields),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(4, 8, 4, 0),
                        Children =
                        {
                            paste,
                            new Configuration_Management.Controls.HelpLink
                            {
                                HelpText = LocalizationManager.T("Connection.PasteStringHelp"),
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    }
                }
            };
        }

        private Control BuildRepositoryTab()
        {
            var fields = FieldsGrid(4);
            var server = Tb("RepositoryServer");
            ToolTip.SetTip(server, LocalizationManager.T("Connection.RepositoryServerTooltip"));
            Place(fields, 0, "Connection.RepositoryServerLabel", server);
            var name = Tb("RepositoryName");
            ToolTip.SetTip(name, LocalizationManager.T("Connection.RepositoryNameTooltip"));
            Place(fields, 1, "Connection.RepositoryNameLabel", name);
            var user = Tb("RepositoryUser");
            ToolTip.SetTip(user, LocalizationManager.T("Connection.RepositoryUserTooltip"));
            Place(fields, 2, "Connection.RepositoryLoginLabel", user);

            _repositoryPasswordBox.Margin = new Thickness(0, 3);
            _repositoryPasswordBox.Padding = new Thickness(6, 4);
            _repositoryPasswordBox.VerticalContentAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(_repositoryPasswordBox, LocalizationManager.T("Connection.RepositoryPasswordTooltip"));
            _repositoryPasswordBox.PasswordChanged += (_, _) =>
            {
                if (_isSyncingRepositoryPassword) return;
                _viewModel.RepositoryPassword = _repositoryPasswordBox.Password;
            };
            Place(fields, 3, "Connection.RepositoryPasswordLabel", _repositoryPasswordBox);

            var content = new StackPanel
            {
                Children = { fields, Hint("Connection.RepositoryDescription", margin: new Thickness(0, 8, 0, 0)) }
            };
            return Group("IconMerge", "Connection.GroupRepository", content);
        }

        private Control BuildAuthTab()
        {
            var enterprise = FieldsGrid(3);
            Place(enterprise, 0, "Connection.ModeLabel", new StackPanel
            {
                Children =
                {
                    OptionCard("Auth", "IsAuthPrompt", "Connection.AuthPrompt", "Connection.AuthPromptHint"),
                    OptionCard("Auth", "IsAuthCredentials", "Connection.AuthAuto", "Connection.AuthAutoHint"),
                    OptionCard("Auth", "IsAuthWindows", "Connection.AuthOs", "Connection.AuthOsHint")
                }
            }, labelAlignment: VerticalAlignment.Top);

            var user = Tb("User");
            Place(enterprise, 1, "Connection.UserLabel", user, "IsCredentialsVisible");

            _passwordBox.Margin = new Thickness(0, 3);
            _passwordBox.Padding = new Thickness(6, 4);
            _passwordBox.VerticalContentAlignment = VerticalAlignment.Center;
            _passwordBox.PasswordChanged += (_, _) =>
            {
                if (_isSyncingPassword) return;
                _viewModel.Password = _passwordBox.Password;
            };
            Place(enterprise, 2, "Connection.PasswordLabel", _passwordBox, "IsCredentialsVisible");

            var configurator = FieldsGrid(3);
            Place(configurator, 0, "Connection.ModeLabel", new StackPanel
            {
                Children =
                {
                    OptionCard("ConfigAuth", "IsConfiguratorAuthPrompt", "Connection.AuthPrompt", "Connection.AuthConfiguratorPromptHint"),
                    OptionCard("ConfigAuth", "IsConfiguratorAuthCredentials", "Connection.AuthAuto", "Connection.AuthAutoHint"),
                    OptionCard("ConfigAuth", "IsConfiguratorAuthWindows", "Connection.AuthOs", "Connection.AuthOsHint")
                }
            }, labelAlignment: VerticalAlignment.Top);

            Place(configurator, 1, "Connection.UserLabel", Tb("ConfiguratorUser"), "IsConfiguratorCredentialsVisible");

            _configuratorPasswordBox.Margin = new Thickness(0, 3);
            _configuratorPasswordBox.Padding = new Thickness(6, 4);
            _configuratorPasswordBox.VerticalContentAlignment = VerticalAlignment.Center;
            _configuratorPasswordBox.PasswordChanged += (_, _) =>
            {
                if (_isSyncingConfiguratorPassword) return;
                _viewModel.ConfiguratorPassword = _configuratorPasswordBox.Password;
            };
            Place(configurator, 2, "Connection.PasswordLabel", _configuratorPasswordBox, "IsConfiguratorCredentialsVisible");

            return new StackPanel
            {
                Children =
                {
                    Group("IconAccountKey", "Connection.GroupAuthEnterprise", enterprise),
                    Group("IconApplicationCog", "Connection.GroupAuthConfigurator", configurator)
                }
            };
        }

        private Control BuildLaunchTab()
        {
            var content = new StackPanel
            {
                Children =
                {
                    OptionCard("LaunchMode", "IsAutoMode", "Connection.LaunchAuto", "Connection.LaunchAutoHint", wrapHint: true),
                    OptionCard("LaunchMode", "IsThinClient", "Connection.LaunchThin", "Connection.LaunchThinHint", wrapHint: true),
                    OptionCard("LaunchMode", "IsThickClient", "Connection.LaunchThickManaged", "Connection.LaunchThickManagedHint", wrapHint: true),
                    OptionCard("LaunchMode", "IsThickOrdinaryClient", "Connection.LaunchThickOrdinary", "Connection.LaunchThickOrdinaryHint", wrapHint: true),
                    // Веб-клиент доступен только у веб-подключения, как в разметке
                    // (ConnectionSettingsWindow.xaml:734).
                    OptionCard("LaunchMode", "IsWebClient", "Connection.LaunchWeb", "Connection.LaunchWebHint", "IsWebClientAllowed", wrapHint: true),
                    Hint(bindingPath: "LaunchModeHint")
                }
            };
            return Group("IconApplication", "Connection.GroupLaunchMode", content);
        }

        private Control BuildBitnessTab()
        {
            // Тексты автора говорят «Windows» в обоих вариантах, поэтому для
            // Linux заведены свои ключи, а не переиспользованы его.
            var osHint = Hint(Environment.Is64BitOperatingSystem
                ? "Connection.OsLinux64Text"
                : "Connection.OsLinux32Text", margin: new Thickness(2, 2, 0, 0));
            if (!Environment.Is64BitOperatingSystem)
                osHint.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));

            var content = new StackPanel
            {
                Children =
                {
                    OptionCard("Arch", "IsArchitecture32Priority", "Connection.Arch32Priority", "Connection.Arch32PriorityHint", wrapHint: true),
                    OptionCard("Arch", "IsArchitecture64Priority", "Connection.Arch64Priority", "Connection.Arch64PriorityHint", wrapHint: true),
                    OptionCard("Arch", "IsArchitecture32", "Connection.ArchOnly32", "Connection.ArchOnly32Hint", wrapHint: true),
                    // Только 64 недоступно на 32-битной системе (xaml:852).
                    OptionCard("Arch", "IsArchitecture64", "Connection.ArchOnly64", "Connection.ArchOnly64Hint", "IsOs64Bit", wrapHint: true),
                    Hint(bindingPath: "ArchitectureHint"),
                    osHint
                }
            };
            return Group("IconMonitor", "Connection.GroupBitness", content);
        }

        private Control BuildPlatformTab()
        {
            var fields = FieldsGrid(3);

            var version = Tb("PlatformVersion");
            version.Padding = new Thickness(8, 6);
            ToolTip.SetTip(version, LocalizationManager.T("Connection.PlatformVersionTooltip"));
            var pickPlatform = SecondaryButton("IconPackage", "Connection.ChoosePlatform", OnPlatformSettings_Click);
            pickPlatform.Padding = new Thickness(8, 3);
            Place(fields, 0, "Connection.VersionLabel", WithButton(version, pickPlatform));

            var configRow = new Grid { Margin = new Thickness(0, 6, 0, 3) };
            configRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            configRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
            configRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var configName = Tb("ConfigurationName");
            configName.Margin = new Thickness(0);
            configName.Padding = new Thickness(8, 6);
            ToolTip.SetTip(configName, LocalizationManager.T("Connection.ConfigurationNameTooltip"));
            Grid.SetColumn(configName, 0);
            configRow.Children.Add(configName);
            var configVersion = Tb("ConfigurationVersion");
            configVersion.Margin = new Thickness(0);
            configVersion.Padding = new Thickness(8, 6);
            ToolTip.SetTip(configVersion, LocalizationManager.T("Connection.ConfigurationVersionTooltip"));
            Grid.SetColumn(configVersion, 2);
            configRow.Children.Add(configVersion);
            Place(fields, 1, "Connection.ConfigurationLabel", configRow);

            var parameters = Tb("LaunchParameters");
            parameters.Padding = new Thickness(8, 6);
            var pickParameters = SecondaryButton("IconTune", "Connection.Parameters", OnLaunchParameters_Click);
            pickParameters.Padding = new Thickness(8, 3);
            Place(fields, 2, "Connection.ParametersLabel", WithButton(parameters, pickParameters));

            return Group("IconPackage", "Connection.GroupPlatform", fields);
        }

        private Control BuildIdTab()
        {
            var id = Tb("Id");
            ToolTip.SetTip(id, LocalizationManager.T("Connection.IdTooltip"));

            var label = new TextBlock
            {
                Text = LocalizationManager.T("Connection.IdLabel"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            // Значок обновления зелёный, как в разметке (xaml:996).
            var generate = SecondaryButton("IconRefresh", "Connection.GenerateId",
                () => _viewModel.Id = Guid.NewGuid().ToString("D"), "Connection.GenerateIdTooltip",
                iconBrush: new SolidColorBrush(Color.Parse("#22C55E")));
            generate.Padding = new Thickness(10, 5);
            generate.Margin = new Thickness(0, 0, 8, 0);
            var copy = SecondaryButton("IconCopy", "Connection.CopyId", OnCopyId_Click, "Connection.CopyIdTooltip");
            copy.Padding = new Thickness(10, 5);

            return new StackPanel
            {
                Margin = new Thickness(8, 12, 8, 8),
                VerticalAlignment = VerticalAlignment.Top,
                Children =
                {
                    Hint("Connection.IdDescription", margin: new Thickness(0, 0, 0, 10), fontSize: 12),
                    label,
                    id,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 8, 0, 0),
                        Children = { generate, copy }
                    }
                }
            };
        }

        // ===================== Обработчики =====================

        private void OnSelectGroup_Click()
        {
            var dialog = new GroupPickerWindow(
                _viewModel.Groups,
                currentGroupId: _viewModel.SelectedGroup?.Id,
                allowNone: true,
                noneLabel: LocalizationManager.T("Connection.NoGroup"));
            if (dialog.ShowDialogSync(this))
            {
                _viewModel.SelectedGroup = dialog.ResultGroup;
                if (dialog.ResultGroup is null)
                    _viewModel.Group = string.Empty;
            }
        }

        private void OnBrowseFilePath_Click()
        {
            var current = _viewModel.FilePath;
            var path = _dialogs.OpenFolderDialog(LocalizationManager.T("Connection.ChooseFolderTitle"),
                !string.IsNullOrWhiteSpace(current) && Directory.Exists(current) ? current : null);
            if (!string.IsNullOrWhiteSpace(path))
                _viewModel.FilePath = path;
        }

        private void OnCopyId_Click()
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.Id))
            {
                try { Clipboard?.SetTextAsync(_viewModel.Id); } catch { /* ignore */ }
            }
        }

        private void OnPasteConnectionString_Click()
        {
            var dialog = new ConnectionStringInputWindow(_viewModel.ConnectionString);
            if (!dialog.ShowDialogSync(this))
                return;

            _viewModel.ApplyConnectionString(dialog.Result);
            _viewModel.ConnectionString = dialog.Result ?? string.Empty;
            _dialogs.ShowInfo(LocalizationManager.T("Connection.PasteSuccess"), LocalizationManager.T("Connection.PasteSuccessTitle"));
        }

        private void OnLaunchParameters_Click()
        {
            var dialog = new LaunchParametersWindow(_viewModel.LaunchParameters);
            if (dialog.ShowDialogSync(this))
            {
                _viewModel.LaunchParameters = dialog.Result;
            }
        }

        private void OnPlatformSettings_Click()
        {
            var current = _viewModel.PlatformVersion ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_viewModel.Architecture)
                && _viewModel.Architecture is "32" or "64"
                && !current.Contains('('))
            {
                current = $"{current} ({_viewModel.Architecture})".Trim();
            }

            var dialog = new PlatformVersionPickerWindow(_viewModel.InstalledPlatformVersions, current);
            if (!dialog.ShowDialogSync(this))
                return;

            var result = (dialog.Result ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(result))
                return;

            PlatformVersionService.ParseVariant(result, out var version, out var architecture);
            _viewModel.PlatformVersion = string.IsNullOrWhiteSpace(version) ? result : version;
            if (result.Contains('(') && (architecture == "32" || architecture == "64"))
                _viewModel.Architecture = architecture;
        }

        private void OnSave_Click()
        {
            _viewModel.ApplyTo(Result);

            if (string.IsNullOrWhiteSpace(Result.Id))
            {
                var id = IbasesV8iImporter.FindId(Result.Name, Result.Connection.ToConnectionString());
                if (!string.IsNullOrWhiteSpace(id))
                {
                    Result.Id = id;
                }
                else
                {
                    Result.Id = Guid.NewGuid().ToString("D");
                }
            }

            DialogResult = true;
            Close();
        }

        // ===================== Синхронизация PasswordBox =====================

        private void SyncPasswordBoxFromViewModel()
        {
            _isSyncingPassword = true;
            try
            {
                if (_passwordBox.Password != (_viewModel.Password ?? string.Empty))
                    _passwordBox.Password = _viewModel.Password ?? string.Empty;
            }
            finally
            {
                _isSyncingPassword = false;
            }
        }

        private void SyncRepositoryPasswordBoxFromViewModel()
        {
            _isSyncingRepositoryPassword = true;
            try
            {
                if (_repositoryPasswordBox.Password != (_viewModel.RepositoryPassword ?? string.Empty))
                    _repositoryPasswordBox.Password = _viewModel.RepositoryPassword ?? string.Empty;
            }
            finally
            {
                _isSyncingRepositoryPassword = false;
            }
        }

        private void SyncConfiguratorPasswordBoxFromViewModel()
        {
            _isSyncingConfiguratorPassword = true;
            try
            {
                if (_configuratorPasswordBox.Password != (_viewModel.ConfiguratorPassword ?? string.Empty))
                    _configuratorPasswordBox.Password = _viewModel.ConfiguratorPassword ?? string.Empty;
            }
            finally
            {
                _isSyncingConfiguratorPassword = false;
            }
        }
    }
}
#endif