namespace Configuration_Management.Models;

/// <summary>Запись в истории запусков информационной базы.</summary>
public sealed class LaunchHistoryEntry
{
    /// <summary>Момент запуска.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Режим: Enterprise, Configurator, DumpDT, DumpCF, Test и т.п.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Краткое описание (клиент, разрядность, путь выгрузки).</summary>
    public string Details { get; set; } = string.Empty;

    public string Display =>
        string.IsNullOrWhiteSpace(Details)
            ? $"{Timestamp:dd.MM.yyyy HH:mm} — {Mode}"
            : $"{Timestamp:dd.MM.yyyy HH:mm} — {Mode} ({Details})";
}
