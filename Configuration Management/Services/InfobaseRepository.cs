using System.IO;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Репозиторий для сохранения и загрузки настроек информационных баз в JSON-файл.
/// </summary>
public class InfobaseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly string _groupsFilePath;
    private readonly string _settingsFilePath;

    public InfobaseRepository(string? filePath = null)
    {
        var directory = filePath != null
            ? Path.GetDirectoryName(filePath)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConfigurationManagement");

        _filePath = filePath ?? Path.Combine(directory!, "infobases.json");
        _groupsFilePath = Path.Combine(directory!, "groups.json");
        _settingsFilePath = Path.Combine(directory!, "settings.json");
    }

    /// <summary>
    /// Загружает список информационных баз из файла. Если файл отсутствует — возвращает пустой список.
    /// </summary>
    public List<Infobase> Load()
    {
        if (!File.Exists(_filePath))
            return new List<Infobase>();
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Infobase>>(json, JsonOptions) ?? new List<Infobase>();
        }
        catch (Exception ex)
        {
            // При ошибке десериализации возвращаем пустой список, чтобы не падать при повреждённом файле.
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки баз: {ex.Message}");
            return new List<Infobase>();
        }
    }

    /// <summary>
    /// Сохраняет список информационных баз в файл.
    /// </summary>
    public void Save(List<Infobase> infobases)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(infobases, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Загружает список групп из файла. Если файл отсутствует — возвращает пустой список.
    /// </summary>
    public List<Group> LoadGroups()
    {
        if (!File.Exists(_groupsFilePath))
            return new List<Group>();
        try
        {
            var json = File.ReadAllText(_groupsFilePath);
            return JsonSerializer.Deserialize<List<Group>>(json, JsonOptions) ?? new List<Group>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки групп: {ex.Message}");
            return new List<Group>();
        }
    }

    /// <summary>
    /// Сохраняет список групп в файл.
    /// </summary>
    public void SaveGroups(List<Group> groups)
    {
        var directory = Path.GetDirectoryName(_groupsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(groups, JsonOptions);
        File.WriteAllText(_groupsFilePath, json);
    }

    /// <summary>
    /// Загружает настройки интерфейса из файла. Если файл отсутствует — возвращает настройки по умолчанию.
    /// </summary>
    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();
        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки интерфейса в файл.
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }
}