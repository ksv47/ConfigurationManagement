using System.Configuration;
using System.Data;
using System.Windows;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Синхронизируем состояние менеджера тем с темой, загруженной из App.xaml.
            ThemeManager.ApplyTheme(ThemeManager.LightThemeName);
        }
    }

}
