using Configuration_Management.Models;

namespace Configuration_Management.Services;

public interface IInfobaseRepository
{
    List<Infobase> Load();
    void Save(List<Infobase> infobases);
    Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default);
    List<Group> LoadGroups();
    void SaveGroups(List<Group> groups);
    Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default);
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
