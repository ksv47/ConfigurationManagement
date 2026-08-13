namespace Configuration_Management.Services;

public interface IPlatformVersionService
{
    List<string> FindInstalledVersions();
}

public sealed class PlatformVersionServiceAdapter : IPlatformVersionService
{
    public List<string> FindInstalledVersions() => PlatformVersionService.FindInstalledVersions();
}
