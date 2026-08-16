namespace Configuration_Management.Services;

public interface IPlatformVersionService
{
    List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null);
}

public sealed class PlatformVersionServiceAdapter : IPlatformVersionService
{
    public List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
        => PlatformVersionService.FindInstalledVersions(additionalPaths);
}
