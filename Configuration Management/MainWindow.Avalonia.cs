#if LINUX
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Configuration_Management.Controls;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Avalonia-версия главного окна (Linux). Собирается в коде (без XAML-компилятора),
    /// чтобы гарантировать компиляцию без Linux-SDK. Реализует: верхнюю панель (группы,
    /// поиск, вкладки Все/Избранное/Недавние, синхронизация, тема, настройки), дерево
    /// списка баз, правую панель (карточка базы + действия), нижнюю панель статуса и трей.
    /// </summary>
    public class MainWindow : Window
    {
        private MainViewModel? _vm;
        private TextBox _searchBox = null!;
        private TextBlock _statusInfo = null!;
        private TextBlock _syncMessage = null!;
        private LeveledTreeView _tree = null!;

        /// <summary>
        /// Если true — закрытие окна уводит приложение в трей (а не завершает).
        /// Сбрасывается командой «Выход» из трея перед Shutdown.
        /// </summary>
        private bool _allowCloseToTray = true;

        public MainWindow(MainViewModel viewModel)
        {
            _vm = viewModel;

            Title = "Управление конфигурациями 1С";
            Width = 1200;
            Height = 760;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            DataContext = viewModel;

            Content = BuildRoot();
            Loaded += OnWindowLoaded;
        }

        // ======================= Построение UI =======================

        private Control BuildRoot()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topBar = BuildTopBar();
            var mainArea = BuildMainArea();
            var statusBar = BuildStatusBar();

            Grid.SetRow(topBar, 0);
            Grid.SetRow(mainArea, 1);
            Grid.SetRow(statusBar, 2);

            grid.Children.Add(topBar);
            grid.Children.Add(mainArea);
            grid.Children.Add(statusBar);
            return grid;
        }

        private Control BuildTopBar()
        {
            var grid = new Grid { Margin = new Thickness(12, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Слева: кнопки групп/тегов
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

            var groupByToggle = new ToggleButton
            {
                Content = "⇶",
                ToolTip = new ToolTip { Content = "Показывать / скрывать группы" },
                Margin = new Thickness(0, 0, 2, 0),
                IsChecked = true
            };
            groupByToggle.Click += (_, _) => { if (_vm is not null) _vm.GroupByGroup = groupByToggle.IsChecked == true; };
            left.Children.Add(groupByToggle);

            var tagsToggle = new ToggleButton
            {
                Content = "#",
                ToolTip = new ToolTip { Content = "Показывать / скрывать теги" },
                IsChecked = true
            };
            tagsToggle.Click += (_, _) => { if (_vm is not null) _vm.ShowTagFilterPanel = tagsToggle.IsChecked == true; };
            left.Children.Add(tagsToggle);

            grid.Children.Add(left);
            Grid.SetColumn(left, 0);

            // Поиск
            var searchBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent
            };
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchIcon = new TextBlock { Text = "🔍", Margin = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            searchGrid.Children.Add(searchIcon);
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = "Поиск по базам, конфигурациям, серверу…"
            };
            _searchBox.Bind(TextBox.TextProperty, new Binding("SearchText") { Mode = BindingMode.TwoWay });
            searchGrid.Children.Add(_searchBox);
            Grid.SetColumn(_searchBox, 1);

            var clearBtn = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 0) };
            clearBtn.Bind(Button.CommandProperty, new Binding("ClearSearchCommand"));
            searchGrid.Children.Add(clearBtn);
            Grid.SetColumn(clearBtn, 2);

            searchBorder.Child = searchGrid;
            grid.Children.Add(searchBorder);
            Grid.SetColumn(searchBorder, 1);

            // Вкладки Все / Избранное / Недавние
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

            var allTab = new RadioButton { GroupName = "ListMode", Content = "Все", Margin = new Thickness(0, 0, 4, 0) };
            allTab.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeAll") { Mode = BindingMode.TwoWay });
            tabs.Children.Add(allTab);

            var favTab = new RadioButton { GroupName = "ListMode", Content = "★ Избранное", Margin = new Thickness(0, 0, 4, 0) };
            favTab.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeFavorites") { Mode = BindingMode.TwoWay });
            tabs.Children.Add(favTab);

            var recTab = new RadioButton { GroupName = "ListMode", Content = "⌛ Недавние" };
            recTab.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeRecent") { Mode = BindingMode.TwoWay });
            tabs.Children.Add(recTab);

            grid.Children.Add(tabs);
            Grid.SetColumn(tabs, 2);

            // Справа: синхронизация, тема, настройки
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var syncBtn = new Button { Content = "⟳", ToolTip = new ToolTip { Content = "Синхронизация с ibases.v8i" }, Margin = new Thickness(0, 0, 2, 0) };
            syncBtn.Bind(Button.CommandProperty, new Binding("SynchronizeWithIbasesCommand"));
            actions.Children.Add(syncBtn);

            var themeBtn = new Button { Content = "◐", ToolTip = new ToolTip { Content = "Переключить тему" }, Margin = new Thickness(0, 0, 2, 0) };
            themeBtn.Bind(Button.CommandProperty, new Binding("ToggleThemeCommand"));
            actions.Children.Add(themeBtn);

            var settingsBtn = new Button { Content = "⚙", ToolTip = new ToolTip { Content = "Настройки приложения" } };
            settingsBtn.Bind(Button.CommandProperty, new Binding("OpenSettingsCommand"));
            actions.Children.Add(settingsBtn);

            grid.Children.Add(actions);
            Grid.SetColumn(actions, 3);

            return new Border
            {
                Child = grid,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10)
            };
        }

        private Control BuildMainArea()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tree = new LeveledTreeView
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            _tree.Bind(TreeView.ItemsSourceProperty, new Binding("GroupNodes"));
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                BuildTreeRow,
                item => item is GroupNodeViewModel g ? g.Items : null);
            _tree.SelectionChanged += OnTreeSelectionChanged;

            var leftPanel = new Border
            {
                Child = new ScrollViewer
                {
                    Content = _tree,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8, 8)
                },
                Margin = new Thickness(12, 12, 8, 12)
            };

            grid.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);

            var rightPanel = new ScrollViewer
            {
                Content = BuildRightPanel(),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16, 14),
                MinWidth = 300,
                MaxWidth = 380
            };

            grid.Children.Add(rightPanel);
            Grid.SetColumn(rightPanel, 1);

            return grid;
        }

        /// <summary>Строит строку дерева: заголовок группы или карточку базы.</summary>
        private Control BuildTreeRow(object? item)
        {
            if (item is GroupNodeViewModel group)
                return BuildGroupRow(group);
            if (item is Infobase ib)
                return BuildInfobaseRow(ib);
            return new TextBlock { Text = item?.ToString() ?? string.Empty };
        }

        private Control BuildGroupRow(GroupNodeViewModel group)
        {
            var header = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 3),
                Margin = new Thickness(0, 1)
            };
            header.Bind(Border.BackgroundProperty, new Binding("HeaderBrush") { Source = group });

            var text = new TextBlock
            {
                Text = $"{group.DisplayName}  ({group.TotalInfobaseCount})",
                FontWeight = FontWeight.SemiBold
            };
            text.Bind(TextBlock.ForegroundProperty, new Binding("HeaderTextBrush") { Source = group });
            header.Child = text;
            return header;
        }

        private Control BuildInfobaseRow(Infobase ib)
        {
            var panel = new StackPanel { Margin = new Thickness(6, 2) };
            var name = new TextBlock { Text = ib.Name, FontWeight = FontWeight.SemiBold };
            panel.Children.Add(name);

            var details = new TextBlock
            {
                Text = $"{ib.ServerDatabaseDisplay}  •  {ib.ConnectionTypeDisplay}",
                FontSize = 11,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(details);

            if (ib.IsFavorite || ib.IsPinned)
            {
                var marks = new TextBlock { FontSize = 10, Text = (ib.IsFavorite ? "★ " : "") + (ib.IsPinned ? "📌" : "") };
                panel.Children.Add(marks);
            }
            return panel;
        }

        private Control BuildRightPanel()
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

            // Заголовок базы
            var nameBlock = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            nameBlock.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.Name"));

            var groupBlock = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            groupBlock.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.GroupDisplay"));

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Spacing = 10 };
            header.Children.Add(new TextBlock { Text = "🗄", FontSize = 26 });
            var headerText = new StackPanel();
            headerText.Children.Add(nameBlock);
            headerText.Children.Add(groupBlock);
            header.Children.Add(headerText);
            panel.Children.Add(header);

            // Информация о подключении
            panel.Children.Add(SectionLabel("Информация о подключении"));
            panel.Children.Add(DetailRow("Тип", new Binding("SelectedInfobase.ConnectionTypeDisplay")));
            panel.Children.Add(DetailRow("Сервер / путь", new Binding("SelectedInfobase.ConnectionPathDisplay")));
            panel.Children.Add(DetailRow("Строка", new Binding("SelectedInfobase.ConnectionStringDisplay")));
            panel.Children.Add(DetailRow("Платформа", new Binding("SelectedInfobase.PlatformVersion")));
            panel.Children.Add(DetailRow("Режим запуска", new Binding("SelectedInfobase.ParsedLaunchMode")));
            panel.Children.Add(DetailRow("Разрядность", new Binding("SelectedInfobase.ArchitectureDisplay")));
            panel.Children.Add(DetailRow("Последний запуск", new Binding("SelectedInfobase.LastLaunchDisplay")));

            // Описание
            panel.Children.Add(SectionLabel("Описание"));
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            desc.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.Description"));
            panel.Children.Add(desc);

            // Действия
            panel.Children.Add(SectionLabel("Действия"));

            var launchBtn = ActionButton("▶ 1С:Предприятие", "LaunchEnterpriseCommand");
            var configBtn = ActionButton("🔧 Конфигуратор", "LaunchConfiguratorCommand");
            var editBtn = ActionButton("✎ Изменить настройки", "EditInfobaseCommand");
            var favBtn = ActionButton("★ Избранное", "ToggleFavoriteCommand");
            var pinBtn = ActionButton("📌 Закрепить", "TogglePinCommand");
            var addBtn = ActionButton("＋ Добавить базу / группу", "AddInfobaseCommand");
            var delBtn = ActionButton("🗑 Удалить", "DeleteInfobaseCommand");

            var openFolderBtn = ActionButton("🗁 Открыть папку базы", "OpenInfobaseFolderCommand");
            var shortcutBtn = ActionButton("⌗ Ярлык на рабочем столе", "CreateDesktopShortcutCommand");
            var starterBtn = ActionButton("⌨ Запустить стартер 1С", "OpenNativeStarterCommand");

            panel.Children.Add(launchBtn);
            panel.Children.Add(configBtn);
            panel.Children.Add(editBtn);
            panel.Children.Add(favBtn);
            panel.Children.Add(pinBtn);
            panel.Children.Add(addBtn);
            panel.Children.Add(delBtn);
            panel.Children.Add(openFolderBtn);
            panel.Children.Add(shortcutBtn);
            panel.Children.Add(starterBtn);

            var exitBtn = ActionButton("✕ Выход", "ExitCommand");
            panel.Children.Add(new Separator() { Margin = new Thickness(0, 6, 0, 6) });
            panel.Children.Add(exitBtn);

            return panel;
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private Control DetailRow(string label, Binding binding)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 };
            grid.Children.Add(labelBlock);
            Grid.SetColumn(labelBlock, 0);

            var valueBlock = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            valueBlock.Bind(TextBlock.TextProperty, binding);
            grid.Children.Add(valueBlock);
            Grid.SetColumn(valueBlock, 1);
            return grid;
        }

        private static Button ActionButton(string text, string commandPath)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        private Control BuildStatusBar()
        {
            var grid = new Grid { Padding = new Thickness(12, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusInfo = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            _statusInfo.Bind(TextBlock.TextProperty, new Binding("StatusBarInfo"));
            grid.Children.Add(_statusInfo);
            Grid.SetColumn(_statusInfo, 0);

            _syncMessage = new TextBlock { FontSize = 12, Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            _syncMessage.Bind(TextBlock.TextProperty, new Binding("SyncMessage"));
            grid.Children.Add(_syncMessage);
            Grid.SetColumn(_syncMessage, 1);

            var toggleBtn = new Button { Content = "◧", ToolTip = new ToolTip { Content = "Правая панель" }, Margin = new Thickness(4, 0, 0, 0) };
            toggleBtn.Bind(Button.CommandProperty, new Binding("ToggleRightPanelDetailsCommand"));
            grid.Children.Add(toggleBtn);
            Grid.SetColumn(toggleBtn, 2);

            return new Border { Child = grid };
        }

        // ======================= Обработчики =======================

        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            _vm?.Initialize();
            SetupTray();
        }

        private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_vm is null)
                return;
            var selected = _tree.SelectedItem;
            switch (selected)
            {
                case Infobase ib:
                    _vm.SelectedInfobase = ib;
                    _vm.SelectedGroupNode = null;
                    break;
                case GroupNodeViewModel g:
                    _vm.SelectedGroupNode = g;
                    _vm.SelectedInfobase = null;
                    break;
                default:
                    break;
            }
        }

        // ======================= Трей =======================

        private void SetupTray()
        {
            try
            {
                var menu = new NativeMenu();

                var showItem = new NativeMenuItem("Показать окно");
                showItem.Click += (_, _) => ShowAndActivate();
                menu.Add(showItem);
                menu.Add(new NativeMenuItemSeparator());

                // Недавние базы (быстрый запуск прямо из трея).
                var recent = _vm?.RecentInfobases;
                if (recent is { Count: > 0 })
                {
                    var recentMenu = new NativeMenu();
                    foreach (var ib in recent)
                    {
                        var item = new NativeMenuItem($"{ib.Name}  ({ib.ServerDatabaseDisplay})");
                        var baseRef = ib;
                        item.Click += (_, _) => LaunchInfobase(baseRef);
                        recentMenu.Add(item);
                    }
                    menu.Add(new NativeMenuItem("Недавние базы") { Menu = recentMenu });
                    menu.Add(new NativeMenuItemSeparator());
                }

                // Запуск выбранной базы: Предприятие / Конфигуратор.
                if (_vm?.SelectedInfobase is { } sel)
                {
                    var ent = new NativeMenuItem($"▶ Предприятие: {sel.Name}");
                    ent.Click += (_, _) => _vm.LaunchEnterpriseCommand.Execute(null);
                    menu.Add(ent);

                    var cfg = new NativeMenuItem($"🔧 Конфигуратор: {sel.Name}");
                    cfg.Click += (_, _) => _vm.LaunchConfiguratorCommand.Execute(null);
                    menu.Add(cfg);
                    menu.Add(new NativeMenuItemSeparator());
                }

                // Синхронизация и настройки.
                var sync = new NativeMenuItem("⟳ Синхронизация с ibases.v8i");
                sync.Click += (_, _) => _vm?.SynchronizeWithIbasesCommand.Execute(null);
                menu.Add(sync);

                var settings = new NativeMenuItem("⚙ Настройки");
                settings.Click += (_, _) => _vm?.OpenSettingsCommand.Execute(null);
                menu.Add(settings);
                menu.Add(new NativeMenuItemSeparator());

                // Выход: разрешаем реальное закрытие и завершаем приложение.
                var exitItem = new NativeMenuItem("Выход");
                exitItem.Click += (_, _) =>
                {
                    _allowCloseToTray = false;
                    _vm?.ExitCommand.Execute(null);
                };
                menu.Add(exitItem);

                var tray = new TrayIcon
                {
                    Icon = LoadTrayIcon(),
                    ToolTipText = "Управление конфигурациями 1С",
                    Menu = menu
                };
                TrayIcon.SetIcons(this, new TrayIcons { tray });
            }
            catch
            {
                // Трей не обязателен для работы окна; игнорируем ошибки инициализации.
                // Примечание: на GNOME Shell без AppIndicator трей Avalonia может не отображаться —
                // это ограничение DE, окно продолжает работать обычным образом.
            }
        }

        /// <summary>Запускает базу из меню трея (Предприятие).</summary>
        private void LaunchInfobase(Infobase ib)
        {
            if (_vm is null)
                return;
            _vm.SelectedInfobase = ib;
            _vm.LaunchEnterpriseCommand.Execute(null);
        }

        /// <summary>
        /// Загружает иконку трея без System.Drawing — из PNG/ICO на диске либо из
        /// встроенного ресурса (tray_icon_preview.png), через Avalonia WindowIcon.
        /// </summary>
        private static WindowIcon? LoadTrayIcon()
        {
            try
            {
                foreach (var name in new[] { "tray_icon_preview.png", "app_icon_preview.png", "app.ico", "tray.ico" })
                {
                    foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                    {
                        var path = Path.Combine(dir, name);
                        if (File.Exists(path))
                            return new WindowIcon(new Bitmap(path));
                    }

                    // Встроенный ресурс (добавлен как EmbeddedResource в Linux-конфигурацию).
                    if (name == "tray_icon_preview.png")
                    {
                        var asm = Assembly.GetExecutingAssembly();
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream is not null)
                            return new WindowIcon(new Bitmap(stream));
                    }
                }
            }
            catch
            {
                // иконка не обязательна — трей будет без иконки/с иконкой по умолчанию
            }
            return null;
        }

        /// <summary>
        /// Закрытие окна уводит приложение в трей, а не завершает его
        /// (свойство «закрытие в трей»). Реальный выход — команда «Выход».
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (_allowCloseToTray && _vm is not null)
            {
                e.Cancel = true;
                Hide();
            }
        }

        /// <summary>Позволяет повторно показать окно из трея/активации.</summary>
        public void ShowAndActivate()
        {
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
    }
}
#endif