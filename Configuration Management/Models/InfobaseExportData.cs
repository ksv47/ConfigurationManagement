namespace Configuration_Management.Models;

/// <summary>
/// Контейнер для экспорта/импорта данных приложения:
/// список информационных баз и список групп с цветами.
/// </summary>
public class InfobaseExportData
{
    /// <summary>Список информационных баз.</summary>
    public List<Infobase> Infobases { get; set; } = new();

    /// <summary>Список групп информационных баз (с цветами).</summary>
    public List<Group> Groups { get; set; } = new();
}