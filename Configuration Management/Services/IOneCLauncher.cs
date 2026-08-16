using Configuration_Management.Models;

namespace Configuration_Management.Services;

public interface IOneCLauncher
{
    bool Launch(Infobase infobase, OneCLaunchMode mode);
    bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture);
}

/// <summary>Адаптер над статическим OneCLauncher.</summary>
public sealed class OneCLauncherService : IOneCLauncher
{
    public bool Launch(Infobase infobase, OneCLaunchMode mode) =>
        OneCLauncher.Launch(infobase, mode);

    public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture) =>
        OneCLauncher.Launch(infobase, mode, clientType, architecture);
}
