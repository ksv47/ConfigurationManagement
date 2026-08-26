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
    public partial class MainWindow
    {

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

    }
}
#endif
