#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management
{
    /// <summary>
    /// Системный трей главного окна (Avalonia/Linux): создание значка, меню,
    /// обновление по данным и увод окна в трей.
    /// </summary>
    public partial class MainWindow : Window
    {
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
                // «Свернуть» — просто свернуть окно в панель задач, не уводя его в трей (issue #201).
                if (action == Models.AfterLaunchAction.Minimize)
                {
                    WindowState = WindowState.Minimized;
                    return;
                }

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

            // Состав и порядок как в Windows-версии (MainWindow.Tray.cs:209):
            // открыть, недавние базы (или выбранная, если недавних нет),
            // синхронизация, настройки, выход. У каждой базы своё подменю
            // «Предприятие / Конфигуратор»: раньше пункт запускал только
            // Предприятие, а выбора не было.
            var showItem = new NativeMenuItem(LocalizationManager.T("Main.TrayOpen"));
            showItem.Click += (_, _) => ShowAndActivate();
            menu.Add(showItem);

            var recent = _vm?.RecentInfobases;
            if (recent is { Count: > 0 })
            {
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(TrayHeader(LocalizationManager.T("Main.RecentBases")));
                foreach (var ib in recent)
                    menu.Add(TrayInfobaseItem(ib, TrayItemName(ib.Name, "Main.NoName")));
            }
            else if (_vm?.SelectedInfobase is { } sel)
            {
                menu.Add(new NativeMenuItemSeparator());
                menu.Add(TrayHeader(LocalizationManager.T("Main.SelectedBase")));
                // У выбранной базы сам пункт ничего не запускает, только раскрывает
                // подменю, и её имя не обрезается: так у автора
                // (MainWindow.Tray.cs:240).
                var selName = string.IsNullOrWhiteSpace(sel.Name)
                    ? LocalizationManager.T("Main.SelectedBaseNoName")
                    : sel.Name;
                menu.Add(TrayInfobaseItem(sel, selName, launchOnClick: false));
            }

            menu.Add(new NativeMenuItemSeparator());

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

        /// <summary>
        /// Заголовок раздела в меню трея. В Windows это отдельный нерабочий
        /// пункт (MainWindow.Tray.cs:216), здесь он же, но недоступный:
        /// собственного вида у заголовка в системном меню нет.
        /// </summary>
        private static NativeMenuItem TrayHeader(string text) => new(text) { IsEnabled = false };

        /// <summary>
        /// Подпись базы в меню трея: пустое имя заменяется на подпись автора,
        /// длинное обрезается до 48 знаков, как в Windows-версии
        /// (MainWindow.Tray.cs:224).
        /// </summary>
        private static string TrayItemName(string? name, string emptyKey)
        {
            if (string.IsNullOrWhiteSpace(name))
                return LocalizationManager.T(emptyKey);
            return name.Length > 48 ? name.Substring(0, 45) + "…" : name;
        }

        /// <summary>
        /// Пункт базы с подменю выбора режима запуска: сам пункт запускает
        /// Предприятие, подменю даёт Предприятие и Конфигуратор
        /// (MainWindow.Tray.cs:271).
        /// </summary>
        private NativeMenuItem TrayInfobaseItem(Infobase ib, string title, bool launchOnClick = true)
        {
            var baseRef = ib;
            var item = new NativeMenuItem(title);
            if (launchOnClick)
                item.Click += (_, _) => LaunchInfobase(baseRef, configurator: false);

            var submenu = new NativeMenu();
            var enterprise = new NativeMenuItem(LocalizationManager.T("Main.Enterprise"));
            enterprise.Click += (_, _) => LaunchInfobase(baseRef, configurator: false);
            submenu.Add(enterprise);

            var configurator = new NativeMenuItem(LocalizationManager.T("Main.SectionConfigurator"));
            configurator.Click += (_, _) => LaunchInfobase(baseRef, configurator: true);
            submenu.Add(configurator);

            item.Menu = submenu;
            return item;
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
                // Одиночный клик левой кнопкой по иконке трея показывает и
                // фокусирует главное окно, как в WPF-версии (issue #224).
                // Правый клик открывает меню (Menu выше), левый — нет.
                tray.Clicked += (_, _) => ShowAndActivate();
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
        private void LaunchInfobase(Infobase ib, bool configurator = false)
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
            _vm.LaunchFromTray(ib, configurator);
        }

        /// <summary>
        /// Загружает иконку трея — тот же значок приложения (app.ico), что и у
        /// заголовка окна. Без System.Drawing, через Avalonia WindowIcon.
        /// </summary>
        private static WindowIcon? LoadTrayIcon() => Services.AppIconLoader.LoadAppIcon();

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