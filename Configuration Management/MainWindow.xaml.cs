using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Configuration_Management.Models;
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

        public MainWindow(ViewModels.MainViewModel? viewModel = null)
        {
            InitializeComponent();

            // Выводим версию программы в заголовок окна.
            Title = $"{Title} v{Assembly.GetExecutingAssembly().GetName().Version}";

            _viewModel = viewModel ?? new ViewModels.MainViewModel();
            DataContext = _viewModel;

            // Применяем сохранённую тему оформления при запуске.
            if (!string.IsNullOrEmpty(_viewModel.SavedTheme))
            {
                ThemeManager.ApplyTheme(_viewModel.SavedTheme);
            }

            UpdateThemeButton();

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

                if (e.PropertyName is nameof(MainViewModel.HotkeyEnterprise)
                    or nameof(MainViewModel.HotkeyConfigurator)
                    or nameof(MainViewModel.HotkeyFavorite)
                    or nameof(MainViewModel.HotkeyEdit)
                    or nameof(MainViewModel.HotkeyDelete)
                    or nameof(MainViewModel.HotkeyClearCache)
                    or nameof(MainViewModel.HotkeyAdd)
                    or nameof(MainViewModel.HotkeyPin))
                {
                    try { RegisterLaunchHotkeys(); } catch { /* ignore */ }
                }
            };
        }

        /// <summary>
        /// Восстанавливает сохранённые размер, позицию и состояние окна приложения.
        /// </summary>
        private void ApplySavedWindowLayout()
        {
            var width = _viewModel.SavedWindowWidth;
            var height = _viewModel.SavedWindowHeight;

            if (width > 0 && height > 0)
            {
                // Не допускаем выход окна за пределы рабочей области экрана.
                var area = SystemParameters.WorkArea;
                var left = _viewModel.SavedWindowLeft;
                var top = _viewModel.SavedWindowTop;
                if (left <= 0 && top <= 0)
                {
                    // Если позиция не сохранена — центрируем окно.
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                else
                {
                    // Ограничиваем позицию, чтобы окно оставалось видимым.
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
            // Сохраняем только в обычном состоянии, чтобы не сохранить развёрнутое окно как размер по умолчанию.
            if (WindowState == WindowState.Normal)
            {
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
                    Text = "Управление конфигурациями 1С",
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
                        Text = "Управление конфигурациями 1С",
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
            // 1) Embedded resource «app.ico»
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("app.ico", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream is null) continue;
                    return CreateTraySizedIcon(stream);
                }
            }
            catch { /* ignore */ }

            // 2) WPF Resource (pack URI)
            try
            {
                var uri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info?.Stream is not null)
                {
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
                    var iconPath = System.IO.Path.Combine(dir, "app.ico");
                    if (!System.IO.File.Exists(iconPath)) continue;
                    using var fs = System.IO.File.OpenRead(iconPath);
                    return CreateTraySizedIcon(fs);
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

            menu.Items.Add(CreateTrayItem("Открыть программу", TrayIconKind.Open, (_, _) => RestoreFromTray()));

            // Недавние базы (по дате последнего запуска)
            var recent = _viewModel.GetRecentInfobases(7).ToList();
            if (recent.Count > 0)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add(CreateTrayHeader("Недавние базы"));

                foreach (var ib in recent)
                {
                    var name = string.IsNullOrWhiteSpace(ib.Name) ? "(без имени)" : ib.Name;
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
                menu.Items.Add(CreateTrayHeader("Выбранная база"));
                var selName = _viewModel.SelectedInfobase.Name;
                if (string.IsNullOrWhiteSpace(selName)) selName = "(выбранная база)";
                var selId = _viewModel.SelectedInfobase.Id;
                var selItem = CreateTrayItem(selName, TrayIconKind.Database, null);
                AttachLaunchSubmenu(selItem, menu, selId);
                menu.Items.Add(selItem);
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateTrayItem("Синхронизация с ibases.v8i", TrayIconKind.Sync, (_, _) =>
            {
                RestoreFromTray();
                if (_viewModel.SynchronizeWithIbasesCommand.CanExecute(null))
                    _viewModel.SynchronizeWithIbasesCommand.Execute(null);
            }));
            menu.Items.Add(CreateTrayItem("Настройки…", TrayIconKind.Settings, (_, _) =>
            {
                RestoreFromTray();
                if (_viewModel.OpenSettingsCommand.CanExecute(null))
                    _viewModel.OpenSettingsCommand.Execute(null);
            }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateTrayItem("Выход", TrayIconKind.Exit, (_, _) =>
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
            parent.DropDownItems.Add(CreateTrayItem("1С:Предприятие", TrayIconKind.Enterprise, (_, _) =>
                _viewModel.LaunchInfobaseById(infobaseId, isConfigurator: false)));
            parent.DropDownItems.Add(CreateTrayItem("Конфигуратор", TrayIconKind.Configurator, (_, _) =>
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

        /// <summary>Простые цветные иконки 18×18 для пунктов меню трея.</summary>
        private static Drawing.Image GetTrayIcon(TrayIconKind kind)
        {
            if (TrayIconCache.TryGetValue(kind, out var cached))
                return cached;

            var bmp = new Drawing.Bitmap(18, 18);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);

                Drawing.Color accent = kind switch
                {
                    TrayIconKind.Open => Drawing.Color.FromArgb(34, 197, 94),
                    TrayIconKind.Database => Drawing.Color.FromArgb(59, 130, 246),
                    TrayIconKind.Enterprise => Drawing.Color.FromArgb(16, 185, 129),
                    TrayIconKind.Configurator => Drawing.Color.FromArgb(245, 158, 11),
                    TrayIconKind.Sync => Drawing.Color.FromArgb(20, 184, 166),
                    TrayIconKind.Settings => Drawing.Color.FromArgb(100, 116, 139),
                    TrayIconKind.Exit => Drawing.Color.FromArgb(239, 68, 68),
                    _ => Drawing.Color.FromArgb(100, 116, 139)
                };

                using var brush = new Drawing.SolidBrush(accent);
                using var pen = new Drawing.Pen(accent, 1.6f)
                {
                    StartCap = Drawing.Drawing2D.LineCap.Round,
                    EndCap = Drawing.Drawing2D.LineCap.Round
                };

                switch (kind)
                {
                    case TrayIconKind.Open:
                        // окно / рамка
                        g.DrawRectangle(pen, 3, 3, 12, 11);
                        g.DrawLine(pen, 3, 6, 15, 6);
                        break;
                    case TrayIconKind.Database:
                        // цилиндр БД
                        g.DrawEllipse(pen, 4, 2, 10, 4);
                        g.DrawLine(pen, 4, 4, 4, 13);
                        g.DrawLine(pen, 14, 4, 14, 13);
                        g.DrawArc(pen, 4, 11, 10, 4, 0, 180);
                        break;
                    case TrayIconKind.Enterprise:
                        // play
                        g.FillPolygon(brush, new[]
                        {
                            new Drawing.Point(5, 3),
                            new Drawing.Point(14, 9),
                            new Drawing.Point(5, 15)
                        });
                        break;
                    case TrayIconKind.Configurator:
                        // шестерёнка-упрощённо: круг + крест
                        g.DrawEllipse(pen, 4, 4, 10, 10);
                        g.DrawLine(pen, 9, 2, 9, 16);
                        g.DrawLine(pen, 2, 9, 16, 9);
                        break;
                    case TrayIconKind.Sync:
                        // две дуги
                        g.DrawArc(pen, 3, 3, 12, 12, 40, 140);
                        g.DrawArc(pen, 3, 3, 12, 12, 220, 140);
                        break;
                    case TrayIconKind.Settings:
                        g.DrawEllipse(pen, 5, 5, 8, 8);
                        g.DrawEllipse(pen, 2, 2, 14, 14);
                        break;
                    case TrayIconKind.Exit:
                        // дверь / выход
                        g.DrawRectangle(pen, 3, 3, 8, 12);
                        g.DrawLine(pen, 11, 9, 16, 9);
                        g.DrawLine(pen, 14, 7, 16, 9);
                        g.DrawLine(pen, 14, 11, 16, 9);
                        break;
                }
            }

            TrayIconCache[kind] = bmp;
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
            ThemeManager.ToggleTheme();
            UpdateThemeButton();
            _viewModel.SaveTheme(ThemeManager.CurrentTheme);
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
        /// (привязка ShowTagFilterPanel) синхронно включает/выключает и теги в списке баз.
        /// </summary>
        private void OnTopTagsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && DataContext is MainViewModel vm)
                vm.ShowTags = toggle.IsChecked == true;
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы колонка «Название» заголовка совпадала с колонкой «Название» первой
        /// видимой строки базы в дереве.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var nameCell = FindNameCell(item);
            if (nameCell is null)
                return;

            // Положение колонки «Название» данных относительно дерева (левого края списка).
            // В заголовке слева: [кнопки развернуть/свернуть 56] + [Offset] + [★ 28] + [📌 26] + Название.
            // Кнопки — отдельная колонка; Offset только подгоняет выравнивание с деревом
            // (у листьев expander схлопнут, у групп — шире).
            var dataX = nameCell.TranslatePoint(new Point(0, 0), MainTree).X;
            var expandCol = _viewModel.ShowExpandCollapseButtons ? 56.0 : 0.0;
            // dataX ≈ отступ до «Название» в строке; вычитаем ★+📌 и колонку кнопок.
            var offset = Math.Max(0, dataX - 54 - expandCol);
            HeaderOffsetColumn.Width = new GridLength(offset);

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Выравнивает ширину сетки заголовка с контентом списка, чтобы горизонтальная
        /// прокрутка «до конца» не разъезжала колонки заголовка и строк.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            var treeScroll = GetTreeScrollViewer();
            double sbw = 0;
            double extent = MainTree.ActualWidth;
            double viewport = MainTree.ActualWidth;
            if (treeScroll is not null)
            {
                sbw = treeScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                    ? SystemParameters.VerticalScrollBarWidth
                    : 0;
                extent = Math.Max(treeScroll.ExtentWidth, treeScroll.ViewportWidth);
                viewport = treeScroll.ViewportWidth;
            }

            double target = Math.Max(extent + sbw, viewport + sbw);
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
        /// Находит текстовый элемент колонки «Название» (Grid.Column=3) строки базы.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && Grid.GetColumn(tb) == 3)
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
            // Двойной клик по базе по-прежнему запускает 1С.
            var source = e.OriginalSource as DependencyObject;
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
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            // NameColumn = * (растягивается) — фиксированную ширину не задаём
            SetColumnWidth(VersionColumn, _viewModel.VersionColumnWidth);
            SetColumnWidth(LaunchModeColumn, _viewModel.LaunchModeColumnWidth);
            SetColumnWidth(ServerColumn, _viewModel.ServerColumnWidth);
            SetColumnWidth(LastLaunchColumn, _viewModel.LastLaunchColumnWidth);
        }

        /// <summary>
        /// Устанавливает ширину колонки, если задано значение больше нуля.
        /// </summary>
        private static void SetColumnWidth(ColumnDefinition? column, double width)
        {
            if (column is null || width <= 0)
                return;
            column.Width = new GridLength(width);
        }

        // Поля для ручного перетаскивания разделителя колонок.
        private ColumnDefinition? _resizeColumn;
        private double _resizeStartWidth;
        private Point _resizeStartMouse;

        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель в Grid.Column=N расположен слева от колонки N, поэтому он меняет колонку N-1.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, VersionSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return ServerColumn;
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
        /// Соседние колонки не затрагиваются: разность впитывает последняя гибкая колонка (*).
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

            _resizeColumn.Width = new GridLength(newWidth);

            _viewModel.UpdateColumnWidths(
                NameColumn?.ActualWidth ?? 0,
                VersionColumn?.ActualWidth ?? 0,
                LaunchModeColumn?.ActualWidth ?? 0,
                ServerColumn?.ActualWidth ?? 0,
                LastLaunchColumn?.ActualWidth ?? 0);
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
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0);
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
            ThemeToggleButton.ToolTip = isDark ? "Светлая тема" : "Тёмная тема";

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
                if (targetGroup.DisplayName == "Закреплённые")
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
    }
}