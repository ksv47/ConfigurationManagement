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
            _infoVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Title = $"{Title} v{_infoVersion}";

            // Смена языка интерфейса: заголовок окна, подсказки и меню трея, которые
            // задаются в code-behind, обновляются вручную (LocExtension-привязки XAML
            // обновляются сами через LocalizationManager.Source.NotifyAll()).
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;

            _viewModel = viewModel ?? new ViewModels.MainViewModel();
            DataContext = _viewModel;

            // Действие «после запуска базы/конфигуратора» согласно глобальной настройке.
            _viewModel.AfterLaunchRequested += OnAfterLaunchRequested;

            // Пересчитываем выравнивание колонок заголовка после переключения компактного
            // режима: ApplyCompact масштабирует отступы/шрифты/компенсатор заголовка,
            // поэтому старое значение HeaderOffsetColumn становится неактуальным и данные
            // разъезжаются относительно заголовков. Пересчёт откладывается до Loaded-приоритета,
            // чтобы он выполнился уже после того, как ApplyCompact завершит изменения раскладки.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

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

        /// <summary>
        /// Запускает бесконечное «подпрыгивание» индикатора выгрузки .dt/.cf (стрелка вверх).
        /// </summary>
        private void StartExportIndicatorAnimation()
        {
            if (_exportAnimating || ExportIndicatorBounce is null)
                return;
            _exportAnimating = true;
            _exportBounceAnimation = new DoubleAnimation
            {
                From = 0,
                To = -4,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            ExportIndicatorBounce.BeginAnimation(TranslateTransform.YProperty, _exportBounceAnimation);
        }

        /// <summary>
        /// Останавливает анимацию индикатора выгрузки .dt/.cf (по завершении операции).
        /// </summary>
        private void StopExportIndicatorAnimation()
        {
            if (!_exportAnimating)
                return;
            _exportAnimating = false;
            ExportIndicatorBounce?.BeginAnimation(TranslateTransform.YProperty, null);
            if (ExportIndicatorBounce is not null)
                ExportIndicatorBounce.Y = 0;
        }

        /// <summary>
        /// Восстанавливает сохранённые размер, позицию и состояние окна приложения.
        /// </summary>
        private void ApplySavedWindowLayout()
        {
            var width = _viewModel.SavedWindowWidth;
            var height = _viewModel.SavedWindowHeight;

            // Если запоминание окна отключено — не восстанавливаем положение и размер.
            if (!_viewModel.RememberWindowLayout)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            if (width > 0 && height > 0)
            {
                var left = _viewModel.SavedWindowLeft;
                var top = _viewModel.SavedWindowTop;
                if (left == 0 && top == 0)
                {
                    // Если позиция не сохранена — центрируем окно.
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                else
                {
                    // Позиция восстановлена — отключаем авторасположение по центру,
                    // иначе WPF переопределит Left/Top при показе окна (в XAML задан CenterScreen).
                    WindowStartupLocation = WindowStartupLocation.Manual;

                    // Определяем монитор, на котором окно было закрыто, по сохранённой позиции.
                    // Это возвращает окно на тот же экран (в т.ч. при нескольких мониторах).
                    var area = SystemParameters.WorkArea;
                    try
                    {
                        var screen = Forms.Screen.FromPoint(
                            new Drawing.Point((int)Math.Round(left), (int)Math.Round(top)));
                        if (screen != null)
                        {
                            var wa = screen.WorkingArea;
                            area = new System.Windows.Rect(wa.Left, wa.Top, wa.Width, wa.Height);
                        }
                    }
                    catch
                    {
                        // Если экран недоступен — используем рабочую область основного монитора.
                        area = SystemParameters.WorkArea;
                    }

                    // Ограничиваем позицию, чтобы окно оставалось видимым на выбранном мониторе.
                    var safeLeft = Math.Max(area.Left, Math.Min(left, area.Right - Math.Min(width, area.Width)));
                    var safeTop = Math.Max(area.Top, Math.Min(top, area.Bottom - Math.Min(height, area.Height)));
                    Left = safeLeft;
                    Top = safeTop;
                }

                Width = width;
                Height = height;
            }

            // Восстанавливаем развёрнутое состояние окна.
            if (Enum.TryParse<WindowState>(_viewModel.SavedWindowState, out var state) &&
                state != WindowState.Minimized)
            {
                WindowState = state;
            }
        }

        /// <summary>
        /// Сохраняет размер, позицию и состояние окна приложения при закрытии.
        /// При включённой опции «Закрывать в трей» скрывает окно вместо выхода.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Гарантированно сохраняем все настройки (включая компактный режим) при закрытии,
            // даже если переключатель не был задействован через сеттер.
            _viewModel.SaveSettings();

            if (!_viewModel.RememberWindowLayout)
            {
                // Если запоминание окна отключено — сбрасываем сохранённый макет,
                // чтобы при следующем запуске окно не открывалось в старом месте/размере.
                _viewModel.SaveWindowLayout(0, 0, 0, 0, string.Empty);
            }
            else if (WindowState == WindowState.Normal)
            {
                // Сохраняем только в обычном состоянии, чтобы не сохранить развёрнутое окно как размер по умолчанию.
                _viewModel.SaveWindowLayout(Width, Height, Left, Top, WindowState.ToString());
            }
            else if (WindowState == WindowState.Maximized)
            {
                _viewModel.SaveWindowLayout(RestoreBounds.Width, RestoreBounds.Height, RestoreBounds.Left, RestoreBounds.Top, WindowState.ToString());
            }

            if (!_forceClose && _viewModel.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (_trayIcon != null)
                    _trayIcon.Visible = true;
                return;
            }

            // Останавливаем автоматическую синхронизацию при закрытии окна.
            _viewModel.StopAutoSync();
            DisposeTrayIcon();

            // Отписываемся от события смены языка, чтобы не держать ссылку на окно
            // (избегаем утечки памяти после полного закрытия приложения).
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            // Отписываем ViewModel от события смены языка, чтобы не осталось дублирующей
            // подписки (VM живёт весь срок приложения, но отписка защищает от утечек).
            _viewModel.UnsubscribeLanguageChanged();

            base.OnClosing(e);
        }

        /// <summary>
        /// Обработчик кнопки «Выход» в правой панели.
        /// Всегда полностью завершает работу приложения, игнорируя настройку
        /// «Закрывать в трей» (в отличие от обычного закрытия окна).
        /// </summary>
        private void OnExitApplicationClick(object sender, RoutedEventArgs e)
        {
            _forceClose = true;
            Close();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                // Старый экземпляр (повторный вызов) — освобождаем
                if (_trayIcon != null)
                {
                    try { _trayIcon.Visible = false; _trayIcon.Dispose(); } catch { /* ignore */ }
                    _trayIcon = null;
                }

                // Важно: Icon задать ДО Visible — иначе на Win10/11 иконка в трее может не появиться
                var icon = LoadApplicationIcon() ?? Drawing.SystemIcons.Application;

                _trayIcon = new Forms.NotifyIcon
                {
                    Text = LocalizationManager.T("App.Title"),
                    Icon = icon,
                    Visible = false
                };

                try
                {
                    var menu = CreateModernTrayMenu();
                    menu.Opening += (_, _) => RebuildTrayMenu(menu);
                    RebuildTrayMenu(menu);
                    _trayIcon.ContextMenuStrip = menu;
                }
                catch
                {
                    // Меню не критично — иконка всё равно должна быть
                }

                _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
                _trayIcon.MouseClick += (_, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                        RestoreFromTray();
                };

                // Показ: по настройке «Показывать в трее» или всегда, если закрытие в трей
                _trayIcon.Visible = _viewModel.ShowTrayIcon || _viewModel.CloseToTray;
            }
            catch
            {
                // Последняя попытка — минимальный NotifyIcon без меню
                try
                {
                    _trayIcon = new Forms.NotifyIcon
                    {
                        Text = LocalizationManager.T("App.Title"),
                        Icon = Drawing.SystemIcons.Application,
                        Visible = true
                    };
                    _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
                }
                catch
                {
                    _trayIcon = null;
                }
            }
        }

        /// <summary>
        /// Загрузка иконки приложения для трея (16×16 предпочтительно).
        /// Работает при PublishSingleFile.
        /// </summary>
        private static Drawing.Icon? LoadApplicationIcon()
        {
            // 1) Embedded resource «app.ico» (приоритет), затем «tray.ico»
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string[] preferred = ["app.ico", "tray.ico"];
                var names = asm.GetManifestResourceNames();
                foreach (var pref in preferred)
                {
                    foreach (var name in names)
                    {
                        if (!name.EndsWith(pref, StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals(pref, StringComparison.OrdinalIgnoreCase))
                            continue;
                        using var stream = asm.GetManifestResourceStream(name);
                        if (stream is null) continue;
                        return CreateTraySizedIcon(stream);
                    }
                }
            }
            catch { /* ignore */ }

            // 2) WPF Resource (pack URI): app.ico, затем tray.ico
            try
            {
                foreach (var res in new[] { "app.ico", "tray.ico" })
                {
                    var uri = new Uri($"pack://application:,,,/{res}", UriKind.Absolute);
                    var info = Application.GetResourceStream(uri);
                    if (info?.Stream is null) continue;
                    using (info.Stream)
                        return CreateTraySizedIcon(info.Stream);
                }
            }
            catch { /* ignore */ }

            // 3) Файл рядом с exe / BaseDirectory
            try
            {
                var dirs = new List<string>();
                if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
                    dirs.Add(AppDomain.CurrentDomain.BaseDirectory);
                var proc = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(proc))
                {
                    var d = System.IO.Path.GetDirectoryName(proc);
                    if (!string.IsNullOrEmpty(d)) dirs.Add(d);
                }
                foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var fileName in new[] { "app.ico", "tray.ico" })
                    {
                        var iconPath = System.IO.Path.Combine(dir, fileName);
                        if (!System.IO.File.Exists(iconPath)) continue;
                        using var fs = System.IO.File.OpenRead(iconPath);
                        return CreateTraySizedIcon(fs);
                    }
                }
            }
            catch { /* ignore */ }

            // 4) Иконка, вшитая в exe (ApplicationIcon)
            try
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && System.IO.File.Exists(exePath))
                {
                    var extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (extracted is not null)
                        return extracted;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>Иконка 16×16 для трея (лучше отображается в notification area).</summary>
        private static Drawing.Icon CreateTraySizedIcon(System.IO.Stream stream)
        {
            using var source = new Drawing.Icon(stream);
            try
            {
                using var sized = new Drawing.Icon(source, 16, 16);
                return (Drawing.Icon)sized.Clone();
            }
            catch
            {
                return (Drawing.Icon)source.Clone();
            }
        }

        /// <summary>Создаёт ContextMenuStrip в современном стиле (рендерер, шрифт, отступы).</summary>
        private static Forms.ContextMenuStrip CreateModernTrayMenu()
        {
            var menu = new Forms.ContextMenuStrip
            {
                Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Regular),
                ShowImageMargin = true,
                ShowCheckMargin = false,
                ImageScalingSize = new Drawing.Size(16, 16),
                Padding = new Forms.Padding(2, 4, 2, 4),
                Renderer = new ModernTrayMenuRenderer()
            };
            return menu;
        }

        /// <summary>
        /// Собирает меню трея: открыть, недавние базы (Предприятие / Конфигуратор), сервис, выход.
        /// </summary>
        private void RebuildTrayMenu(Forms.ContextMenuStrip menu)
        {
            menu.Items.Clear();

            menu.Items.Add(CreateTrayItem(LocalizationManager.T("Main.TrayOpen"), TrayIconKind.Open, (_, _) => RestoreFromTray()));

            // Недавние базы (по дате последнего запуска)
            var recent = _viewModel.GetRecentInfobases(7).ToList();
            if (recent.Count > 0)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(CreateTrayHeader(LocalizationManager.T("Main.RecentBases")));

                foreach (var ib in recent)
                {
                    var name = string.IsNullOrWhiteSpace(ib.Name) ? LocalizationManager.T("Main.NoName") : ib.Name;
                    if (name.Length > 48)
                        name = name.Substring(0, 45) + "…";

                    var id = ib.Id;
                    var baseItem = CreateTrayItem(name, TrayIconKind.Database, (_, _) =>
                        _viewModel.LaunchInfobaseById(id, isConfigurator: false));
                    AttachLaunchSubmenu(baseItem, menu, id);
                    menu.Items.Add(baseItem);
                }
            }
            else if (_viewModel.SelectedInfobase != null)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(CreateTrayHeader(LocalizationManager.T("Main.SelectedBase")));
                var selName = _viewModel.SelectedInfobase.Name;
                if (string.IsNullOrWhiteSpace(selName)) selName = LocalizationManager.T("Main.SelectedBaseNoName");
                var selId = _viewModel.SelectedInfobase.Id;
                var selItem = CreateTrayItem(selName, TrayIconKind.Database, null);
                AttachLaunchSubmenu(selItem, menu, selId);
                menu.Items.Add(selItem);
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateTrayItem(LocalizationManager.T("Main.SyncWithIbases"), TrayIconKind.Sync, (_, _) =>
            {
                RestoreFromTray();
                if (_viewModel.SynchronizeWithIbasesCommand.CanExecute(null))
                    _viewModel.SynchronizeWithIbasesCommand.Execute(null);
            }));
            menu.Items.Add(CreateTrayItem(LocalizationManager.T("Main.Settings"), TrayIconKind.Settings, (_, _) =>
            {
                RestoreFromTray();
                if (_viewModel.OpenSettingsCommand.CanExecute(null))
                    _viewModel.OpenSettingsCommand.Execute(null);
            }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateTrayItem(LocalizationManager.T("Main.Exit"), TrayIconKind.Exit, (_, _) =>
            {
                _forceClose = true;
                Close();
            }));
        }

        /// <summary>
        /// Подменю «Предприятие / Конфигуратор» без зазора от родительского пункта.
        /// </summary>
        private void AttachLaunchSubmenu(Forms.ToolStripMenuItem parent, Forms.ContextMenuStrip ownerMenu, string infobaseId)
        {
            parent.DropDownItems.Add(CreateTrayItem(LocalizationManager.T("Main.Enterprise"), TrayIconKind.Enterprise, (_, _) =>
                _viewModel.LaunchInfobaseById(infobaseId, isConfigurator: false)));
            parent.DropDownItems.Add(CreateTrayItem(LocalizationManager.T("Main.SectionConfigurator"), TrayIconKind.Configurator, (_, _) =>
                _viewModel.LaunchInfobaseById(infobaseId, isConfigurator: true)));

            var dd = parent.DropDown;
            dd.Renderer = ownerMenu.Renderer;
            dd.Font = ownerMenu.Font;
            dd.ImageScalingSize = ownerMenu.ImageScalingSize;
            dd.Padding = new Forms.Padding(2, 2, 2, 2);
            dd.Margin = new Forms.Padding(0);
            if (dd is Forms.ToolStripDropDownMenu dropMenu)
            {
                dropMenu.ShowImageMargin = true;
                dropMenu.ShowCheckMargin = false;
            }

            // WinForms по умолчанию оставляет щель между меню и подменю — сдвигаем вплотную
            parent.DropDownOpened += (_, _) =>
            {
                try
                {
                    var loc = dd.Location;
                    dd.Location = new Drawing.Point(loc.X - 8, loc.Y);
                }
                catch { /* ignore */ }
            };
        }

        private enum TrayIconKind
        {
            Open, Database, Enterprise, Configurator, Sync, Settings, Exit
        }

        private static Forms.ToolStripMenuItem CreateTrayItem(string text, TrayIconKind kind, EventHandler? onClick)
        {
            var item = new Forms.ToolStripMenuItem(text)
            {
                Image = GetTrayIcon(kind),
                ImageScaling = Forms.ToolStripItemImageScaling.None,
                DisplayStyle = Forms.ToolStripItemDisplayStyle.ImageAndText,
                ImageAlign = Drawing.ContentAlignment.MiddleLeft,
                TextAlign = Drawing.ContentAlignment.MiddleLeft,
                Padding = new Forms.Padding(2, 3, 4, 3),
                AutoSize = true
            };
            if (onClick != null)
                item.Click += onClick;
            return item;
        }

        private static Forms.ToolStripLabel CreateTrayHeader(string text)
        {
            return new Forms.ToolStripLabel(text)
            {
                ForeColor = Drawing.Color.FromArgb(100, 116, 139),
                Font = new Drawing.Font("Segoe UI Semibold", 8.5f, Drawing.FontStyle.Regular),
                Padding = new Forms.Padding(6, 4, 6, 2)
            };
        }

        private static readonly Dictionary<TrayIconKind, Drawing.Image> TrayIconCache = new();

        /// <summary>
        /// Иконки меню трея: те же символы Material Design, что PackIcon в главном окне
        /// (Play, Wrench, Database, Cog, Sync, ExitToApp, Application), 16×16.
        /// Рисуются через GDI+ по геометрии MD — без зависимости от WPF visual tree
        /// (PackIcon вне окна даёт пустой bitmap).
        /// </summary>
        private static Drawing.Image GetTrayIcon(TrayIconKind kind)
        {
            if (TrayIconCache.TryGetValue(kind, out var cached))
                return cached;

            var img = DrawMaterialTrayIcon(kind, 16);
            TrayIconCache[kind] = img;
            return img;
        }

        private static Drawing.Image DrawMaterialTrayIcon(TrayIconKind kind, int size)
        {
            var bmp = new Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Drawing.Graphics.FromImage(bmp);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            // Цвета как у кнопок/меню главного окна
            Drawing.Color c = kind switch
            {
                TrayIconKind.Open => Drawing.Color.FromArgb(34, 197, 94),
                TrayIconKind.Database => Drawing.Color.FromArgb(59, 130, 246),
                TrayIconKind.Enterprise => Drawing.Color.FromArgb(37, 99, 235),
                TrayIconKind.Configurator => Drawing.Color.FromArgb(100, 116, 139),
                TrayIconKind.Sync => Drawing.Color.FromArgb(20, 184, 166),
                TrayIconKind.Settings => Drawing.Color.FromArgb(100, 116, 139),
                TrayIconKind.Exit => Drawing.Color.FromArgb(220, 38, 38),
                _ => Drawing.Color.FromArgb(100, 116, 139)
            };

            using var brush = new Drawing.SolidBrush(c);
            using var pen = new Drawing.Pen(c, Math.Max(1.4f, size / 11f))
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round,
                LineJoin = Drawing.Drawing2D.LineJoin.Round
            };

            float s = size;
            switch (kind)
            {
                case TrayIconKind.Enterprise:
                    // PackIcon Kind=Play — треугольник
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.28f, s * 0.18f),
                        new Drawing.PointF(s * 0.82f, s * 0.50f),
                        new Drawing.PointF(s * 0.28f, s * 0.82f)
                    });
                    break;

                case TrayIconKind.Configurator:
                    // PackIcon Kind=Wrench
                    using (var path = new Drawing.Drawing2D.GraphicsPath())
                    {
                        // рукоять
                        path.AddLine(s * 0.22f, s * 0.78f, s * 0.55f, s * 0.45f);
                        using var thick = new Drawing.Pen(c, s * 0.18f)
                        {
                            StartCap = Drawing.Drawing2D.LineCap.Round,
                            EndCap = Drawing.Drawing2D.LineCap.Round
                        };
                        g.DrawLine(thick, s * 0.25f, s * 0.75f, s * 0.55f, s * 0.45f);
                        // головка
                        g.DrawArc(pen, s * 0.48f, s * 0.12f, s * 0.40f, s * 0.40f, 200, 220);
                        g.FillEllipse(brush, s * 0.58f, s * 0.22f, s * 0.12f, s * 0.12f);
                    }
                    break;

                case TrayIconKind.Database:
                    // PackIcon Kind=Database — цилиндр
                    g.FillEllipse(brush, s * 0.22f, s * 0.12f, s * 0.56f, s * 0.28f);
                    g.FillRectangle(brush, s * 0.22f, s * 0.26f, s * 0.56f, s * 0.48f);
                    g.FillEllipse(brush, s * 0.22f, s * 0.60f, s * 0.56f, s * 0.28f);
                    using (var top = new Drawing.SolidBrush(Drawing.Color.FromArgb(90, 255, 255, 255)))
                        g.FillEllipse(top, s * 0.28f, s * 0.16f, s * 0.44f, s * 0.16f);
                    break;

                case TrayIconKind.Settings:
                    // PackIcon Kind=Cog
                    {
                        float cx = s * 0.5f, cy = s * 0.5f, r = s * 0.28f, tooth = s * 0.12f;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4.0;
                            float x = cx + (float)(Math.Cos(a) * (r + tooth * 0.35));
                            float y = cy + (float)(Math.Sin(a) * (r + tooth * 0.35));
                            g.FillEllipse(brush, x - tooth * 0.55f, y - tooth * 0.55f, tooth * 1.1f, tooth * 1.1f);
                        }
                        g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
                        using var hole = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 255, 255, 255));
                        // вырезаем центр «дыркой» поверх — для меню фон светлый, белый ок
                        g.FillEllipse(hole, cx - r * 0.38f, cy - r * 0.38f, r * 0.76f, r * 0.76f);
                        using var ring = new Drawing.Pen(c, s * 0.08f);
                        g.DrawEllipse(ring, cx - r * 0.38f, cy - r * 0.38f, r * 0.76f, r * 0.76f);
                    }
                    break;

                case TrayIconKind.Sync:
                    // PackIcon Kind=Sync — две стрелки по кругу
                    g.DrawArc(pen, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.64f, -40, 200);
                    g.DrawArc(pen, s * 0.18f, s * 0.18f, s * 0.64f, s * 0.64f, 140, 200);
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.72f, s * 0.12f),
                        new Drawing.PointF(s * 0.92f, s * 0.32f),
                        new Drawing.PointF(s * 0.62f, s * 0.34f)
                    });
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.28f, s * 0.88f),
                        new Drawing.PointF(s * 0.08f, s * 0.68f),
                        new Drawing.PointF(s * 0.38f, s * 0.66f)
                    });
                    break;

                case TrayIconKind.Exit:
                    // PackIcon Kind=ExitToApp
                    g.DrawRectangle(pen, s * 0.12f, s * 0.18f, s * 0.42f, s * 0.64f);
                    g.DrawLine(pen, s * 0.42f, s * 0.50f, s * 0.88f, s * 0.50f);
                    g.FillPolygon(brush, new[]
                    {
                        new Drawing.PointF(s * 0.68f, s * 0.32f),
                        new Drawing.PointF(s * 0.92f, s * 0.50f),
                        new Drawing.PointF(s * 0.68f, s * 0.68f)
                    });
                    break;

                case TrayIconKind.Open:
                default:
                    // PackIcon Kind=Application — окно
                    g.DrawRectangle(pen, s * 0.16f, s * 0.18f, s * 0.68f, s * 0.64f);
                    g.DrawLine(pen, s * 0.16f, s * 0.36f, s * 0.84f, s * 0.36f);
                    g.FillEllipse(brush, s * 0.24f, s * 0.24f, s * 0.10f, s * 0.10f);
                    break;
            }

            return bmp;
        }

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

        private void MinimizeToTray()
        {
            if (_trayIcon == null)
                InitializeTrayIcon();
            if (_trayIcon != null)
                _trayIcon.Visible = true;
            Hide();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }

        /// <summary>
        /// Обработчик события «действие после запуска базы/конфигуратора»:
        /// сворачивает окно в трей или полностью закрывает приложение согласно
        /// глобальной настройке.
        /// </summary>
        private void OnAfterLaunchRequested(Models.AfterLaunchAction action)
        {
            if (action == Models.AfterLaunchAction.None)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (action == Models.AfterLaunchAction.MinimizeToTray)
                    MinimizeToTray();
                else if (action == Models.AfterLaunchAction.Close)
                {
                    // «Закрыть» — полностью завершить работу приложения, не уводя его в трей.
                    _forceClose = true;
                    Close();
                }
            }));
        }

        /// <summary>Уводит главное окно в системный трей (не завершая приложение).</summary>
        private void HideToTray()
        {
            if (_trayIcon == null)
                InitializeTrayIcon();
            if (_trayIcon != null)
                _trayIcon.Visible = true;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            // Иконку в трее оставляем видимой, если она включена в настройках.
            if (_trayIcon != null && _viewModel != null)
                _trayIcon.Visible = _viewModel.ShowTrayIcon;
        }

        /// <summary>
        /// Публичный вход для активации окна из другого экземпляра приложения
        /// (в том числе когда окно скрыто в системный трей).
        /// </summary>
        public void RestoreFromTrayPublic() => RestoreFromTray();

        private void DisposeTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void UpdateTrayVisibility()
        {
            if (_trayIcon == null && (_viewModel.ShowTrayIcon || _viewModel.CloseToTray))
            {
                InitializeTrayIcon();
                return;
            }
            if (_trayIcon != null)
                _trayIcon.Visible = _viewModel.ShowTrayIcon || _viewModel.CloseToTray;
        }

        /// <summary>
        /// Регистрирует настраиваемые горячие клавиши действий (запуск, правка, удаление и т.д.).
        /// </summary>
        private void RegisterLaunchHotkeys()
        {
            // Удаляем ранее зарегистрированные «пользовательские» биндинги (кроме Alt+1…9).
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Command is not null &&
                             kb.Modifiers != ModifierKeys.Alt)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            void Add(string? gesture, ICommand? command)
            {
                if (command is null) return;
                if (!TryParseKeyGesture(gesture, out var key, out var mods)) return;
                InputBindings.Add(new KeyBinding(command, key, mods));
            }

            Add(_viewModel.HotkeyEnterprise, _viewModel.LaunchEnterpriseCommand);
            Add(_viewModel.HotkeyConfigurator, _viewModel.LaunchConfiguratorCommand);
            Add(_viewModel.HotkeyFavorite, _viewModel.ToggleFavoriteCommand);
            Add(_viewModel.HotkeyEdit, _viewModel.EditInfobaseCommand);
            Add(_viewModel.HotkeyDelete, _viewModel.DeleteInfobaseCommand);
            Add(_viewModel.HotkeyClearCache, _viewModel.ClearCacheCommand);
            Add(_viewModel.HotkeyAdd, _viewModel.AddInfobaseCommand);
            Add(_viewModel.HotkeyPin, _viewModel.TogglePinCommand);
            // Переключение вкладок списка баз: Все / Избранное / Недавние.
            Add(_viewModel.HotkeyShowAll, _viewModel.ShowAllCommand);
            Add(_viewModel.HotkeyShowFavorites, _viewModel.ShowFavoritesCommand);
            Add(_viewModel.HotkeyShowRecent, _viewModel.ShowRecentCommand);
        }

        /// <summary>
        /// Разбирает жест вида «F3», «Delete», «Ctrl+F2», «Shift+Insert».
        /// </summary>
        internal static bool TryParseKeyGesture(string? text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text.Trim(), "—", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "-", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "Нет", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
                else
                    return false;
            }

            var keyPart = parts[^1];
            // Синонимы
            if (keyPart.Equals("Del", StringComparison.OrdinalIgnoreCase))
                keyPart = "Delete";
            if (keyPart.Equals("Ins", StringComparison.OrdinalIgnoreCase))
                keyPart = "Insert";
            if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase))
                keyPart = "Escape";

            if (!Enum.TryParse<Key>(keyPart, true, out var parsed) || parsed == Key.None)
                return false;

            key = parsed;
            return true;
        }

        /// <summary>
        /// Регистрирует KeyBinding Alt+1…Alt+9 для быстрого запуска избранных баз.
        /// </summary>
        private void RegisterFavoriteHotkeys()
        {
            // Удаляем предыдущие биндинги Alt+1…9
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Modifiers == ModifierKeys.Alt &&
                             kb.Key >= Key.D1 && kb.Key <= Key.D9)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            for (int i = 1; i <= 9; i++)
            {
                int index = i;
                var binding = new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.LaunchFavoriteByHotkey(index)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Alt);
                InputBindings.Add(binding);
            }
        }

        /// <summary>
        /// Надёжный обработчик Alt+1…9 (KeyBinding с Alt иногда перехватывается системой).
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Стрелки ↑/↓/←/→ управляют выделением в списке баз, только если
            // фокус находится в пределах дерева и не в поле ввода текста.
            // Это гарантирует, что стрелки всегда перемещают выделение по дереву,
            // а не «прыгают» по кнопкам внутри строки (избранное, закрепление, теги).
            if (key is Key.Up or Key.Down or Key.Left or Key.Right &&
                Keyboard.Modifiers == ModifierKeys.None &&
                Keyboard.FocusedElement is not TextBox &&
                IsFocusInsideMainTree())
            {
                if (HandleArrowNavigation(key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Esc → в трей (если включено в настройках)
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Не перехватываем, если фокус в поле ввода тега — там свой обработчик
                if (Keyboard.FocusedElement is TextBox { Name: "InlineTagBox" })
                    return;

                if (_viewModel.EscapeToTray && _viewModel.ShowTrayIcon)
                {
                    MinimizeToTray();
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+F → фокус в поле поиска (в том числе когда фокус в другом поле ввода)
            if (key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (SearchTextBox is not null)
                {
                    SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                    e.Handled = true;
                    return;
                }
            }

            if (Keyboard.Modifiers != ModifierKeys.Alt)
                return;

            if (key >= Key.D1 && key <= Key.D9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.D0);
                e.Handled = true;
            }
            else if (key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.NumPad0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Определяет, находится ли клавиатурный фокус внутри дерева баз.
        /// Возвращает false, если фокус вне дерева (поле поиска, кнопка верхней панели и т.п.).
        /// </summary>
        private bool IsFocusInsideMainTree()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused is not null && MainTree is not null &&
                   IsDescendantOf(focused, MainTree);
        }

        /// <summary>
        /// Проверяет, является ли <paramref name="candidate"/> потомком <paramref name="root"/> в визуальном дереве.
        /// </summary>
        private static bool IsDescendantOf(DependencyObject candidate, DependencyObject root)
        {
            for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, root))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Обрабатывает нажатие стрелки для навигации по дереву баз.
        /// ↑/↓ — перемещение по видимым строкам, ←/→ — раскрытие/сворачивание групп.
        /// Возвращает true, если событие обработано.
        /// </summary>
        private bool HandleArrowNavigation(Key key)
        {
            if (MainTree is null || _viewModel.GroupNodes.Count == 0)
                return false;

            // ↑/↓ — перемещение выделения по видимым узлам дерева.
            if (key is Key.Up or Key.Down)
            {
                var visible = GetVisibleTreeNodes();
                if (visible.Count == 0)
                    return false;

                var current = FindCurrentTreeNode(visible);
                var index = current is null ? -1 : visible.IndexOf(current);
                int targetIndex;

                if (current is null)
                {
                    targetIndex = key == Key.Down ? 0 : visible.Count - 1;
                }
                else
                {
                    var last = visible.Count - 1;
                    targetIndex = key == Key.Down
                        ? (index >= last ? last : index + 1)
                        : (index <= 0 ? 0 : index - 1);
                }

                if (targetIndex == index && current is not null)
                    return false;

                SelectTreeNode(visible[targetIndex]);
                return true;
            }

            // ←/→ — раскрытие/сворачивание выбранной группы.
            var selectedGroup = _viewModel.SelectedGroupNode ?? (_viewModel.SelectedInfobase is null
                ? null
                : FindGroupNodeByInfobase(_viewModel.SelectedInfobase));

            if (selectedGroup is not null)
            {
                if (key == Key.Right && !selectedGroup.IsExpanded && selectedGroup.Items.Count > 0)
                {
                    _viewModel.ToggleGroupExpandedCommand.Execute(selectedGroup);
                    return true;
                }
                if (key == Key.Left && selectedGroup.IsExpanded)
                {
                    _viewModel.ToggleGroupExpandedCommand.Execute(selectedGroup);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Собирает список узлов дерева в порядке их отображения (сверху вниз),
        /// включая развёрнутые подгруппы. Возвращает элементы GroupNodeViewModel и Infobase.
        /// </summary>
        private List<object> GetVisibleTreeNodes()
        {
            var result = new List<object>();
            foreach (var root in _viewModel.GroupNodes)
                AppendVisible(root, result);
            return result;
        }

        private static void AppendVisible(object node, List<object> result)
        {
            result.Add(node);

            if (node is not GroupNodeViewModel group || !group.IsExpanded)
                return;

            foreach (var item in group.Items)
                AppendVisible(item, result);
        }

        /// <summary>
        /// Определяет текущий выбранный узел дерева по модели представления.
        /// </summary>
        private object? FindCurrentTreeNode(List<object> visible)
        {
            if (_viewModel.SelectedInfobase is not null)
            {
                foreach (var node in visible)
                {
                    if (ReferenceEquals(node, _viewModel.SelectedInfobase))
                        return node;
                }
                return _viewModel.SelectedInfobase;
            }

            if (_viewModel.SelectedGroupNode is not null)
            {
                foreach (var node in visible)
                {
                    if (ReferenceEquals(node, _viewModel.SelectedGroupNode))
                        return node;
                }
                return _viewModel.SelectedGroupNode;
            }

            return null;
        }

        /// <summary>
        /// Выделяет указанный узел дерева (группу или базу), синхронизирует модель
        /// и переводит фокус на соответствующий TreeViewItem, чтобы дальнейшая
        /// навигация стрелками была стабильной и не «прыгала» на кнопки.
        /// </summary>
        private void SelectTreeNode(object node)
        {
            var item = FindTreeViewItemForData(node);
            switch (node)
            {
                case Infobase infobase:
                    if (item is not null)
                        ApplySelection(item, infobase);
                    else
                        _viewModel.SelectedInfobase = infobase;
                    break;
                case GroupNodeViewModel group when group.Group is not null:
                    if (item is not null)
                        ApplyGroupSelection(item, group);
                    else
                        _viewModel.SelectedGroupNode = group;
                    break;
            }

            if (item is not null)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
            else
            {
                Keyboard.Focus(MainTree);
            }

            // Прокручиваем список к выбранной строке (отложенно, чтобы контейнер
            // виртуализированного узла успел создаться после установки выделения).
            var scrollTarget = item;
            Dispatcher.BeginInvoke(new Action(() => ScrollSelectedIntoView(scrollTarget)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Восстанавливает последнюю выбранную строку списка (базу или группу) после запуска.
        /// Строка определяется по сохранённым значениям
        /// <see cref="ViewModels.MainViewModel.LastSelectedInfobaseId"/> и
        /// <see cref="ViewModels.MainViewModel.LastSelectedGroupPath"/>.
        /// </summary>
        private void RestoreLastSelection()
        {
            if (MainTree is null || _viewModel is null)
                return;

            object? target = null;

            var infobaseId = _viewModel.LastSelectedInfobaseId;
            if (!string.IsNullOrEmpty(infobaseId))
            {
                var ib = _viewModel.Infobases.FirstOrDefault(
                    i => string.Equals(i.Id, infobaseId, StringComparison.Ordinal));
                if (ib is not null)
                    target = ib;
            }
            else
            {
                var groupPath = _viewModel.LastSelectedGroupPath;
                if (!string.IsNullOrEmpty(groupPath))
                {
                    var groupNode = _viewModel.FindGroupNodeByPath(groupPath);
                    if (groupNode is not null)
                        target = groupNode;
                }
            }

            if (target is null)
                return;

            // Отложенно, чтобы виртуализированное дерево успело сгенерировать
            // контейнер строки после первой отрисовки.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (target is Infobase infobase)
                    SelectTreeNode(infobase);
                else if (target is GroupNodeViewModel group)
                    SelectTreeNode(group);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Прокручивает список так, чтобы указанный элемент дерева был в зоне видимости.
        /// Использует внутренний ScrollViewer дерева (вертикальная прокрутка списка).
        /// </summary>
        private void ScrollSelectedIntoView(TreeViewItem? item)
        {
            if (item is null)
                return;

            var scrollViewer = GetTreeScrollViewer();
            if (scrollViewer is null)
                return;

            try
            {
                var point = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                var top = point.Y;
                var bottom = top + item.ActualHeight;

                if (top < scrollViewer.VerticalOffset)
                {
                    scrollViewer.ScrollToVerticalOffset(top);
                }
                else if (bottom > scrollViewer.VerticalOffset + scrollViewer.ViewportHeight)
                {
                    scrollViewer.ScrollToVerticalOffset(bottom - scrollViewer.ViewportHeight);
                }
            }
            catch
            {
                // Элемент мог отсоединиться от визуального дерева — игнорируем.
            }
        }

        /// <summary>
        /// Возвращает контейнер TreeViewItem для указанного DataContext
        /// (поиск по всем раскрытым уровням дерева).
        /// </summary>
        private TreeViewItem? FindTreeViewItemForData(object data)
        {
            if (MainTree is null)
                return null;
            return FindTreeViewItemIn(MainTree, data);
        }

        private static TreeViewItem? FindTreeViewItemIn(ItemsControl parent, object data)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;

                if (ReferenceEquals(tvi.DataContext, data))
                    return tvi;

                if (tvi.Items.Count > 0)
                {
                    var found = FindTreeViewItemIn(tvi, data);
                    if (found is not null)
                        return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Находит узел группы, в котором размещена указанная база.
        /// </summary>
        private GroupNodeViewModel? FindGroupNodeByInfobase(Infobase infobase)
        {
            foreach (var root in _viewModel.GroupNodes)
            {
                var found = FindInNode(root, infobase);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private static GroupNodeViewModel? FindInNode(GroupNodeViewModel node, Infobase infobase)
        {
            foreach (var child in node.Children)
            {
                var found = FindInNode(child, infobase);
                if (found is not null)
                    return found;
            }
            if (node.Infobases.Any(ib => ReferenceEquals(ib, infobase)))
                return node;
            return null;
        }

        /// <summary>
        /// Обработчик клика по заголовку колонки для смены сортировки.
        /// </summary>
        private void OnColumnHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string field)
                return;
            _viewModel.SetSortField(field);
            e.Handled = true;
        }

        private void OnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ToggleTheme();
            UpdateThemeButton();
        }

        /// <summary>Переключатель компактного режима на верхней панели: применяет сразу и сохраняет.</summary>
        private void OnCompactMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (CompactModeButton is null || _viewModel is null)
                return;
            _viewModel.ApplyCompactMode(CompactModeButton.IsChecked == true);
        }

        /// <summary>
        /// Реакция на изменение компактного режима в модели: пересчитывает выравнивание
        /// колонок заголовка списка баз (см. <see cref="AlignHeaderToData"/>), т.к. компактный
        /// режим масштабирует отступы/шрифты и меняет положение данных относительно заголовков.
        /// Вызов откладывается, чтобы выполняться после применения раскладки компактного режима.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CompactMode))
            {
                Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (e.PropertyName == nameof(MainViewModel.ColumnOrderKeys))
            {
                // Пользователь поменял порядок колонок в настройках: пересобираем
                // заголовок и все уже созданные строки баз.
                Dispatcher.BeginInvoke(new Action(ApplyColumnOrder), System.Windows.Threading.DispatcherPriority.Loaded);
            }
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
        /// Строит целевую последовательность колонок (логические ключи) по выбранному
        /// пользователем порядку. Колонка «Действия» теперь участвует в порядке наравне
        /// с остальными и размещается там, куда её поставил пользователь в настройках.
        /// </summary>
        private List<string> BuildColumnLayout()
        {
            var known = new[] { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Configuration" };
            var keys = new List<string>();
            foreach (var k in known)
                if (_viewModel?.ColumnOrderKeys.Contains(k) == true && !keys.Contains(k))
                    keys.Add(k);
            // Гарантируем, что все известные колонки присутствуют (незнакомые ключи
            // из сохранённого порядка пропускаются, как в Avalonia-версии).
            foreach (var k in known)
                if (!keys.Contains(k))
                    keys.Add(k);

            return keys;
        }

        /// <summary>
        /// Перестраивает колонки данных сетки <paramref name="grid"/> (заголовка, строки базы
        /// или заголовка группы) под выбранный порядок: передвигает определения колонок и
        /// обновляет позиции размещённых в них элементов. Фиксированные колонки слева и
        /// «Название» не трогаются.
        /// </summary>
        private void ReorderGridColumns(Grid grid, int firstDataCol)
        {
            if (grid is null || _viewModel is null)
                return;

            var layout = BuildColumnLayout();
            var leading = firstDataCol;
            var dataCount = grid.ColumnDefinitions.Count - leading;
            if (dataCount <= 0)
                return;

            // Первый проход: определяем логический ключ для каждого перемещаемого элемента
            // региона данных по статической раскладке. Ключ сохраняется в attached-свойстве
            // ColumnKey (Tag занят — сортировка/двойной клик), поэтому повторные вызовы
            // корректно работают и после перестановок, и для уже перестроенных сеток.
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue; // объединённые ячейки (название/теги) двигаются отдельно
                var c = Grid.GetColumn(fe);
                if (c < leading || c >= leading + dataCount)
                    continue;
                if (string.IsNullOrEmpty(GetColumnKey(fe)))
                    SetColumnKey(fe, StaticDataColumnKeys[c - leading]);
            }

            // Сопоставление «позиция колонки данных -> логический ключ» по ключам детей.
            var defKey = new string[dataCount];
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (string.IsNullOrEmpty(s))
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue;
                var c = Grid.GetColumn(fe);
                if (c >= leading && c < leading + dataCount)
                    defKey[c - leading] = s;
            }

            // Для сеток без дочерних элементов в части колонок данных (например, строка группы,
            // где заполнена только колонка «Действия») ключ незаполненных колонок берём по
            // статической раскладке: такие сетки всегда создаются в статическом порядке.
            for (var i = 0; i < dataCount; i++)
                if (string.IsNullOrEmpty(defKey[i]))
                    defKey[i] = StaticDataColumnKeys[i];

            // Новый порядок определений колонок под нужную раскладку.
            var defs = grid.ColumnDefinitions;
            var newOrder = new List<ColumnDefinition>(dataCount);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in layout)
            {
                for (var i = 0; i < dataCount; i++)
                {
                    if (defKey[i] == token && placed.Add(token))
                    {
                        newOrder.Add(defs[leading + i]);
                        break;
                    }
                }
            }
            for (var i = 0; i < dataCount; i++)
                if (!placed.Contains(defKey[i]))
                {
                    newOrder.Add(defs[leading + i]);
                    placed.Add(defKey[i]);
                }

            // Пересобираем коллекцию определений: фиксированные слева + новый порядок данных.
            var leadingDefs = new List<ColumnDefinition>(leading);
            for (var i = 0; i < leading; i++)
                leadingDefs.Add(defs[i]);
            defs.Clear();
            foreach (var d in leadingDefs)
                defs.Add(d);
            foreach (var d in newOrder)
                defs.Add(d);

            // Обновляем позиции детей и span широких ячеек (до «Действий»).
            // Заголовок группы: имя/счётчик занимают область названия и тянутся до «Действий».
            var actionsColumn = leading + layout.IndexOf("Actions");
            var isGroup = ReferenceEquals(grid.Tag, GroupGridMarker);
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (!string.IsNullOrEmpty(s))
                {
                    var ti = layout.IndexOf(s);
                    if (ti >= 0)
                        Grid.SetColumn(fe, leading + ti);
                }
                else if (isGroup && Grid.GetRow(fe) == 0 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
                else if (Grid.GetRow(fe) == 1 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
            }
        }

        /// <summary>Рекурсивно собирает уже созданные сетки строк баз/заголовков групп по маркеру.</summary>
        private static List<Grid> FindRowGrids(DependencyObject? root, object marker)
        {
            var result = new List<Grid>();
            FindRowGridsCore(root, marker, result);
            return result;
        }

        private static void FindRowGridsCore(DependencyObject? parent, object marker, List<Grid> acc)
        {
            if (parent is null)
                return;
            if (parent is Grid g && ReferenceEquals(g.Tag, marker))
                acc.Add(g);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                FindRowGridsCore(VisualTreeHelper.GetChild(parent, i), marker, acc);
        }

        /// <summary>
        /// Находит первую сетку с указанным маркером внутри <paramref name="root"/>
        /// (используется для выравнивания заголовка по строке базы).
        /// </summary>
        private static Grid? FindGridByMarker(DependencyObject? root, object marker)
        {
            if (root is Grid g && ReferenceEquals(g.Tag, marker))
                return g;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindGridByMarker(VisualTreeHelper.GetChild(root, i), marker);
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Применяет выбранный порядок колонок к заголовку и всем созданным строкам баз
        /// и заголовкам групп. Вызывается при старте (после компоновки) и при изменении
        /// порядка в настройках.
        /// </summary>
        private void ApplyColumnOrder()
        {
            if (HeaderGrid is not null)
                ReorderGridColumns(HeaderGrid, HeaderFirstDataColumn);
            foreach (var grid in FindRowGrids(MainTree, RowGridMarker))
                ReorderGridColumns(grid, RowFirstDataColumn);
            foreach (var grid in FindRowGrids(MainTree, GroupGridMarker))
                ReorderGridColumns(grid, RowFirstDataColumn);
        }

        /// <summary>
        /// Обработчик Loaded сетки строки базы в шаблоне: применяет выбранный порядок
        /// колонок к каждой вновь созданной строке (включая строки, появляющиеся при
        /// виртуализации/прокрутке дерева).
        /// </summary>
        private void OnInfobaseRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            ReorderGridColumns(grid, RowFirstDataColumn);
            grid.Tag = RowGridMarker;
        }

        /// <summary>
        /// Обработчик Loaded сетки заголовка группы в шаблоне: применяет выбранный порядок
        /// колонок, чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        /// </summary>
        private void OnGroupRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            ReorderGridColumns(grid, RowFirstDataColumn);
            grid.Tag = GroupGridMarker;
        }

        /// <summary>
        /// Выравнивает колонки заголовка по фактическому положению колонки «Название»
        /// первой видимой базы в списке. Это необходимо, потому что при группировке
        /// базы смещаются вправо отступами вложенности дерева, и фиксированный сдвиг
        /// заголовка (рассчитанный для баз верхнего уровня) перестаёт совпадать с данными.
        /// </summary>
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            AttachTreeScrollHandler();
            if (MainTree is not null)
            {
                MainTree.Loaded += (_, __) => AttachTreeScrollHandler();
            }

            AlignHeaderToData();
            // Повторное выравнивание после завершения первичной компоновки,
            // когда уже известны реальные размеры контейнеров дерева.
            Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);

            // Применяем сохранённый пользователем порядок колонок списка баз.
            Dispatcher.BeginInvoke(new Action(ApplyColumnOrder), System.Windows.Threading.DispatcherPriority.Loaded);

            // Применяем сохранённый компактный режим при старте. Делаем это здесь, на
            // событии Loaded, когда визуальное дерево окна уже построено (ApplyCompact
            // обходит его через VisualTreeHelper; до показа дерево пустое и масштабирование
            // не срабатывает). После применения пересчитываем выравнивание колонок заголовка.
            if (_viewModel.CompactMode)
            {
                ThemeManager.ApplyCompact(true);
                Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // Запускаем автоматическую синхронизацию с файлом ibases.v8i.
            _viewModel.StartAutoSync();
        }

        /// <summary>
        /// Пересчитывает выравнивание заголовка при переключении режима группировки,
        /// когда дерево перестраивается и меняется глубина вложенности баз.
        /// </summary>
        private void OnGroupByToggle_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Верхняя кнопка «теги»: помимо переключения панели быстрого отбора тегов
        /// (привязка ShowTagFilterPanel) синхронно управляет и тегами в списке баз.
        /// При выключении запоминает текущее состояние нижней кнопки тегов, а при
        /// повторном включении восстанавливает его (не включает нижнюю кнопку принудительно).
        /// </summary>
        private void OnTopTagsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle || DataContext is not MainViewModel vm)
                return;

            if (toggle.IsChecked == true)
            {
                // Включение: возвращаем нижней кнопке состояние, которое было до выключения
                // верхней кнопкой (либо оставляем текущее, если ранее не выключали).
                if (_savedTagsStateBeforeTopOff is bool saved)
                    vm.ShowTags = saved;
                _savedTagsStateBeforeTopOff = null;
            }
            else
            {
                // Выключение: запоминаем состояние нижней кнопки и выключаем теги в списке.
                _savedTagsStateBeforeTopOff = vm.ShowTags;
                vm.ShowTags = false;
            }
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы первая колонка данных заголовка точно совпадала по горизонтали с первой
        /// колонкой данных строки базы. Строка базы и заголовок имеют одинаковый набор
        /// ведущих колонок, поэтому колонки данных всех строк (которые не смещаются
        /// отступами вложенности) оказываются на одной линии с заголовками.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderGrid is null || HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var rowGrid = FindGridByMarker(item, RowGridMarker);
            if (rowGrid is null)
                return;

            // Позиция первой колонки данных строки базы (отсчитывается от левого края сетки).
            double rowStart = 0;
            for (var i = 0; i < RowFirstDataColumn; i++)
                rowStart += rowGrid.ColumnDefinitions[i].ActualWidth;
            var rowOrigin = rowGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            // Позиция первой колонки данных заголовка без учёта текущей ширины компенсатора Offset.
            double headerStart = 0;
            for (var i = 0; i < HeaderFirstDataColumn; i++)
            {
                if (!ReferenceEquals(HeaderGrid.ColumnDefinitions[i], HeaderOffsetColumn))
                    headerStart += HeaderGrid.ColumnDefinitions[i].ActualWidth;
            }
            var headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            var offset = Math.Max(0, (rowOrigin + rowStart) - (headerOrigin + headerStart));
            if (Math.Abs(offset - HeaderOffsetColumn.Width.Value) > 0.5)
                HeaderOffsetColumn.Width = new GridLength(offset);

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Выравнивает ширину сетки заголовка с контентом списка, чтобы горизонтальная
        /// прокрутка «до конца» не разъезжала колонки заголовка и строк.
        /// Важно: ширина заголовка не должна включать ширину вертикальной полосы прокрутки
        /// (sbw), иначе гибкая колонка «Название» в заголовке растянется шире, чем в данных,
        /// и фиксированные колонки данных окажутся смещёнными влево относительно заголовков.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            var treeScroll = GetTreeScrollViewer();
            double extent = MainTree.ActualWidth;
            double viewport = MainTree.ActualWidth;
            if (treeScroll is not null)
            {
                extent = Math.Max(treeScroll.ExtentWidth, treeScroll.ViewportWidth);
                viewport = treeScroll.ViewportWidth;
            }

            // Не добавляем sbw: заголовок и данные должны иметь одинаковую общую ширину,
            // чтобы колонки данных совпадали с колонками заголовка.
            double target = Math.Max(extent, viewport);
            if (target > 0)
                HeaderGrid.Width = target;
        }

        /// <summary>
        /// Ищет первый реально созданный (видимый) элемент дерева с базой.
        /// </summary>
        private static TreeViewItem? FindFirstInfobaseItem(DependencyObject parent)
        {
            if (parent is TreeViewItem tvi && tvi.DataContext is Infobase)
                return tvi;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindFirstInfobaseItem(VisualTreeHelper.GetChild(parent, i));
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Находит текстовый элемент названия базы (x:Name=NameText) в строке.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && tb.Name == "NameText")
                    return tb;

                var result = FindNameCell(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Синхронизирует выделение в дереве с выбранной базой.
        /// При выборе группы снимает выделение базы.
        /// </summary>
        private void OnMainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Выбором базы управляет code-behind через обработчики кликов
            // (OnInfobaseTree_PreviewMouseLeftButtonDown), которые явно устанавливают
            // TreeViewItem.IsSelected и SelectedInfobase. Здесь лишь фиксируем результат
            // изменения выбранного элемента, не трогая свойство Infobase.IsSelected.
            // Ранее двухсторонняя привязка IsSelected к модели порождала каскад событий
            // SelectedItemChanged (база дублируется в «Закреплённых» и в своей группе),
            // что приводило к бесконечной рекурсии и StackOverflowException.
            if (e.NewValue is Infobase infobase)
            {
                _viewModel.SelectedInfobase = infobase;
                // Выбор базы снимает выбор группы.
                _viewModel.SelectedGroupNode = null;
            }
            else if (e.NewValue is GroupNodeViewModel groupNode)
            {
                // Выбор группы снимает выбор базы и фиксирует выбранную группу.
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = groupNode;
            }
            else if (e.NewValue is null)
            {
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = null;
            }

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // чтобы они активировались при программной установке выделения в дереве.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Открывает выпадающее меню выбора типа клиента при нажатии на стрелку
        /// кнопки запуска 1С:Предприятие.
        /// </summary>
        private void OnLaunchSplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            // Открываем меню отложенно, чтобы клик по кнопке не закрыл его сразу.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                button.ContextMenu.IsOpen = true;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnInfobaseTree_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Двойной клик по группе сворачивает/разворачивает её в зависимости от текущего состояния.
            // Двойной клик по ячейке «Версия платформы» — выбор версии без полного окна свойств.
            // Двойной клик по базе по-прежнему запускает 1С.
            var source = e.OriginalSource as DependencyObject;

            if (source is FrameworkElement fe
                && string.Equals(fe.Tag as string, "PlatformVersion", StringComparison.Ordinal)
                && fe.DataContext is Infobase versionIb)
            {
                OpenPlatformVersionPicker(versionIb);
                e.Handled = true;
                return;
            }

            // Клик по дочернему Run/тексту внутри TextBlock с Tag
            var tagged = source is null ? null : FindAncestorWithTag(source, "PlatformVersion");
            if (tagged?.DataContext is Infobase versionIb2)
            {
                OpenPlatformVersionPicker(versionIb2);
                e.Handled = true;
                return;
            }

            var treeViewItem = source is null ? null : FindAncestor<TreeViewItem>(source);
            if (treeViewItem?.DataContext is GroupNodeViewModel groupNode && groupNode.Group is not null)
            {
                _viewModel.ToggleGroupExpandedCommand.Execute(groupNode);
                return;
            }

            if (_viewModel.LaunchEnterpriseCommand.CanExecute(null))
            {
                _viewModel.LaunchEnterpriseCommand.Execute(null);
            }
        }

        private static FrameworkElement? FindAncestorWithTag(DependencyObject? current, string tag)
        {
            while (current is not null)
            {
                if (current is FrameworkElement fe && string.Equals(fe.Tag as string, tag, StringComparison.Ordinal))
                    return fe;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void OpenPlatformVersionPicker(Infobase ib)
        {
            _viewModel.SelectedInfobase = ib;
            var dialog = new PlatformVersionPickerWindow(_viewModel.InstalledPlatformVersions, ib.PlatformVersion)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;

            var selected = dialog.Result?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected))
                return;

            // Разбираем выбранный вариант: суффикс разрядности «(32)/(64)» выносим
            // в отдельное поле Architecture, а в PlatformVersion сохраняем чистую версию.
            PlatformVersionService.ParseVariant(selected, out var cleanVersion, out var arch);
            var newVersion = string.IsNullOrWhiteSpace(cleanVersion) ? selected : cleanVersion;
            if (string.Equals(ib.PlatformVersion, newVersion, StringComparison.Ordinal))
                return;

            ib.PlatformVersion = newVersion;
            if (arch == "32" || arch == "64")
                ib.Architecture = arch;
            _viewModel.PersistInfobasesAfterInlineEdit();
        }

        /// <summary>
        /// Выделяет базу или группу под курсором при правом клике в дереве,
        /// чтобы команды контекстного меню применялись именно к этому элементу.
        /// </summary>
        private void OnInfobaseTree_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView is null)
            {
                return;
            }

            // Если клик попал по строке базы или группы, выделяем её.
            var source = e.OriginalSource as DependencyObject;
            var treeViewItem = source is null ? null : FindAncestor<TreeViewItem>(source);
            switch (treeViewItem?.DataContext)
            {
                case Infobase infobase:
                    treeViewItem.IsSelected = true;
                    _viewModel.SelectedInfobase = infobase;
                    break;
                case GroupNodeViewModel groupNode when groupNode.Group is not null:
                    treeViewItem.IsSelected = true;
                    _viewModel.SelectedInfobase = null;
                    _viewModel.SelectedGroupNode = groupNode;
                    break;
            }
        }

        /// <summary>
        /// Выделяет базу или группу под курсором при левом клике в дереве.
        /// Сами устанавливаем выбор и помечаем событие обработанным, чтобы
        /// собственная логика TreeView не сбросила выделение. Клики по
        /// интерактивным элементам строки (кнопки, поле ввода) не
        /// перехватываются, чтобы они продолжали работать.
        /// </summary>
        private void OnInfobaseTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Payload DnD фиксируем здесь (не в MouseMove): иначе при сдвиге курсора
            // на дочернюю базу TreeViewItem под курсором меняется и «уезжает» не группа, а базы.
            CaptureDragStart(e);

            var treeView = sender as TreeView;
            if (treeView is null)
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            if (source is null)
            {
                return;
            }

            // Если клик пришёлся по интерактивному элементу (кнопка, поле ввода),
            // не вмешиваемся и не начинаем drag.
            if (FindAncestor<Button>(source) is not null ||
                FindAncestor<TextBox>(source) is not null)
            {
                _draggedData = null;
                return;
            }

            var treeViewItem = FindAncestor<TreeViewItem>(source);
            if (treeViewItem is null)
            {
                return;
            }

            switch (treeViewItem.DataContext)
            {
                case Infobase infobase:
                    _draggedData = infobase;
                    ApplySelection(treeViewItem, infobase);
                    break;
                case GroupNodeViewModel groupNode when groupNode.Group is not null:
                    _draggedData = groupNode;
                    ApplyGroupSelection(treeViewItem, groupNode);
                    break;
                default:
                    _draggedData = null;
                    return;
            }

            // Переводим клавиатурный фокус на строку дерева (отложенно, после завершения
            // обработки клика), чтобы последующие нажатия стрелок управляли выделением
            // в дереве, а не «прыгали» по кнопкам внутри строки.
            var focusTarget = treeViewItem;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (focusTarget is not null)
                {
                    focusTarget.Focus();
                    Keyboard.Focus(focusTarget);
                }
            }), System.Windows.Threading.DispatcherPriority.Input);

            // Помечаем клик обработанным, чтобы TreeView не сбросил выбранный элемент.
            e.Handled = true;
        }

        /// <summary>
        /// Блокирует автоматическую прокрутку TreeView к выделенному элементу
        /// (по умолчанию WPF вызывает BringIntoView при IsSelected/Focus — список «прыгает» вверх).
        /// </summary>


        /// <summary>
        /// Синхронизирует IsExpanded контейнеров TreeView с моделью.
        /// Только узлы GroupNodeViewModel; без многократных layout-проходов.
        /// </summary>
        internal void ApplyGroupExpandedState(bool expand)
        {
            if (MainTree is null)
                return;

            if (expand)
                ExpandGroupContainers(MainTree);
            else
                CollapseGroupContainers(MainTree);
        }

        private static void CollapseGroupContainers(ItemsControl parent)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is not ViewModels.GroupNodeViewModel node)
                    continue;
                node.NotifyIsExpanded();
            }
        }

        /// <summary>
        /// Раскрывает только контейнеры групп. Вложенность подтянется из Binding IsExpanded
        /// (в модели уже выставлено silent), без ручного обхода каждого уровня.
        /// </summary>
        private static void ExpandGroupContainers(ItemsControl parent)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is not ViewModels.GroupNodeViewModel node)
                    continue;
                node.NotifyIsExpanded();
                // Уже раскрытый узел: обновить вложенные контейнеры, если они есть.
                if (tvi.IsExpanded)
                    ExpandGroupContainers(tvi);
            }
        }

        private void OnMainTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// Устанавливает выделение указанного узла группы и синхронизирует
        /// выбранную группу в модели представления (снимая выбор базы).
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplyGroupSelection(TreeViewItem item, GroupNodeViewModel groupNode)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = null;
            _viewModel.SelectedGroupNode = groupNode;

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // т.к. программная установка выделения не всегда гарантирует автоматический
            // пересчёт CanExecute команд через CommandManager.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Устанавливает выделение указанного элемента дерева и синхронизирует
        /// выбранную базу в модели представления.
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplySelection(TreeViewItem item, Infobase infobase)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = infobase;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// Показывает поле ввода тега прямо в строке названия базы.
        /// </summary>
        private void OnAddTagInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // InlineTagBox находится в том же StackPanel, что и кнопка «+ тег»,
            // поэтому ищем его через общий предок TreeViewItem.
            var treeViewItem = FindAncestor<TreeViewItem>(button);
            var tagBox = treeViewItem is null ? null : FindVisualChild<TextBox>(treeViewItem);
            if (tagBox is null)
                return;

            // Скрываем кнопку «+ тег» и показываем поле ввода на её месте.
            button.Visibility = Visibility.Collapsed;
            tagBox.Text = string.Empty;
            tagBox.Visibility = Visibility.Visible;
            tagBox.Focus();
            Keyboard.Focus(tagBox);
        }

        /// <summary>
        /// Удаляет тег из базы при нажатии на кнопку «✕» у тега.
        /// </summary>
        private void OnRemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // База определяется через общий предок TreeViewItem.
            var treeViewItem = FindAncestor<TreeViewItem>(button);
            if (treeViewItem?.DataContext is not Infobase infobase)
                return;

            // Тег — это DataContext кнопки (кнопка находится в ItemsControl.ItemTemplate тегов).
            if (button.DataContext is not string tag)
                return;

            if (_viewModel.RemoveTagCommand.CanExecute(null))
            {
                _viewModel.RemoveTagCommand.Execute(new object[] { infobase, tag });
            }
        }

        /// <summary>
        /// Обрабатывает нажатие Enter в поле ввода тега: добавляет тег и скрывает поле.
        /// </summary>
        private void OnInlineTagBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelInlineTag(sender as TextBox);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter)
                return;

            CommitInlineTag(sender as TextBox);
            e.Handled = true;
        }

        /// <summary>
        /// При потере фокуса полем ввода тега — сохраняем непустой тег и всегда скрываем поле.
        /// </summary>
        private void OnInlineTagBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Dispatcher: клик вне поля сначала переводит фокус, затем обрабатываем.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (sender is TextBox { Visibility: Visibility.Visible } box)
                    CommitInlineTag(box);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>Скрывает поле тега без добавления (Esc).</summary>
        private void CancelInlineTag(TextBox? tagBox)
        {
            if (tagBox is null) return;
            tagBox.Text = string.Empty;
            HideInlineTagBox(tagBox);
        }

        private void HideInlineTagBox(TextBox tagBox)
        {
            tagBox.Visibility = Visibility.Collapsed;
            var treeViewItem = FindAncestor<TreeViewItem>(tagBox);
            var addButton = treeViewItem is null
                ? null
                : FindVisualChildByName<Button>(treeViewItem, "AddTagButton");
            if (addButton is not null)
                addButton.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Добавляет введённый тег к базе и скрывает поле ввода.
        /// </summary>
        private void CommitInlineTag(TextBox? tagBox)
        {
            if (tagBox is null || tagBox.Visibility != Visibility.Visible)
                return;

            var infobase = tagBox.DataContext;
            var tag = tagBox.Text?.Trim() ?? string.Empty;

            HideInlineTagBox(tagBox);
            tagBox.Text = string.Empty;

            if (string.IsNullOrEmpty(tag) || infobase is null)
                return;

            if (_viewModel.AddTagInlineCommand.CanExecute(null))
            {
                _viewModel.AddTagInlineCommand.Execute(new object[] { infobase, tag });
            }
        }

        /// <summary>
        /// Ищет дочерний элемент заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Ищет дочерний элемент заданного типа с указанным именем в визуальном дереве.
        /// </summary>
        private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && typedChild.Name == name)
                    return typedChild;

                var result = FindVisualChildByName<T>(child, name);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Применяет сохранённые ширины колонок списка баз.
        /// Ширины уже загружены в модель (VersionColumnWidth и т.д.), а колонки заголовка
        /// и строки данных привязаны к ним через ColumnVisibilityConverter, поэтому ручная
        /// установка Width не требуется и лишь перебивала бы binding, рассинхронизируя
        /// заголовок с данными.
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            // Колонка «Название» — гибкая (*), фиксированную ширину не задаём.
            // Остальные колонки применяют сохранённые ширины автоматически через binding.
        }

        // Поля для ручного перетаскивания разделителя колонок.
        private ColumnDefinition? _resizeColumn;
        private double _resizeStartWidth;
        private Point _resizeStartMouse;

        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель расположен на правом краю своей колонки (Grid.Column=N),
        /// поэтому он меняет ширину колонки с тем же индексом N.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, NameSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, VersionSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ConfigurationSplitter))
                return ConfigurationColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, ActionsSplitter))
                return ActionsColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return ServerColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return LastLaunchColumn;
            if (ReferenceEquals(sender, SizeSplitter))
                return SizeColumn;
            return null;
        }

        /// <summary>
        /// Начинает перетаскивание разделителя: захватывает мышь и запоминает стартовые значения.
        /// </summary>
        private void OnColumnResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var column = GetSplitterTargetColumn(sender);
            if (column is null)
                return;

            _resizeColumn = column;
            _resizeStartWidth = column.ActualWidth;
            _resizeStartMouse = e.GetPosition(this);

            if (sender is UIElement element)
                element.CaptureMouse();

            e.Handled = true;
        }

        /// <summary>
        /// Меняет ширину только целевой колонки при движении мыши.
        /// Ширина записывается в модель (VersionColumnWidth и т.д.), к которой привязаны
        /// и колонка заголовка, и колонки данных — поэтому заголовок и данные синхронно
        /// изменяются. Прямая установка Width перебивала бы binding и рассинхронизировала их.
        /// </summary>
        private void OnColumnResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeColumn is null || sender is not UIElement element || !element.IsMouseCaptured)
                return;

            var current = e.GetPosition(this);
            var delta = current.X - _resizeStartMouse.X;

            var newWidth = _resizeStartWidth + delta;
            if (newWidth < 40)
                newWidth = 40;

            if (ReferenceEquals(_resizeColumn, SizeColumn))
            {
                // SizeColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.SizeColumnWidth = newWidth;
                return;
            }

            _viewModel.UpdateColumnWidths(
                ReferenceEquals(_resizeColumn, NameColumn) ? newWidth : NameColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, VersionColumn) ? newWidth : VersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationColumn) ? newWidth : ConfigurationColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LaunchModeColumn) ? newWidth : LaunchModeColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ServerColumn) ? newWidth : ServerColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LastLaunchColumn) ? newWidth : LastLaunchColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ActionsColumn) ? newWidth : ActionsColumn?.ActualWidth ?? 0);
        }

        /// <summary>
        /// Завершает перетаскивание разделителя и сохраняет ширины колонок.
        /// </summary>
        private void OnColumnResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.ReleaseMouseCapture();

            if (_resizeColumn is not null)
            {
                _viewModel.SaveColumnWidths(
                    NameColumn?.ActualWidth ?? 0,
                    VersionColumn?.ActualWidth ?? 0,
                    ConfigurationColumn?.ActualWidth ?? 0,
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0,
                    ActionsColumn?.ActualWidth ?? 0);
                SyncHeaderWidthWithList();
            }

            _resizeColumn = null;
            e.Handled = true;
        }

        private void UpdateThemeButton()
        {
            if (ThemeToggleButton is null)
                return;

            // В тёмной теме кнопка предлагает перейти на светлую (иконка солнца), и наоборот.
            var isDark = ThemeManager.CurrentTheme == ThemeManager.DarkThemeName;
            ThemeToggleButton.ToolTip = isDark ? LocalizationManager.T("Main.LightTheme") : LocalizationManager.T("Main.DarkTheme");

            if (ThemeToggleIcon is not null)
            {
                ThemeToggleIcon.Data = isDark
                    ? (System.Windows.Media.Geometry)FindResource("IconSun")
                    : (System.Windows.Media.Geometry)FindResource("IconMoon");
            }
            else if (ThemeToggleButton.Content is System.Windows.Shapes.Path path)
            {
                path.Data = isDark
                    ? (System.Windows.Media.Geometry)FindResource("IconSun")
                    : (System.Windows.Media.Geometry)FindResource("IconMoon");
            }
        }

        /// <summary>
        /// Обработчик смены языка интерфейса. Выполняется на UI-потоке (при необходимости через
        /// диспетчер), чтобы элементы, заданные в code-behind, обновились сразу без перезапуска.
        /// Индексаторные LocExtension-привязки XAML обновляются сами через
        /// <see cref="LocalizationManager.Source"/>, а остальные привязки обновляются
        /// принудительно через проход по визуальному дереву
        /// (<see cref="RefreshAllBindingsOnVisualTree"/>).
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.CheckAccess())
                RebuildAfterLanguageChange();
            else
                Dispatcher.BeginInvoke(new Action(RebuildAfterLanguageChange));
        }

        /// <summary>
        /// Пересобирает элементы интерфейса, заданные в code-behind, при смене языка:
        /// заголовок окна (перекрытый локальным значением с версией), подсказку кнопки смены темы,
        /// подсказку и меню трея. Работает для любого направления (ru <-> en и внешние языки).
        /// </summary>
        private void RebuildAfterLanguageChange()
        {
            // XAML-привязка Title="{loc:Loc App.Title}" была перекрыта в конструкторе
            // локальным значением с версией, поэтому заголовок собираем заново.
            Title = $"{LocalizationManager.T("App.Title")} v{_infoVersion}";

            // Подсказка кнопки смены темы («Переключить на светлую/тёмную») зависит от языка.
            UpdateThemeButton();

            // Подсказка и меню трея тоже должны переключиться на новый язык.
            if (_trayIcon is not null)
            {
                _trayIcon.Text = LocalizationManager.T("App.Title");
                if (_trayIcon.ContextMenuStrip is Forms.ContextMenuStrip menu)
                    RebuildTrayMenu(menu);
            }

            // Принудительно обновляем целевые значения всех привязок визуального/логического
            // дерева окна. Это чинит элементы, которые не реагируют на PropertyChanged("Item[]")
            // (например MultiBinding-подсказки кнопок запуска с Path="Source" + конвертер).
            RefreshAllBindingsOnVisualTree();
        }

        /// <summary>
        /// Обходит визуальное (и логическое, где необходимо) дерево окна и принудительно вызывает
        /// <c>UpdateTarget()</c> для всех найденных привязок через
        /// <see cref="BindingOperations.GetBindingExpressionBase(DependencyObject, DependencyProperty)"/>.
        /// Нужно для полноты обновления при смене языка: индексаторные привязки {loc:Loc}
        /// обновляются сами, а привязки с Path="Source" + конвертер (например
        /// MultiBinding-подсказки кнопок запуска) — только при вызове UpdateTarget().
        /// Все операции обёрнуты в try/catch, чтобы сбой в одной привязке не прерывал
        /// пересборку интерфейса.
        /// </summary>
        private void RefreshAllBindingsOnVisualTree()
        {
            var visited = new HashSet<DependencyObject>();
            try
            {
                // Корневой элемент содержимого окна.
                if (Content is DependencyObject root)
                    UpdateBindingTargetsRecursive(root, visited);
                // Само окно (заголовок, атрибуты окна и т.п.).
                UpdateBindingTargetsRecursive(this, visited);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[l10n] RefreshAllBindings failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Рекурсивно обходит визуальное дерево и дополняет его логическими детьми,
        /// отсутствующими в визуальном дереве (переход между визуальным и логическим
        /// деревом). Посещённые узлы не обрабатываются повторно.
        /// </summary>
        private static void UpdateBindingTargetsRecursive(DependencyObject d, HashSet<DependencyObject> visited)
        {
            if (d is null || !visited.Add(d))
                return;

            UpdateBindingTarget(d);

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(d); }
            catch { return; }

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var child = VisualTreeHelper.GetChild(d, i);
                    if (child is not null)
                        UpdateBindingTargetsRecursive(child, visited);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[l10n] visual child walk failed: " + ex.Message);
                }
            }

            // Переход между визуальным и логическим деревом.
            if (d is FrameworkElement fe)
            {
                try
                {
                    foreach (var logicalChild in LogicalTreeHelper.GetChildren(fe))
                    {
                        if (logicalChild is DependencyObject lo && !visited.Contains(lo))
                            UpdateBindingTargetsRecursive(lo, visited);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[l10n] logical child walk failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Обновляет целевые значения всех привязок на указанном элементе
        /// (включая MultiBinding). Обёрнуто в try/catch.
        /// </summary>
        private static void UpdateBindingTarget(DependencyObject d)
        {
            try
            {
                var enumerator = d.GetLocalValueEnumerator();
                while (enumerator.MoveNext())
                {
                    var dp = enumerator.Current.Property;
                    if (dp is null)
                        continue;

                    try
                    {
                        BindingOperations.GetBindingExpressionBase(d, dp)?.UpdateTarget();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[l10n] UpdateTarget(" + dp.Name + ") failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[l10n] GetLocalValueEnumerator failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Внутренний ScrollViewer шаблона TreeView (отвечает за вертикальную и горизонтальную прокрутку).
        /// </summary>
        private ScrollViewer? GetTreeScrollViewer()
        {
            if (MainTree is null)
                return null;
            // Шаблон может быть ещё не применён.
            MainTree.ApplyTemplate();
            return FindVisualChild<ScrollViewer>(MainTree);
        }

        /// <summary>
        /// Подписывается на ScrollChanged внутреннего ScrollViewer дерева (синхронизация заголовка).
        /// </summary>
        private void AttachTreeScrollHandler()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            treeScroll.ScrollChanged -= OnTreeScroll_ScrollChanged;
            treeScroll.ScrollChanged += OnTreeScroll_ScrollChanged;
        }

        private void OnTreeScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (DbHeaderScroll is null)
                return;

            if (Math.Abs(DbHeaderScroll.HorizontalOffset - e.HorizontalOffset) > 0.01)
                DbHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

            if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
                SyncHeaderWidthWithList();
        }

        private void OnMainTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        private void OnDbHeader_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        /// <summary>
        /// Прокрутка списка: колесо — вертикаль, Shift+колесо — горизонталь.
        /// Всегда помечает событие обработанным, чтобы вложенные элементы не «съедали» колесо.
        /// </summary>
        private void ScrollListByWheel(MouseWheelEventArgs e)
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
            {
                // Повторная попытка после загрузки шаблона.
                AttachTreeScrollHandler();
                treeScroll = GetTreeScrollViewer();
            }

            if (treeScroll is null)
                return;

            // e.Delta обычно ±120; делим для плавности.
            var offset = -e.Delta / 3.0;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                treeScroll.ScrollToHorizontalOffset(treeScroll.HorizontalOffset + offset);
            }
            else
            {
                treeScroll.ScrollToVerticalOffset(treeScroll.VerticalOffset + offset);
            }

            e.Handled = true;
        }

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

        private void OnMainTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging || _draggedData is null)
                return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Не переопределяем payload по позиции — он зафиксирован в MouseDown.
            var data = new DataObject();
            if (_draggedData is Infobase ib)
                data.SetData(DragFormatInfobase, ib);
            else if (_draggedData is GroupNodeViewModel gn)
                data.SetData(DragFormatGroup, gn);
            else
                return;

            // Дублируем по Type — совместимость с GetData(typeof(...)).
            data.SetData(_draggedData.GetType(), _draggedData);

            _isDragging = true;
            try
            {
                DragDrop.DoDragDrop(MainTree, data, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
                _draggedData = null;
            }
        }

        /// <summary>
        /// Шаг накопительного отступа вложенности дерева (см. Margin у ItemsHost
        /// в ControlTemplate TreeViewItem: "18,0,0,0" на каждый уровень).
        /// Базы внутри групп смещаются вправо на этот шаг, чтобы была видна
        /// иерархия «группа в группе».
        /// </summary>
        private const double GroupTreeIndentStep = 18.0;

        /// <summary>Ширина кнопки разворота группы (px). Синхронизирована с Expander Width в XAML.</summary>
        private const double GroupTreeExpanderWidth = 26.0;

        /// <summary>
        /// Считает количество РОДИТЕЛЬСКИХ (не считая собственного) TreeViewItem
        /// от строки базы до корня TreeView — это и есть число уровней вложенности
        /// групп, чьи ItemsPresenter.Margin реально сдвигают строку вправо.
        /// Собственный TreeViewItem строки базы (лист без детей) в счёт не идёт:
        /// его ItemsPresenter ничего не сдвигает, так как у листа нет дочерних строк.
        /// </summary>
        private static int CountAncestorTreeViewItems(DependencyObject node)
        {
            var depth = 0;
            var skippedOwnContainer = false;
            var parent = VisualTreeHelper.GetParent(node);
            while (parent is not null)
            {
                if (parent is TreeViewItem)
                {
                    if (!skippedOwnContainer)
                        skippedOwnContainer = true;
                    else
                        depth++;
                }
                else if (parent is TreeView)
                {
                    break;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
            return depth;
        }

        private void OnMainTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void OnMainTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            var payload = ResolveDragPayload(e);
            ResolveDropTarget(e.OriginalSource as DependencyObject, out var targetGroup, out _);
            if (payload is null || targetGroup is null)
            {
                e.Handled = true;
                return;
            }

            if (payload is Infobase)
            {
                e.Effects = DragDropEffects.Move;
            }
            else if (payload is GroupNodeViewModel sourceNode && sourceNode.Group is not null)
            {
                var targetId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceNode.Group.Id, targetId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(targetId)
                        || !GroupHierarchyHelper.IsAncestorOrSelf(targetId, sourceNode.Group.Id, _viewModel.Groups)))
                {
                    e.Effects = DragDropEffects.Move;
                }
            }

            e.Handled = true;
        }

        private void OnMainTree_Drop(object sender, DragEventArgs e)
        {
            var payload = ResolveDragPayload(e);
            if (payload is null)
            {
                e.Handled = true;
                return;
            }

            ResolveDropTarget(e.OriginalSource as DependencyObject,
                out var targetGroup, out var insertBefore);

            if (payload is GroupNodeViewModel sourceGroupNode
                && sourceGroupNode.Group is not null
                && targetGroup is not null)
            {
                var newParentId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceGroupNode.Group.Id, newParentId, StringComparison.OrdinalIgnoreCase))
                    _viewModel.MoveGroupUnder(sourceGroupNode.Group, newParentId);

                e.Handled = true;
                return;
            }

            if (payload is Infobase infobase && targetGroup is not null)
            {
                if (string.Equals(targetGroup.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                {
                    _viewModel.MoveInfobaseToGroup(infobase, infobase.Group ?? string.Empty, insertBefore);
                }
                else
                {
                    var path = targetGroup.Group is null
                        ? string.Empty
                        : GroupHierarchyHelper.GetFullPath(targetGroup.Group, _viewModel.Groups);
                    if (insertBefore is not null && ReferenceEquals(insertBefore, infobase))
                        insertBefore = null;
                    _viewModel.MoveInfobaseToGroup(infobase, path, insertBefore);
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// Полезная нагрузка DnD: сначала поле (валидно во время DoDragDrop), затем DataObject.
        /// </summary>
        private object? ResolveDragPayload(DragEventArgs e)
        {
            if (_draggedData is Infobase or GroupNodeViewModel)
                return _draggedData;

            if (e.Data.GetDataPresent(DragFormatGroup))
                return e.Data.GetData(DragFormatGroup);
            if (e.Data.GetDataPresent(DragFormatInfobase))
                return e.Data.GetData(DragFormatInfobase);
            if (e.Data.GetDataPresent(typeof(GroupNodeViewModel)))
                return e.Data.GetData(typeof(GroupNodeViewModel));
            if (e.Data.GetDataPresent(typeof(Infobase)))
                return e.Data.GetData(typeof(Infobase));
            return null;
        }

        /// <summary>
        /// Запоминает точку начала потенциального перетаскивания (из обработчика LButtonDown).
        /// </summary>
        private void CaptureDragStart(MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>
        /// Цель drop: группа + база, перед которой вставить (если курсор над строкой базы).
        /// </summary>
        private static void ResolveDropTarget(
            DependencyObject? source,
            out GroupNodeViewModel? group,
            out Infobase? insertBefore)
        {
            group = null;
            insertBefore = null;
            if (source is null)
                return;

            var item = FindAncestor<TreeViewItem>(source);
            if (item is null)
                return;

            if (item.DataContext is Infobase ib)
            {
                insertBefore = ib;
                var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(item));
                while (parentItem is not null)
                {
                    if (parentItem.DataContext is GroupNodeViewModel gn)
                    {
                        group = gn;
                        return;
                    }
                    parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(parentItem));
                }
                return;
            }

            if (item.DataContext is GroupNodeViewModel g)
                group = g;
        }

        private void OnEnterpriseMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
                return;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.DataContext = DataContext;
            btn.ContextMenu.IsOpen = true;
        }

        private void OnConfiguratorMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
                return;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.DataContext = DataContext;
            btn.ContextMenu.IsOpen = true;
        }

        private void OnClearCacheMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.ContextMenu is null)
                return;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.DataContext = DataContext;
            btn.ContextMenu.IsOpen = true;
        }

        /// <summary>
        /// Клик по заголовку пункта «Очистить кеш» в контекстном меню дерева баз.
        /// Пункт устроен по аналогии со split-кнопкой правой панели: сам заголовок
        /// открывает окно очистки кеша (<see cref="CacheCleanWindow"/>), а стрелка
        /// справа раскрывает подменю с выбором типа кеша. Здесь закрываем меню,
        /// чтобы оно не осталось поверх модального окна, и запускаем команду.
        /// </summary>
        private void OnClearCacheSplitClick(object sender, RoutedEventArgs e)
        {
            // Пункт живёт в ContextMenu, которого нет в визуальном дереве окна,
            // поэтому до него идём по логическому дереву от кнопки-заголовка.
            var o = sender as DependencyObject;
            ContextMenu? menu = null;
            while (o is not null)
            {
                if (o is ContextMenu candidate)
                {
                    menu = candidate;
                    break;
                }
                o = System.Windows.LogicalTreeHelper.GetParent(o);
            }
            if (menu is not null)
                menu.IsOpen = false;

            if (DataContext is MainViewModel vm && vm.ClearCacheCommand.CanExecute(null))
                vm.ClearCacheCommand.Execute(null);
        }

    }
}
#endif