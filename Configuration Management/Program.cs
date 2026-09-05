#if LINUX
using System;
using Avalonia;

namespace Configuration_Management
{
    /// <summary>
    /// Точка входа Avalonia-приложения (Linux).
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                // Экспорт меню окна через DBus отключён: меню окна в приложении нет,
                // а в средах без com.canonical.AppMenu.Registrar каждое окно заводит
                // экспортёр, который при закрытии зовёт UnregisterWindowAsync без await,
                // и отказ службы всплывает необработанной задачей в errors.log.
                // Меню значка в области уведомлений идёт другим путём и не затрагивается.
                .With(new X11PlatformOptions { UseDBusMenu = false })
                .WithInterFont()
                .LogToTrace();
    }
}
#else
using System;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Точка входа WPF-приложения (Windows). Написана вручную вместо автоматически
    /// генерируемой из App.xaml, чтобы режим COM-агента можно было перехватить
    /// <b>до</b> создания <see cref="App"/>.
    /// <para>
    /// Это существенно: <c>App.InitializeComponent()</c> подгружает словари ресурсов
    /// (MaterialDesign, тема, иконки). Агенту, которому нужен один COM-вызов, всё это
    /// не нужно — а раньше проверка стояла в <c>OnStartup</c>, то есть уже после загрузки.
    /// Заодно у агента нет обработчиков необработанных исключений приложения, и любой
    /// сбой загрузки XAML уходил бы стеком в stderr.
    /// </para>
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Режим агента: обслуживаем запросы по stdin и выходим, не поднимая WPF.
            if (ComReadHost.TryHandleCommandLine(args))
                return 0;

            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }
    }
}
#endif
