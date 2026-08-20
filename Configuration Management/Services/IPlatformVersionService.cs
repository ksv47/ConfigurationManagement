namespace Configuration_Management.Services;

public interface IPlatformVersionService
{
    List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null);
}

// Адаптер ссылается на Windows-only статический класс PlatformVersionService,
// поэтому в Linux-сборке исключается (см. Services/LinuxStubs.cs — заглушка).
#if WINDOWS
public sealed class PlatformVersionServiceAdapter : IPlatformVersionService
{
    public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
        => PlatformVersionService.FindInstalledVersions(additionalPaths);
}
#endif
