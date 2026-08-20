using Configuration_Management.Models;

namespace Configuration_Management.Services;

public interface IOneCLauncher
{
    bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false);
    bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false);
    bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false);
}

// Адаптер ссылается на Windows-only статический класс OneCLauncher, поэтому
// в Linux-сборке исключается (см. Services/LinuxStubs.cs — временная заглушка).
#if WINDOWS
public sealed class OneCLauncherService : IOneCLauncher
{
    public bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false) =>
        OneCLauncher.Launch(infobase, mode, runAsAdmin);

    public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false) =>
        OneCLauncher.Launch(infobase, mode, clientType, architecture, runAsAdmin);

    public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false) =>
        OneCLauncher.Launch(infobase, mode, clientType, runMode, architecture, runAsAdmin);
}
#endif
