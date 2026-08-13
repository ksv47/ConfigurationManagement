using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    public partial class App : Application
    {
        private static Mutex? _instanceMutex;
        private const string MutexName = "Global\\ConfigurationManagement_1C_SingleInstance";

        protected override void OnStartup(StartupEventArgs e)
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
                    // Уже запущен другой экземпляр — активируем его окно и выходим.
                    ActivateExistingInstance();
                    Shutdown();
                    return;
                }
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
            var versionText = version is null
                ? ""
                : $" v{version.Major}.{version.Minor}.{version.Build}";
            mainWindow.Title = $"Управление конфигурациями 1С{versionText}";

            mainWindow.Show();
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
        /// Находит окно уже запущенного экземпляра и выводит его на передний план.
        /// </summary>
        private static void ActivateExistingInstance()
        {
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
