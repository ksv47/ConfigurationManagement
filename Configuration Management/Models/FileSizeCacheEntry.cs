namespace Configuration_Management.Models;

/// <summary>
/// Запись кеша размера файловой информационной базы.
///
/// Хранит вычисленный размер и время последней записи пути (файла 1Cv8.1CD или каталога).
/// При запуске, если время последней записи совпадает с сохранённым, диск повторно
/// не сканируется — это заметно ускоряет появление главного окна при большом списке баз.
/// </summary>
public sealed class FileSizeCacheEntry
{
    /// <summary>Размер файловой ИБ в байтах.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Время последней записи пути на момент вычисления размера (UTC).</summary>
    public DateTime LastWriteUtc { get; set; }
}