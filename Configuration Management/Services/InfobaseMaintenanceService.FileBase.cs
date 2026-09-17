using System.IO;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Обслуживание файловой базы на Windows: проверка существования,
/// физическое удаление, маркер блокировки (WPF-часть).
/// </summary>
public static partial class InfobaseMaintenanceService
{
    /// <summary>
    /// Проверяет, существует ли файловая база (каталог или 1Cv8.1CD).
    /// Для не-файловых всегда true.
    /// </summary>
    public static bool FileBaseExists(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return true;

        var path = ib.Connection.FilePath?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path))
            return false;

        if (File.Exists(path))
            return true;
        if (Directory.Exists(path))
        {
            // Каталог базы: ищем 1Cv8.1CD
            if (File.Exists(Path.Combine(path, "1Cv8.1CD")))
                return true;
            // Иногда путь указывает на каталог с подкаталогами
            return Directory.EnumerateFiles(path, "1Cv8.1CD", SearchOption.TopDirectoryOnly).Any();
        }

        return false;
    }

    /// <summary>
    /// Физически удаляет каталог файловой базы (или файл 1Cv8.1CD и соседние файлы в каталоге).
    /// Для не-файловых баз ничего не делает. Возвращает null при успехе или текст ошибки.
    /// </summary>
    public static string? TryDeleteFileBasePhysically(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return LocalizationManager.T("Maint.PhysicalDeleteOnlyFile");

        var dir = GetFileBaseDirectory(ib);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return LocalizationManager.T("Maint.FileBaseDirNotFound");

        // Защита от удаления слишком «корневых» путей
        try
        {
            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var roots = new[]
            {
                Path.GetPathRoot(full) ?? "",
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            foreach (var r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                var rr = Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(full, rr, StringComparison.OrdinalIgnoreCase))
                    return string.Format(LocalizationManager.T("Maint.CannotDeleteSystemRootFormat"), full);
            }
        }
        catch
        {
            // продолжаем с осторожностью
        }

        try
        {
            // Снимаем атрибуты только для чтения у файлов, иначе Directory.Delete может упасть
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(file);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                }
                catch { /* ignore single file */ }
            }

            Directory.Delete(dir, recursive: true);
            return null;
        }
        catch (Exception ex)
        {
            return string.Format(LocalizationManager.T("Maint.DeleteFailedFormat"), dir, ex.Message);
        }
    }

    /// <summary>Установить/снять блокировку файловой базы (маркер в каталоге).</summary>
    public static bool SetFileBaseBlocked(Infobase ib, bool blocked)
    {
        var dir = GetFileBaseDirectory(ib);
        if (dir is null) return false;
        var marker = Path.Combine(dir, BlockMarkerFileName);
        try
        {
            if (blocked)
            {
                if (!File.Exists(marker))
                    File.WriteAllText(marker,
                        $"Blocked by Configuration Management at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");
            }
            else if (File.Exists(marker))
            {
                File.Delete(marker);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}