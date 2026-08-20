using Configuration_Management.Services;
using Configuration_Management.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Configuration_Management;

/// <summary>
/// Корневой контейнер зависимостей приложения.
/// </summary>
public static class AppServices
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static void Configure()
    {
        var services = new ServiceCollection();

#if WINDOWS
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IInfobaseRepository, InfobaseRepository>();
        services.AddSingleton<IOneCLauncher, OneCLauncherService>();
        services.AddSingleton<IOneCComConnector, OneCComConnector>();
        services.AddSingleton<IOneCComConnectorRegistrar, OneCComConnectorRegistrar>();
        services.AddSingleton<IPlatformVersionService, PlatformVersionServiceAdapter>();
        services.AddSingleton<IIbasesSyncService, IbasesSyncService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
#else
        // Linux (Avalonia): полноценные реализации сервисов платформы 1С (Этап 5).
        // Регистратор COM-коннектора не подключается — на Linux COM отсутствует
        // (чтение конфигурации выполняется без COM: 1Cv8.1CD / DESIGNER).
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IInfobaseRepository, InfobaseRepository>();
        services.AddSingleton<IOneCLauncher, OneCLauncherService>();
        services.AddSingleton<IOneCComConnector, OneCComConnector>();
        services.AddSingleton<IPlatformVersionService, PlatformVersionServiceAdapter>();
        services.AddSingleton<IIbasesSyncService, IbasesSyncService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
#endif

        Services = services.BuildServiceProvider();
    }

    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();
}
