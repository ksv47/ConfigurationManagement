using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Резервное копирование файла ibases.v8i перед синхронизацией.
/// Копии сохраняются рядом с исходным файлом: ibases.v8i.bak_yyyyMMdd_HHmmss
/// </summary>
public static class IbasesBackupService
{
    /// <summary>
    /// Создаёт резервную копию <paramref name="filePath"/>, если файл существует.
    /// Удаляет старые копии, оставляя не более <paramref name="keepCount"/> последних.
    /// </summary>
    /// <returns>Путь к созданной копии или null, если копия не создавалась.</returns>
    public static string? CreateBackup(string filePath, int keepCount = 5)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        keepCount = Math.Clamp(keepCount, 1, 50);

        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var name = Path.GetFileName(filePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(dir, $"{name}.bak_{stamp}");

        File.Copy(filePath, backupPath, overwrite: false);

        // Удаляем лишние старые копии.
        var prefix = name + ".bak_";
        var backups = Directory.GetFiles(dir, name + ".bak_*")
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var old in backups.Skip(keepCount))
        {
            try { File.Delete(old); }
            catch { /* ignore */ }
        }

        return backupPath;
    }

    /// <summary>
    /// Возвращает список путей к резервным копиям (от новых к старым).
    /// </summary>
    public static IReadOnlyList<string> ListBackups(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var name = Path.GetFileName(filePath);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.GetFiles(dir, name + ".bak_*")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Восстанавливает файл из указанной резервной копии (перезаписывает целевой файл).
    /// </summary>
    public static void RestoreBackup(string backupPath, string targetFilePath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Резервная копия не найдена.", backupPath);

        var targetDir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        File.Copy(backupPath, targetFilePath, overwrite: true);
    }
}
