#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using Avalonia.Utilities;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        private SegmentButton? _tagsToggle;
        private SegmentButton? _groupByToggle;
        private Border? _columnHeader;
        private Grid? _columnHeaderRow;
        private ColumnDefinition? _headerOffsetColumn;
        private Control? _headerPinMark;
        private double _headerToolbarWidth;
        private StackPanel? _groupToolbar;
        private Grid? _listContent;
        /// <summary>Шаг прокрутки колесом, как у штатного ScrollContentPresenter.</summary>
        private const double WheelScrollStep = 50;

        private ScrollBar? _listVerticalBar;
        private ScrollViewer? _listScroll;
        private ScrollViewer? _boundTreeScroll;
        private ScrollBar? _boundScrollBar;
        private readonly List<IDisposable> _scrollBarLinks = new();
        private bool _syncingScrollBar;
        private bool _columnHeaderRefreshQueued;
        private bool _headerAlignQueued;
        private readonly Dictionary<string, int> _headerColumnIndex = new(StringComparer.Ordinal);
        private object? _dragPayload;
        private Point _dragStartPoint;
        private bool _isDragging;
        private string? _resizeKey;
        private int _resizePointerId;
        private readonly List<Grid> _resizeRowGrids = new();
        private double _resizeStartWidth;
        private double _resizeStartX;
        private Border? _tagPanel;
        private WrapPanel? _tagPanelItems;
        private Button? _tagClearButton;
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
            // Значок в заголовке окна — тот же app.ico, что и у приложения и трея.
            Icon = Services.AppIconLoader.LoadAppIcon();
            Width = 1200;
            Height = 760;
            MinWidth = 900;
            MinHeight = 600;
            ApplySavedWindowLayout();

            DataContext = viewModel;

            Content = BuildRoot();
            Loaded += OnWindowLoaded;
            KeyDown += OnWindowKeyDown;

            // Геометрия обычного состояния запоминается на ходу: у Avalonia нет
            // аналога RestoreBounds, а развёрнутое окно надо сохранять размером,
            // к которому оно вернётся.
            PositionChanged += (_, _) => RememberNormalBounds();
            SizeChanged += (_, _) => RememberNormalBounds();

            // Действие после запуска базы или конфигуратора по глобальной настройке.
            _vm.AfterLaunchRequested += OnAfterLaunchRequested;

            // Подписка здесь, а не в построении содержимого: компактный режим
            // пересобирает содержимое, и обработчики копились бы на каждый показ.
            _vm.TraySettingsChanged += ApplyTrayVisibility;
            _vm.TreeRebuilding += RememberTreeScroll;
            _vm.TreeRebuilt += RestoreTreeSelection;

            // Смена языка интерфейса: названия колонок, кнопки правой панели и подсказки
            // создаются в коде через LocalizationManager.T(...), поэтому окно пересобирается,
            // чтобы переведённый текст появился сразу, а не после перезапуска.
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>Значок трея создан без ошибки: значение проверяется перед тем, как прятать окно.</summary>
        private bool _trayIconCreated;

        /// <summary>Ссылка на значок трея, чтобы обновлять меню при смене языка.</summary>
        private TrayIcon? _trayIcon;
        private NativeMenu? _trayMenu;
        private string? _traySignature;
        private bool _trayRefreshQueued;
        private IDisposable? _toolbarWidthLink;
        private NotifyCollectionChangedEventHandler? _groupNodesChanged;
        private NotifyCollectionChangedEventHandler? _flatItemsChanged;
        private EventHandler? _tagFiltersRebuilt;
        private PropertyChangedEventHandler? _vmPropertyChanged;

        /// <summary>
        /// Можно ли вернуть спрятанное окно. Значок трея это первый путь, но
        /// на GNOME Shell без AppIndicator он не появится, а ошибки при этом не
        /// будет. Второй путь, повторный запуск приложения через файл-сигнал,
        /// работает только пока включён режим единственного экземпляра.
        /// Берётся состояние текущего процесса, а не настройка: блокировка
        /// и слушатель сигнала заводятся один раз при старте, и снятый на ходу
        /// флажок «несколько экземпляров» пути возврата не создаёт.
        /// </summary>
        private bool CanRestoreHiddenWindow =>
            (_trayIconCreated && TrayIconWanted && !Services.LinuxDesktopEnvironment.TrayMayBeUnavailable)
            || App.SingleInstanceActive;

        /// <summary>Нужен ли значок по настройкам: сам значок либо закрытие в трей.</summary>
        private bool TrayIconWanted => _vm is null || _vm.ShowTrayIcon || _vm.CloseToTray;

        /// <summary>
        /// Уводит окно в трей после успешного запуска, как это делает WPF-версия
        /// для обоих значений настройки. Выполняется через диспетчер: запрос
        /// приходит из обработчика команды, а окно к этому моменту ещё показывает
        /// нажатую кнопку.
        /// </summary>
        private void OnAfterLaunchRequested(Models.AfterLaunchAction action)
        {
            if (action == Models.AfterLaunchAction.None)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Спрятанное окно живёт только в трее, поэтому без пути возврата
                // оно сворачивается: иначе пользователь остался бы с работающим
                // процессом, который нечем показать.
                if (!CanRestoreHiddenWindow)
                {
                    WindowState = WindowState.Minimized;
                    _vm?.LogWarning(LocalizationManager.T("Main.AfterLaunchTrayUnavailable"));
                    return;
                }

                SaveWindowLayout();
                Hide();
                if (action == Models.AfterLaunchAction.MinimizeToTray
                    && WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
            });
        }

        // ======================= Построение UI =======================

        private Control BuildRoot()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var topBar = BuildTopBar();
            var tagPanel = BuildTagFilterPanel();
            var mainArea = BuildMainArea();
            var statusBar = BuildStatusBar();

            Grid.SetRow(topBar, 0);
            Grid.SetRow(tagPanel, 1);
            Grid.SetRow(mainArea, 2);
            Grid.SetRow(statusBar, 3);

            grid.Children.Add(topBar);
            grid.Children.Add(tagPanel);
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

            _groupByToggle = MakeSegmentToggle("IconGroups", LocalizationManager.T("Main.ToggleGroups"));
            _groupByToggle.IsChecked = _vm?.GroupByGroup ?? true;
            _groupByToggle.Click += (_, _) => { if (_vm is not null) _vm.GroupByGroup = _groupByToggle.IsChecked == true; };
            left.Children.Add(_groupByToggle);

            // Подсказка подробная, как в разметке WPF (MainWindow.xaml:194): этот
            // переключатель управляет и панелью тегов сверху, и тегами в списке,
            // в отличие от переключателя в шапке списка.
            _tagsToggle = MakeSegmentToggle("IconTag", LocalizationManager.T("Main.ToggleTagsFull"));
            _tagsToggle.IsChecked = _vm?.ShowTagFilterPanel ?? true;
            _tagsToggle.Click += (_, _) => { if (_vm is not null) _vm.ShowTagFilterPanel = _tagsToggle.IsChecked == true; };
            left.Children.Add(_tagsToggle);

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

            // Все команды верхней панели значками без подписей, как в разметке WPF.
            var addBtn = TopBarIconButton("IconAdd", LocalizationManager.T("Main.AddTooltip"));
            addBtn.Bind(Button.CommandProperty, new Binding("AddInfobaseCommand"));
            actions.Children.Add(addBtn);

            // Очистить кеш выбранной базы: перенесено в верхнюю панель команд,
            // действует на SelectedInfobase (недоступна, если база не выбрана).
            var clearCacheBtn = TopBarIconButton("IconBroom", LocalizationManager.T("Main.ClearCacheTooltip"));
            clearCacheBtn.Bind(Button.CommandProperty, new Binding("ClearCacheCommand"));
            actions.Children.Add(clearCacheBtn);

            var syncBtn = TopBarIconButton("IconSync", LocalizationManager.T("Main.SyncDetailedTooltip"));
            syncBtn.Bind(Button.CommandProperty, new Binding("SynchronizeWithIbasesCommand"));
            actions.Children.Add(syncBtn);

            // Проверить доступность всех баз 1С: ручная команда вместо автопроверки при запуске.
            // Иконка — зелёный гидролокатор (сонар), как экран на подводных лодках.
            var checkAvailBtn = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush")
            {
                Content = new Avalonia.Controls.Shapes.Path
                {
                    Width = UiMetrics.Scaled(16),
                    Height = UiMetrics.Scaled(16),
                    Data = IconHelper.Geometry("IconSonar"),
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Fill = new SolidColorBrush(Color.Parse("#14B8A6"))
                },
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ToolTip.SetTip(checkAvailBtn, LocalizationManager.T("Main.CheckAvailabilityTooltip"));
            checkAvailBtn.Bind(Button.CommandProperty, new Binding("CheckAvailabilityCommand"));
            // Проверка доступности стоит между синхронизацией и темой, как у автора.
            actions.Children.Add(checkAvailBtn);

            var themeBtn = TopBarIconButton("IconTheme", LocalizationManager.T("Main.Theme"));
            themeBtn.Bind(Button.CommandProperty, new Binding("ToggleThemeCommand"));
            actions.Children.Add(themeBtn);

            // Быстрый переключатель плотности интерфейса, как в разметке WPF:
            // тот же режим уже есть в настройках, здесь он под рукой.
            var compactBtn = TopBarIconButton("IconCollapseAll", LocalizationManager.T("Main.CompactModeTooltip"));
            compactBtn.Click += (_, _) =>
            {
                if (_vm is null)
                    return;
                var next = !_vm.CompactMode;
                _vm.CompactMode = next;
                ApplyCompactMode(next);
            };
            actions.Children.Add(compactBtn);

            var settingsBtn = TopBarIconButton("IconSettings", LocalizationManager.T("Main.SettingsTooltip"));
            settingsBtn.Bind(Button.CommandProperty, new Binding("OpenSettingsCommand"));
            actions.Children.Add(settingsBtn);


            // Подсказка «?»: справа, после всех команд верхней панели.
            actions.Children.Add(new HelpLink
            {
                HelpText = LocalizationManager.T("Main.BaseListHelp"),
                Margin = new Thickness(4, 0, 0, 0)
            });

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
            ToolTip.SetTip(allSeg, LocalizationManager.T("Main.AllBasesTooltip"));
            allSeg.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeAll") { Mode = BindingMode.TwoWay });
            panel.Children.Add(allSeg);

            var favSeg = new SegmentButton("IconFavorite", LocalizationManager.T("Main.Favorites"), "ItemHoverBrush", "ItemSelectedBrush");
            ToolTip.SetTip(favSeg, LocalizationManager.T("Main.FavoritesTooltip"));
            favSeg.Bind(ToggleButton.IsCheckedProperty, new Binding("IsListModeFavorites") { Mode = BindingMode.TwoWay });
            panel.Children.Add(favSeg);

            var recSeg = new SegmentButton("IconRecent", LocalizationManager.T("Main.Recent"), "ItemHoverBrush", "ItemSelectedBrush");
            ToolTip.SetTip(recSeg, LocalizationManager.T("Main.RecentTooltip"));
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

            // Подписки привязаны к жизни рамки: содержимое окна пересобирается
            // при переключении компактного режима, и наблюдатель, живущий
            // у приложения, удерживал бы прежнее дерево целиком.
            ThemeBrushes.Observe(border, "CardBackgroundColorBrush", b => { baseBg = b; Refresh(); });
            ThemeBrushes.Observe(border, "ItemHoverBrush", b => { hoverBg = b; Refresh(); });
            ThemeBrushes.Observe(border, "BorderColorBrush", b => { baseBorder = b; Refresh(); });
            ThemeBrushes.Observe(border, "AccentBrush", b => { hoverBorder = b; accentBorder = b; Refresh(); });

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
                Content = ThemedIconAndText(iconKey, text, "TextOnAccentBrush", UiMetrics.ScaledFont(15), centered: false),
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
                Content = ThemedIconAndText(iconKey, text, "ButtonTextBrush", UiMetrics.ScaledFont(15), centered: false),
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
            // Горизонтальная прокрутка отключена: иначе строка растягивается
            // по сумме ширин колонок и уезжает за правый край, а заголовки,
            // живущие вне области прокрутки, перестают совпадать со значениями.
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            // Внутренняя прокрутка появляется только вместе с шаблоном, а он
            // применяется заново при каждой пересборке окна компактным режимом.
            _tree.TemplateApplied += (_, _) => AttachVerticalScrollBar();
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

            // Раскрытие узла связывает с моделью сам LeveledTreeView, при подготовке
            // контейнера на любом уровне вложенности. Здесь остаётся только
            // выравнивание заголовка: раскрытие группы добавляет строки, а с ними
            // может измениться и самый левый отступ, по которому выровнен заголовок.
            _tree.ContainerPrepared += (_, _) => QueueHeaderAlign();

            // Меню висит на дереве, как в WPF: над группой и над пустым местом
            // оно тоже открывается, а недоступные пункты гасит CanExecute.
            // Строку под курсором дерево выделяет само, по правому нажатию.
            _tree.ContextMenu = BuildRowContextMenu();

            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(object),
                (item, _) => BuildTreeRow(item),
                item => item is GroupNodeViewModel g ? g.Items : null);
            _tree.SelectionChanged += OnTreeSelectionChanged;

            // Перетаскивание баз и групп. Нажатие ловится по туннелю: TreeView
            // помечает PointerPressed обработанным, обновляя выделение, и
            // обычная подписка не сработала бы. Это прямой аналог
            // PreviewMouseLeftButtonDown в WPF-версии.
            _tree.AddHandler(InputElement.PointerPressedEvent, OnTreeDragPointerPressed, RoutingStrategies.Tunnel);
            _tree.PointerMoved += OnTreeDragPointerMoved;
            DragDrop.SetAllowDrop(_tree, true);
            _tree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
            _tree.AddHandler(DragDrop.DropEvent, OnTreeDrop);

            // Горизонтальную прокрутку списка ведёт внешний ScrollViewer, общий
            // с заголовком колонок, а вертикальную сам TreeView.
            // Прежнее опасение про бесконечную высоту и потерю виртуализации здесь
            // неприменимо: у TreeView в Avalonia 11.3.20 виртуализации нет вовсе,
            // панель элементов по умолчанию обычный StackPanel, и ни тема Fluent,
            // ни сам контрол её не переопределяют.
            _emptyState = BuildEmptyState();
            var leftInner = new Grid();
            leftInner.Children.Add(_tree);
            leftInner.Children.Add(_emptyState);

            // Заголовки колонок и строки живут в одной области горизонтальной
            // прокрутки: колонок может не хватить по ширине, и если прокручивать
            // только список, заголовки перестанут совпадать со значениями.
            _listContent = new Grid();
            _listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _listContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            var columnHeader = BuildColumnHeader();
            _listContent.Children.Add(columnHeader);
            Grid.SetRow(columnHeader, 0);
            _listContent.Children.Add(leftInner);
            Grid.SetRow(leftInner, 1);

            var listArea = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _listContent
            };
            _listScroll = listArea;

            // Вертикальная полоса вынесена из области горизонтальной прокрутки
            // и стоит отдельным столбцом справа. Собственная полоса дерева
            // рисуется у правого края его содержимого, поэтому уезжала за границу,
            // как только колонки переставали помещаться по ширине.
            ScrollViewer.SetVerticalScrollBarVisibility(_tree, ScrollBarVisibility.Hidden);
            // Ширина и авто-скрытие не задаются: полоса должна выглядеть так же,
            // как горизонтальная полоса списка и полоса правой панели, то есть
            // по правилам темы. Видимость тоже ведёт сам контрол: при Auto он
            // показывает полосу ровно когда есть что прокручивать. Присваивать
            // IsVisible руками нельзя, при значении Visible полоса включает себя
            // обратно на каждое изменение Maximum и ViewportSize.
            _listVerticalBar = new ScrollBar
            {
                Orientation = Orientation.Vertical,
                Visibility = ScrollBarVisibility.Auto,
                Minimum = 0,
                Maximum = 0,
                ViewportSize = 0
            };
            // Локальная ссылка на созданную полосу: поле к этому моменту
            // указывает на неё, но при следующей пересборке окна начнёт
            // указывать на другую, а подписки живут вместе с этой.
            var verticalBar = _listVerticalBar;
            // Полоса не входит в шаблон ScrollViewer, поэтому колесо над ней
            // некому переадресовать. Шаг и разбор осей взяты из
            // ScrollContentPresenter платформы: с Shift вертикальная дельта
            // становится горизонтальной, и в версии для Windows сделано так же.
            // Вертикаль ведёт прокрутка дерева, горизонталь общая с шапкой.
            _listVerticalBar.PointerWheelChanged += (_, e) =>
            {
                if (!ReferenceEquals(_listVerticalBar, verticalBar))
                    return;
                var delta = e.Delta;
                if (e.KeyModifiers == KeyModifiers.Shift && MathUtilities.IsZero(delta.X))
                    delta = new Vector(delta.Y, delta.X);

                // Событие считается разобранным только если список сдвинулся:
                // на краю платформа отдаёт прокрутку выше по дереву, и глушить
                // её здесь значит ломать это правило.
                var moved = false;
                if (delta.Y != 0 && TreeScroll is { } vertical)
                {
                    var hidden = Math.Max(0, vertical.Extent.Height - vertical.Viewport.Height);
                    var next = vertical.Offset.WithY(
                        Math.Clamp(vertical.Offset.Y - delta.Y * WheelScrollStep, 0, hidden));
                    if (next != vertical.Offset)
                    {
                        vertical.Offset = next;
                        moved = true;
                    }
                }

                if (delta.X != 0 && _listScroll is { } horizontal)
                {
                    var hidden = Math.Max(0, horizontal.Extent.Width - horizontal.Viewport.Width);
                    var next = horizontal.Offset.WithX(
                        Math.Clamp(horizontal.Offset.X - delta.X * WheelScrollStep, 0, hidden));
                    if (next != horizontal.Offset)
                    {
                        horizontal.Offset = next;
                        moved = true;
                    }
                }

                e.Handled = moved;
            };
            // Прокручиваются только строки, поэтому полоса начинается под шапкой
            // колонок. Высота шапки меняется вместе с компактным режимом и темой.
            columnHeader.GetObservable(Visual.BoundsProperty).Subscribe(new PropertyObserver<Rect>(bounds =>
                verticalBar.Margin = new Thickness(0, bounds.Height, 0, 0)));
            _listVerticalBar.GetObservable(RangeBase.ValueProperty).Subscribe(new PropertyObserver<double>(value =>
            {
                // Флаг разводит два направления: пользователь тянет полосу,
                // и наоборот, прокрутка списка двигает полосу.
                if (_syncingScrollBar || !ReferenceEquals(_listVerticalBar, verticalBar)
                    || TreeScroll is not { } scroll)
                    return;
                _syncingScrollBar = true;
                try { scroll.Offset = scroll.Offset.WithY(value); }
                finally { _syncingScrollBar = false; }
            }));

            // Полоса занимает свой столбец, как в версии для Windows. Ширина
            // ей задана темой и при наведении не меняется, поэтому список от неё
            // не дёргается, а пока прокручивать нечего, полоса скрыта и столбец
            // пуст. Поверх списка её класть нельзя: она забирала бы клики,
            // контекстное меню и перетаскивание по правой кромке строк.
            var listWithBar = new Grid();
            listWithBar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            listWithBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_listVerticalBar, 1);
            listWithBar.Children.Add(listArea);
            listWithBar.Children.Add(_listVerticalBar);

            var leftPanel = new Border
            {
                Child = listWithBar,
                Margin = new Thickness(UiMetrics.TopBarH, UiMetrics.TopBarV, 8, UiMetrics.TopBarV),
                Padding = new Thickness(UiMetrics.Scaled(8), UiMetrics.Scaled(8))
            };

            grid.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);

            // Показываем/скрываем заглушку при любых изменениях списка и поиска.
            if (_vm is not null)
            {
                // Содержимое окна пересобирается при смене языка и компактного
                // режима, а вьюмодель живёт дальше: без снятия прежние
                // обработчики накапливались бы и делали ту же работу заново.
                DetachViewModelHandlers();

                // Строки пересобираются вместе с деревом, поэтому заголовок
                // выравнивается по ним заново: отступ уровня мог измениться.
                _groupNodesChanged = (_, _) => { UpdateEmptyState(); QueueHeaderAlign(); };
                _flatItemsChanged = (_, _) => UpdateEmptyState();
                _tagFiltersRebuilt = (_, _) => RefreshTagFilterPanel();
                _vmPropertyChanged = (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SearchText))
                        UpdateEmptyState();
                    // Меню трея показывает выбранную базу и недавние: без этого
                    // оно осталось бы таким, каким было собрано при запуске.
                    if (e.PropertyName == nameof(MainViewModel.SelectedInfobase)
                        || e.PropertyName == nameof(MainViewModel.RecentInfobases))
                        QueueTrayMenuRefresh();
                    // Заголовок строится до загрузки настроек, поэтому обновляется
                    // при уведомлении о колонках: иначе сохранённые ширина и состав
                    // применились бы к строкам, но не к уже собранному заголовку.
                    if (e.PropertyName is not null && e.PropertyName.Contains("Column", StringComparison.Ordinal))
                        QueueColumnHeaderRefresh();
                    // Кнопки групп живут в заголовке и видны только при группировке.
                    if (e.PropertyName == nameof(MainViewModel.ShowExpandCollapseButtons))
                        QueueColumnHeaderRefresh();
                    // Переключатель тегов в списке живёт в том же заголовке,
                    // а его настройка меняется и из окна настроек.
                    if (e.PropertyName == nameof(MainViewModel.ShowTags))
                        QueueColumnHeaderRefresh();
                    // Группировку меняют и верхняя панель, и окно настроек,
                    // поэтому переключатель подтягивает состояние вьюмодели.
                    if (e.PropertyName == nameof(MainViewModel.GroupByGroup) && _groupByToggle is not null)
                        _groupByToggle.IsChecked = _vm.GroupByGroup;
                    if (e.PropertyName == nameof(MainViewModel.ShowTagFilterPanel)
                        || e.PropertyName == nameof(MainViewModel.HasActiveTagFilter))
                    {
                        // Кнопка «теги» строится до загрузки настроек, поэтому
                        // её состояние подтягивается отсюда, иначе после перезапуска
                        // она разошлась бы с реальной видимостью панели.
                        if (_tagsToggle is not null)
                            _tagsToggle.IsChecked = _vm.ShowTagFilterPanel;
                        RefreshTagFilterPanel();
                    }
                };

                _vm.GroupNodes.CollectionChanged += _groupNodesChanged;
                _vm.FlatItems.CollectionChanged += _flatItemsChanged;
                _vm.TagFiltersRebuilt += _tagFiltersRebuilt;
                _vm.PropertyChanged += _vmPropertyChanged;
            }
            UpdateEmptyState();
            RefreshTagFilterPanel();
            RefreshColumnHeader();

            var rightPanel = new ScrollViewer
            {
                Name = "RightPanelBorder",
                Content = BuildRightPanel(),
                // Более плотные отступы — кнопки и карточки занимают меньше места.
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(UiMetrics.Scaled(12), UiMetrics.Scaled(10)),
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
            // Высота оформления группы и расстояние между группами берутся из метрик:
            // в компактном режиме вертикальный padding заголовка и внешний отступ
            // уменьшаются, чтобы группы занимали меньше места.
            var header = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusSm),
                Padding = new Thickness(6, UiMetrics.GroupHeaderPadV),
                Margin = new Thickness(0, UiMetrics.GroupHeaderMarginV)
            };
            header.Bind(Border.BackgroundProperty, new Binding("HeaderBrush") { Source = group });

            // Имя и счётчик привязаны к узлу, а не подставлены строкой: состав узла
            // меняется и без пересборки дерева (закрепление базы), и тогда готовый
            // текст остался бы со старым числом.
            var caption = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Имя группы наследует применяемый к интерфейсу шрифт; в компактном режиме
            // размер задаётся явно и уменьшается, чтобы строки групп были плотнее.
            var text = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (UiMetrics.Compact)
                text.FontSize = UiMetrics.GroupNameFont;
            text.Bind(TextBlock.TextProperty, new Binding("DisplayName") { Source = group });
            text.Bind(TextBlock.ForegroundProperty, new Binding("HeaderTextBrush") { Source = group });
            caption.Children.Add(text);

            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            if (UiMetrics.Compact)
                count.FontSize = UiMetrics.GroupNameFont;
            count.Bind(TextBlock.TextProperty,
                new Binding("TotalInfobaseCount") { Source = group, StringFormat = "({0})" });
            count.Bind(TextBlock.ForegroundProperty, new Binding("HeaderTextBrush") { Source = group });
            caption.Children.Add(count);

            // Команды группы «Изменить группу» и «Удалить группу» выровнены по
            // левому краю строки; имя и счётчик группы следуют за ними правее.
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionsColumnWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(GroupRowActionButton(group, "IconEdit", "EditGroupCommand", LocalizationManager.T("Main.EditGroupTooltip")));
            actions.Children.Add(GroupRowActionButton(group, "IconDelete", "DeleteGroupCommand", LocalizationManager.T("Main.DeleteGroupTooltip")));
            Grid.SetColumn(actions, 0);
            row.Children.Add(actions);

            Grid.SetColumn(caption, 1);
            row.Children.Add(caption);

            header.Child = row;
            return header;
        }

        /// <summary>
        /// Кнопка действия в колонке «Действия» строки группы: иконка, команда из вьюмодели,
        /// параметром служит узел группы строки.
        /// </summary>
        private Button GroupRowActionButton(GroupNodeViewModel group, string iconKey, string commandPath, string tooltip)
        {
            var button = new Button
            {
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(15), "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                CommandParameter = group
            };
            ToolTip.SetTip(button, tooltip);
            // Команда живёт во вьюмодели, а контекстом строки служит узел группы.
            button.Bind(Button.CommandProperty, new Binding(commandPath) { Source = _vm });
            return button;
        }

        private Control BuildInfobaseRow(Infobase ib)
        {
            // Карточка с фоном/границей из темы; hover и выделение отслеживает сама
            // (см. InfobaseRowCard): обычное → CardBackgroundBrush, hover → ItemHoverBrush,
            // выделено → ItemSelectedBrush + AccentBrush-граница.
            var card = new InfobaseRowCard();

            var grid = new Grid();
            // Слева направо: звезда, булавка, иконка типа подключения, имя базы,
            // дальше колонки значений. Звезда и булавка повторяют колонки заголовка
            // теми же ширинами и подчиняются тем же настройкам.
            var showFavorite = _vm?.ShowFavoritesButton ?? true;
            var showPin = _vm?.ShowPinnedButton ?? true;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showFavorite ? FavoriteColumnWidth : 0) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showPin ? PinColumnWidth : 0) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumnWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = NameColumnLength(),
                MinWidth = MinColumnWidth
            });

            // Колонки идут теми же ширинами, что и в заголовке, поэтому значения
            // строк выстраиваются под своими заголовками.
            var columns = ListColumns();
            // Колонка «Действия» встаёт сразу после колонки «Режим запуска», поэтому её
            // определение встраивается в последовательность, как и в заголовке.
            var actionsOffset = ActionsOffsetInColumns(columns);
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionsColumnWidth) });
                grid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(columns[i].Width), MinWidth = MinColumnWidth });
            }
            // «Режим запуска» скрыт или стоит последним — действия уходят в самый конец.
            if (actionsOffset >= columns.Count)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionsColumnWidth) });

            if (showFavorite)
            {
                var favorite = RowMarkButton(card, ib, "IconFavorite", "FavoriteBrush",
                    nameof(Infobase.IsFavorite), () => ib.IsFavorite,
                    LocalizationManager.T("Main.ToggleFavoriteTooltip"), "ToggleFavoriteForCommand", FavoriteColumnWidth);
                grid.Children.Add(favorite);
                Grid.SetColumn(favorite, 0);

                // Номер слота Alt+N. В разметке WPF это плашка рядом со звездой,
                // здесь номер наложен на угол её колонки: колонки строки узкие
                // и заданы по ширине значков, отдельного места под плашку в них
                // нет. Дерево ради номера не пересобирается, строка обновляет
                // его сама по уведомлению модели.
                var slot = FavoriteSlotBadge(card, ib);
                grid.Children.Add(slot);
                Grid.SetColumn(slot, 0);
            }

            if (showPin)
            {
                var pin = RowMarkButton(card, ib, "IconPin", "AccentBrush",
                    nameof(Infobase.IsPinned), () => ib.IsPinned,
                    LocalizationManager.T("Main.TogglePinTooltip"), "TogglePinForCommand", PinColumnWidth);
                grid.Children.Add(pin);
                Grid.SetColumn(pin, 1);
            }

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
            Grid.SetColumn(iconBox, 2);

            // Правая колонка: имя (крупно) + строки вторичной информации.
            // В компактном режиме уменьшаем и межстрочный промежуток, чтобы строки с
            // полным набором метаданных тоже «сжимались», а не оставались прежней высоты.
            var content = new StackPanel { Spacing = UiMetrics.Scaled(2), VerticalAlignment = VerticalAlignment.Center };

            // Имя базы кладётся в колонку напрямую: в горизонтальной панели оно
            // получало бы бесконечную ширину и при узкой колонке налезало бы
            // на соседние значения вместо обрезки многоточием.
            var name = new TextBlock
            {
                Text = ib.Name,
                FontSize = UiMetrics.RowNameFont,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            content.Children.Add(name);

            // Вторичной строкой остаётся только то, чего нет в колонках: тип
            // подключения и путь. Остальное ушло в колонки, иначе одни и те же
            // данные показывались бы дважды.
            var shown = new HashSet<string>(columns.Select(c => c.Key), StringComparer.Ordinal);
            var location = ib.Connection.Type switch
            {
                ConnectionType.WebServer => ib.Connection.WebUrl,
                _ => ib.ServerDatabaseDisplay
            };
            var summary = shown.Contains("ServerBase")
                ? ib.ConnectionTypeDisplay
                : JoinSegments(ib.ConnectionTypeDisplay, location);
            if (!string.IsNullOrWhiteSpace(summary))
                content.Children.Add(SecondaryText(summary, card));

            grid.Children.Add(content);
            Grid.SetColumn(content, 3);

            var dataColumn = NameRowColumn + 1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                    dataColumn++;
                var value = ColumnValue(ib, columns[i].Key);
                var cell = SecondaryText(string.IsNullOrWhiteSpace(value) ? string.Empty : value, card);
                cell.VerticalAlignment = VerticalAlignment.Center;
                if (columns[i].Key == "Version")
                {
                    // Двойной щелчок открывает выбор версии платформы, как
                    // в разметке WPF (MainWindow.xaml:1230). До этого окно
                    // PlatformVersionPickerWindow собиралось, но из интерфейса
                    // Linux-версии было недостижимо.
                    ToolTip.SetTip(cell, LocalizationManager.T("Main.PlatformVersionTooltip"));
                    cell.DoubleTapped += (_, e) =>
                    {
                        e.Handled = true;
                        _vm?.PickPlatformVersionFor(ib);
                    };
                }
                grid.Children.Add(cell);
                Grid.SetColumn(cell, dataColumn);
                dataColumn++;
            }

            // Кнопки действий в колонке «Действия» (после колонки «Режим запуска»):
            // запуск, конфигуратор, изменить настройки, очистить кеш, удалить.
            var actionsCol = NameRowColumn + 1 + actionsOffset;
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 1
            };
            actions.Children.Add(RowActionButton(ib, "IconPlay", "LaunchEnterpriseCommand", LocalizationManager.T("Main.LaunchEnterpriseTooltip")));
            actions.Children.Add(RowActionButton(ib, "IconWrench", "LaunchConfiguratorCommand", LocalizationManager.T("Main.LaunchConfiguratorSectionTooltip")));
            actions.Children.Add(RowActionButton(ib, "IconEdit", "EditInfobaseCommand", LocalizationManager.T("Main.EditBaseTooltip")));
            actions.Children.Add(RowActionButton(ib, "IconDelete", "ClearCacheCommand", LocalizationManager.T("Main.ClearCacheTooltip")));
            actions.Children.Add(RowActionButton(ib, "IconDelete", "DeleteInfobaseCommand", LocalizationManager.T("Main.DeleteTooltip")));
            grid.Children.Add(actions);
            Grid.SetColumn(actions, actionsCol);

            if (_vm?.ShowTags == true)
            {
                // Теги идут второй строкой сетки во всю ширину, как в WPF-версии:
                // внутри колонки имени они переносились бы по её ширине и тянули
                // высоту строки вверх.
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var tags = BuildRowTags(card, ib);
                grid.Children.Add(tags);
                Grid.SetRow(tags, 1);
                Grid.SetColumn(tags, NameRowColumn);
                Grid.SetColumnSpan(tags, grid.ColumnDefinitions.Count - NameRowColumn);
            }

            card.Child = grid;
            return card;
        }

        /// <summary>
        /// Теги базы под её именем: чип с крестиком на каждый тег и кнопка
        /// «+ тег». Панель перестраивается по уведомлению самой базы, поэтому
        /// после правки тегов строку пересобирать не нужно.
        /// </summary>
        private Control BuildRowTags(InfobaseRowCard card, Infobase infobase)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var chipSubscriptions = new List<IDisposable>();

            void Fill()
            {
                foreach (var subscription in chipSubscriptions)
                    subscription.Dispose();
                chipSubscriptions.Clear();
                panel.Children.Clear();

                foreach (var tag in infobase.Tags)
                    panel.Children.Add(BuildTagChip(infobase, tag, chipSubscriptions));

                panel.Children.Add(BuildAddTagButton(infobase, chipSubscriptions));
            }

            void OnInfobaseChanged(object? _, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Infobase.Tags))
                    Fill();
            }

            card.AddSubscription(() =>
            {
                infobase.PropertyChanged += OnInfobaseChanged;
                Fill();
                return new ActionDisposable(() =>
                {
                    infobase.PropertyChanged -= OnInfobaseChanged;
                    foreach (var subscription in chipSubscriptions)
                        subscription.Dispose();
                    chipSubscriptions.Clear();
                });
            });

            return panel;
        }

        /// <summary>Чип тега: клик отбирает базы по тегу, крестик убирает тег у базы.</summary>
        private Control BuildTagChip(Infobase infobase, string tag, ICollection<IDisposable> subscriptions)
        {
            var text = new TextBlock
            {
                Text = tag,
                FontSize = UiMetrics.ScaledFont(10),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = UiMetrics.Scaled(180),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(text, tag);
            ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var name = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new Cursor(StandardCursorType.Hand),
                CommandParameter = tag
            };
            ToolTip.SetTip(name, LocalizationManager.T("Main.ShowTagBases"));
            name.Bind(Button.CommandProperty, new Binding("SearchByTagCommand") { Source = _vm });

            var remove = new Button
            {
                Content = IconHelper.MakeIcon("IconClose", UiMetrics.Scaled(9), "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new Cursor(StandardCursorType.Hand),
                // Форма параметра та же, что в WPF-версии: база и тег.
                CommandParameter = new object[] { infobase, tag }
            };
            ToolTip.SetTip(remove, LocalizationManager.T("Main.RemoveTag"));
            remove.Bind(Button.CommandProperty, new Binding("RemoveTagCommand") { Source = _vm });

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(name);
            row.Children.Add(remove);

            var chip = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusMd),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 4, 2),
                Child = row
            };
            ThemeBrushes.Bind(chip, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(chip, Border.BorderBrushProperty, "BorderColorBrush");
            return chip;
        }

        /// <summary>Складывает подписку в приёмник, пропуская пустую (Application ещё не поднят).</summary>
        private static void Track(ICollection<IDisposable> sink, IDisposable? subscription)
        {
            if (subscription is not null)
                sink.Add(subscription);
        }

        /// <summary>
        /// Кнопка «+ тег» в конце списка тегов строки. По клику раскрывается поле ввода
        /// прямо в строке: Enter добавляет тег, Esc отменяет, потеря фокуса сохраняет введённое.
        /// </summary>
        private Control BuildAddTagButton(Infobase infobase, ICollection<IDisposable> subscriptions)
        {
            var text = new TextBlock
            {
                Text = LocalizationManager.T("Main.AddTagShort"),
                FontSize = UiMetrics.ScaledFont(10),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(IconHelper.MakeIcon("IconTag", UiMetrics.Scaled(9), "TextSecondaryBrush"));
            content.Children.Add(text);

            var button = new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1),
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(button, LocalizationManager.T("Main.AddTag"));

            // Поле ввода тега показывается на месте кнопки во время редактирования.
            var input = new TextBox
            {
                Watermark = LocalizationManager.T("Main.AddTag"),
                MinWidth = UiMetrics.Scaled(120),
                MaxWidth = UiMetrics.Scaled(220),
                FontSize = UiMetrics.ScaledFont(11),
                Padding = new Thickness(4, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            ToolTip.SetTip(input, LocalizationManager.T("Main.EnterTagHint"));

            void ShowEditor()
            {
                button.IsVisible = false;
                input.Text = string.Empty;
                input.IsVisible = true;
                input.Focus();
                input.SelectAll();
            }

            void HideEditor()
            {
                input.IsVisible = false;
                button.IsVisible = true;
            }

            void Commit()
            {
                if (!input.IsVisible)
                    return;

                var tag = input.Text?.Trim() ?? string.Empty;
                HideEditor();
                input.Text = string.Empty;

                if (tag.Length == 0)
                    return;

                if (_vm?.AddTagInlineCommand.CanExecute(null) == true)
                    _vm.AddTagInlineCommand.Execute(new object[] { infobase, tag });
            }

            void Cancel()
            {
                if (!input.IsVisible)
                    return;

                input.Text = string.Empty;
                HideEditor();
            }

            button.Click += (_, _) => ShowEditor();

            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Cancel();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    Commit();
                    e.Handled = true;
                }
            };

            // Потеря фокуса сохраняет введённый тег, как в WPF-версии. Откладываем
            // обработку: клик вне поля сначала переводит фокус, затем фиксируем ввод.
            input.LostFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (input.IsVisible)
                    Commit();
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(button);
            row.Children.Add(input);
            return row;
        }

        /// <summary>
        /// Кнопка-маркер в строке базы: звезда «избранное» или булавка «закреплено».
        /// Цвет иконки следит за состоянием самой базы, поэтому после переключения
        /// строку не нужно пересобирать, и за кистями темы он тоже следует.
        /// </summary>
        /// <summary>
        /// Номер слота Alt+N у избранной базы. Пусто, если слот не назначен:
        /// их девять, а избранных может быть больше.
        /// </summary>
        private Control FavoriteSlotBadge(InfobaseRowCard card, Infobase infobase)
        {
            var text = new TextBlock
            {
                FontSize = UiMetrics.ScaledFont(9),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var host = new Border
            {
                Child = text,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = UiMetrics.Scaled(12),
                Height = UiMetrics.Scaled(12),
                IsHitTestVisible = false
            };

            void Apply()
            {
                text.Text = infobase.FavoriteHotkeyDisplay;
                // Разметка WPF гасит плашку и по номеру, и по самой звезде
                // (MainWindow.xaml:1156-1183): без второго условия номер
                // остаётся висеть у базы, которую убрали из избранного.
                host.IsVisible = infobase.IsFavorite && !string.IsNullOrEmpty(text.Text);
            }

            void OnChanged(object? _, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Infobase.FavoriteHotkeyDisplay)
                    || e.PropertyName == nameof(Infobase.FavoriteHotkeyNumber)
                    || e.PropertyName == nameof(Infobase.IsFavorite))
                    Apply();
            }

            card.AddSubscription(() =>
            {
                infobase.PropertyChanged += OnChanged;
                Apply();
                return new ActionDisposable(() => infobase.PropertyChanged -= OnChanged);
            });

            return host;
        }

        private Button RowMarkButton(InfobaseRowCard card, Infobase infobase, string iconKey,
            string activeBrushKey, string stateProperty, Func<bool> isActive,
            string tooltip, string commandPath, double width)
        {
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Width = UiMetrics.Scaled(14),
                Height = UiMetrics.Scaled(14),
                Data = IconHelper.Geometry(iconKey),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            IBrush? active = null;
            IBrush? idle = null;
            void ApplyState()
            {
                var brush = isActive() ? active : idle;
                if (brush is not null)
                    icon.Fill = brush;
            }

            card.AddSubscription(() => Application.Current?.GetResourceObservable(activeBrushKey)
                .Subscribe(new BrushObserver(brush => active = brush, ApplyState)));
            card.AddSubscription(() => Application.Current?.GetResourceObservable("TextSecondaryBrush")
                .Subscribe(new BrushObserver(brush => idle = brush, ApplyState)));

            void OnInfobaseChanged(object? _, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == stateProperty)
                    ApplyState();
            }

            card.AddSubscription(() =>
            {
                infobase.PropertyChanged += OnInfobaseChanged;
                // Состояние могло измениться, пока строка была отсоединена.
                ApplyState();
                return new ActionDisposable(() => infobase.PropertyChanged -= OnInfobaseChanged);
            });

            var button = new Button
            {
                Content = icon,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Width = width,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                CommandParameter = infobase
            };
            ToolTip.SetTip(button, tooltip);
            // Команда живёт во вьюмодели, а контекстом строки служит сама база,
            // поэтому источник привязки указывается явно.
            button.Bind(Button.CommandProperty, new Binding(commandPath) { Source = _vm });
            return button;
        }

        /// <summary>
        /// Кнопка действия в колонке «Действия» строки базы: иконка, команда из вьюмодели,
        /// параметром служит сама информационная база строки.
        /// </summary>
        private Button RowActionButton(Infobase ib, string iconKey, string commandPath, string tooltip)
        {
            var button = new Button
            {
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(15), "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                CommandParameter = ib
            };
            ToolTip.SetTip(button, tooltip);
            // Команда живёт во вьюмодели, а контекстом строки служит сама база.
            button.Bind(Button.CommandProperty, new Binding(commandPath) { Source = _vm });
            return button;
        }

        /// <summary>Освобождение по вызову действия: снятие подписки на событие модели.</summary>
        private sealed class ActionDisposable : IDisposable
        {
            private Action? _dispose;

            public ActionDisposable(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                var action = _dispose;
                _dispose = null;
                action?.Invoke();
            }
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
        private static TextBlock SecondaryText(string text, InfobaseRowCard? owner = null)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.RowSecondaryFont,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(block, text);
            if (owner is null)
                ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            else
                ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return block;
        }

        private Control BuildRightPanel()
        {
            // Компактная правая панель: primary-запуски на всю ширину, вторичные
            // действия — списком в один столбец, секции без тяжёлых карточек.
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = UiMetrics.ActionGridGap
            };

            // Заголовок базы
            var nameBlock = new TextBlock
            {
                FontSize = UiMetrics.ScaledFont(15),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            nameBlock.Bind(TextBlock.TextProperty, new Binding("RightPanelTitle"));

            var groupBlock = new TextBlock
            {
                FontSize = UiMetrics.ScaledFont(11.5),
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap
            };
            groupBlock.Bind(TextBlock.TextProperty, new Binding("RightPanelSubtitle"));

            // Заголовок сеткой, а не горизонтальной панелью: в панели подпись
            // получала бы бесконечную ширину и не переносилась бы по словам.
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Значок базы показывается только когда база выбрана: при выбранной
            // группе и при пустом выборе он висел бы один без подписи.
            var headerIcon = IconHelper.MakeIcon("IconDatabase", UiMetrics.Scaled(24));
            headerIcon.Bind(Avalonia.Controls.Shapes.Path.DataProperty,
                new Binding("RightPanelIconKey") { Converter = IconKeyConverter });
            headerIcon.Bind(Control.IsVisibleProperty, new Binding("HasRightPanelIcon"));
            header.Children.Add(headerIcon);
            var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            headerText.Children.Add(nameBlock);
            headerText.Children.Add(groupBlock);
            header.Children.Add(headerText);
            Grid.SetColumn(headerText, 1);

            // Подсказка «выберите базу» отдельной строкой под заголовком,
            // как в WPF: там она видна, пока база не выбрана.
            var hintBlock = new TextBlock
            {
                FontSize = UiMetrics.ScaledFont(11.5),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 4)
            };
            hintBlock.Bind(TextBlock.TextProperty, new Binding("RightPanelHint"));
            hintBlock.Bind(Control.IsVisibleProperty, new Binding("!IsInfobaseSelected"));
            panel.Children.Add(header);
            panel.Children.Add(hintBlock);

            // Запуск 1С:Предприятие (primary) — акцентная кнопка запуска с меню
            // дополнительных вариантов.
            var launchEnterpriseBlock = BuildLaunchSplitButton(
                "IconPlay",
                LocalizationManager.T("Main.LaunchEnterprise"),
                "LaunchEnterpriseCommand",
                LocalizationManager.T("Main.LaunchEnterpriseTooltip"),
                primary: true,
                new[]
                {
                    (LocalizationManager.T("Main.LaunchWithParams"), "LaunchEnterpriseWithParamsCommand"),
                    (LocalizationManager.T("Main.LaunchWithAuth"), "LaunchEnterpriseWithAuthCommand")
                });

            // Конфигуратор — secondary full-width (без отдельной тяжёлой карточки).
            var launchConfiguratorBlock = BuildLaunchSplitButton(
                "IconWrench",
                LocalizationManager.T("Main.LaunchConfiguratorSection"),
                "LaunchConfiguratorCommand",
                LocalizationManager.T("Main.LaunchConfiguratorSectionTooltip"),
                primary: false,
                new[]
                {
                    (LocalizationManager.T("Main.LaunchWithParams"), "LaunchConfiguratorWithParamsCommand")
                });

            // Остальные действия («Очистить кеш», «Изменить настройки», «Удалить»,
            // «Добавить») перенесены в колонку «Действия» строк базы и верхнюю панель
            // команд. Здесь остаются вторичные действия списком.
            var actionListBlock = BuildActionList(
                CompactActionButton("IconOpen", LocalizationManager.T("Main.OpenFolder"), "OpenInfobaseFolderCommand", LocalizationManager.T("Main.OpenFolderTooltip")),
                CompactActionButton("IconKeyboard", LocalizationManager.T("Main.NativeStarter"), "OpenNativeStarterCommand", LocalizationManager.T("Main.NativeStarterTooltipLinux")),
                CompactActionButtonBound("IconWeb", "OpenByLinkCaption", "OpenInfobaseByLinkCommand", LocalizationManager.T("Main.OpenLinkTooltip")),
                CompactActionButton("IconShortcut", LocalizationManager.T("Main.DesktopShortcut"), "CreateDesktopShortcutCommand", LocalizationManager.T("Main.DesktopShortcutTooltip"))
            );

            // Бейдж «Закреплено» и секция тегов выбранной базы, как в разметке WPF.
            var pinnedBadge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3),
                Margin = new Thickness(0, 0, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = ThemedIconAndText("IconPin", LocalizationManager.T("Main.PinnedLabel"),
                    "AccentColorBrush", 12, centered: false)
            };
            ThemeBrushes.Bind(pinnedBadge, Border.BackgroundProperty, "ItemHoverColorBrush");
            pinnedBadge.Bind(Control.IsVisibleProperty, new Binding("SelectedInfobase.IsPinned"));

            var tagsHeader = ThemedIconAndText("IconTag", LocalizationManager.T("Main.Tags"),
                "TextSecondaryColorBrush", 14, centered: false);
            var tagsList = new ItemsControl
            {
                Margin = new Thickness(0, 0, 0, 4),
                ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
                ItemTemplate = new FuncDataTemplate<string>((tag, _) =>
                {
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(8, 3),
                        Margin = new Thickness(0, 0, 4, 4),
                        BorderThickness = new Thickness(1),
                        Child = ThemedIconAndText("IconTag", tag ?? "", "AccentColorBrush", 10, centered: false)
                    };
                    ThemeBrushes.Bind(chip, Border.BackgroundProperty, "ItemHoverColorBrush");
                    ThemeBrushes.Bind(chip, Border.BorderBrushProperty, "BorderColorBrush");
                    return chip;
                })
            };
            tagsList.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedInfobase.Tags"));
            var tagsBlock = new StackPanel { Spacing = 4 };
            tagsBlock.Children.Add(tagsHeader);
            tagsBlock.Children.Add(tagsList);

            // Информация о подключении.
            // Блок сведений подчинён переключателю подробностей правой панели,
            // как в WPF: там по нему прячется та же таблица.
            var connectionLabel = SectionLabel(LocalizationManager.T("Main.SectionConnInfo"));
            var connectionCard = PlainCard(
                DetailRow(LocalizationManager.T("Main.Type"), new Binding("SelectedInfobase.ConnectionTypeDisplay")),
                DetailRow(LocalizationManager.T("Main.ServerPath"), new Binding("SelectedInfobase.ConnectionPathDisplay")),
                DetailRow(LocalizationManager.T("Column.ServerBase"), new Binding("SelectedInfobase.ServerDatabaseDisplay")),
                DetailRow(LocalizationManager.T("Main.ConnectionString"), new Binding("SelectedInfobase.ConnectionStringDisplay")),
                DetailRow(LocalizationManager.T("Main.Platform"), new Binding("SelectedInfobase.PlatformVersion")),
                DetailRow(LocalizationManager.T("Main.LaunchMode"), new Binding("SelectedInfobase.ParsedLaunchMode")),
                DetailRow(LocalizationManager.T("Main.Client"), new Binding("SelectedInfobase.ClientTypeDisplay")),
                DetailRow(LocalizationManager.T("Main.Bitness"), new Binding("SelectedInfobase.ArchitectureDisplay")),
                DetailRow(LocalizationManager.T("Main.Parameters"), new Binding("SelectedInfobase.LaunchParameters")),
                DetailRow(LocalizationManager.T("Main.LastLaunch"), new Binding("SelectedInfobase.LastLaunchDisplay")),
                DetailRow(LocalizationManager.T("Main.CacheSize"), new Binding("SelectedInfobase.CacheSizeDisplay")));
            connectionCard.Bind(Control.IsVisibleProperty, new Binding("ShowConnectionInfo"));

            // Блок «Текущая сессия»: значения действуют только на следующий запуск.
            var sessionCard = BuildSessionCard();

            // Описание (стиль из ConfigurationManagement): значение TextPrimaryBrush, нижний отступ.
            var desc = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = UiMetrics.ScaledFont(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            ThemeBrushes.Bind(desc, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            desc.Bind(TextBlock.TextProperty, new Binding("SelectedInfobase.Description"));
            var descriptionLabel = SectionLabel(LocalizationManager.T("Main.Description"), smallCaps: false);

            // Порядок блоков взят из разметки WPF: сведения о подключении, описание,
            // теги, затем действия и текущая сессия. Раньше действия стояли первыми.
            panel.Children.Add(pinnedBadge);
            panel.Children.Add(connectionLabel);
            panel.Children.Add(connectionCard);
            panel.Children.Add(descriptionLabel);
            panel.Children.Add(desc);
            panel.Children.Add(tagsBlock);
            panel.Children.Add(launchEnterpriseBlock);
            panel.Children.Add(launchConfiguratorBlock);
            panel.Children.Add(actionListBlock);
            panel.Children.Add(sessionCard);

            // Выход — компактная кнопка внизу, без лишней «карточки».
            var exitBtn = CompactActionButton("IconExit", LocalizationManager.T("Main.Exit"), "ExitCommand",
                LocalizationManager.T("Main.ExitTooltip"));
            exitBtn.Margin = new Thickness(0, UiMetrics.ActionGridGap, 0, 0);
            panel.Children.Add(exitBtn);

            return panel;
        }

        /// <summary>
        /// Двухколоночная сетка компактных кнопок действий правой панели.
        /// Равномерно заполняет ширину и заметно экономит вертикальное место
        /// по сравнению со стеком полноширинных secondary-кнопок.
        /// </summary>
        private static Control BuildActionList(params Control[] buttons)
        {
            // Кнопки идут в один столбец, как в версии для Windows. Двухколоночная
            // раскладка экономила высоту, но подписи в неё не помещались ни при
            // какой ширине панели: при её пределе 340 на ячейку остаётся около 160
            // пикселей, а «Ярлык на рабочем столе» требует почти 190, и подпись
            // обрывалась на середине слова.
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = UiMetrics.ActionGridGap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var btn in buttons)
                stack.Children.Add(btn);

            return stack;
        }

        /// <summary>
        /// Компактная кнопка действия правой панели: иконка + текст, низкая высота,
        /// растягивается на всю ширину панели.
        /// </summary>
        private static Control CompactActionButton(string iconKey, string text, string commandPath, string tooltip)
        {
            var btn = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(UiMetrics.RadiusMd))
            {
                Content = CompactIconAndText(iconKey, text, "ButtonTextBrush"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = UiMetrics.ActionButtonMinHeight,
                Padding = new Thickness(UiMetrics.ActionButtonPadH, UiMetrics.ActionButtonPadV),
                Margin = new Thickness(0)
            };
            ToolTip.SetTip(btn, tooltip);
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        /// <summary>Компактное содержимое кнопки: иконка + подпись меньшего размера.</summary>
        /// <summary>
        /// Вариант кнопки действия с подписью из привязки: нужен там, где текст
        /// меняется по состоянию, как короткая подпись открытия по ссылке.
        /// </summary>
        private static Control CompactActionButtonBound(string iconKey, string textPath, string commandPath, string tooltip)
        {
            var btn = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(UiMetrics.RadiusMd))
            {
                Content = CompactIconAndText(iconKey, "", "ButtonTextBrush", textPath),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = UiMetrics.ActionButtonMinHeight,
                Padding = new Thickness(UiMetrics.ActionButtonPadH, UiMetrics.ActionButtonPadV),
                Margin = new Thickness(0)
            };
            ToolTip.SetTip(btn, tooltip);
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        private static Control CompactIconAndText(string iconKey, string text, string brushKey, string? textPath = null)
        {
            // Сеткой, а не горизонтальной панелью: панель меряет подпись
            // бесконечной шириной, поэтому обрезка многоточием не срабатывает
            // и длинный текст вылезает за кнопку вместо того, чтобы сократиться.
            var sp = new Grid { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            sp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sp.Children.Add(IconHelper.MakeIcon(iconKey, UiMetrics.ActionIconSize, brushKey));
            var tb = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.ActionFontSize,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
            if (textPath is not null)
                tb.Bind(TextBlock.TextProperty, new Binding(textPath));
            Grid.SetColumn(tb, 1);
            sp.Children.Add(tb);
            return sp;
        }

        /// <summary>
        /// Блок «Текущая сессия»: режим клиента и разрядность только для
        /// следующего запуска, сохранённые настройки базы он не меняет.
        /// Видимостью управляет настройка, как и в WPF-версии.
        /// </summary>
        private Control BuildSessionCard()
        {
            var hint = new TextBlock
            {
                Text = LocalizationManager.T("Main.SessionOnceHint"),
                FontSize = UiMetrics.ScaledFont(11),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            ToolTip.SetTip(hint, LocalizationManager.T("Main.CurrentSessionHelp"));

            var card = SectionCard(LocalizationManager.T("Main.CurrentSession"), "IconInfo",
                hint,
                SessionGroupLabel(LocalizationManager.T("Main.ClientMode")),
                SessionOption(LocalizationManager.T("Main.SessionClientAuto"), "SessionClient", "IsSessionClientAuto"),
                SessionOption(LocalizationManager.T("Main.SessionClientOrdinary"), "SessionClient", "IsSessionClientOrdinary"),
                SessionOption(LocalizationManager.T("Main.SessionClientThickManaged"), "SessionClient", "IsSessionClientThick",
                    LocalizationManager.T("Main.SessionThickManagedTooltip")),
                SessionOption(LocalizationManager.T("Main.SessionClientThickOrdinary"), "SessionClient", "IsSessionClientThickOrdinary",
                    LocalizationManager.T("Main.SessionThickOrdinaryTooltip")),
                SessionOption(LocalizationManager.T("Main.SessionClientThin"), "SessionClient", "IsSessionClientThin"),
                SessionGroupLabel(LocalizationManager.T("Main.Bitness")),
                SessionOption(LocalizationManager.T("Main.SessionClientAuto"), "SessionArch", "IsSessionArchAuto"),
                SessionOption("32", "SessionArch", "IsSessionArch32"),
                SessionOption("64", "SessionArch", "IsSessionArch64"));

            card.Bind(Control.IsVisibleProperty, new Binding("ShowSessionLaunchPanel"));
            return card;
        }

        /// <summary>Подпись группы переключателей в блоке текущей сессии.</summary>
        private Control SessionGroupLabel(string text)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.ScaledFont(11),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 2)
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return block;
        }

        /// <summary>Переключатель в блоке текущей сессии: одна из взаимоисключающих опций.</summary>
        private static Control SessionOption(string text, string group, string propertyPath, string? tooltip = null)
        {
            var option = new RadioButton
            {
                Content = text,
                GroupName = group,
                FontSize = UiMetrics.ScaledFont(12),
                Margin = new Thickness(0, 1)
            };
            option.Bind(RadioButton.IsCheckedProperty, new Binding(propertyPath) { Mode = BindingMode.TwoWay });
            if (tooltip is not null)
                ToolTip.SetTip(option, tooltip);
            return option;
        }

        /// <summary>Ключ значка в геометрию из Icons.axaml для привязок заголовка.</summary>
        private static readonly Avalonia.Data.Converters.FuncValueConverter<string?, Geometry?> IconKeyConverter =
            new(key => string.IsNullOrEmpty(key) ? null : IconHelper.Geometry(key));

        /// <summary>
        /// Карточка-секция: лёгкий фон/граница из темы + компактный заголовок с иконкой.
        /// Без тяжёлой тени — правая панель выглядит современнее и занимает меньше места.
        /// </summary>
        /// <summary>
        /// Подпись секции правой панели: у автора она стоит снаружи рамки,
        /// малыми капителями и вторичным цветом, без значка.
        /// </summary>
        private static Control SectionLabel(string text, bool smallCaps = true)
        {
            // У автора подпись набрана малыми капителями (Typography.Capitals).
            // В Avalonia 11.3 такого свойства у TextBlock нет, поэтому приближаем:
            // прописные буквы кеглем помельче дают тот же рисунок строки.
            var block = new TextBlock
            {
                Text = smallCaps ? text.ToUpperInvariant() : text,
                FontSize = UiMetrics.ScaledFont(smallCaps ? 10.5 : 12),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, UiMetrics.ActionGridGap, 0, smallCaps ? 8 : 4)
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            return block;
        }

        /// <summary>Рамка секции без собственного заголовка: подпись живёт снаружи.</summary>
        private static Control PlainCard(params Control[] children)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusLg),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(UiMetrics.SectionPad),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");
            UiMetrics.AddBrushTransition(card);
            var content = new StackPanel { Spacing = UiMetrics.Gap };
            foreach (var child in children)
                content.Children.Add(child);
            card.Child = content;
            return card;
        }

        private static Control SectionCard(string title, string iconKey, params Control[] children)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(UiMetrics.RadiusLg),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(UiMetrics.SectionPad),
                Margin = new Thickness(0, UiMetrics.ActionGridGap, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderColorBrush");
            UiMetrics.AddBrushTransition(card);

            var content = new StackPanel { Spacing = UiMetrics.Gap };

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 2)
            };
            header.Children.Add(IconHelper.MakeIcon(iconKey, 14, "TextSecondaryBrush"));
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = UiMetrics.ScaledFont(12),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(titleBlock, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            header.Children.Add(titleBlock);
            content.Children.Add(header);

            foreach (var child in children)
                content.Children.Add(child);

            card.Child = content;
            return card;
        }

        /// <summary>Крупная primary-кнопка на акцентном фоне с контрастным текстом/иконкой.</summary>
        private static Control PrimaryActionButton(string iconKey, string text, string commandPath, string tooltip)
        {
            var btn = new PanelButton("AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush")
            {
                Content = ThemedIconAndText(iconKey, text, "TextOnAccentBrush", UiMetrics.ScaledFont(18), centered: true),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, UiMetrics.SectionMarginBottom),
                Padding = new Thickness(UiMetrics.ButtonPadH, UiMetrics.ButtonPadV)
            };
            // В Avalonia подсказка это присоединённое свойство, а не свойство контрола.
            ToolTip.SetTip(btn, tooltip);
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        /// <summary>Secondary-кнопка с приглушённым фоном и hover/pressed из ресурсов темы.</summary>
        private static Control SecondaryActionButton(string iconKey, string text, string commandPath, string tooltip)
        {
            var btn = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(UiMetrics.RadiusMd))
            {
                Content = ThemedIconAndText(iconKey, text, "ButtonTextBrush", UiMetrics.ActionIconSize, centered: false),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = UiMetrics.ActionButtonMinHeight,
                Padding = new Thickness(UiMetrics.ActionButtonPadH, UiMetrics.ActionButtonPadV),
                Margin = new Thickness(0, 0, 0, UiMetrics.ActionGridGap / 2)
            };
            ToolTip.SetTip(btn, tooltip);
            btn.Bind(Button.CommandProperty, new Binding(commandPath));
            return btn;
        }

        /// <summary>
        /// Split-кнопка «Очистка кеша» по аналогии с кнопкой запуска 1С:Предприятие:
        /// основная часть открывает окно очистки кеша (<see cref="CacheCleanWindow"/>)
        /// (с выделенной базой или без неё, если выбрана группа), а правая стрелка «▾»
        /// открывает выпадающее меню с выбором типа кеша и полным окном очистки.
        /// Доступна даже при выбранной группе.
        /// </summary>
        private static Control BuildClearCacheSplitButton()
        {
            var radius = UiMetrics.RadiusLg;

            // Основная часть: открывает окно очистки кеша.
            var main = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(radius, 0, 0, radius))
            {
                Content = ThemedIconAndText("IconDelete", LocalizationManager.T("Main.ClearCache"), "ButtonTextBrush",
                    UiMetrics.ActionIconSize, centered: false),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = UiMetrics.ActionButtonMinHeight,
                Padding = new Thickness(UiMetrics.ActionButtonPadH, UiMetrics.ActionButtonPadV),
                Margin = new Thickness(0)
            };
            ToolTip.SetTip(main, LocalizationManager.T("Main.ClearCacheTooltip"));
            main.Bind(Button.CommandProperty, new Binding("ClearCacheCommand"));

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

            menu.Items.Add(new Separator());

            var both = new MenuItem { Header = LocalizationManager.T("Main.ClearCacheBoth") };
            both.Bind(MenuItem.CommandProperty, new Binding("ClearCacheBothCommand"));
            menu.Items.Add(both);

            var arrow = new PanelButton(
                "SecondaryButtonBackgroundBrush",
                "SecondaryButtonHoverBrush",
                "SecondaryButtonPressedBrush",
                "BorderColorBrush",
                new CornerRadius(0, radius, radius, 0))
            {
                Width = 32,
                MinHeight = UiMetrics.ActionButtonMinHeight,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            var arrowGlyph = new TextBlock
            {
                Text = "▾",
                FontSize = UiMetrics.ScaledFont(12),
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

        /// <summary>
        /// Кнопка запуска со стрелкой и меню дополнительных вариантов, как
        /// в WPF-версии. Пункт «от имени администратора» не переносится:
        /// на Linux нет повышения прав через оболочку, параметр runAsAdmin
        /// в лаунчере не используется, а запуск клиента 1С от root оставил бы
        /// в домашнем каталоге пользователя файлы, которые ему не принадлежат.
        /// </summary>
        private static Control BuildLaunchSplitButton(
            string iconKey,
            string text,
            string commandPath,
            string tooltip,
            bool primary,
            IReadOnlyList<(string Header, string Command)> menuItems)
        {
            var radius = UiMetrics.RadiusLg;
            var mainCorner = new CornerRadius(radius, 0, 0, radius);

            var main = primary
                ? new PanelButton("AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush", mainCorner)
                : new PanelButton("SecondaryButtonBackgroundBrush", "SecondaryButtonHoverBrush",
                    "SecondaryButtonPressedBrush", "BorderColorBrush", mainCorner);

            var contentBrush = primary ? "TextOnAccentBrush" : "ButtonTextBrush";
            main.Content = ThemedIconAndText(iconKey, text, contentBrush,
                primary ? UiMetrics.ScaledFont(16) : UiMetrics.ActionIconSize, centered: primary);
            main.HorizontalContentAlignment = primary ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            main.HorizontalAlignment = HorizontalAlignment.Stretch;
            main.MinHeight = UiMetrics.ActionButtonMinHeight + (primary ? 4 : 0);
            main.Padding = new Thickness(UiMetrics.ActionButtonPadH, UiMetrics.ActionButtonPadV + (primary ? 2 : 0));
            main.Margin = new Thickness(0);
            ToolTip.SetTip(main, tooltip);
            main.Bind(Button.CommandProperty, new Binding(commandPath));

            var menu = new ContextMenu();
            foreach (var (header, command) in menuItems)
            {
                if (header.Length == 0)
                {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var item = new MenuItem { Header = header };
                item.Bind(MenuItem.CommandProperty, new Binding(command));
                menu.Items.Add(item);
            }

            var arrowCorner = new CornerRadius(0, radius, radius, 0);
            var arrow = primary
                ? new PanelButton("AccentBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentBrush", arrowCorner)
                : new PanelButton("SecondaryButtonBackgroundBrush", "SecondaryButtonHoverBrush",
                    "SecondaryButtonPressedBrush", "BorderColorBrush", arrowCorner);
            arrow.Width = 32;
            arrow.MinHeight = main.MinHeight;
            arrow.Padding = new Thickness(0);
            arrow.Margin = new Thickness(0);

            var arrowGlyph = new TextBlock
            {
                Text = "▾",
                FontSize = UiMetrics.ScaledFont(12),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ThemeBrushes.Bind(arrowGlyph, TextBlock.ForegroundProperty, contentBrush);
            arrow.Content = arrowGlyph;
            ToolTip.SetTip(arrow, LocalizationManager.T("Main.MoreLaunchOptions"));
            arrow.ContextMenu = menu;
            arrow.Click += (_, _) => menu.Open(arrow);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
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
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (centered)
                sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(IconHelper.MakeIcon(iconKey, iconSize, brushKey));
            var tb = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.ActionFontSize + (centered ? 0.5 : 0),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, brushKey);
            sp.Children.Add(tb);
            return sp;
        }

        private Control DetailRow(string label, Binding binding)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            // 100 точек не хватало самой длинной подписи («Последний запуск»),
            // она упиралась в значение. У автора колонка шире.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = UiMetrics.ScaledFont(12)
            };
            ThemeBrushes.Bind(labelBlock, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            grid.Children.Add(labelBlock);
            Grid.SetColumn(labelBlock, 0);

            // Значение полужирное, как в разметке WPF: подпись вторичная, значение основное.
            var valueBlock = new TextBlock
            {
                FontSize = UiMetrics.ScaledFont(12),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
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
                // Подписка снимается вместе с уходом кнопки из дерева: список
                // _subs не освобождался нигде, и каждая пересборка правой панели
                // оставляла кнопку и всё её дерево укоренёнными.
                => ThemeBrushes.Observe(this, key, brush => { setter(brush); ApplyState(); });

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
                BorderThickness = new Thickness(2);
                BorderBrush = Brushes.Transparent;

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
                        // Без этого фон измеряется ровно по содержимому и обрезает текст.
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
                // Подписка снимается вместе с уходом кнопки из дерева: список
                // освобождался только у кнопок тегов, поэтому пять сегментов
                // верхней панели держали прежнее дерево окна после каждой
                // пересборки содержимого.
                => ThemeBrushes.Observe(this, key, brush => { setter(brush); ApplyState(); });

            /// <summary>Собирает содержимое «иконка + текст» с цветом по состоянию выбора.</summary>
            private void UpdateContent()
            {
                // Невыбранное состояние — приглушённый текст (как в WPF SegmentRadioButton),
                // выбранное — контрастный текст на акцентной заливке.
                var brushKey = IsChecked == true ? "TextOnAccentBrush" : "TextSecondaryBrush";
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                // Пустой ключ означает кнопку без иконки: IconHelper на пустой ключ
                // подставляет запасную папку, и она выглядела бы как настоящая иконка.
                if (!string.IsNullOrEmpty(_iconKey))
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

                // Толщина постоянна, меняется только цвет: иначе фокус
                // расширял бы кнопку на четыре пикселя, а по её правому краю
                // выравнивается подпись «Название» в шапке списка.
                BorderBrush = _focused ? _accent : Brushes.Transparent;
            }
        }

        /// <summary>Описание колонки списка баз: ключ, заголовок, ширина.</summary>
        private readonly record struct ListColumn(string Key, string Header, double Width);

        /// <summary>Минимум под имя базы: колонка звёздная, но схлопываться ей нельзя.</summary>
        private const double NameColumnMinWidth = 220;

        /// <summary>Ширина колонки звезды «избранное» в заголовке и в строке базы.</summary>
        private static double FavoriteColumnWidth => UiMetrics.Scaled(26);

        /// <summary>Ширина колонки булавки «закреплено» в заголовке и в строке базы.</summary>
        private static double PinColumnWidth => UiMetrics.Scaled(24);

        /// <summary>Ширина фиксированной колонки «Действия» в заголовке и в строке базы.</summary>
        private static double ActionsColumnWidth => UiMetrics.Scaled(170);

        /// <summary>
        /// Ширина колонки иконки базы: сама иконка и её правый отступ. В заголовке
        /// эта колонка пустая, но она есть, иначе подпись «Название» стояла бы
        /// левее имён строк на ширину иконки.
        /// </summary>
        private static double IconColumnWidth => UiMetrics.RowIconBox + 10;

        /// <summary>Ширина одной кнопки панели инструментов над списком.</summary>
        private static double ToolbarButtonWidth => UiMetrics.Scaled(24);

        /// <summary>Ширина блока кнопок групп: четыре кнопки с промежутками.</summary>
        private static double GroupToolbarWidth => ToolbarButtonWidth * 4 + 6 + UiMetrics.Scaled(6);

        /// <summary>
        /// Расчётная ширина переключателя тегов: он сделан сегментной кнопкой
        /// с горизонтальным отступом 12, рамкой 2 и иконкой 15, то есть
        /// заметно шире обычной кнопки панели.
        /// </summary>
        private const double TagsToggleWidth = 12 * 2 + 2 * 2 + 15;

        /// <summary>Зазор между блоком кнопок и подписью «Название».</summary>
        private const double HeaderToolbarGap = 2;

        /// <summary>
        /// Номер колонки заголовка с именем базы: компенсатор отступа дерева,
        /// звезда, булавка, иконка.
        /// </summary>
        private const int NameHeaderColumn = 4;

        /// <summary>Номер колонки заголовка с пометкой закрепления.</summary>
        private const int PinHeaderColumn = NameHeaderColumn - 2;

        /// <summary>Номер колонки строки с именем базы: звезда, булавка, иконка.</summary>
        private const int NameRowColumn = 3;

        /// <summary>Минимальная ширина колонки при перетаскивании разделителя.</summary>
        private const double MinColumnWidth = 40;

        /// <summary>Ширина зоны захвата разделителя колонок.</summary>
        private const double ResizeGripWidth = 8;

        /// <summary>
        /// Ширина колонки имени: пока её не тянули за разделитель, колонка
        /// звёздная и занимает остаток, после перетаскивания становится заданной.
        /// </summary>
        private GridLength NameColumnLength()
        {
            var width = _vm?.NameColumnWidth ?? 0;
            return width > 0 ? new GridLength(width) : new GridLength(1, GridUnitType.Star);
        }

        /// <summary>
        /// Колонки списка в порядке отображения, кроме первой (имя базы),
        /// которая занимает оставшееся место. Состав и ширины берутся
        /// из настроек, поэтому заголовок и строки всегда согласованы.
        /// </summary>
        private List<ListColumn> ListColumns()
        {
            var columns = new List<ListColumn>();
            if (_vm is null)
                return columns;

            // Ширина из настроек, а при нуле (настройка ещё не трогалась) свой
            // разумный размер под содержимое колонки.
            void Add(bool visible, string key, string header, double width, double fallback)
            {
                if (visible)
                    columns.Add(new ListColumn(key, LocalizationManager.T(header), width > 0 ? width : fallback));
            }

            // Порядок колонок берётся из настроек; неизвестные ключи пропускаются,
            // поэтому пользовательский список не ломает сборку при изменении состава.
            foreach (var key in _vm.ColumnOrderKeys)
            {
                switch (key)
                {
                    case "Version":
                        Add(_vm.ShowVersionColumn, "Version", "Column.Version", _vm.VersionColumnWidth, 95);
                        break;
                    case "Configuration":
                        Add(_vm.ShowConfigurationColumn, "Configuration", "Column.Configuration", _vm.ConfigurationColumnWidth, 140);
                        break;
                    case "LaunchMode":
                        Add(_vm.ShowLaunchModeColumn, "LaunchMode", "Column.LaunchMode", _vm.LaunchModeColumnWidth, 115);
                        break;
                    case "ServerBase":
                        Add(_vm.ShowServerColumn, "ServerBase", "Column.ServerBase", _vm.ServerColumnWidth, 140);
                        break;
                    case "LastLaunch":
                        Add(_vm.ShowLastLaunchColumn, "LastLaunch", "Column.LastLaunch", _vm.LastLaunchColumnWidth, 115);
                        break;
                    case "Size":
                        Add(_vm.ShowSizeColumn, "Size", "Column.Size", _vm.SizeColumnWidth, 65);
                        break;
                }
            }
            return columns;
        }

        /// <summary>
        /// Сколько колонок данных стоит до колонки «Действия»: она встаёт сразу
        /// после колонки «Режим запуска» (или в самый конец, если та скрыта или
        /// отсутствует в текущем порядке).
        /// </summary>
        private static int ActionsOffsetInColumns(List<ListColumn> columns)
        {
            for (var i = 0; i < columns.Count; i++)
                if (columns[i].Key == "LaunchMode")
                    return i + 1;
            return columns.Count;
        }

        /// <summary>Значение колонки для конкретной базы.</summary>
        private static string ColumnValue(Infobase ib, string key) => key switch
        {
            "Version" => ib.PlatformVersion ?? string.Empty,
            "Configuration" => ib.ConfigurationDisplay ?? string.Empty,
            "LaunchMode" => ib.LaunchMode ?? string.Empty,
            "ServerBase" => ib.Connection.Type == ConnectionType.WebServer
                ? (ib.Connection.WebUrl ?? string.Empty)
                : (ib.ServerDatabaseDisplay ?? string.Empty),
            "LastLaunch" => ib.LastLaunchDisplay ?? string.Empty,
            "Size" => ib.FileSizeDisplay ?? string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Строка заголовков колонок над списком. Пересобирается вместе
        /// со списком, чтобы состав колонок совпадал со строками.
        /// </summary>
        private Control BuildColumnHeader()
        {
            _columnHeaderRow = new Grid();
            _columnHeader = new Border
            {
                // Имя как у шапки списка в разметке WPF: по нему ThemeManager
                // применяет шрифт области «Шапка списка».
                Name = "HeaderGrid",
                // Отступы совпадают с карточкой строки: колонки в обеих сетках
                // прижаты вправо, поэтому заголовки встают над значениями только
                // при одинаковом правом отступе.
                Padding = new Thickness(UiMetrics.PaddingControl, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = _columnHeaderRow
            };
            ThemeBrushes.Bind(_columnHeader, Border.BorderBrushProperty, "BorderColorBrush");
            return _columnHeader;
        }

        /// <summary>
        /// Ставит пересборку заголовка в очередь диспетчера. Настройки колонок
        /// уведомляют о шестнадцати свойствах подряд, и без склейки заголовок
        /// пересобирался бы на каждое из них.
        /// </summary>
        /// <summary>
        /// Подсказка, описывающая колонку списка. Есть не у всех: набор взят
        /// из разметки WPF, где такие подсказки стоят только у части заголовков.
        /// </summary>
        private static string? ColumnHeaderTooltipKey(string columnKey) => columnKey switch
        {
            "Size" => "Main.ColumnSizeTooltip",
            "Configuration" => "Main.ColumnNameTooltip",
            _ => null
        };

        private void QueueColumnHeaderRefresh()
        {
            if (_columnHeaderRefreshQueued)
                return;
            _columnHeaderRefreshQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _columnHeaderRefreshQueued = false;
                RefreshColumnHeader();
            });
        }

        /// <summary>
        /// Пересобирает панель инструментов и заголовки колонок по текущим настройкам.
        /// Слева направо: кнопки групп, компенсатор отступа дерева, звезда, булавка,
        /// имя базы, дальше колонки значений. Первые колонки повторяются в строке
        /// базы теми же ширинами, поэтому заголовок и значения стоят друг под другом.
        /// </summary>
        private void RefreshColumnHeader()
        {
            if (_vm is null || _columnHeaderRow is null || _columnHeader is null)
                return;

            _columnHeaderRow.Children.Clear();
            _columnHeaderRow.ColumnDefinitions.Clear();

            var columns = ListColumns();
            // Ширина блока кнопок нужна, чтобы подпись «Название» встала правее
            // него. Здесь она только расчётная: панель ещё не создана, а Bounds
            // прежней панели относятся к прежнему составу кнопок. Измеренную
            // ширину подставляет AlignHeaderToRows, когда панель разложена.
            _headerToolbarWidth = (_vm.ShowExpandCollapseButtons ? GroupToolbarWidth : 0)
                + TagsToggleWidth + HeaderToolbarGap;
            var favoriteWidth = _vm.ShowFavoritesButton ? FavoriteColumnWidth : 0;
            var pinWidth = _vm.ShowPinnedButton ? PinColumnWidth : 0;

            _headerOffsetColumn = new ColumnDefinition { Width = new GridLength(0) };
            _columnHeaderRow.ColumnDefinitions.Add(_headerOffsetColumn);
            _columnHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(favoriteWidth) });
            _columnHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pinWidth) });
            _columnHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumnWidth) });
            _columnHeaderRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = NameColumnLength(), MinWidth = MinColumnWidth });
            // Колонка «Действия» встаёт сразу после колонки «Режим запуска», поэтому
            // её определение встраивается в последовательность, а не добавляется в конец.
            var actionsOffset = ActionsOffsetInColumns(columns);
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                    _columnHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionsColumnWidth) });
                _columnHeaderRow.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(columns[i].Width), MinWidth = MinColumnWidth });
            }
            // «Режим запуска» скрыт или стоит последним — действия уходят в самый конец.
            if (actionsOffset >= columns.Count)
                _columnHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ActionsColumnWidth) });

            _headerPinMark = null;
            if (_vm.ShowPinnedButton)
            {
                // Значок закрепления в заголовке — только пометка колонки, как в WPF.
                var pinMark = IconHelper.MakeIcon("IconPin", UiMetrics.Scaled(13), "TextSecondaryBrush");
                ToolTip.SetTip(pinMark, LocalizationManager.T("Main.Pinned"));
                _columnHeaderRow.Children.Add(pinMark);
                Grid.SetColumn(pinMark, PinHeaderColumn);
                _headerPinMark = pinMark;
            }

            // Кнопки лежат поверх компенсатора и пустых колонок звезды,
            // булавки и иконки: своя колонка сдвинула бы подписи вправо
            // от значений, а колонки заголовка тут ничего не показывают.
            var tools = BuildGroupToolbar();
            _columnHeaderRow.Children.Add(tools);
            Grid.SetColumn(tools, 0);
            Grid.SetColumnSpan(tools, NameHeaderColumn);
            tools.ZIndex = 1;

            var nameHeader = ColumnHeader(LocalizationManager.T("Column.Name"), IconHelper.ColumnIconKey("Name"));
            // У «Названия» отступ слева нулевой: заголовок равняется по тексту строк
            // списка, а не по границе колонки. В разметке WPF так же (MainWindow.xaml:627).
            nameHeader.Margin = new Thickness(0, 0, 8, 4);
            MakeSortableHeader(nameHeader, "Name", LocalizationManager.T("Main.ColumnNameSortTooltip"));
            _columnHeaderRow.Children.Add(nameHeader);
            Grid.SetColumn(nameHeader, NameHeaderColumn);

            _headerColumnIndex.Clear();
            _headerColumnIndex["Name"] = NameHeaderColumn;

            var nameGrip = BuildResizeGrip("Name", NameHeaderColumn);
            _columnHeaderRow.Children.Add(nameGrip);

            // Заголовки идут по порядку колонок данных, перескакивая колонку
            // «Действия», встроенную после «Режима запуска».
            var dataColumn = NameHeaderColumn + 1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (i == actionsOffset)
                    dataColumn++;
                _headerColumnIndex[columns[i].Key] = dataColumn;

                var text = ColumnHeader(columns[i].Header, IconHelper.ColumnIconKey(columns[i].Key));
                if (columns[i].Key == "LastLaunch")
                    MakeSortableHeader(text, "LastLaunchDate", LocalizationManager.T("Main.ColumnLastLaunchSortTooltip"));
                // Подсказка, описывающая саму колонку. В разметке WPF она есть
                // не у всех заголовков, набор взят оттуда (MainWindow.xaml:687, 697).
                if (ColumnHeaderTooltipKey(columns[i].Key) is { } tooltipKey)
                    ToolTip.SetTip(text, LocalizationManager.T(tooltipKey));
                _columnHeaderRow.Children.Add(text);
                Grid.SetColumn(text, dataColumn);

                var grip = BuildResizeGrip(columns[i].Key, dataColumn);
                _columnHeaderRow.Children.Add(grip);
                dataColumn++;
            }

            // Подпись колонки «Действия» — сразу после колонки «Режим запуска»,
            // без разделителя.
            var actionsHeader = ColumnHeader(LocalizationManager.T("Column.Actions"), IconHelper.ColumnIconKey("Actions"));
            ToolTip.SetTip(actionsHeader, LocalizationManager.T("Main.Actions"));
            _columnHeaderRow.Children.Add(actionsHeader);
            Grid.SetColumn(actionsHeader, NameHeaderColumn + 1 + actionsOffset);

            UpdateListMinWidth();

            QueueHeaderAlign();
        }

        /// <summary>
        /// Измеренная ширина блока кнопок; null, пока он не разложен.
        /// Панель зажата в узкие колонки заголовка, поэтому её собственный
        /// Bounds обрезан по ним, а кнопки рисуются дальше: ширину берём
        /// по самому правому краю содержимого.
        /// </summary>
        private double? MeasuredToolbarWidth
        {
            get
            {
                if (_groupToolbar is null)
                    return null;
                var right = 0d;
                foreach (var child in _groupToolbar.Children)
                    right = Math.Max(right, child.Bounds.Right);
                return right > 0 ? right : null;
            }
        }

        /// <summary>
        /// Блок кнопок над списком: развернуть и свернуть все группы и две
        /// сортировки групп (только при группировке), а также переключатель
        /// тегов в строках, который нужен всегда.
        /// </summary>
        private Control BuildGroupToolbar()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (_vm?.ShowExpandCollapseButtons == true)
            {
                panel.Children.Add(HeaderIconButton("IconExpandAll",
                    LocalizationManager.T("Main.ExpandAllGroups"), "ExpandAllGroupsCommand"));
                panel.Children.Add(HeaderIconButton("IconCollapseAll",
                    LocalizationManager.T("Main.CollapseAllGroups"), "CollapseAllGroupsCommand"));
                panel.Children.Add(HeaderIconButton("IconSortAscending",
                    LocalizationManager.T("Main.SortGroupsAscending"), "SortGroupsAscendingCommand"));
                panel.Children.Add(HeaderIconButton("IconSortDescending",
                    LocalizationManager.T("Main.SortGroupsDescending"), "SortGroupsDescendingCommand"));
            }

            var tagsToggle = BuildTagsInListToggle();
            panel.Children.Add(tagsToggle);

            _groupToolbar = panel;
            // Ширина известна только после раскладки, а подпись выравнивается
            // по ней. Следим за переключателем: он всегда крайний справа,
            // поэтому именно он задаёт правый край блока. Bounds самой панели
            // обрезаны колонками заголовка и на измерение не влияют.
            _toolbarWidthLink?.Dispose();
            _toolbarWidthLink = tagsToggle.GetObservable(Visual.BoundsProperty)
                .Subscribe(new PropertyObserver<Rect>(_ => QueueHeaderAlign()));
            return panel;
        }

        /// <summary>
        /// Переключатель показа тегов в строках списка. Сделан тем же
        /// сегментным контролом, что и переключатели верхней панели: у Fluent
        /// в нажатом состоянии свой синий фон, чужой для этой темы.
        /// </summary>
        private Control BuildTagsInListToggle()
        {
            var toggle = MakeSegmentToggle("IconTag", LocalizationManager.T("Main.ToggleListTags"));
            toggle.IsChecked = _vm?.ShowTags ?? false;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Click += (_, _) =>
            {
                if (_vm is not null)
                    _vm.ShowTags = toggle.IsChecked == true;
            };
            return toggle;
        }

        /// <summary>Компактная иконко-кнопка панели инструментов над списком.</summary>
        private Button HeaderIconButton(string iconKey, string tooltip, string commandPath)
        {
            var button = new Button
            {
                Content = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(14), "TextSecondaryBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 0),
                MinWidth = 0,
                MinHeight = 0,
                Width = ToolbarButtonWidth,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(button, tooltip);
            button.Bind(Button.CommandProperty, new Binding(commandPath));
            return button;
        }

        /// <summary>
        /// Зона захвата у правого края колонки заголовка: тонкая линия по центру
        /// и широкая невидимая полоса вокруг неё, иначе в разделитель трудно попасть.
        /// </summary>
        private Border BuildResizeGrip(string key, int column)
        {
            var line = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4),
                Opacity = 0.55
            };
            ThemeBrushes.Bind(line, Border.BackgroundProperty, "BorderColorBrush");

            var grip = new Border
            {
                Width = ResizeGripWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ZIndex = 2,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                Tag = key,
                Child = line
            };
            ToolTip.SetTip(grip, LocalizationManager.T("Main.ResizeColumnTooltip"));
            Grid.SetColumn(grip, column);
            grip.PointerPressed += OnColumnResizePressed;
            grip.PointerMoved += OnColumnResizeMoved;
            grip.PointerReleased += OnColumnResizeReleased;
            // Захват теряется не только отпусканием кнопки: его снимает и оконная
            // система, и пересборка окна в компактном режиме. Без этого обработчика
            // перетаскивание осталось бы незавершённым, а ширина несохранённой.
            grip.PointerCaptureLost += OnColumnResizeCaptureLost;
            return grip;
        }

        /// <summary>Формат содержимого перетаскивания: сама нагрузка живёт в поле окна.</summary>
        private const string DragPayloadMarker = "ConfigurationManagement.Row";

        /// <summary>
        /// Фиксация того, что поедет: как в WPF, нагрузка берётся в нажатии,
        /// а не в движении. Иначе при сдвиге курсора на дочернюю строку под ним
        /// оказывается другой узел и вместо группы уезжает база.
        /// </summary>
        private void OnTreeDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _dragPayload = null;
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _dragStartPoint = point.Position;

            // Клик по кнопке или полю ввода внутри строки перетаскивание не начинает:
            // звезда, булавка, чип тега и раскрытие группы должны работать как обычно.
            if (e.Source is not Visual source
                || source.FindAncestorOfType<Button>(includeSelf: true) is not null
                || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
                return;

            var item = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            _dragPayload = item?.DataContext switch
            {
                Infobase infobase => infobase,
                GroupNodeViewModel node when node.Group is not null => node,
                _ => null
            };
        }

        private async void OnTreeDragPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging || _dragPayload is null)
                return;

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _dragPayload = null;
                return;
            }

            // Порог сдвига обязателен: без него обычный клик по строке начинал бы
            // перетаскивание. Аналога SystemParameters в Avalonia нет.
            if (Math.Abs(point.Position.X - _dragStartPoint.X) < DragThreshold
                && Math.Abs(point.Position.Y - _dragStartPoint.Y) < DragThreshold)
                return;

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(DragPayloadMarker));

            _isDragging = true;
            try
            {
                await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                _vm.LogWarning($"Перетаскивание прервано: {ex.Message}");
            }
            finally
            {
                _isDragging = false;
                _dragPayload = null;
            }
        }

        /// <summary>Порог сдвига в пикселях, после которого нажатие считается перетаскиванием.</summary>
        private const double DragThreshold = 4;

        private void OnTreeDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.None;
            ResolveDropTarget(e.Source as Visual, out var targetNode, out _);
            if (targetNode is not null && IsDropAllowed(_dragPayload, targetNode))
                e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnTreeDrop(object? sender, DragEventArgs e)
        {
            e.Handled = true;

            // Отпускание поднимается из насоса сырых событий, а не со стека
            // DoDragDropAsync: исключение отсюда не дало бы завершиться самой
            // операции, и перетаскивание осталось бы включённым навсегда,
            // вместе с курсором и перехватом движений по всему приложению.
            try
            {
                ApplyDrop(e);
            }
            catch (Exception ex)
            {
                _vm.LogWarning($"Перенос не выполнен: {ex.Message}");
            }
        }

        private void ApplyDrop(DragEventArgs e)
        {
            var payload = _dragPayload;
            ResolveDropTarget(e.Source as Visual, out var targetNode, out var insertBefore);

            // Проверки повторяются здесь намеренно: в Avalonia отпускание приходит
            // на последнюю цель независимо от того, что вернул DragOver, поэтому
            // на отказ DragOver полагаться нельзя.
            if (targetNode is null || !IsDropAllowed(payload, targetNode))
                return;

            if (payload is GroupNodeViewModel sourceNode && sourceNode.Group is not null)
            {
                _vm.MoveGroupUnder(sourceNode.Group, targetNode.Group?.Id ?? string.Empty);
                return;
            }

            if (payload is not Infobase infobase)
                return;

            if (ReferenceEquals(insertBefore, infobase))
                insertBefore = null;

            // Сброс на «Закреплённые» группу не меняет: там лежат базы из разных групп,
            // и перенос туда означал бы потерю группы.
            if (string.Equals(targetNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
            {
                _vm.MoveInfobaseToGroup(infobase, infobase.Group ?? string.Empty, insertBefore);
                return;
            }

            var path = targetNode.Group is null
                ? string.Empty
                : GroupHierarchyHelper.GetFullPath(targetNode.Group, _vm.Groups);
            _vm.MoveInfobaseToGroup(infobase, path, insertBefore);
        }

        /// <summary>
        /// Допустимость сброса. Узлы без группы разрешены не все: «Все базы» и узел
        /// результата поиска группой не являются, и сброс на них молча обнулил бы
        /// группу базы. В WPF эта ловушка есть, здесь она закрыта.
        /// </summary>
        private bool IsDropAllowed(object? payload, GroupNodeViewModel targetNode)
        {
            if (payload is Infobase)
            {
                return targetNode.Group is not null
                    || string.Equals(targetNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal)
                    || string.Equals(targetNode.Marker, GroupNodeViewModel.NoGroupMarker, StringComparison.Ordinal);
            }

            if (payload is not GroupNodeViewModel sourceNode || sourceNode.Group is null)
                return false;

            // Узел без группы означает для группы корень, но не любой: «Все базы»,
            // результат поиска и «Закреплённые» группой не являются, и сброс на них
            // молча вынес бы подгруппу наверх. Вернуть её на место в интерфейсе
            // нечем: смена родителя есть только у перетаскивания.
            if (targetNode.Group is null
                && !string.Equals(targetNode.Marker, GroupNodeViewModel.NoGroupMarker, StringComparison.Ordinal))
                return false;

            var targetId = targetNode.Group?.Id ?? string.Empty;
            if (string.Equals(sourceNode.Group.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return false;

            // Перенос под собственного потомка создал бы цикл в иерархии.
            return string.IsNullOrEmpty(targetId)
                   || !GroupHierarchyHelper.IsAncestorOrSelf(targetId, sourceNode.Group.Id, _vm.Groups);
        }

        /// <summary>
        /// Цель сброса: группа и база, перед которой вставить. Курсор над строкой
        /// базы означает вставку перед ней в её же группу.
        /// </summary>
        private static void ResolveDropTarget(Visual? source, out GroupNodeViewModel? targetNode, out Infobase? insertBefore)
        {
            targetNode = null;
            insertBefore = null;
            if (source is null)
                return;

            var item = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            while (item is not null)
            {
                switch (item.DataContext)
                {
                    case GroupNodeViewModel node:
                        targetNode = node;
                        return;
                    case Infobase infobase when insertBefore is null:
                        insertBefore = infobase;
                        break;
                }
                item = item.FindAncestorOfType<TreeViewItem>();
            }
        }

        private void OnColumnResizePressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border grip || grip.Tag is not string key || _columnHeaderRow is null)
                return;
            if (_resizeKey is not null)
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            var column = Grid.GetColumn(grip);
            if (column < 0 || column >= _columnHeaderRow.ColumnDefinitions.Count)
                return;

            _resizeKey = key;
            _resizePointerId = e.Pointer.Id;
            _resizeStartWidth = _columnHeaderRow.ColumnDefinitions[column].ActualWidth;
            _resizeStartX = e.GetPosition(this).X;

            // Сетки строк собираются один раз на перетаскивание: во время него
            // дерево не пересобирается, а обход визуального дерева на каждое
            // движение указателя стоил бы дорого на списке в сотни баз.
            _resizeRowGrids.Clear();
            if (_tree is not null)
            {
                foreach (var card in _tree.GetVisualDescendants().OfType<InfobaseRowCard>())
                {
                    if (card.Child is Grid grid)
                        _resizeRowGrids.Add(grid);
                }
            }

            e.Pointer.Capture(grip);
            e.Handled = true;
        }

        private void OnColumnResizeMoved(object? sender, PointerEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;
            if (sender is not Border grip || !ReferenceEquals(e.Pointer.Captured, grip))
                return;

            var width = Math.Max(MinColumnWidth, _resizeStartWidth + e.GetPosition(this).X - _resizeStartX);
            ApplyColumnWidth(_resizeKey, width);
            _vm?.UpdateColumnWidth(_resizeKey, width, save: false);
        }

        private void OnColumnResizeReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;

            e.Pointer.Capture(null);
            FinishColumnResize();
            e.Handled = true;
        }

        private void OnColumnResizeCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_resizeKey is null || e.Pointer.Id != _resizePointerId)
                return;

            FinishColumnResize();
        }

        /// <summary>
        /// Завершает перетаскивание: пишет ширину в настройки один раз, а не
        /// на каждое движение указателя, и отпускает собранные сетки строк.
        /// </summary>
        private void FinishColumnResize()
        {
            if (_resizeKey is not null)
                _vm?.UpdateColumnWidth(_resizeKey, ColumnWidthOf(_resizeKey), save: true);

            _resizeKey = null;
            _resizeRowGrids.Clear();
        }

        /// <summary>Текущая ширина колонки заголовка по её ключу.</summary>
        private double ColumnWidthOf(string key)
        {
            var index = HeaderColumnIndex(key);
            return index >= 0 && _columnHeaderRow is not null && index < _columnHeaderRow.ColumnDefinitions.Count
                ? _columnHeaderRow.ColumnDefinitions[index].ActualWidth
                : 0;
        }

        /// <summary>Номер колонки заголовка по ключу колонки списка.</summary>
        private int HeaderColumnIndex(string key) =>
            _headerColumnIndex.TryGetValue(key, out var index) ? index : -1;

        /// <summary>
        /// Ведёт ширину колонки в двух сетках сразу: в заголовке и в каждой
        /// построенной строке. Пересборки дерева при этом не происходит, поэтому
        /// перетаскивание не мигает списком.
        /// </summary>
        private void ApplyColumnWidth(string key, double width)
        {
            var header = HeaderColumnIndex(key);
            if (header < 0 || _columnHeaderRow is null || header >= _columnHeaderRow.ColumnDefinitions.Count)
                return;

            _columnHeaderRow.ColumnDefinitions[header].Width = new GridLength(width);

            var row = header - (NameHeaderColumn - NameRowColumn);
            foreach (var grid in _resizeRowGrids)
            {
                if (row < 0 || row >= grid.ColumnDefinitions.Count)
                    continue;
                grid.ColumnDefinitions[row].Width = new GridLength(width);
            }

            // Минимум области считается заново: иначе после сужения колонки
            // прокручиваемая область осталась бы прежней ширины с пустотой справа.
            UpdateListMinWidth();
        }

        /// <summary>
        /// Минимальная ширина области списка: сумма колонок заголовка плюс отступы.
        /// При более узком окне включается горизонтальная прокрутка, и заголовок
        /// едет вместе со строками, а не разъезжается с ними.
        /// </summary>
        private void UpdateListMinWidth()
        {
            if (_listContent is null || _columnHeaderRow is null
                || _columnHeaderRow.ColumnDefinitions.Count <= NameHeaderColumn)
                return;

            var definitions = _columnHeaderRow.ColumnDefinitions;
            double lead = 0;
            for (var i = 1; i < NameHeaderColumn; i++)
                lead += definitions[i].Width.IsAbsolute ? definitions[i].Width.Value : 0;

            var nameWidth = definitions[NameHeaderColumn].Width.IsAbsolute
                ? definitions[NameHeaderColumn].Width.Value
                : NameColumnMinWidth;

            double values = 0;
            for (var i = NameHeaderColumn + 1; i < definitions.Count; i++)
                values += definitions[i].Width.IsAbsolute ? definitions[i].Width.Value : 0;

            _listContent.MinWidth = nameWidth + Math.Max(lead, _headerToolbarWidth)
                + UiMetrics.PaddingControl * 2 + values;
        }

        /// <summary>Делает заголовок колонки кликабельным: клик меняет поле сортировки.</summary>
        private void MakeSortableHeader(Control header, string field, string tooltip)
        {
            header.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(header, tooltip);
            header.Tapped += (_, _) => _vm?.SetSortField(field);
        }

        /// <summary>
        /// Ставит выравнивание заголовка со строками в очередь диспетчера:
        /// положение строки известно только после раскладки.
        /// </summary>
        private void QueueHeaderAlign()
        {
            if (_headerAlignQueued)
                return;
            _headerAlignQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _headerAlignQueued = false;
                AlignHeaderToRows();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Подгоняет ширину колонки-компенсатора так, чтобы звезда и булавка
        /// заголовка встали над теми же значками первой строки базы. Дерево
        /// сдвигает строки на отступ уровня, и без компенсации заголовок
        /// разошёлся бы со списком.
        /// </summary>
        private void AlignHeaderToRows()
        {
            if (_headerOffsetColumn is null || _columnHeaderRow is null || _tree is null
                || _columnHeaderRow.ColumnDefinitions.Count == 0)
                return;

            // Ориентир — самая левая из видимых строк: узлы разной вложенности
            // сдвинуты по-разному, и по первой встреченной заголовок уехал бы
            // вправо от большинства строк.
            double? left = null;
            foreach (var card in _tree.GetVisualDescendants().OfType<InfobaseRowCard>())
            {
                if (card.Child is not { } content)
                    continue;
                var origin = content.TranslatePoint(new Point(0, 0), _columnHeaderRow);
                if (origin is null)
                    continue;
                if (left is null || origin.Value.X < left.Value)
                    left = origin.Value.X;
            }
            // Строк может не быть вовсе: список пуст, всё отобрано фильтром
            // или группы свёрнуты. Выходить нельзя, иначе подпись «Название»
            // остаётся под блоком кнопок и он её перекрывает.

            // Пустые колонки звезды, булавки и иконки заголовка кнопки перекрывают,
            // а на подпись «Название» налезать не должны, отсюда нижняя граница.
            var lead = _columnHeaderRow.ColumnDefinitions[1].ActualWidth
                + _columnHeaderRow.ColumnDefinitions[2].ActualWidth
                + _columnHeaderRow.ColumnDefinitions[3].ActualWidth;
            // Измеренная ширина точнее расчётной, и она же нужна минимуму
            // области списка: там расчёт по константам оставил бы пустоту
            // или лишнюю прокрутку.
            if (MeasuredToolbarWidth is { } measured)
            {
                var target = measured + HeaderToolbarGap;
                if (Math.Abs(target - _headerToolbarWidth) > 0.5)
                {
                    _headerToolbarWidth = target;
                    UpdateListMinWidth();
                }
            }

            var offset = Math.Max(Math.Max(0, _headerToolbarWidth - lead), left ?? 0);
            if (Math.Abs(offset - _headerOffsetColumn.Width.Value) > 0.5)
                _headerOffsetColumn.Width = new GridLength(offset);

            // Пометка булавки прячется, когда её место занял блок кнопок.
            if (_headerPinMark is not null)
                _headerPinMark.IsVisible = offset + _columnHeaderRow.ColumnDefinitions[1].ActualWidth
                    >= _headerToolbarWidth;
        }

        /// <summary>
        /// Заголовок колонки: иконка колонки и подпись. Иконки заголовков совпадают
        /// с иконками списка колонок на вкладке «Отображение» — оба берут ключ из
        /// <see cref="IconHelper.ColumnIconKey"/>.
        /// </summary>
        private static Control ColumnHeader(string text, string iconKey)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                // Отступы как в разметке WPF (Margin="6,0,6,4"): без них подпись
                // встаёт вплотную к соседней, а горизонтальный StackPanel меряет
                // детей без ограничения по ширине, поэтому сам текст не подрезается.
                Margin = new Thickness(6, 0, 6, 4),
                ClipToBounds = true
            };
            panel.Children.Add(IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(13), "TextSecondaryBrush"));

            var block = new TextBlock
            {
                Text = text,
                FontSize = UiMetrics.ScaledFont(12),
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(block);
            return panel;
        }

        /// <summary>
        /// Панель отбора по тегам: по кнопке на каждый тег и кнопка сброса.
        /// Видимость подчинена переключателю «теги» в верхней панели, а состав
        /// пересобирается при каждом изменении набора тегов.
        /// </summary>
        private Control BuildTagFilterPanel()
        {
            _tagPanelItems = new WrapPanel { Orientation = Orientation.Horizontal };

            _tagClearButton = new Button
            {
                Content = ThemedIconAndText("IconClose", LocalizationManager.T("Main.ClearTagFilters"),
                    "ButtonTextBrush", UiMetrics.ScaledFont(12), centered: false),
                Padding = new Thickness(8, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _tagClearButton.Bind(Button.CommandProperty, new Binding("ClearTagFiltersCommand"));

            // Подсказка остаётся на месте и когда тегов нет: панель не прячется,
            // иначе переключатель «теги» выглядел бы неработающим. Раскладка как
            // в WPF-версии: подсказка и кнопка сброса сверху, чипы тегов под ними.
            var hint = ThemedIconAndText("IconTag", LocalizationManager.T("Main.TagFilterTitle"),
                "TextSecondaryBrush", UiMetrics.ScaledFont(12), centered: false);
            hint.HorizontalAlignment = HorizontalAlignment.Left;

            // Кнопка справки рядом с заголовком панели, как в разметке WPF.
            var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            hintRow.Children.Add(hint);
            hintRow.Children.Add(new Controls.HelpLink
            {
                HelpText = LocalizationManager.T("Main.TagFilterHelp"),
                VerticalAlignment = VerticalAlignment.Center
            });
            hintRow.HorizontalAlignment = HorizontalAlignment.Left;

            var header = new Grid();
            header.Children.Add(hintRow);
            header.Children.Add(_tagClearButton);

            var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
            rows.Children.Add(header);
            rows.Children.Add(_tagPanelItems);

            _tagPanel = new Border
            {
                Padding = new Thickness(UiMetrics.TopBarH, 6),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = rows
            };
            ThemeBrushes.Bind(_tagPanel, Border.BackgroundProperty, "CardBackgroundBrush");
            ThemeBrushes.Bind(_tagPanel, Border.BorderBrushProperty, "BorderColorBrush");
            return _tagPanel;
        }

        /// <summary>Пересобирает кнопки тегов и обновляет видимость панели.</summary>
        private void RefreshTagFilterPanel()
        {
            if (_vm is null || _tagPanelItems is null || _tagPanel is null || _tagClearButton is null)
                return;

            // Старые кнопки держат подписки на ресурсы темы, поэтому освобождаются
            // явно: очистка коллекции детей сама по себе их не отпускает.
            foreach (var child in _tagPanelItems.Children.OfType<IDisposable>().ToList())
                child.Dispose();
            _tagPanelItems.Children.Clear();

            foreach (var tag in _vm.TagFilterItems)
            {
                var item = tag;
                var button = new SegmentButton("IconTag", item.Name, "ItemHoverBrush", "ItemSelectedBrush")
                {
                    Margin = new Thickness(0, 0, 4, 0),
                    IsChecked = item.IsSelected
                };
                button.Click += (_, _) => _vm.SearchByTagCommand.Execute(item.Name);
                _tagPanelItems.Children.Add(button);
            }

            _tagClearButton.IsVisible = _vm.HasActiveTagFilter;
            _tagPanel.IsVisible = _vm.ShowTagFilterPanel;
        }

        private Control BuildStatusBar()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusInfo = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            _statusInfo.Bind(TextBlock.TextProperty, new Binding("StatusBarInfo"));
            // Подсказка показывает строку целиком: в нижней панели она обрезается
            // многоточием. Контекстное меню с копированием строки подключения
            // взято из разметки WPF (MainWindow.xaml:2288-2292).
            _statusInfo.Bind(ToolTip.TipProperty, new Binding("StatusBarInfo"));
            if (_vm is not null)
            {
                var statusMenu = new ContextMenu();
                statusMenu.Items.Add(MenuAction("Main.CopyPath", _vm.CopyConnectionStringCommand));
                _statusInfo.ContextMenu = statusMenu;
            }
            grid.Children.Add(_statusInfo);
            Grid.SetColumn(_statusInfo, 0);

            _syncMessage = new TextBlock { FontSize = 12, Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            _syncMessage.Bind(TextBlock.TextProperty, new Binding("SyncMessage"));
            ToolTip.SetTip(_syncMessage, LocalizationManager.T("Main.SyncResultTooltip"));
            grid.Children.Add(_syncMessage);
            Grid.SetColumn(_syncMessage, 1);

            var sessionToggleBtn = new Button { Content = IconHelper.MakeIcon("IconRecent", 16), Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(sessionToggleBtn, LocalizationManager.T("Main.CurrentSession"));
            sessionToggleBtn.Bind(Button.CommandProperty, new Binding("ToggleSessionLaunchPanelCommand"));
            grid.Children.Add(sessionToggleBtn);
            Grid.SetColumn(sessionToggleBtn, 2);

            var toggleBtn = new Button { Content = IconHelper.MakeIcon("IconPanel", 16), Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(toggleBtn, LocalizationManager.T("Main.RightPanel"));
            toggleBtn.Bind(Button.CommandProperty, new Binding("ToggleRightPanelDetailsCommand"));
            grid.Children.Add(toggleBtn);
            Grid.SetColumn(toggleBtn, 3);

            return new Border { Child = grid, Name = "StatusBarBorder", Padding = new Thickness(UiMetrics.TopBarH, 6) };
        }

        // ======================= Обработчики =======================

        private void OnWindowLoaded(object? sender, RoutedEventArgs e)
        {
            // Инициализация выполняется синхронно при загрузке окна. Откладывать её на
            // следующий кадр нельзя: во время неё могут открываться модальные диалоги
            // (импорт/восстановление конфига), и внутри отложенного колбэка их вложенный
            // цикл сообщений приводил к зависанию приложения.
            _vm?.Initialize();
            RegisterHotkeys();
            // Шаблон дерева готов только после загрузки окна, раньше внутренней
            // прокрутки ещё нет.
            AttachVerticalScrollBar();
            if (_vm is not null)
            {
                // Переназначение клавиш меняет и привязки, и подписи в меню.
                _vm.HotkeysChanged += (_, _) =>
                {
                    RegisterHotkeys();
                    if (_tree is not null)
                        _tree.ContextMenu = BuildRowContextMenu();
                };

            }
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

        /// <summary>
        /// Пересобирает главное окно при смене языка интерфейса, чтобы названия колонок,
        /// кнопки правой панели и подсказки (создаваемые через <c>LocalizationManager.T(...)</c>)
        /// обновились на новый язык сразу, а не после перезапуска. Компактный режим
        /// (<see cref="UiMetrics.Compact"/>) при этом сохраняется; выделение и прокрутка
        /// списка восстанавливаются после пересборки содержимого.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                RebuildAfterLanguageChange();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(RebuildAfterLanguageChange);
        }

        private void RebuildAfterLanguageChange()
        {
            var selected = (object?)_vm?.SelectedInfobase ?? _vm?.SelectedGroupNode;
            var offset = TreeScroll?.Offset;

            Content = BuildRoot();
            Title = LocalizationManager.T("App.Title");

            // Обновляем меню и подсказку трея, чтобы подписи кнопок и ToolTip
            // тоже переключились на новый язык без перезапуска.
            if (_trayIcon is not null)
            {
                _trayIcon.Menu = BuildTrayMenu();
                _trayIcon.ToolTipText = LocalizationManager.T("App.Title");
            }

            // Выделение и прокрутка восстанавливаются после того, как новое дерево
            // построено и разложено (иначе строки ещё не существуют).
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (selected is not null && _tree is not null && !ReferenceEquals(_tree.SelectedItem, selected))
                {
                    _tree.SelectionChanged -= OnTreeSelectionChanged;
                    try { _tree.SelectedItem = selected; }
                    finally { _tree.SelectionChanged += OnTreeSelectionChanged; }
                }
                if (offset is { } off && TreeScroll is { } scroll)
                    scroll.Offset = off;
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>Позиция прокрутки списка, снятая перед пересборкой дерева.</summary>
        private Avalonia.Vector? _treeScrollOffset;

        /// <summary>Внутренняя прокрутка дерева: вертикаль ведёт сам TreeView.</summary>
        private ScrollViewer? TreeScroll =>
            _tree?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        /// <summary>
        /// Связывает внешнюю полосу прокрутки с деревом. Полоса стоит отдельным
        /// столбцом, вне горизонтальной прокрутки, поэтому остаётся у правого
        /// края области даже когда колонки шире окна.
        /// </summary>
        private void AttachVerticalScrollBar()
        {
            // Компактный режим пересобирает окно целиком, и дерево с полосой
            // становятся другими объектами. Поэтому сверяемся с самой прокруткой,
            // а не с признаком «уже привязывались»: иначе после переключения
            // полоса остаётся подписанной на выброшенный ScrollViewer.
            if (_listVerticalBar is not { } bar || TreeScroll is not { } scroll)
                return;
            if (ReferenceEquals(_boundTreeScroll, scroll) && ReferenceEquals(_boundScrollBar, bar))
                return;

            foreach (var link in _scrollBarLinks)
                link.Dispose();
            _scrollBarLinks.Clear();
            _boundTreeScroll = scroll;
            _boundScrollBar = bar;

            void Sync()
            {
                if (_syncingScrollBar)
                    return;
                _syncingScrollBar = true;
                try
                {
                    var hidden = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                    bar.Maximum = hidden;
                    bar.ViewportSize = scroll.Viewport.Height;
                    // Шаги берёт сама прокрутка по своему содержимому. Без этого
                    // у отдельной полосы остаются значения RangeBase по умолчанию,
                    // и щелчок по дорожке двигает список на десять точек вместо
                    // страницы, а стрелка на одну точку вместо строки.
                    bar.SmallChange = scroll.SmallChange.Height;
                    bar.LargeChange = scroll.LargeChange.Height;
                    bar.Value = Math.Min(scroll.Offset.Y, hidden);
                }
                finally { _syncingScrollBar = false; }
            }

            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(new PropertyObserver<Vector>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ExtentProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.SmallChangeProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            _scrollBarLinks.Add(scroll.GetObservable(ScrollViewer.LargeChangeProperty)
                .Subscribe(new PropertyObserver<Size>(_ => Sync())));
            Sync();
        }

        /// <summary>Наблюдатель за значением свойства.</summary>
        private sealed class PropertyObserver<T> : IObserver<T>
        {
            private readonly Action<T> _apply;
            public PropertyObserver(Action<T> apply) => _apply = apply;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _apply(value);
        }

        /// <summary>
        /// Запоминает позицию прокрутки до пересборки: список опустеет, и после
        /// неё прежнюю позицию узнать уже неоткуда.
        /// </summary>
        private void RememberTreeScroll()
            // Значение перезаписывается каждой пересборкой и не обнуляется после
            // применения: две пересборки подряд тогда восстановят одну и ту же
            // позицию, а не потеряют её из-за уже отработавшего вызова.
            => _treeScrollOffset = TreeScroll?.Offset ?? _treeScrollOffset;

        /// <summary>
        /// Возвращает выделение строки после пересборки дерева: узлы групп
        /// пересоздаются, и дерево теряет подсветку вместе с ними. Объекты баз
        /// при этом те же самые, поэтому выбранная база ищется по ссылке.
        /// Установка откладывается ниже компоновки, чтобы попасть после
        /// перестроения строк, а цель читается в момент выполнения: вызывающие
        /// меняют выбор уже после возврата из пересборки (создание, регистрация
        /// и удаление базы), и снятая заранее цель подсветила бы чужую строку.
        /// </summary>
        private void RestoreTreeSelection()
        {
            if (_vm is null)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null)
                    return;

                var target = (object?)_vm.SelectedInfobase ?? _vm.SelectedGroupNode;
                if (target is not null && !ReferenceEquals(_tree.SelectedItem, target))
                {
                    // Выбор ставится напрямую, без обработчика: он уже согласован
                    // с вьюмоделью, и повторный проход только сбросил бы парное поле.
                    _tree.SelectionChanged -= OnTreeSelectionChanged;
                    try { _tree.SelectedItem = target; }
                    finally { _tree.SelectionChanged += OnTreeSelectionChanged; }
                }

                // Прокрутка возвращается последней: у дерева включено
                // AutoScrollToSelectedItem, и установка выбора синхронно тянет
                // строку в видимую область, затирая прежнюю позицию.
                if (_treeScrollOffset is { } offset && TreeScroll is { } scroll)
                    scroll.Offset = offset;

                // Вернуть клавиатурный фокус строке после закрытия модального
                // диалога (например, сохранения настроек базы): контейнер прежней
                // строки уничтожен пересборкой, и фокус осел на окне. Если сейчас
                // идёт ввод в текстовом поле (поиск, теги), фокус не трогаем,
                // чтобы не выбивать курсор из поля во время набора.
                if (target is not null
                    && FocusManager?.GetFocusedElement() is not TextBox
                    && _tree.ContainerForItem(target) is { } row)
                {
                    row.Focus();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
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

        /// <summary>
        /// Контекстное меню строки базы: те же действия, что и в WPF-версии,
        /// кроме тех, чьих команд в Avalonia-вьюмодели пока нет (регистрация
        /// COM-коннектора на Linux неприменима, выгрузка в dt и cf и история
        /// запусков ждут порта сервисов запуска).
        /// </summary>
        private ContextMenu BuildRowContextMenu()
        {
            var menu = new ContextMenu();
            if (_vm is null)
                return menu;

            var cacheMenu = new MenuItem
            {
                Header = LocalizationManager.T("Main.ClearCache"),
                Icon = MenuIcon("IconBroom", "#14B8A6")
            };
            cacheMenu.Items.Add(MenuAction("Main.ClearProgramCache", _vm.ClearProgramCacheCommand));
            cacheMenu.Items.Add(MenuAction("Main.ClearUserCache", _vm.ClearUserCacheCommand));
            cacheMenu.Items.Add(new Separator());
            // Сочетание показано здесь, а не у программного кеша: Ctrl+Shift+C
            // открывает очистку обоих кешей. В WPF подпись стоит у программного,
            // хотя клавиша делает то же самое, что этот пункт.
            cacheMenu.Items.Add(MenuAction("Main.ClearCacheBoth", _vm.ClearCacheBothCommand, _vm.HotkeyClearCache));

            menu.Items.Add(MenuAction("Main.LaunchEnterprise", _vm.LaunchEnterpriseCommand, _vm.HotkeyEnterprise, "IconPlay", "#22C55E"));
            menu.Items.Add(MenuAction("Main.LaunchConfigurator", _vm.LaunchConfiguratorCommand, _vm.HotkeyConfigurator, "IconSettings", "#3B82F6"));
            menu.Items.Add(MenuAction("Main.EditSettings", _vm.EditInfobaseCommand, _vm.HotkeyEdit, "IconEdit", "#3B82F6"));
            menu.Items.Add(MenuAction("Main.RefreshConfigInfo", _vm.RefreshConfigurationInfoCommand, null, "IconCloudDownload", "#14B8A6"));
            // «Зарегистрировать COM-коннектор» здесь нет намеренно: внешнее соединение
            // это COM, в Linux регистрировать нечего. Windows-сторона решение подтвердила.
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction("Main.ToFavorites", _vm.ToggleFavoriteCommand, _vm.HotkeyFavorite, "IconStar", "#FBBF24"));
            menu.Items.Add(MenuAction("Main.Pin", _vm.TogglePinCommand, _vm.HotkeyPin, "IconPin", "#8B5CF6"));
            menu.Items.Add(cacheMenu);
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction("Main.CopyConnectionString", _vm.CopyConnectionStringCommand, null, "IconCopy", "#06B6D4"));
            menu.Items.Add(MenuAction("Main.OpenCatalog", _vm.OpenInfobaseFolderCommand, null, "IconFolder", "#0EA5E9"));
            menu.Items.Add(MenuAction("Main.DesktopShortcut", _vm.CreateDesktopShortcutCommand, null, "IconMonitor", "#6366F1"));
            menu.Items.Add(MenuAction("Main.AddBase", _vm.AddInfobaseCommand, _vm.HotkeyAdd, "IconAdd", "#22C55E"));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction("Main.DumpToDt", _vm.DumpInfobaseDtCommand, null, "IconDatabaseExport", "#0EA5E9"));
            menu.Items.Add(MenuAction("Main.DumpConfigToCf", _vm.DumpConfigurationCfCommand, null, "IconFileExport", "#3B82F6"));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuAction("Main.Delete", _vm.DeleteInfobaseCommand, _vm.HotkeyDelete, "IconDelete", "#EF4444"));
            return menu;
        }

        /// <summary>
        /// Значок пункта меню. Цвет задаётся явно, а не ресурсом темы: в разметке
        /// WPF у каждого пункта свой цвет, и он один и тот же в светлой и тёмной.
        /// </summary>
        private static Control MenuIcon(string iconKey, string colorHex) =>
            new Avalonia.Controls.Shapes.Path
            {
                Width = 16,
                Height = 16,
                Data = IconHelper.Geometry(iconKey),
                Stretch = Stretch.Uniform,
                Fill = new SolidColorBrush(Color.Parse(colorHex)),
                VerticalAlignment = VerticalAlignment.Center
            };

        /// <summary>Пункт меню с подписью из словаря, командой и подсказкой сочетания клавиш.</summary>
        private static MenuItem MenuAction(string textKey, System.Windows.Input.ICommand command, string? gesture = null,
            string? iconKey = null, string? iconColor = null)
        {
            var item = new MenuItem
            {
                Header = LocalizationManager.T(textKey),
                Command = command
            };
            if (iconKey is not null && iconColor is not null)
                item.Icon = MenuIcon(iconKey, iconColor);
            if (Controls.HotkeyBox.TryParse(gesture, out var parsed) && parsed is not null)
                item.InputGesture = parsed;
            return item;
        }

        /// <summary>
        /// Горячие клавиши действий. Сочетания берутся из вьюмодели, оттуда же
        /// их показывают подсказки и контекстное меню, поэтому список и подписи
        /// не расходятся.
        /// Важно про порядок: в Avalonia привязки окна проверяются раньше, чем
        /// клавишу получит элемент с фокусом, в отличие от WPF. Ни одно из этих
        /// сочетаний не совпадает с правкой текста, поэтому ввод в поле поиска
        /// они не задевают, но добавлять сюда Ctrl+C, Ctrl+V и подобное нельзя:
        /// они отберут клавишу у поля ввода. Delete по этой же причине живёт
        /// в отдельном обработчике с проверкой фокуса, а не здесь.
        /// </summary>
        private void RegisterHotkeys()
        {
            if (_vm is null)
                return;

            KeyBindings.Clear();

            // Alt+1…Alt+9 запускают избранные базы по порядку слотов и ставятся
            // ПЕРЕД пользовательскими: Avalonia перебирает привязки по порядку
            // списка и останавливается на первой подошедшей, поэтому иначе
            // назначенный пользователем Alt+1 перебивал бы избранное. Версия
            // для Windows добивается того же с другого конца: там
            // RegisterFavoriteHotkeys сперва удаляет из InputBindings все
            // Alt+1…9, включая пользовательские (MainWindow.Hotkeys.cs:118).
            // Незанятый слот привязку всё равно имеет и клавишу поглощает,
            // но действия не выполняет: так же ведёт себя и версия для Windows.
            for (var number = 1; number <= 9; number++)
            {
                var slot = number;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = new KeyGesture((Key)((int)Key.D0 + slot), KeyModifiers.Alt),
                    Command = new ViewModels.RelayCommand(_ => _vm.LaunchFavoriteByHotkey(slot))
                });
            }

            // Delete в привязки не идёт: он правит текст, и в поле ввода
            // не должен удалять базу. Ему отдельный обработчик ниже.
            AddHotkey(_vm.HotkeyEnterprise, _vm.LaunchEnterpriseCommand);
            AddHotkey(_vm.HotkeyConfigurator, _vm.LaunchConfiguratorCommand);
            AddHotkey(_vm.HotkeyEdit, _vm.EditInfobaseCommand);
            AddHotkey(_vm.HotkeyAdd, _vm.AddInfobaseCommand);
            AddHotkey(_vm.HotkeyFavorite, _vm.ToggleFavoriteCommand);
            AddHotkey(_vm.HotkeyPin, _vm.TogglePinCommand);
            AddHotkey(_vm.HotkeyClearCache, _vm.ClearCacheCommand);
            // Переключение режимов списка баз: Все, Избранное, Недавние.
            AddHotkey(_vm.HotkeyShowAll, _vm.ShowAllCommand);
            AddHotkey(_vm.HotkeyShowFavorites, _vm.ShowFavoritesCommand);
            AddHotkey(_vm.HotkeyShowRecent, _vm.ShowRecentCommand);
        }

        /// <summary>
        /// Удаление базы по назначенному сочетанию. В общие привязки оно
        /// не идёт, потому что по умолчанию это Delete: клавиша текстовая,
        /// и в поле ввода она должна править текст, а не удалять базу.
        /// </summary>
        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || _vm is null)
                return;

            // Esc уводит окно в трей, если так задано настройкой. В поле ввода
            // клавиша остаётся своей: там ей отменяют правку.
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None
                && _vm.EscapeToTray && _vm.ShowTrayIcon && CanRestoreHiddenWindow
                && FocusManager?.GetFocusedElement() is not TextBox)
            {
                SaveWindowLayout();
                _vm.PersistSettings();
                ApplyTrayVisibility();
                Hide();
                e.Handled = true;
                return;
            }
            if (!Controls.HotkeyBox.TryParse(_vm.HotkeyDelete, out var gesture) || gesture is null)
                return;
            if (e.Key != gesture.Key || e.KeyModifiers != gesture.KeyModifiers)
                return;

            // Только для текстовых клавиш без модификаторов: назначенное F8
            // должно работать и в поле ввода, как любая другая горячая клавиша.
            var isTextEditingKey = gesture.KeyModifiers == KeyModifiers.None
                && gesture.Key is Key.Delete or Key.Back or Key.Insert;
            if (isTextEditingKey && FocusManager?.GetFocusedElement() is TextBox)
                return;

            if (_vm.DeleteInfobaseCommand.CanExecute(null))
                _vm.DeleteInfobaseCommand.Execute(null);
            e.Handled = true;
        }

        private void AddHotkey(string? gesture, System.Windows.Input.ICommand? command)
        {
            if (command is null || !Controls.HotkeyBox.TryParse(gesture, out var parsed) || parsed is null)
                return;
            KeyBindings.Add(new KeyBinding { Gesture = parsed, Command = command });
        }

        // ======================= Трей =======================

        /// <summary>
        /// Строит меню трея с актуальными переводами. Вызывается при создании
        /// значка и при смене языка, чтобы подписи и подсказки обновились.
        /// </summary>
        private NativeMenu BuildTrayMenu()
        {
            var menu = new NativeMenu();
            // Состав меню зависит от данных: недавние базы и выбранная база
            // меняются в работе. Штатный запрос обновления перед показом
            // (NeedsUpdate) на Linux не приходит: его поднимает только
            // бэкенд macOS, в Avalonia.FreeDesktop вызова нет, проверено
            // и прогоном, и разбором сборок. Поэтому меню пересобирается
            // по изменению самих данных, а подписка оставлена на случай,
            // если событие появится.
            menu.NeedsUpdate += (_, _) => FillTrayMenu(menu);
            FillTrayMenu(menu);
            _trayMenu = menu;
            _traySignature = TrayMenuSignature();
            return menu;
        }

        /// <summary>Снимает обработчики вьюмодели, навешенные прошлой сборкой окна.</summary>
        private void DetachViewModelHandlers()
        {
            if (_vm is null)
                return;
            if (_groupNodesChanged is not null)
                _vm.GroupNodes.CollectionChanged -= _groupNodesChanged;
            if (_flatItemsChanged is not null)
                _vm.FlatItems.CollectionChanged -= _flatItemsChanged;
            if (_tagFiltersRebuilt is not null)
                _vm.TagFiltersRebuilt -= _tagFiltersRebuilt;
            if (_vmPropertyChanged is not null)
                _vm.PropertyChanged -= _vmPropertyChanged;
        }

        /// <summary>
        /// Ставит пересборку меню трея в очередь диспетчера. Одно действие даёт
        /// несколько уведомлений подряд (выбор, список, запуск), а пересборка
        /// заново экспортирует меню по DBus, поэтому вызовы склеиваются, как
        /// это уже делают QueueHeaderAlign и QueueColumnHeaderRefresh.
        /// </summary>
        private void QueueTrayMenuRefresh()
        {
            if (_trayRefreshQueued || _trayMenu is null || _vm is null)
                return;

            _trayRefreshQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _trayRefreshQueued = false;
                RefreshTrayMenu();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Пересобирает меню трея, если его состав действительно изменился:
        /// уведомления приходят и на поиск, и на фильтр, поэтому сверяется
        /// отпечаток состава.
        /// </summary>
        private void RefreshTrayMenu()
        {
            if (_trayMenu is null || _vm is null)
                return;

            var signature = TrayMenuSignature();
            if (signature == _traySignature)
                return;

            _traySignature = signature;
            FillTrayMenu(_trayMenu);
        }

        /// <summary>Состав меню трея строкой: выбранная база и недавние.</summary>
        private string TrayMenuSignature()
        {
            if (_vm is null)
                return string.Empty;

            var builder = new System.Text.StringBuilder();
            builder.Append(_vm.SelectedInfobase?.Id).Append('|').Append(_vm.SelectedInfobase?.Name);
            foreach (var ib in _vm.RecentInfobases)
                builder.Append('|').Append(ib.Id).Append('~').Append(ib.Name);
            return builder.ToString();
        }

        /// <summary>Наполняет меню трея заново по текущему состоянию списка баз.</summary>
        private void FillTrayMenu(NativeMenu menu)
        {
            menu.Items.Clear();

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
        }

        private void SetupTray()
        {
            try
            {
                var menu = BuildTrayMenu();

                var tray = new TrayIcon
                {
                    Icon = LoadTrayIcon(),
                    ToolTipText = LocalizationManager.T("App.Title"),
                    Menu = menu
                };
                _trayIcon = tray;
                if (Application.Current is { } app)
                {
                    TrayIcon.SetIcons(app, new TrayIcons { tray });
                    _trayIconCreated = true;
                }

                ApplyTrayVisibility();

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
            // Пункт меню держит ссылку на базу, а список мог смениться
            // импортом или удалением: запускать исчезнувшую нельзя.
            if (!_vm.Infobases.Contains(ib))
            {
                QueueTrayMenuRefresh();
                return;
            }
            _vm.SelectedInfobase = ib;
            _vm.LaunchEnterpriseCommand.Execute(null);
        }

        /// <summary>
        /// Загружает иконку трея — тот же значок приложения (app.ico), что и у
        /// заголовка окна. Без System.Drawing, через Avalonia WindowIcon.
        /// </summary>
        private static WindowIcon? LoadTrayIcon() => Services.AppIconLoader.LoadAppIcon();

        /// <summary>
        /// Восстанавливает сохранённые размер, позицию и состояние окна.
        /// Настройки читаются здесь из репозитория, а не из модели: она
        /// загружает их только в Initialize по событию Loaded, то есть уже
        /// после того, как окно построено и показано.
        /// </summary>
        private void ApplySavedWindowLayout()
        {
            Models.AppSettings settings;
            try
            {
                settings = AppServices.GetRequiredService<Services.IInfobaseRepository>().LoadSettings();
            }
            catch
            {
                // Настройки недоступны: окно открывается по центру, как раньше.
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            if (!settings.RememberWindowLayout)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }

            var left = settings.WindowLeft;
            var top = settings.WindowTop;
            if (left == 0 && top == 0)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = ClampToScreen(new PixelPoint((int)Math.Round(left), (int)Math.Round(top)));
            }

            // IsDefined обязателен: TryParse принимает и числовую строку, даже
            // когда числа нет среди членов перечисления, и «999» дошло бы
            // до окна. Тот же класс ошибки уже стрелял на разборе клавиш.
            if (Enum.TryParse<WindowState>(settings.WindowState, out var state)
                && Enum.IsDefined(state)
                && state != WindowState.Minimized)
            {
                WindowState = state;
            }
        }

        /// <summary>
        /// Уточняет прижатие к экрану после показа окна. В конструкторе размер
        /// рамки ещё не известен (FrameSize равен null), поэтому там прижатие
        /// считается по содержимому и оставляет за краем высоту заголовка.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (WindowStartupLocation == WindowStartupLocation.Manual
                && WindowState == WindowState.Normal)
            {
                Position = ClampToScreen(Position);
            }
        }

        /// <summary>
        /// Прижимает позицию к рабочей области монитора, на котором окно закрыли,
        /// чтобы оно не оказалось за границей экрана после смены конфигурации мониторов.
        /// </summary>
        private PixelPoint ClampToScreen(PixelPoint point)
        {
            Screen? screen;
            try
            {
                // Точка вне всех экранов (монитор отключили) даёт null, а не
                // исключение: тогда берётся основной, как это делает WPF-версия.
                screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
            }
            catch
            {
                // Сведений об экранах может не быть: на Wayland их отдаёт
                // не всякий сервер. Тогда позиция остаётся как сохранена.
                return point;
            }

            if (screen is null)
                return point;

            var area = screen.WorkingArea;
            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            // Position это угол рамки, а Width и Height задают клиентскую часть,
            // поэтому размер берётся вместе с тем, что рисует менеджер окон.
            // До показа окна рамка ещё не известна, и прижатие уточняется
            // в OnOpened, когда FrameSize уже есть.
            var frame = FrameSize ?? new Size(Width, Height);
            var width = Math.Min((int)Math.Round(Math.Max(frame.Width, Width) * scaling), area.Width);
            var height = Math.Min((int)Math.Round(Math.Max(frame.Height, Height) * scaling), area.Height);
            return new PixelPoint(
                Math.Max(area.X, Math.Min(point.X, area.Right - width)),
                Math.Max(area.Y, Math.Min(point.Y, area.Bottom - height)));
        }

        /// <summary>
        /// Сохраняет размер, позицию и состояние окна. Позиция пишется в физических
        /// пикселях: в Avalonia Position задан в них, в отличие от Left и Top в WPF.
        /// </summary>
        private void SaveWindowLayout()
        {
            if (_vm is null)
                return;

            if (!_vm.RememberWindowLayout)
            {
                // Настройка выключена: сохранённый макет сбрасывается, иначе
                // следующий запуск открыл бы окно в старом месте и размере.
                if (_vm.SavedWindowWidth != 0 || _vm.SavedWindowHeight != 0
                    || _vm.SavedWindowLeft != 0 || _vm.SavedWindowTop != 0
                    || !string.IsNullOrEmpty(_vm.SavedWindowState))
                {
                    _vm.SaveWindowLayout(0, 0, 0, 0, string.Empty);
                }
                return;
            }

            // У спрятанного окна X11 отдаёт Position со сдвигом на высоту
            // заголовка, и каждый уход в трей с последующим выходом сдвигал бы
            // окно вниз. Геометрия такого окна уже записана перед Hide.
            if (!IsVisible)
                return;

            if (WindowState == WindowState.Normal)
            {
                RememberNormalBounds();
            }
            else if (WindowState == WindowState.Minimized)
            {
                // Свёрнутое окно ничего не говорит о своей геометрии.
                return;
            }

            // Развёрнутое окно сохраняется своим состоянием, но размером
            // и положением обычного: иначе следующий запуск взял бы размер
            // во весь экран как обычный. Так же устроена версия для Windows
            // (MainWindow.Events.cs, ветка Maximized и RestoreBounds).
            if (_normalBounds is not { } bounds)
                return;

            // Ничего не изменилось: лишняя запись настроек при закрытии не нужна.
            var state = WindowState.ToString();
            if (bounds.Width == _vm.SavedWindowWidth && bounds.Height == _vm.SavedWindowHeight
                && bounds.Position.X == _vm.SavedWindowLeft && bounds.Position.Y == _vm.SavedWindowTop
                && string.Equals(state, _vm.SavedWindowState, StringComparison.Ordinal))
            {
                return;
            }

            _vm.SaveWindowLayout(bounds.Width, bounds.Height,
                bounds.Position.X, bounds.Position.Y, state);
        }

        /// <summary>Геометрия окна в обычном состоянии, чтобы развёрнутое
        /// сохранялось размером, к которому оно вернётся.</summary>
        private (double Width, double Height, PixelPoint Position)? _normalBounds;

        /// <summary>Запоминает геометрию, пока окно в обычном состоянии.</summary>
        private void RememberNormalBounds()
        {
            if (WindowState == WindowState.Normal && IsVisible)
                _normalBounds = (ClientSize.Width, ClientSize.Height, Position);
        }

        /// <summary>
        /// Закрытие окна уводит приложение в трей, а не завершает его
        /// (свойство «закрытие в трей»). Реальный выход — команда «Выход».
        /// Перед уходом в трей сохраняем настройки (в т.ч. язык интерфейса),
        /// чтобы выбранный язык не терялся при последующем полном выходе.
        /// </summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            SaveWindowLayout();
            if (_allowCloseToTray && _vm is { CloseToTray: true } && CanRestoreHiddenWindow
                && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                _vm.PersistSettings();
                ApplyTrayVisibility();
                e.Cancel = true;
                Hide();
            }
        }

        /// <summary>
        /// Показывает или прячет значок по настройкам. Значок нужен и когда сам
        /// он выключен, но закрытие уводит окно в трей: иначе окно нечем вернуть.
        /// </summary>
        private void ApplyTrayVisibility()
        {
            if (_trayIcon is null)
            {
                // Значок не создался при загрузке: пробуем ещё раз, иначе
                // включение настройки не даст ничего до перезапуска.
                if (TrayIconWanted)
                    SetupTray();
                return;
            }

            var wanted = TrayIconWanted;

            // Пока окно спрятано, значок обязан оставаться видимым: он
            // единственный надёжный путь назад. Если настройки требуют его
            // убрать, сперва возвращаем окно.
            if (!wanted && !IsVisible)
                ShowAndActivate();

            _trayIcon.IsVisible = wanted;
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