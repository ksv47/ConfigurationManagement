using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Платформенно-нейтральная часть <see cref="InfobaseMaintenanceService"/>:
/// каталог файловой базы, маркер блокировки, размер базы. Идентична и для WPF,
/// и для Linux, поэтому вынесена в общий файл без #if.
/// </summary>
public static partial class InfobaseMaintenanceService
{
    /// <summary>Имя маркера блокировки файловой базы в её каталоге.</summary>
    public const string BlockMarkerFileName = "1Cv8.blocked";

    /// <summary>Каталог файловой базы (родитель 1Cv8.1CD или сам путь-каталог).</summary>
    public static string? GetFileBaseDirectory(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return null;
        var path = ib.Connection.FilePath?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
            return null;
        if (Directory.Exists(path))
            return path;
        if (File.Exists(path))
            return Path.GetDirectoryName(path);
        var parent = Path.GetDirectoryName(path);
        return Directory.Exists(parent) ? parent : null;
    }

    /// <summary>Проверяет наличие маркера блокировки.</summary>
    public static bool IsFileBaseBlocked(Infobase ib)
    {
        var dir = GetFileBaseDirectory(ib);
        if (dir is null) return false;
        return File.Exists(Path.Combine(dir, BlockMarkerFileName));
    }

    /// <summary>Считает размер файловой базы (каталог целиком или файл 1Cv8.1CD).</summary>
    public static long? CalculateFileBaseSize(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return null;
        var path = ib.Connection.FilePath?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;
            if (Directory.Exists(path))
                return DirSize(path);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return DirSize(parent);
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static long DirSize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* locked file */ }
            }
        }
        catch
        {
            // partial
        }
        return total;
    }
}