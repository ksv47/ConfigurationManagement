namespace Configuration_Management.Models;

/// <summary>
/// Вариант запуска 1С из UI (параметр единой LaunchCommand).
/// </summary>
public enum LaunchKind
{
    Enterprise,
    Configurator,
    Thin32,
    Thick32,
    Thin64,
    Thick64
}
