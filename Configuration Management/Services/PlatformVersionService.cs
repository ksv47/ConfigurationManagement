using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис поиска установленных версий платформы 1С:Предприятие.
/// </summary>
public static class PlatformVersionService
{
    /// <summary>
    /// Ищет установленные версии платформы 1С в стандартных каталогах
    /// Program Files\1cv8 и Program Files (x86)\1cv8.
    /// Имена каталогов версий имеют вид «8.3.25.1234».
    /// </summary>
    /// <returns>Отсортированный по убыванию список версий платформы.</returns>
    public static List<string> FindInstalledVersions()
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var searchRoots = new List<string>();
        if (!string.IsNullOrEmpty(programFiles))
            searchRoots.Add(programFiles);
        if (!string.IsNullOrEmpty(programFilesX86) && programFilesX86 != programFiles)
            searchRoots.Add(programFilesX86);

        foreach (var root in searchRoots)
        {
            var baseDir = Path.Combine(root, "1cv8");
            if (!Directory.Exists(baseDir))
                continue;

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var name = Path.GetFileName(dir);
                // Каталог версии платформы имеет вид «8.3.25.1234».
                if (IsVersionDirectory(name))
                {
                    versions.Add(name);
                }
            }
        }

        // Сортируем по убыванию версии (сначала самые новые).
        return versions
            .OrderByDescending(v => v, new VersionComparer())
            .ToList();
    }

    /// <summary>
    /// Проверяет, является ли имя каталога версией платформы вида «8.3.25.1234».
    /// </summary>
    private static bool IsVersionDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var parts = name.Split('.');
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Компаратор для сортировки версий по убыванию.
    /// </summary>
    private sealed class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var xParts = x.Split('.').Select(int.Parse).ToArray();
            var yParts = y.Split('.').Select(int.Parse).ToArray();

            var length = Math.Max(xParts.Length, yParts.Length);
            for (var i = 0; i < length; i++)
            {
                var xVal = i < xParts.Length ? xParts[i] : 0;
                var yVal = i < yParts.Length ? yParts[i] : 0;
                if (xVal != yVal)
                    return xVal.CompareTo(yVal);
            }

            return 0;
        }
    }
}