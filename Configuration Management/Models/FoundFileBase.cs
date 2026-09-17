using System;

namespace Configuration_Management.Models;

/// <summary>
/// Найденная на диске файловая база 1С 8 — каталог, содержащий файл базы
/// <c>1Cv8.1CD</c>. Используется диалогом «Поиск потерянных и забытых баз
/// 1С 8» (issue #247): сервис <see cref="Services.InfobaseDiskScanner"/>
/// сканирует выбранные корни поиска и возвращает такие записи.
/// </summary>
public sealed class FoundFileBase
{
    /// <summary>Каталог базы (родитель файла <c>1Cv8.1CD</c>).</summary>
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>Полный путь к файлу базы <c>1Cv8.1CD</c>.</summary>
    public string DbFilePath { get; init; } = string.Empty;

    /// <summary>Размер файла <c>1Cv8.1CD</c> в байтах.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Дата последнего изменения файла <c>1Cv8.1CD</c>.</summary>
    public DateTime LastWriteTime { get; init; }
}