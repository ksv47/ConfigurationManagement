namespace Configuration_Management.Models;

/// <summary>
/// Группа установленных версий платформы 1С, объединённых по мажорной версии
/// (например, «8.3.27»). Содержит полные версии (например, «8.3.27.1234»).
/// </summary>
public class PlatformVersionGroup
{
    /// <summary>Мажорная версия группы (например, «8.3.27»).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Полные версии платформы, входящие в группу.</summary>
    public List<string> Versions { get; set; } = new();
}