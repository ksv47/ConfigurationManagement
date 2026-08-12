using System.Windows;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppServices.Configure();

            // Синхронизируем состояние менеджера тем с темой, загруженной из App.xaml.
            ThemeManager.ApplyTheme(ThemeManager.LightThemeName);

            var mainWindow = AppServices.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var logger = AppServices.GetRequiredService<Services.IAppLogger>();
                logger.Info("Приложение завершает работу");
            }
            catch
            {
                // ignore
            }
            base.OnExit(e);
        }
    }
}
