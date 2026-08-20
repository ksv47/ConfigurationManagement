#if LINUX
using System;
using Avalonia;

namespace Configuration_Management
{
    /// <summary>
    /// Точка входа Avalonia-приложения (Linux). Windows использует автоматически
    /// сгенерированную WPF-точку входа из App.xaml.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
#endif