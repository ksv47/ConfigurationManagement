#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private Point _dragStartPoint;
        private object? _draggedData;
        private bool _isDragging;
        private Forms.NotifyIcon? _trayIcon;
        private bool _forceClose;

        /// <summary>Информационная версия сборки для заголовка окна (например «0.3.3.41»).</summary>
        private readonly string? _infoVersion;

        /// <summary>
        /// Состояние нижней кнопки тегов (<see cref="MainViewModel.ShowTags"/>), запомненное
        /// в момент выключения верхней кнопки «теги», чтобы восстановить его при повторном
        /// включении (вместо принудительного включения нижней кнопки).
        /// </summary>
        private bool? _savedTagsStateBeforeTopOff;

        public MainWindow(ViewModels.MainViewModel? viewModel = null)
        {
            InitializeComponent();

            // Выводим версию программы в заголовок окна (информационная версия,
            // чтобы показать точное значение «0.3.3.41»).
            // Из InformationalVersion отбрасываем возможный суффикс «+<sha>».
            _infoVersion = VersionInfo.Display();
            Title = $"{Title} v{_infoVersion}";

            // Смена языка интерфейса: заголовок окна, подсказки и меню трея, которые
            // задаются в code-behind, обновляются вручную (LocExtension-привязки XAML
            // обновляются сами через LocalizationManager.Source.NotifyAll()).
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;

            _viewModel = viewModel ?? new ViewModels.MainViewModel();
            DataContext = _viewModel;

            // Действие «после запуска базы/конфигуратора» согласно глобальной настройке.
            _viewModel.AfterLaunchRequested += OnAfterLaunchRequested;

            // После пересборки дерева (например, сохранения настроек базы) возвращаем
            // клавиатурный фокус на выбранную строку — прежний контейнер уничтожен.
            _viewModel.TreeRebuilt += RestoreTreeKeyboardFocus;

            // Пересчитываем выравнивание колонок заголовка после переключения компактного
            // режима: ApplyCompact масштабирует отступы/шрифты/компенсатор заголовка,
            // поэтому старое значение HeaderOffsetColumn становится неактуальным и данные
            // разъезжаются относительно заголовков. Пересчёт откладывается до Loaded-приоритета,
            // чтобы он выполнился уже после того, как ApplyCompact завершит изменения раскладки.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // После завершения фоновой инициализации (дерево уже построено) восстанавливаем
            // последнее выделение и пересчитываем раскладку — раньше дерево ещё пустое.
            _viewModel.StartupInitializationCompleted += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        RestoreLastSelection();
                        AlignHeaderToData();
                    }
                    catch { /* не блокируем запуск из-за восстановления выделения */ }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // Применяем сохранённую цветовую схему (тему оформления) при запуске.
            _viewModel.ApplyActiveColorSchemeToUi();

            UpdateThemeButton();

            // Компактный режим: синхронизируем состояние кнопки на верхней панели.
            if (CompactModeButton != null)
                CompactModeButton.IsChecked = _viewModel.CompactMode;

            // Применяем сохранённые ширины колонок списка баз.
            ApplySavedColumnWidths();

            // Применяем сохранённые размер, позицию и состояние окна.
            ApplySavedWindowLayout();

            // Трей и хоткеи — после загрузки окна (STA/иконка безопаснее на Loaded).
            Loaded += (_, _) =>
            {
                try
                {
                    InitializeTrayIcon();
                    RegisterLaunchHotkeys();
                    RegisterFavoriteHotkeys();
                    RestoreLastSelection();
                }
                catch
                {
                    // не блокируем запуск из‑за трея/хоткеев
                }
            };
            _viewModel.FavoriteHotkeysChanged += (_, _) =>
            {
                try { RegisterFavoriteHotkeys(); }
                catch { /* ignore */ }
            };
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.ShowTrayIcon))
                {
                    try { UpdateTrayVisibility(); } catch { /* ignore */ }
                    return;
                }

                // Индикатор выгрузки .dt/.cf: запускаем/останавливаем анимацию.
                if (e.PropertyName is nameof(MainViewModel.IsExporting))
                {
                    if (_viewModel.IsExporting) StartExportIndicatorAnimation();
                    else StopExportIndicatorAnimation();
                    return;
                }

                // При изменении видимости колонок/кнопок пересчитываем выравнивание
                // заголовка с данными, чтобы колонки не разъезжались.
                if (e.PropertyName is nameof(MainViewModel.ShowVersionColumn)
                    or nameof(MainViewModel.ShowLaunchModeColumn)
                    or nameof(MainViewModel.ShowServerColumn)
                    or nameof(MainViewModel.ShowLastLaunchColumn)
                    or nameof(MainViewModel.ShowSizeColumn)
                    or nameof(MainViewModel.ShowFavoritesButton)
                    or nameof(MainViewModel.ShowPinnedButton))
                {
                    Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                if (e.PropertyName is nameof(MainViewModel.HotkeyEnterprise)
                    or nameof(MainViewModel.HotkeyConfigurator)
                    or nameof(MainViewModel.HotkeyFavorite)
                    or nameof(MainViewModel.HotkeyEdit)
                    or nameof(MainViewModel.HotkeyDelete)
                    or nameof(MainViewModel.HotkeyClearCache)
                    or nameof(MainViewModel.HotkeyAdd)
                    or nameof(MainViewModel.HotkeyPin)
                    or nameof(MainViewModel.HotkeyShowAll)
                    or nameof(MainViewModel.HotkeyShowFavorites)
                    or nameof(MainViewModel.HotkeyShowRecent))
                {
                    try { RegisterLaunchHotkeys(); } catch { /* ignore */ }
                }
            };
        }

        private DoubleAnimation? _exportBounceAnimation;
        private bool _exportAnimating;












        private enum TrayIconKind
        {
            Open, Database, Enterprise, Configurator, Sync, Settings, Exit
        }



        private static readonly Dictionary<TrayIconKind, Drawing.Image> TrayIconCache = new();



        /// <summary>Современный рендерер меню трея (скругление, hover, светлый фон).</summary>
        private sealed class ModernTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            public ModernTrayMenuRenderer() : base(new ModernTrayColorTable())
            {
                RoundedEdges = true;
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed)
                {
                    base.OnRenderMenuItemBackground(e);
                    return;
                }

                var rect = new Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
                using var b = new Drawing.SolidBrush(Drawing.Color.FromArgb(239, 246, 255));
                e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = RoundedRect(rect, 6);
                e.Graphics.FillPath(b, path);
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item is Forms.ToolStripLabel
                    ? Drawing.Color.FromArgb(100, 116, 139)
                    : Drawing.Color.FromArgb(30, 41, 59);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                var y = e.Item.Height / 2;
                using var pen = new Drawing.Pen(Drawing.Color.FromArgb(226, 232, 240));
                e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 8, y);
            }

            private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle bounds, int radius)
            {
                var path = new Drawing.Drawing2D.GraphicsPath();
                int d = radius * 2;
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        private sealed class ModernTrayColorTable : Forms.ProfessionalColorTable
        {
            public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(226, 232, 240);
            public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
            public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(239, 246, 255);
            public override Drawing.Color MenuItemSelectedGradientBegin => Drawing.Color.FromArgb(239, 246, 255);
            public override Drawing.Color MenuItemSelectedGradientEnd => Drawing.Color.FromArgb(239, 246, 255);
            public override Drawing.Color MenuItemPressedGradientBegin => Drawing.Color.FromArgb(219, 234, 254);
            public override Drawing.Color MenuItemPressedGradientEnd => Drawing.Color.FromArgb(219, 234, 254);
            public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.FromArgb(248, 250, 252);
            public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.FromArgb(248, 250, 252);
            public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.FromArgb(248, 250, 252);
            public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(255, 255, 255);
            public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(226, 232, 240);
            public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(241, 245, 249);
        }





























        // ---- Динамический порядок колонок списка баз ----

        // Статический порядок колонок данных в сетке (заголовке и строке базы) после
        // фиксированных колонок слева (кнопки групп, компенсатор, избранное, закрепление,
        // название). Совпадает с порядком по умолчанию: «Действия» сразу после «Режим запуска».
        private static readonly string[] StaticDataColumnKeys =
            { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Configuration" };

        // Индекс первой колонки данных в сетке заголовка / строки базы.
        // Строка базы и заголовок имеют одинаковый набор ведущих колонок
        // (кнопки групп + компенсатор + избранное + закрепление + название),
        // поэтому данные строк точно совпадают по горизонтали с заголовками.
        private const int HeaderFirstDataColumn = 5; // после «Названия» заголовка
        private const int RowFirstDataColumn = 5;    // после «Названия» строки базы (как у заголовка)

        // Метка сетки строки базы: по ней находим созданные строки при обходе дерева.
        private static readonly object RowGridMarker = new();
        // Метка сетки заголовка группы: строки групп тоже перестраиваются по порядку колонок,
        // чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        private static readonly object GroupGridMarker = new();

        /// <summary>
        /// Ключ логической колонки элемента сетки (для динамического порядка колонок).
        /// Используется вместо Tag, т.к. Tag занят другими целями (сортировка, двойной клик).
        /// </summary>
        public static readonly DependencyProperty ColumnKeyProperty =
            DependencyProperty.RegisterAttached(
                "ColumnKey", typeof(string), typeof(MainWindow), new PropertyMetadata(null));

        public static void SetColumnKey(DependencyObject obj, string? value) =>
            obj.SetValue(ColumnKeyProperty, value);

        public static string? GetColumnKey(DependencyObject obj) =>
            (string?)obj.GetValue(ColumnKeyProperty);























        /// <summary>
        /// Блокирует автоматическую прокрутку TreeView к выделенному элементу
        /// (по умолчанию WPF вызывает BringIntoView при IsSelected/Focus — список «прыгает» вверх).
        /// </summary>



















        // Поля для ручного перетаскивания разделителя колонок.
        private ColumnDefinition? _resizeColumn;
        private double _resizeStartWidth;
        private Point _resizeStartMouse;

















        // ===================== Drag & Drop баз и групп =====================
        //
        // Модель WPF DnD (кратко):
        // 1) Источник: после порога смещения мыши вызывается DragDrop.DoDragDrop — синхронный
        //    цикл до Drop/Cancel. Пока он идёт, приходят DragOver/Drop на цели.
        // 2) Цель: AllowDrop=True; в DragOver обязательно задать e.Effects и e.Handled=true,
        //    иначе курсор «запрещено» и Drop не придёт.
        // 3) Данные: лучше свой payload с MouseDown (не с MouseMove) — иначе под курсором
        //    уже другой TreeViewItem (дочерняя база вместо группы).
        // 4) DoDragDrop возвращается после Drop → finally очищает _draggedData; во время Drop
        //    поле ещё валидно. Дополнительно кладём объект в DataObject по имени формата.

        private const string DragFormatInfobase = "Configuration_Management.Infobase";
        private const string DragFormatGroup = "Configuration_Management.GroupNode";


        /// <summary>
        /// Шаг накопительного отступа вложенности дерева (см. Margin у ItemsHost
        /// в ControlTemplate TreeViewItem: "18,0,0,0" на каждый уровень).
        /// Базы внутри групп смещаются вправо на этот шаг, чтобы была видна
        /// иерархия «группа в группе».
        /// </summary>
        private const double GroupTreeIndentStep = 18.0;

        /// <summary>Ширина кнопки разворота группы (px). Синхронизирована с Expander Width в XAML.</summary>
        private const double GroupTreeExpanderWidth = 26.0;












    }
}
#endif
