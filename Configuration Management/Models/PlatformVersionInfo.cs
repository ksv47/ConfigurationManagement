namespace Configuration_Management.Models;

/// <summary>
/// Установленная версия платформы 1С с путём к каталогу версии.
/// </summary>
public class PlatformVersionInfo
{
    /// <summary>Отображаемая строка, например «8.3.27.2214 (64)».</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>Полный путь к папке версии (каталог с bin/).</summary>
    public string Path { get; set; } = string.Empty;

    public override string ToString() => Display;
}
