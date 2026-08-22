#if LINUX
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Themes;
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

        // Поля empty-state (заглушка пустого списка / «ничего не найдено»).
        private Border _emptyState = null!;
        private Avalonia.Controls.Shapes.Path _emptyIcon = null!;
        private TextBlock _emptyTitle = null!;
        private TextBlock _emptyHint = null!;

        /// <summary>
        /// Если true — закрытие окна уводит приложение в трей (а не завершает).
        /// Сбрасывается командой «Выход» из трея перед Shutdown.
        /// </summary>
        private bool _allowCloseToTray = true;

        public MainWindow(MainViewModel viewModel)
        {
            _vm = viewModel;

            Title = LocalizationManager.T("App.Title");
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

            // Фон рабочей области окна следует теме (перекрашивается при смене схемы).
            ThemeBrushes.Bind(grid, Panel.BackgroundProperty, "ContentBackgroundColorBrush");
            return grid;
        }

        private Control BuildTopBar()
        {
            var grid = new Grid { Margin = new Thickness(UiMetrics.TopBarH, UiMetrics.TopBarV) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Слева: сегментные переключатели групп и тегов (с иконками и состояниями).
            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Spacing = 2
            };

            var groupByToggle = MakeSegmentToggle("IconGroups", LocalizationManager.T("Main.ToggleGroups"));
            groupByToggle.IsChecked = _vm?.GroupByGroup ?? true;
            groupByToggle.Click += (_, _) => { if (_vm is not null) _vm.GroupByGroup = groupByToggle.IsChecked == true; };
            left.Children.Add(groupByToggle);

            var tagsToggle = MakeSegmentToggle("IconTag", LocalizationManager.T("Main.ToggleTags"));
            tagsToggle.IsChecked = _vm?.ShowTagFilterPanel ?? true;
            tagsToggle.Click += (_, _) => { if (_vm is not null) _vm.ShowTagFilterPanel = tagsToggle.IsChecked == true; };
            left.Children.Add(tagsToggle);

            grid.Children.Add(left);
            Grid.SetColumn(left, 0);

            // Поиск: скруглённое поле с иконкой слева, кнопкой очистки справа и hover-подсветкой.
            var search = BuildSearchBox();
            grid.Children.Add(search);
            Grid.SetColumn(search, 1);

            // Сегментированный контроль «Все / Избранное / Недавние» в общем контейнере.
            var tabs = BuildListModeSegments();
            grid.Children.Add(tabs);
            Grid.SetColumn(tabs, 2);

            // Справа: добавить базу, синхронизация, тема, настройки — иконки + подписи, состояния.
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6
            };

            var addBtn = TopBarPrimaryButton("IconAdd", LocalizationManager.T("Main.Add"), LocalizationManager.T("Main.AddTooltip"));
            addBtn.Bind(Button.CommandProperty, new Binding("AddInfobaseCommand"));
            actions.Children.Add(addBtn);

            var syncBtn = TopBarSecondaryButton("IconSync", LocalizationManager.T("Main.Sync"), LocalizationManager.T("Main.SyncWithIbases"));
            syncBtn.Bind(Button.CommandProperty, new Binding("SynchronizeWithIbasesCommand"));
            actions.Children.Add(syncBtn);

            var themeBtn = TopBarIconButton("IconTheme", LocalizationManager.T("Main.Theme"));
            themeBtn.Bind(Button.CommandProperty, new Binding("ToggleThemeCommand"));
            actions.Children.Add(themeBtn);

            var settingsBtn = TopBarSecondaryButton("IconSettings", LocalizationManager.T("Main.Settings"), LocalizationManager.T("Main.SettingsTooltip"));
            settingsBtn.Bind(Button.CommandProperty, new Binding("OpenSettingsCommand"));
            actions.Children.Add(settingsBtn);

            grid.Children.Add(actions);
            Grid.SetColumn(actions, 3);

            var topBarBorder = new Border
            {
                Child = grid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(UiMetrics.TopBarH, UiMetrics.TopBarV)
            };
            // Нижняя граница TopBar из темы.
            ThemeBrushes.Bind(topBarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            return topBarBorder;
        }

        /// <summary>Сегментный переключатель (например «группы»/«теги») с иконкой и состояниями.</summary>
        private SegmentButton MakeSegmentToggle(string iconKey, string tooltip)
        {
            var segment = new SegmentButton(iconKey, string.Empty, "ItemHoverBrush", "ItemSelectedBrush", lockOn: false)
            {
                IsChecked = true
            };
            ToolTip.SetTip(segment, tooltip);
            return segment;
        }

        /// <summary>Сегментированный контроль фильтра списка: Все / Избранное / Недавние.</summary>
        private Control BuildListModeSegments()
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusLg),
                Padding = new Thickness(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            ThemeBrushes.Bind(container, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(container, Border.BorderBrushProperty, "BorderColorBrush");
            UiMetrics.AddBrushTransition(container);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

            var allSeg = new SegmentButton("IconList", LocalizationManager.T("Main.AllBases"), "ItemHoverBrush", "ItemSelectedBrush");
            allSeg.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeAll") { Mode = BindingMode.TwoWay });
            panel.Children.Add(allSeg);

            var favSeg = new SegmentButton("IconFavorite", LocalizationManager.T("Main.Favorites"), "ItemHoverBrush", "ItemSelectedBrush");
            favSeg.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeFavorites") { Mode = BindingMode.TwoWay });
            panel.Children.Add(favSeg);

            var recSeg = new SegmentButton("IconRecent", LocalizationManager.T("Main.Recent"), "ItemHoverBrush", "ItemSelectedBrush");
            recSeg.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeRecent") { Mode = BindingMode.TwoWay });
            panel.Children.Add(recSeg);

            container.Child = panel;
            return container;
        }

        /// <summary>Поле поиска: скруглённая рамка, иконка слева, кнопка очистки справа, hover-подсветка.</summary>
        private Border BuildSearchBox()
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchIcon = IconHelper.MakeIcon("IconSearch", 16, "TextSecondaryBrush");
            searchIcon.Margin = new Thickness(2, 0, 6, 0);
            grid.Children.Add(searchIcon);
            Grid.SetColumn(searchIcon, 0);

            _searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Watermark = LocalizationManager.T("Main.SearchPlaceholder")
            };
            _searchBox.Bind(TextBox.TextProperty, new Binding("SearchText") { Mode = BindingMode.TwoWay });
            grid.Children.Add(_searchBox);
            Grid.SetColumn(_searchBox, 1);

            var clearBtn = new Button
            {
                Content = IconHelper.MakeIcon("IconClose", 14, "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(clearBtn, LocalizationManager.T("Main.ClearSearch"));
            clearBtn.Bind(Button.CommandProperty, new Binding("ClearSearchCommand"));
            grid.Children.Add(clearBtn);
            Grid.SetColumn(clearBtn, 2);

            border.Child = grid;

            // Hover-состояние: фон и граница подсвечиваются из ресурсов темы (без жёстких цветов).
            IBrush baseBg = Brushes.Transparent;
            IBrush hoverBg = Brushes.Transparent;
            IBrush baseBorder = Brushes.Transparent;
            IBrush hoverBorder = Brushes.Transparent;
            IBrush accentBorder = Brushes.Transparent;
            var hovered = false;
            var focused = false;

            void Refresh()
            {
                border.Background = (hovered || focused) ? hoverBg : baseBg;
                border.BorderBrush = focused ? accentBorder : (hovered ? hoverBorder : baseBorder);
                border.BorderThickness = focused ? new Thickness(2) : new Thickness(1);
            }

            if (Application.Current is { } app)
            {
                app.GetResourceObservable("CardBackgroundColorBrush").Subscribe(new BrushObserver(b => baseBg = b, Refresh));
                app.GetResourceObservable("ItemHoverBrush").Subscribe(new BrushObserver(b => hoverBg = b, Refresh));
                app.GetResourceObservable("BorderColorBrush").Subscribe(new BrushObserver(b => baseBorder = b, Refresh));
                app.GetResourceObservable("AccentBrush").Subscribe(new BrushObserver(b => { hoverBorder = b; accentBorder = b; }, Refresh));
            }

            border.PointerEntered += (_, _) => { hovered = true; Refresh(); };
            border.PointerExited += (_, _) => { hovered = false; Refresh(); };
            // Фокус-ринг поля поиска (клавиатурная навигация) акцентным цветом темы.
            _searchBox.GetObservable(TextBox.IsKeyboardFocusWithinProperty)
                .Subscribe(new BoolObserver(v => { focused = v; Refresh(); }));
            UiMetrics.AddBrushTransition(border);
            return border;
        }

        /// <summary>Primary-кнопка топ-бара: акцентный фон, иконка + подпись цветом «на акценте».</summary>
        private static PanelButton TopBarPrimaryButton(string iconKey, string text, string tooltip)
        {
            var button = new PanelButton("AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush")
            {
                Content = ThemedIconAndText(iconKey, text, "TextOnAccentBrush", UiMetrics.Scaled(15), centered: false),
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(button, tooltip);
            return button;
        }

        /// <summary>Secondary-кнопка топ-бара: приглушённый фон, иконка + подпись, hover/pressed.</summary>
        private static PanelButton TopBarSecondaryButton(string iconKey, string text, string tooltip)
        {
            var button = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush")
            {
                Content = ThemedIconAndText(iconKey, text, "ButtonTextBrush", UiMetrics.Scaled(15), centered: false),
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(button, tooltip);
            return button;
        }

        /// <summary>Компактная иконко-кнопка топ-бара (например тема) с состояниями из темы.</summary>
        private static PanelButton TopBarIconButton(string iconKey, string tooltip)
        {
            var button = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush")
            {
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(16), "ButtonTextBrush"),
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(button, tooltip);
            return button;
        }

        private Control BuildMainArea()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tree = new LeveledTreeView
            {
                BorderThickness = new Thickness(0)
            };
            // Фон списка баз — фон рабочей области из темы.
            ThemeBrushes.Bind(_tree, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");
            _tree.Bind(TreeView.ItemsSourceProperty, new Binding("GroupNodes"));
            _tree.SelectionMode = SelectionMode.Single;

            // Убираем стандартную подсветку контейнера TreeViewItem: карточка строки
            // сама рисует hover и выделение из ресурсов темы. Селектор сопоставляется
            // по ключу стиля, а он переопределён на TreeViewItem, иначе стиль
            // не нашёл бы контейнеры.
            var tviStyle = new Style(x => x.OfType<TreeViewItem>());
            tviStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
            _tree.Styles.Add(tviStyle);

            // Фон покоя этим снят, но состояния выделения и наведения Fluent задаёт
            // не свойством контейнера, а вложенным стилем на части шаблона, поэтому
            // синяя полоса рисовалась бы за карточкой. Гасим её адресно.
            foreach (var state in new[] { ":selected", ":pointerover" })
            {
                var stateStyle = new Style(x => x.OfType<TreeViewItem>().Class(state)
                    .Template().OfType<Border>().Name("PART_LayoutRoot"));
                stateStyle.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
                _tree.Styles.Add(stateStyle);
            }

            // Раскрытие контейнера связывается с моделью узла адресно, при подготовке
            // контейнера, а не стилем: стиль повесил бы привязку и на строки баз,
            // у которых свойства IsExpanded нет, и журнал заполнялся бы
            // предупреждениями привязки на каждое перестроение дерева.
            _tree.ContainerPrepared += (_, e) =>
            {
                if (e.Container is TreeViewItem container && container.DataContext is GroupNodeViewModel)
                    container.Bind(TreeViewItem.IsExpandedProperty,
                        new Binding("IsExpanded") { Mode = BindingMode.TwoWay });
            };

            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(object),
                (item, _) => BuildTreeRow(item),
                item => item is GroupNodeViewModel g ? g.Items : null);
            _tree.SelectionChanged += OnTreeSelectionChanged;

            // Дерево само предоставляет ScrollViewer и штатную виртуализацию
            // (VirtualizingStackPanel). Внешний ScrollViewer здесь не нужен — он давал бы дереву
            // бесконечную высоту и отключал бы виртуализацию, поэтому убран.
            _emptyState = BuildEmptyState();
            var leftInner = new Grid();
            leftInner.Children.Add(_tree);
            leftInner.Children.Add(_emptyState);

            var leftPanel = new Border
            {
                Child = leftInner,
                Margin = new Thickness(UiMetrics.TopBarH, UiMetrics.TopBarV, 8, UiMetrics.TopBarV),
                Padding = new Thickness(UiMetrics.Scaled(8), UiMetrics.Scaled(8))
            };

            grid.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);

            // Показываем/скрываем заглушку при любых изменениях списка и поиска.
            if (_vm is not null)
            {
                _vm.GroupNodes.CollectionChanged += (_, _) => UpdateEmptyState();
                _vm.FlatItems.CollectionChanged += (_, _) => UpdateEmptyState();
                _vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SearchText))
                        UpdateEmptyState();
                };
            }
            UpdateEmptyState();

            var rightPanel = new ScrollViewer
            {
                Name = "RightPanelBorder",
                Content = BuildRightPanel(),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(UiMetrics.Scaled(16), UiMetrics.Scaled(14)),
                MinWidth = UiMetrics.RightPanelMin,
                MaxWidth = UiMetrics.RightPanelMax
            };

            grid.Children.Add(rightPanel);
            Grid.SetColumn(rightPanel, 1);

            return grid;
        }

        /// <summary>
        /// Строит карточку-заглушку пустого списка: иконка, заголовок, подсказка и кнопка
        /// «Добавить базу». Иконка/тексты меняются в <see cref="UpdateEmptyState"/> в зависимости
        /// от того, пуст ли список баз вообще или фильтр ничего не нашёл.
        /// </summary>
        private Border BuildEmptyState()
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusXl),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(30, 34),
                MaxWidth = 380,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");
            UiMetrics.AddSoftShadow(card);
            UiMetrics.AddBrushTransition(card);
            UiMetrics.AddOpacityTransition(card);

            var stack = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _emptyIcon = IconHelper.MakeIcon("IconDatabase", 44, "TextSecondaryBrush");
            _emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyIcon.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(_emptyIcon);

            _emptyTitle = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ThemeBrushes.Bind(_emptyTitle, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            stack.Children.Add(_emptyTitle);

            _emptyHint = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ThemeBrushes.Bind(_emptyHint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            stack.Children.Add(_emptyHint);

            var addBtn = TopBarPrimaryButton("IconAdd", LocalizationManager.T("Main.AddBase"), LocalizationManager.T("Main.AddTooltip"));
            addBtn.Bind(Button.CommandProperty, new Binding("AddInfobaseCommand"));
            addBtn.HorizontalAlignment = HorizontalAlignment.Center;
            addBtn.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(addBtn);

            card.Child = stack;
            return card;
        }

        /// <summary>
        /// Обновляет заглушку пустого списка: показывает её, когда нет ни одного элемента
        /// (GroupNodes и FlatItems пусты), и подбирает иконку/текст под контекст (нет баз вообще
        /// либо фильтр/поиск не дал результатов).
        /// </summary>
        private void UpdateEmptyState()
        {
            if (_vm is null)
                return;

            var hasItems = _vm.GroupNodes.Count > 0 || _vm.FlatItems.Count > 0;
            if (hasItems)
            {
                _emptyState.IsVisible = false;
                return;
            }

            var searching = !string.IsNullOrWhiteSpace(_vm.SearchText)
                            || _vm.HasActiveTagFilter
                            || !_vm.IsListModeAll;

            if (searching)
            {
                _emptyIcon.Data = IconHelper.Geometry("IconSearch");
                _emptyTitle.Text = LocalizationManager.T("Main.EmptyNoResults");
                _emptyHint.Text = LocalizationManager.T("Main.EmptyNoResultsHint");
            }
            else
            {
                _emptyIcon.Data = IconHelper.Geometry("IconDatabase");
                _emptyTitle.Text = LocalizationManager.T("Main.EmptyNoBases");
                _emptyHint.Text = LocalizationManager.T("Main.EmptyNoBasesHint");
            }

            // Плавное появление заглушки.
            _emptyState.Opacity = 0;
            _emptyState.IsVisible = true;
            _emptyState.Opacity = 1;
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
                CornerRadius = new CornerRadius(UiMetrics.RadiusSm),
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
            // Карточка с фоном/границей из темы; hover и выделение отслеживает сама
            // (см. InfobaseRowCard): обычное → CardBackgroundBrush, hover → ItemHoverBrush,
            // выделено → ItemSelectedBrush + AccentBrush-граница.
            var card = new InfobaseRowCard();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Иконка статуса базы слева: тип подключения (папка / глобус / сеть)
            // или «недоступна». Цвет зависит от статуса: янтарный — файловая,
            // синий — веб, фиолетовый — клиент-сервер, красный — недоступна.
            var connectionIconKey = ib.StatusIconKey;

            var iconBox = new Border
            {
                Width = UiMetrics.RowIconBox,
                Height = UiMetrics.RowIconBox,
                CornerRadius = new CornerRadius(UiMetrics.RadiusMd),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0)
            };
            ToolTip.SetTip(iconBox, ib.StatusDisplay);
            ThemeBrushes.Bind(iconBox, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(iconBox, Border.BorderBrushProperty, "BorderColorBrush");
            iconBox.Child = new Avalonia.Controls.Shapes.Path
            {
                Width = UiMetrics.RowIcon,
                Height = UiMetrics.RowIcon,
                Data = IconHelper.Geometry(connectionIconKey),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Fill = new SolidColorBrush(Color.Parse(ib.StatusColorHex))
            };

            grid.Children.Add(iconBox);
            Grid.SetColumn(iconBox, 0);

            // Правая колонка: имя (крупно) + строки вторичной информации.
            // В компактном режиме уменьшаем и межстрочный промежуток, чтобы строки с
            // полным набором метаданных тоже «сжимались», а не оставались прежней высоты.
            var content = new StackPanel { Spacing = UiMetrics.Scaled(2), VerticalAlignment = VerticalAlignment.Center };

            // Строка имени с маркерами избранного/закрепления.
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var name = new TextBlock
            {
                Text = ib.Name,
                FontSize = UiMetrics.RowNameFont,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            nameRow.Children.Add(name);
            if (ib.IsFavorite)
                nameRow.Children.Add(IconHelper.MakeIcon("IconFavorite", 13, "FavoriteBrush"));
            if (ib.IsPinned)
                nameRow.Children.Add(IconHelper.MakeIcon("IconPin", 13, "TextSecondaryBrush"));
            content.Children.Add(nameRow);

            // Версия платформы + конфигурация (название (версия)).
            var versionLine = JoinSegments(ib.PlatformVersion, ib.ConfigurationDisplay);
            if (!string.IsNullOrWhiteSpace(versionLine))
                content.Children.Add(SecondaryText(versionLine));

            // Тип подключения + сервер/путь (для веб — URL публикации).
            var location = ib.Connection.Type switch
            {
                ConnectionType.WebServer => ib.Connection.WebUrl,
                _ => ib.ServerDatabaseDisplay
            };
            var serverLine = JoinSegments(ib.ConnectionTypeDisplay, location);
            if (!string.IsNullOrWhiteSpace(serverLine))
                content.Children.Add(SecondaryText(serverLine));

            // Режим запуска (тонкий/толстый/веб-клиент) + последний запуск.
            var launchLine = JoinSegments(ib.ParsedLaunchMode, ib.LastLaunchDisplay);
            if (!string.IsNullOrWhiteSpace(launchLine))
                content.Children.Add(SecondaryText(launchLine));

            grid.Children.Add(content);
            Grid.SetColumn(content, 1);

            card.Child = grid;
            return card;
        }

        /// <summary>Объединяет непустые фрагменты в одну строку с разделителем «•».</summary>
        private static string JoinSegments(params string?[] parts)
        {
            var nonEmpty = parts
                .Select(p => (p ?? string.Empty).Trim())
                .Where(p => p.Length > 0 && p != "—")
                .ToList();
            return nonEmpty.Count == 0 ? string.Empty : string.Join("  •  ", nonEmpty);
        }

        /// <summary>Строка вторичной информации: приглушённый текст из темы с подсказкой по полному значению.</summary>
        private static TextBlock SecondaryText(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.RowSecondaryFont,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(block, text);
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return block;
        }

        private Control BuildRightPanel()
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

            // Заголовок базы
            var nameBlock = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
            nameBlock.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.Name"));

            var groupBlock = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
            groupBlock.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.GroupDisplay"));

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Spacing = 10 };
            header.Children.Add(IconHelper.MakeIcon("IconDatabase", 28));
            var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerText.Children.Add(nameBlock);
            headerText.Children.Add(groupBlock);
            header.Children.Add(headerText);
            panel.Children.Add(header);

            // Основное действие (primary) — крупная акцентная кнопка вверху.
            panel.Children.Add(PrimaryActionButton("IconPlay", LocalizationManager.T("Main.LaunchEnterprise"), "LaunchEnterpriseCommand"));

            // Секции secondary-действий, сгруппированные по смыслу.
            panel.Children.Add(SectionCard(LocalizationManager.T("Main.SectionConfigurator"), "IconConfiguration",
                SecondaryActionButton("IconWrench", LocalizationManager.T("Main.LaunchConfiguratorSection"), "LaunchConfiguratorCommand")));

            panel.Children.Add(SectionCard(LocalizationManager.T("Main.SectionMaintenance"), "IconWrench",
                BuildClearCacheSplitButton(),
                SecondaryActionButton("IconEdit", LocalizationManager.T("Main.EditSettings"), "EditInfobaseCommand"),
                SecondaryActionButton("IconOpen", LocalizationManager.T("Main.OpenFolder"), "OpenInfobaseFolderCommand"),
                SecondaryActionButton("IconKeyboard", LocalizationManager.T("Main.RunStarter"), "OpenNativeStarterCommand")));

            panel.Children.Add(SectionCard(LocalizationManager.T("Main.SectionBaseList"), "IconList",
                SecondaryActionButton("IconAdd", LocalizationManager.T("Main.AddBaseOrGroup"), "AddInfobaseCommand"),
                SecondaryActionButton("IconShortcut", LocalizationManager.T("Main.DesktopShortcut"), "CreateDesktopShortcutCommand"),
                SecondaryActionButton("IconDelete", LocalizationManager.T("Main.Delete"), "DeleteInfobaseCommand")));

            panel.Children.Add(SectionCard(LocalizationManager.T("Main.SectionMarks"), "IconStar",
                SecondaryActionButton("IconFavorite", LocalizationManager.T("Main.ToFavorites"), "ToggleFavoriteCommand"),
                SecondaryActionButton("IconPin", LocalizationManager.T("Main.Pin"), "TogglePinCommand")));

            // Информация о подключении.
            panel.Children.Add(SectionCard(LocalizationManager.T("Main.SectionConnInfo"), "IconInfo",
                DetailRow(LocalizationManager.T("Main.Type"), new Binding("SelectedInfobase.ConnectionTypeDisplay")),
                DetailRow(LocalizationManager.T("Main.ServerPath"), new Binding("SelectedInfobase.ConnectionPathDisplay")),
                DetailRow(LocalizationManager.T("Main.ConnectionString"), new Binding("SelectedInfobase.ConnectionStringDisplay")),
                DetailRow(LocalizationManager.T("Main.Platform"), new Binding("SelectedInfobase.PlatformVersion")),
                DetailRow(LocalizationManager.T("Main.LaunchMode"), new Binding("SelectedInfobase.ParsedLaunchMode")),
                DetailRow(LocalizationManager.T("Main.Bitness"), new Binding("SelectedInfobase.ArchitectureDisplay")),
                DetailRow(LocalizationManager.T("Main.LastLaunch"), new Binding("SelectedInfobase.LastLaunchDisplay"))));

            // Описание.
            var desc = new TextBlock { TextWrapping = TextWrapping.Wrap };
            desc.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.Description"));
            panel.Children.Add(SectionCard(LocalizationManager.T("Main.Description"), "IconInfo", desc));

            panel.Children.Add(SecondaryActionButton("IconExit", LocalizationManager.T("Main.Exit"), "ExitCommand"));

            return panel;
        }

        /// <summary>Карточка-секция: скруглённый фон/граница из темы + заголовок с иконкой и вложенные элементы.</summary>
        private static Control SectionCard(string title, string iconKey, params Control[] children)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusXl),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(UiMetrics.SectionPad),
                Margin = new Thickness(0, 0, 0, UiMetrics.SectionMarginBottom),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");
            // Мягкая тень и плавные переходы цвета у секций-карточек.
            UiMetrics.AddSoftShadow(card);
            UiMetrics.AddBrushTransition(card);

            var content = new StackPanel { Spacing = UiMetrics.Gap };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 2) };
            header.Children.Add(IconHelper.MakeIcon(iconKey, 16, "TextSecondaryBrush"));
            var titleBlock = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            ThemeBrushes.Bind(titleBlock, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            header.Children.Add(titleBlock);
            content.Children.Add(header);

            foreach (var child in children)
                content.Children.Add(child);

            card.Child = content;
            return card;
        }

        /// <summary>Крупная primary-кнопка на акцентном фоне с контрастным текстом/иконкой.</summary>
        private static Control PrimaryActionButton(string iconKey, string text, string commandPath)
        {
            var btn = new PanelButton("AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush")
            {
                Content = ThemedIconAndText(iconKey, text, "TextOnAccentBrush", UiMetrics.Scaled(18), centered: true),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, UiMetrics.SectionMarginBottom),
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV)
            };
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        /// <summary>Secondary-кнопка с приглушённым фоном и hover/pressed из ресурсов темы.</summary>
        private static Control SecondaryActionButton(string iconKey, string text, string commandPath)
        {
            var btn = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush")
            {
                Content = ThemedIconAndText(iconKey, text, "ButtonTextBrush", 16, centered: false),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 2)
            };
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        /// <summary>
        /// Split-кнопка «Очистка кеша»: основная часть выполняет быструю очистку всего кеша
        /// (программного и пользовательского) выбранной базы с предупреждением, а правая
        /// стрелка «▾» открывает выпадающее меню с выбором типа кеша и полным окном очистки.
        /// </summary>
        private static Control BuildClearCacheSplitButton()
        {
            var radius = UiMetrics.RadiusLg;

            // Основная часть: быстрая очистка всего кеша с подтверждением.
            var main = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(radius, 0, 0, radius))
            {
                Content = ThemedIconAndText("IconDelete", LocalizationManager.T("Main.ClearCache"), "ButtonTextBrush", 16, centered: false),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 2)
            };
            main.Bind(Button.CommandProperty, new Binding("QuickClearCacheCommand"));

            // Выпадающее меню, привязанное к кнопке-стрелке.
            var menu = new ContextMenu();

            var openDialog = new MenuItem { Header = LocalizationManager.T("Main.CacheCleanOpenDialog") };
            openDialog.Bind(MenuItem.CommandProperty, new Binding("ClearCacheCommand"));
            menu.Items.Add(openDialog);

            var program = new MenuItem { Header = LocalizationManager.T("Main.ClearProgramCache") };
            program.Bind(MenuItem.CommandProperty, new Binding("ClearProgramCacheCommand"));
            menu.Items.Add(program);

            var user = new MenuItem { Header = LocalizationManager.T("Main.ClearUserCache") };
            user.Bind(MenuItem.CommandProperty, new Binding("ClearUserCacheCommand"));
            menu.Items.Add(user);

            var arrow = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(0, radius, radius, 0))
            {
                Width = 36,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 2)
            };
            var arrowGlyph = new TextBlock
            {
                Text = "▾",
                FontSize = UiMetrics.Scaled(14),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ThemeBrushes.Bind(arrowGlyph, TextBlock.ForegroundProperty, "ButtonTextBrush");
            arrow.Content = arrowGlyph;
            ToolTip.SetTip(arrow, LocalizationManager.T("Main.ClearCacheTooltip"));
            arrow.ContextMenu = menu;
            arrow.Click += (_, _) => menu.Open(arrow);

            // Объединяем обе части в один визуально цельный контрол.
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(main, 0);
            Grid.SetColumn(arrow, 1);
            grid.Children.Add(main);
            grid.Children.Add(arrow);
            return grid;
        }

        /// <summary>Содержимое кнопки: иконка + подпись, окрашенные кистью ресурса темы.</summary>
        private static Control ThemedIconAndText(string iconKey, string text, string brushKey, double iconSize, bool centered)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (centered)
                sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(IconHelper.MakeIcon(iconKey, iconSize, brushKey));
            var tb = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.Scaled(13),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
            sp.Children.Add(tb);
            return sp;
        }

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

        /// <summary>
        /// Кнопка-панель со скруглением и состояниями «обычное / hover / pressed»,
        /// кисти которых берутся из ресурсов темы (перекрашиваются при смене схемы).
        /// Используется для primary- и secondary-кнопок правой панели.
        /// </summary>
        private sealed class PanelButton : Button
        {
            private readonly List<IDisposable> _subs = new();
            private IBrush _baseBg = Brushes.Transparent;
            private IBrush _hoverBg = Brushes.Transparent;
            private IBrush _pressedBg = Brushes.Transparent;
            private IBrush _border = Brushes.Transparent;
            private IBrush _accent = Brushes.Transparent;
            private CornerRadius _radius;
            private bool _hovered;
            private bool _pressed;
            private bool _focused;

            public PanelButton(string baseBgKey, string hoverBgKey, string pressedBgKey, string borderKey, CornerRadius? cornerRadius = null)
            {
                _radius = cornerRadius ?? new CornerRadius(UiMetrics.RadiusLg);
                HorizontalContentAlignment = HorizontalAlignment.Center;
                VerticalContentAlignment = VerticalAlignment.Center;
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV);
                BorderThickness = new Thickness(1);
                Cursor = new Cursor(StandardCursorType.Hand);

                // Кастомный шаблон: скруглённый Border + ContentPresenter (без Fluent-хрома).
                Theme = new ControlTheme(typeof(Button))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<PanelButton>((_, _) =>
                    {
                        var border = new Border { CornerRadius = _radius, BorderThickness = new Thickness(1) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        border[!Border.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                        UiMetrics.AddBrushTransition(border);

                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                    }
                };

                Subscribe(baseBgKey, v => _baseBg = v);
                Subscribe(hoverBgKey, v => _hoverBg = v);
                Subscribe(pressedBgKey, v => _pressedBg = v);
                Subscribe(borderKey, v => _border = v);
                Subscribe("AccentBrush", v => _accent = v);

                PointerEntered += (_, _) => { _hovered = true; ApplyState(); };
                PointerExited += (_, _) => { _hovered = false; _pressed = false; ApplyState(); };
                PointerPressed += (_, _) => { _pressed = true; ApplyState(); };
                PointerReleased += (_, _) => { _pressed = false; ApplyState(); };
                PointerCaptureLost += (_, _) => { _pressed = false; ApplyState(); };

                this.GetObservable(IsEnabledProperty).Subscribe(new BoolObserver(_ => ApplyState()));
                this.GetObservable(IsKeyboardFocusWithinProperty).Subscribe(new BoolObserver(v => { _focused = v; ApplyState(); }));
                ApplyState();
            }

            private void Subscribe(string key, Action<IBrush> setter)
            {
                if (Application.Current is not { } app)
                    return;
                _subs.Add(app.GetResourceObservable(key).Subscribe(new BrushSlot(setter, ApplyState)));
            }

            /// <summary>Применяет состояние к фону/границе/прозрачности кнопки.</summary>
            private void ApplyState()
            {
                if (!IsEnabled)
                {
                    Opacity = 0.55;
                    Background = _baseBg;
                    BorderBrush = _border;
                    BorderThickness = new Thickness(1);
                    return;
                }

                Opacity = 1.0;
                Background = _pressed ? _pressedBg : (_hovered ? _hoverBg : _baseBg);
                if (_focused)
                {
                    // Видимый focus-ринг акцентным цветом темы для клавиатурной навигации.
                    BorderBrush = _accent;
                    BorderThickness = new Thickness(2);
                }
                else
                {
                    BorderBrush = _border;
                    BorderThickness = new Thickness(1);
                }
            }

            /// <summary>Передаёт текущее значение ресурса-кисти в слот и перерисовывает состояние.</summary>
            private sealed class BrushSlot : IObserver<object?>
            {
                private readonly Action<IBrush> _setter;
                private readonly Action _onChanged;

                public BrushSlot(Action<IBrush> setter, Action onChanged)
                {
                    _setter = setter;
                    _onChanged = onChanged;
                }

                public void OnCompleted() { }
                public void OnError(Exception error) { }
                public void OnNext(object? value)
                {
                    if (value is IBrush brush)
                        _setter(brush);
                    _onChanged();
                }
            }

        }

        /// <summary>
        /// Простой наблюдатель ресурса-кисти темы: передаёт текущее значение в setter и
        /// при изменении (в т.ч. при смене схемы) вызывает onChanged.
        /// </summary>
        private sealed class BrushObserver : IObserver<object?>
        {
            private readonly Action<IBrush> _setter;
            private readonly Action _onChanged;

            public BrushObserver(Action<IBrush> setter, Action onChanged)
            {
                _setter = setter;
                _onChanged = onChanged;
            }

            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(object? value)
            {
                if (value is IBrush brush)
                    _setter(brush);
                _onChanged();
            }
        }

        /// <summary>Простой наблюдатель bool (для IsEnabled / клавиатурного фокуса).</summary>
        private sealed class BoolObserver : IObserver<bool>
        {
            private readonly Action<bool> _onNext;
            public BoolObserver(Action<bool> onNext) => _onNext = onNext;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(bool value) => _onNext(value);
        }

        /// <summary>
        /// Сегментная кнопка переключателя (для сегментированного контроля): у выбранного
        /// сегмента акцентная заливка, а иконка/текст — цветом «на акценте»; у невыбранных —
        /// прозрачный фон с приглушённым текстом и hover/pressed-состояниями. Все кисти
        /// берутся из ресурсов темы (перекрашиваются при смене схемы). Если lockOn == true,
        /// активный сегмент нельзя «снять» кликом (поведение как у RadioButton).
        /// </summary>
        private sealed class SegmentButton : ToggleButton
        {
            private readonly List<IDisposable> _subs = new();
            private readonly string _iconKey;
            private readonly string _text;
            private readonly double _iconSize;
            private readonly bool _lockOn;

            private IBrush _hoverBg = Brushes.Transparent;
            private IBrush _pressedBg = Brushes.Transparent;
            private IBrush _accent = Brushes.Transparent;
            private IBrush _accentHover = Brushes.Transparent;
            private IBrush _accentPressed = Brushes.Transparent;

            private bool _hovered;
            private bool _pressed;
            private bool _focused;

            public SegmentButton(string iconKey, string text, string hoverBgKey, string pressedBgKey, bool lockOn = true)
            {
                _iconKey = iconKey;
                _text = text;
                _iconSize = 15;
                _lockOn = lockOn;

                HorizontalContentAlignment = HorizontalAlignment.Center;
                VerticalContentAlignment = VerticalAlignment.Center;
                Cursor = new Cursor(StandardCursorType.Hand);
                MinHeight = 30;
                Padding = new Thickness(12, 5);
                BorderThickness = new Thickness(0);

                // Кастомный шаблон: скруглённый Border + ContentPresenter (без Fluent-хрома).
                Theme = new ControlTheme(typeof(ToggleButton))
                {
                    Setters =
                    {
                        new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<SegmentButton>((_, _) =>
                    {
                        var border = new Border { CornerRadius = new CornerRadius(UiMetrics.RadiusSm), BorderThickness = new Thickness(0) };
                        border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                        border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                        border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                        UiMetrics.AddBrushTransition(border);
                        var presenter = new ContentPresenter();
                        presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                        presenter[!ContentPresenter.HorizontalContentAlignmentProperty] = new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty);
                        presenter[!ContentPresenter.VerticalContentAlignmentProperty] = new TemplateBinding(ContentControl.VerticalContentAlignmentProperty);
                        border.Child = presenter;
                        return border;
                    }))
                    }
                };

                Subscribe(hoverBgKey, v => _hoverBg = v);
                Subscribe(pressedBgKey, v => _pressedBg = v);
                Subscribe("AccentBrush", v => _accent = v);
                Subscribe("AccentHoverBrush", v => _accentHover = v);
                Subscribe("AccentPressedBrush", v => _accentPressed = v);

                PointerEntered += (_, _) => { _hovered = true; ApplyState(); };
                PointerExited += (_, _) => { _hovered = false; _pressed = false; ApplyState(); };
                PointerPressed += (_, _) => { _pressed = true; ApplyState(); };
                PointerReleased += (_, _) => { _pressed = false; ApplyState(); };
                PointerCaptureLost += (_, _) => { _pressed = false; ApplyState(); };

                this.GetObservable(IsCheckedProperty, v => v == true).Subscribe(new BoolObserver(_ => { UpdateContent(); ApplyState(); }));
                this.GetObservable(IsEnabledProperty).Subscribe(new BoolObserver(_ => ApplyState()));
                this.GetObservable(IsKeyboardFocusWithinProperty).Subscribe(new BoolObserver(v => { _focused = v; ApplyState(); }));

                UpdateContent();
                ApplyState();
            }

            /// <summary>Не даём снимать уже активный сегмент (как RadioButton), когда это требуется.</summary>
            protected override void Toggle()
            {
                if (_lockOn && IsChecked == true)
                    return;
                base.Toggle();
            }

            private void Subscribe(string key, Action<IBrush> setter)
            {
                if (Application.Current is not { } app)
                    return;
                _subs.Add(app.GetResourceObservable(key).Subscribe(new BrushObserver(setter, ApplyState)));
            }

            /// <summary>Собирает содержимое «иконка + текст» с цветом по состоянию выбора.</summary>
            private void UpdateContent()
            {
                var brushKey = IsChecked == true ? "TextOnAccentBrush" : "TextPrimaryBrush";
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(IconHelper.MakeIcon(_iconKey, _iconSize, brushKey));
                if (!string.IsNullOrEmpty(_text))
                {
                    var tb = new TextBlock
                    {
                        Text = _text,
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
                    sp.Children.Add(tb);
                }
                Content = sp;
            }

            private void ApplyState()
            {
                if (!IsEnabled)
                {
                    Opacity = 0.55;
                    Background = Brushes.Transparent;
                    BorderBrush = Brushes.Transparent;
                    BorderThickness = new Thickness(0);
                    return;
                }

                Opacity = 1.0;
                if (IsChecked == true)
                    Background = _pressed ? _accentPressed : (_hovered ? _accentHover : _accent);
                else
                    Background = _pressed ? _pressedBg : (_hovered ? _hoverBg : Brushes.Transparent);

                if (_focused)
                {
                    BorderBrush = _accent;
                    BorderThickness = new Thickness(2);
                }
                else
                {
                    BorderBrush = Brushes.Transparent;
                    BorderThickness = new Thickness(0);
                }
            }
        }

        private Control BuildStatusBar()
        {
            var grid = new Grid();
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

            var toggleBtn = new Button { Content = IconHelper.MakeIcon("IconPanel", 16), Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(toggleBtn, LocalizationManager.T("Main.RightPanel"));
            toggleBtn.Bind(Button.CommandProperty, new Binding("ToggleRightPanelDetailsCommand"));
            grid.Children.Add(toggleBtn);
            Grid.SetColumn(toggleBtn, 2);

            return new Border { Child = grid, Name = "StatusBarBorder", Padding = new Thickness(UiMetrics.TopBarH, 6) };
        }

        // ======================= Обработчики =======================

        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            _vm?.Initialize();
            SetupTray();
        }

        /// <summary>
        /// Применяет компактный режим интерфейса: пересобирает главное окно с уменьшенными
        /// отступами, иконками и расстояниями. Вызывается из окна настроек при переключении.
        /// </summary>
        public void ApplyCompactMode(bool compact)
        {
            UiMetrics.Compact = compact;
            Content = BuildRoot();
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

                var showItem = new NativeMenuItem(LocalizationManager.T("Main.ShowWindow"));
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
                    menu.Add(new NativeMenuItem(LocalizationManager.T("Main.RecentBases")) { Menu = recentMenu });
                    menu.Add(new NativeMenuItemSeparator());
                }

                // Запуск выбранной базы: Предприятие / Конфигуратор.
                if (_vm?.SelectedInfobase is { } sel)
                {
                    var ent = new NativeMenuItem($"{LocalizationManager.T("Main.LaunchEnterprise")}: {sel.Name}");
                    ent.Click += (_, _) => _vm.LaunchEnterpriseCommand.Execute(null);
                    menu.Add(ent);

                    var cfg = new NativeMenuItem($"{LocalizationManager.T("Main.LaunchConfigurator")}: {sel.Name}");
                    cfg.Click += (_, _) => _vm.LaunchConfiguratorCommand.Execute(null);
                    menu.Add(cfg);
                    menu.Add(new NativeMenuItemSeparator());
                }

                // Синхронизация и настройки.
                var sync = new NativeMenuItem(LocalizationManager.T("Main.SyncWithIbases"));
                sync.Click += (_, _) => _vm?.SynchronizeWithIbasesCommand.Execute(null);
                menu.Add(sync);

                var settings = new NativeMenuItem(LocalizationManager.T("Main.Settings"));
                settings.Click += (_, _) => _vm?.OpenSettingsCommand.Execute(null);
                menu.Add(settings);
                menu.Add(new NativeMenuItemSeparator());

                // Выход: разрешаем реальное закрытие и завершаем приложение.
                var exitItem = new NativeMenuItem(LocalizationManager.T("Main.Exit"));
                exitItem.Click += (_, _) =>
                {
                    _allowCloseToTray = false;
                    _vm?.ExitCommand.Execute(null);
                };
                menu.Add(exitItem);

                var tray = new TrayIcon
                {
                    Icon = LoadTrayIcon(),
                    ToolTipText = LocalizationManager.T("App.Title"),
                    Menu = menu
                };
                if (Application.Current is { } app)
                    TrayIcon.SetIcons(app, new TrayIcons { tray });

                // На GNOME Shell без расширения AppIndicator иконка трея не появится,
                // и приложение об этом никак не узнает: ошибки не будет, значка просто
                // не будет. Пишем в журнал, чтобы это не выглядело поломкой приложения.
                if (Services.LinuxDesktopEnvironment.TrayMayBeUnavailable)
                {
                    AppServices.GetRequiredService<Services.IAppLogger>().Warn(
                        $"Окружение {Services.LinuxDesktopEnvironment.Describe()}: " +
                        "иконка в трее может не отображаться без расширения AppIndicator.");
                }
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
                        var path = System.IO.Path.Combine(dir, name);
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