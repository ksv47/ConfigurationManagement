#if LINUX
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Avalonia-версия точки входа приложения (Linux). Заменяет WPF App.xaml / App.xaml.cs.
    /// Собирается только под #if LINUX; Windows использует WPF-версию App.
    /// </summary>
    public partial class App : Application
    {
        private static FileStream? _instanceLock;
        private static string? _dataDir;
        private static CancellationTokenSource? _activateCts;
        private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;

        /// <summary>
        /// Работает ли режим единственного экземпляра: блокировка взята и сигнал
        /// от повторного запуска слушается. Значение относится к текущему
        /// процессу и после старта не меняется, даже если настройку переключат:
        /// блокировка берётся один раз, и снятый в окне настроек флажок не
        /// делает окно возвращаемым повторным запуском.
        /// </summary>
        internal static bool SingleInstanceActive { get; private set; }

        private const string LockFileName = "configuration-management.lock";
        private const string ActivateFileName = "activate";

        /// <summary>
        /// Каталог данных приложения (например ~/.config/ConfigurationManagement).
        /// Считается тем же механизмом, что и у репозитория настроек: расчёт
        /// напрямую через SpecialFolder.ApplicationData на Linux даёт пустую
        /// строку, когда каталога из XDG_CONFIG_HOME нет, путь становится
        /// относительным, и приложение молча не запускается.
        /// </summary>
        private static string DataDirectory =>
            _dataDir ??= Services.PlatformPaths.AppDataDirectory;

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Показываем любые необработанные ошибки — иначе окно просто не появляется.
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    ShowFatalError(LocalizationManager.T("App.Fatal.Critical"), ex);
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                ShowFatalError(LocalizationManager.T("App.Fatal.BackgroundTask"), args.Exception);
                args.SetObserved();
            };

            // Освобождаем файловый lock при завершении процесса.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { _activateCts?.Cancel(); _activateCts?.Dispose(); } catch { /* ignore */ }
                try { _instanceLock?.Dispose(); } catch { /* ignore */ }
            };

            try
            {
                // Загружаем настройки до показа окна, чтобы проверить запрет второго экземпляра.
                AppServices.Configure();

                // Инициализируем учётные записи (профили): загружаем реестр, при первом
                // запуске мигрируем легаси-данные в профиль по умолчанию. Репозиторий
                // читает/пишет файлы данных в каталог активного профиля.
                var profileService = AppServices.GetRequiredService<IProfileService>();
                profileService.EnsureInitialized();

                // Если в приложении несколько учётных записей — показываем окно авторизации
                // по аналогии со списком пользователей 1С. При одной записи входим без запроса.
                if (profileService.Profiles.Count > 1)
                {
                    // Окно входа закрывается до создания главного окна. При режиме
                    // завершения по умолчанию (OnLastWindowClose) его закрытие гасит
                    // приложение, и запуск падает с «Dispatcher shut down». Поэтому на
                    // время старта завершение только явное; штатный режим возвращается
                    // после показа главного окна.
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime startupLifetime)
                        startupLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    // Локализацию поднимаем до показа окна: настройки выбранного профиля
                    // читаются ниже, а без словаря окно входа показывает ключи
                    // (Auth.Title, Auth.Login) вместо подписей. Язык берётся из профиля,
                    // активного с прошлого запуска, и уточняется после выбора.
                    try
                    {
                        var startupSettings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
                        LocalizationManager.Instance.Initialize(startupSettings.Language, DataDirectory);
                    }
                    catch
                    {
                        LocalizationManager.Instance.Initialize(null, DataDirectory);
                    }

                    var selectedId = LoginWindow.ShowLogin(profileService);
                    if (selectedId == null)
                    {
                        // Вход отменён — завершаем приложение.
                        Shutdown();
                        return;
                    }
                    profileService.SetCurrentProfile(selectedId);
                }

                ProfileBackupService.DataDirectoryResolver = () => profileService.CurrentProfileDataDirectory;

                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                AppSettings settings;
                try { settings = repository.LoadSettings(); }
                catch { settings = new AppSettings(); }

                // Восстановление профиля из указанного каталога резервной копии
                // (например, после переустановки системы): настройки, список баз
                // (с пользователями и паролями запуска), группы и ibases.v8i.
                // Файлы копируются до загрузки данных главным окном, поэтому приложение
                // сразу открывается с привычным состоянием. Настройки перечитываются,
                // чтобы последующие этапы запуска использовали восстановленные значения.
                if (settings.ProfileRestoreOnStartup
                    && !string.IsNullOrWhiteSpace(settings.ProfileBackupDirectory)
                    && ProfileBackupService.HasBackup(settings.ProfileBackupDirectory))
                {
                    try
                    {
                        ProfileBackupService.Restore(settings.ProfileBackupDirectory, settings.IbasesSyncFilePath);
                        try { settings = repository.LoadSettings(); }
                        catch { /* оставляем уже прочитанные настройки */ }
                    }
                    catch (Exception ex)
                    {
                        // Сбой восстановления не должен блокировать запуск.
                        Console.Error.WriteLine("[profile] Ошибка восстановления профиля: " + ex.Message);
                    }
                }

                // Инициализируем локализацию: выбранный или системный язык, а также
                // загружаем внешние языки (.json) из папок Languages (рядом с приложением
                // и в каталоге данных).
                try
                {
                    LocalizationManager.Instance.Initialize(settings.Language, DataDirectory);
                    // Если словарь уже поднят ради окна входа, Initialize выходит сразу,
                    // поэтому язык выбранного профиля применяется отдельно.
                    if (!string.IsNullOrWhiteSpace(settings.Language))
                        LocalizationManager.Instance.SetLanguage(settings.Language);
                }
                catch
                {
                    // Локализация не должна блокировать запуск приложения.
                }

                if (!settings.AllowMultipleInstances)
                {
                    if (!TryAcquireSingleInstanceLock())
                    {
                        // Уже запущен другой экземпляр — просим его показать окно и выходим.
                        SignalExistingInstance();
                        Shutdown();
                        return;
                    }
                    // Слушаем сигнал от повторных запусков, чтобы поднять окно.
                    StartActivationListener();
                    SingleInstanceActive = true;
                }

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    _desktopLifetime = desktop;

                    // Применяем сохранённую цветовую схему активной базовой темы. Предпочитаем
                    // раздельную схему для светлой/тёмной темы, иначе — старый одиночный
                    // ActiveColorScheme (миграция) или встроенные цвета.
                    var activeTheme = string.IsNullOrWhiteSpace(settings.Theme)
                        ? (settings.ActiveColorScheme?.IsDark == true ? ThemeManager.DarkThemeName : ThemeManager.LightThemeName)
                        : settings.Theme;
                    var isDark = string.Equals(activeTheme, ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase);
                    Configuration_Management.Models.ColorScheme? activeScheme = isDark
                        ? settings.DarkColorScheme
                        : settings.LightColorScheme;
                    if (activeScheme is not { Colors.Count: > 0 }
                        && settings.ActiveColorScheme is { Colors.Count: > 0 }
                        && settings.ActiveColorScheme.IsDark == isDark)
                    {
                        activeScheme = settings.ActiveColorScheme;
                    }
                    ThemeManager.ApplyScheme(activeScheme ?? (isDark
                        ? Configuration_Management.Models.ColorScheme.CreateDark()
                        : Configuration_Management.Models.ColorScheme.CreateLight()));

                    // Компактный режим интерфейса (влияет на метрики отступов/иконок,
                    // должен быть установлен до построения главного окна).
                    UiMetrics.Compact = settings.CompactMode;

                    var mainWindow = AppServices.GetRequiredService<MainWindow>();

                    // Версия в заголовке (информационная версия, напр. «0.3.1.1»).
                    var infoVersion = Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    var versionText = string.IsNullOrWhiteSpace(infoVersion) ? "" : $" v{infoVersion}";
                    mainWindow.Title = $"{LocalizationManager.T("App.Title")}{versionText}";

                    // Применяем сохранённые настройки шрифта интерфейса и отдельных областей
                    // (дерево групп, кнопки, поля ввода, правая панель, статус-бар).
                    ThemeManager.ApplyFont(mainWindow,
                        settings.FontFamily, settings.FontSize, settings.FontWeight, settings.FontStyle);
                    // Меню, подсказки и выпадающие списки живут в отдельных корнях
                    // и шрифт окна не наследуют, поэтому им он ставится стилем.
                    ThemeManager.ApplyFontToPopups(
                        settings.FontFamily, settings.FontSize, settings.FontWeight, settings.FontStyle);
                    ThemeManager.ApplyElementFonts(mainWindow, settings.ElementFonts);

                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();

                    // Штатный режим завершения возвращается: на время окна входа он
                    // переключался на явный, иначе закрытие того окна гасило приложение.
                    desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                }
            }
            catch (Exception ex)
            {
                ShowFatalError(LocalizationManager.T("App.Fatal.StartupFailed"), ex);
                Shutdown(1);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Завершает приложение на этапе запуска. У Avalonia Application нет метода Shutdown,
        /// а desktop.Shutdown здесь не годится: оба вызова происходят внутри
        /// OnFrameworkInitializationCompleted, то есть до входа в цикл сообщений, и гасят
        /// Dispatcher раньше времени. Тогда MainLoop падает с InvalidOperationException
        /// «Cannot perform requested operation because the Dispatcher shut down».
        /// </summary>
        private static void Shutdown(int exitCode = 0) => Environment.Exit(exitCode);

        /// <summary>
        /// Захватывает исключительный файловый lock (один экземпляр на Linux).
        /// Файл-блокировка в каталоге данных с FileShare.None.
        /// </summary>
        private static bool TryAcquireSingleInstanceLock()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                _instanceLock = new FileStream(
                    Path.Combine(DataDirectory, LockFileName),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }                 // другой экземпляр держит lock
            catch (UnauthorizedAccessException) { return false; } // нет прав — считаем занятым
        }

        /// <summary>
        /// Второй экземпляр: создаёт файл-сигнал, чтобы первый показал главное окно.
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(
                    Path.Combine(DataDirectory, ActivateFileName),
                    DateTime.UtcNow.Ticks.ToString());
            }
            catch { /* ignore — повторный запуск просто завершится */ }
        }

        /// <summary>
        /// Основной экземпляр: следит за файлом-сигналом и активирует главное окно.
        /// </summary>
        private static void StartActivationListener()
        {
            try
            {
                _activateCts = new CancellationTokenSource();
                var token = _activateCts.Token;
                var activatePath = Path.Combine(DataDirectory, ActivateFileName);

                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (File.Exists(activatePath))
                            {
                                try { File.Delete(activatePath); } catch { /* ignore */ }
                                Dispatcher.UIThread.Post(ActivateMainWindow);
                            }
                        }
                        catch { /* ignore transient errors */ }
                        Thread.Sleep(300);
                    }
                }, token);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Показывает и активирует главное окно (в том числе если оно было свёрнуто).
        /// </summary>
        private static void ActivateMainWindow()
        {
            try
            {
                if (_desktopLifetime?.MainWindow is not Window win)
                    return;
                if (!win.IsVisible)
                    win.Show();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Записывает фатальную ошибку в errors.log и на stderr.
        /// (Полноценный диалог ошибок появится вместе с портом окон — Этап 3.)
        /// </summary>
        private static void ShowFatalError(string title, Exception ex)
        {
            try
            {
                var text = $"{title}{Environment.NewLine}{ex}{Environment.NewLine}";
                Console.Error.WriteLine(text);
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(
                        Path.Combine(DataDirectory, "errors.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}");
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }
    }
}
#endif