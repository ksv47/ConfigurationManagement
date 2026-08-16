using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    public partial class App : Application
    {
        private static Mutex? _instanceMutex;
        private static EventWaitHandle? _activateEvent;
        private static CancellationTokenSource? _activateCts;
        private const string MutexName = "Global\\ConfigurationManagement_1C_SingleInstance";
        private const string ActivateEventName = "Global\\ConfigurationManagement_1C_Activate";

        protected override void OnStartup(StartupEventArgs e)
        {
            // Показываем любые необработанные ошибки — иначе окно просто не появляется.
            DispatcherUnhandledException += (_, args) =>
            {
                ShowFatalError("Ошибка интерфейса", args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    ShowFatalError("Критическая ошибка", ex);
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                ShowFatalError("Ошибка фоновой задачи", args.Exception);
                args.SetObserved();
            };

            try
            {
                // Загружаем настройки до показа окна, чтобы проверить запрет второго экземпляра.
                AppServices.Configure();

                var repository = AppServices.GetRequiredService<IInfobaseRepository>();
                AppSettings settings;
                try
                {
                    settings = repository.LoadSettings();
                }
                catch
                {
                    settings = new AppSettings();
                }

                if (!settings.AllowMultipleInstances)
                {
                    _instanceMutex = new Mutex(true, MutexName, out var createdNew);
                    if (!createdNew)
                    {
                        // Уже запущен другой экземпляр — просим его показать окно (в т.ч. из трея) и выходим.
                        SignalExistingInstance();
                        Shutdown();
                        return;
                    }

                    // Слушаем сигнал от повторных запусков, чтобы поднять окно (в том числе из трея).
                    StartActivationListener();
                }

                base.OnStartup(e);

                // Синхронизируем тему.
                var theme = string.IsNullOrWhiteSpace(settings.Theme)
                    ? ThemeManager.LightThemeName
                    : settings.Theme;
                ThemeManager.ApplyTheme(theme);

                var mainWindow = AppServices.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;

                // Версия в заголовке.
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                var versionText = version is null ? "" : $" v{version}";
                mainWindow.Title = $"Управление конфигурациями 1С{versionText}";

                mainWindow.Show();
            }
            catch (Exception ex)
            {
                ShowFatalError("Не удалось запустить приложение", ex);
                Shutdown(1);
            }
        }

        private static void ShowFatalError(string title, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(title);
                sb.AppendLine();
                sb.AppendLine(ex.Message);
                if (ex.InnerException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("Внутренняя ошибка:");
                    sb.AppendLine(ex.InnerException.Message);
                }
                sb.AppendLine();
                sb.AppendLine(ex.GetType().FullName);
                // Не перегружаем пользователя огромным стеком, но даём начало.
                var stack = ex.StackTrace ?? "";
                if (stack.Length > 1200)
                    stack = stack[..1200] + "…";
                sb.AppendLine(stack);

                MessageBox.Show(sb.ToString(), "Управление конфигурациями 1С — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // ignore
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var logger = AppServices.GetRequiredService<IAppLogger>();
                logger.Info("Приложение завершает работу");
            }
            catch
            {
                // ignore
            }

            try
            {
                _activateCts?.Cancel();
                _activateCts?.Dispose();
                _activateEvent?.Dispose();
            }
            catch
            {
                // ignore
            }

            try
            {
                _instanceMutex?.ReleaseMutex();
                _instanceMutex?.Dispose();
            }
            catch
            {
                // ignore
            }

            base.OnExit(e);
        }

        /// <summary>
        /// Сообщает уже запущенному экземпляру, что нужно показать главное окно.
        /// Работает и когда окно свёрнуто в трей (MainWindowHandle == 0).
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                evt.Set();
            }
            catch
            {
                // Запасной вариант: попытка через handle главного окна.
                try
                {
                    var current = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                    {
                        if (process.Id == current.Id)
                            continue;

                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                            continue;

                        ShowWindow(handle, 9); // SW_RESTORE
                        SetForegroundWindow(handle);
                        break;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        /// <summary>
        /// В основном процессе ждёт сигнал от повторных запусков и активирует окно.
        /// </summary>
        private static void StartActivationListener()
        {
            try
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
                _activateCts = new CancellationTokenSource();
                var token = _activateCts.Token;

                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_activateEvent.WaitOne(500))
                            {
                                Current?.Dispatcher?.BeginInvoke(new Action(ActivateMainWindow));
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch
                        {
                            // ignore transient errors
                        }
                    }
                }, token);
            }
            catch
            {
                // ignore — повторный запуск всё равно попытается через handle
            }
        }

        /// <summary>
        /// Показывает и активирует главное окно (в том числе если оно было скрыто в трей).
        /// </summary>
        private static void ActivateMainWindow()
        {
            try
            {
                if (Current?.MainWindow is MainWindow mw)
                {
                    mw.RestoreFromTrayPublic();
                    return;
                }

                var win = Current?.MainWindow;
                if (win is null)
                    return;

                if (!win.IsVisible)
                    win.Show();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
                win.Activate();
                win.Topmost = true;
                win.Topmost = false;
                win.Focus();
            }
            catch
            {
                // ignore
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
