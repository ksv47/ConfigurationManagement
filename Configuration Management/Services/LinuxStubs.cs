#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    // ========================================================================
    // Временные заглушки для Linux (Этап 2). Позволяют DI-контейнеру
    // (AppServices) не ссылаться на непортированные Windows-сервисы.
    // Полные реализации — Этап 5 (сервисы платформы 1С).
    // Диалоги портированы: см. AvaloniaDialogService (Этап 3).
    // ========================================================================

    /// <summary>Заглушка запуска платформы 1С (полный порт — Этап 5).</summary>
    public sealed class OneCLauncherService : IOneCLauncher
    {
        public bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false) => false;
        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false) => false;
        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false) => false;
    }

    /// <summary>Заглушка COM-коннектора 1С (на Linux COM отсутствует; порт — Этап 5).</summary>
    public sealed class OneCComConnector : IOneCComConnector
    {
        public string? LastError { get; private set; }
        public OneCComConnection? Connect(Infobase infobase, int timeoutMs = 8000) => null;
        public string BuildConnectString(Infobase infobase) => string.Empty;
        public OneCConfigInfo? ReadConfigurationInfo(Infobase infobase, int timeoutMs = 8000) => null;
    }

    /// <summary>Интерфейс регистрации COM-коннекторов (Linux-заглушка; полный порт — Этап 5).</summary>
    public interface IOneCComConnectorRegistrar
    {
        ComConnectorRegistrationResult Register(string? platformVersion, string architecture);
    }

    /// <summary>Заглушка регистрации COM-коннекторов (на Linux нет COM-регистрации).</summary>
    public sealed class OneCComConnectorRegistrar : IOneCComConnectorRegistrar
    {
        public OneCComConnectorRegistrar(IAppLogger logger) { }
        public ComConnectorRegistrationResult Register(string? platformVersion, string architecture) => new();
    }

    /// <summary>Результат «регистрации» COM-коннектора (Linux-заглушка).</summary>
    public sealed class ComConnectorRegistrationResult { }

    /// <summary>Поиск версий платформы 1С (Linux): делегирует реальной реализации
    /// <see cref="PlatformVersionService"/> (LinuxOneCServiceShims).</summary>
    public sealed class PlatformVersionServiceAdapter : IPlatformVersionService
    {
        public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
            => PlatformVersionService.FindInstalledVersions(additionalPaths);
    }
}
#endif