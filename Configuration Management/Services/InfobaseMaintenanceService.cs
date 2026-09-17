using System.Diagnostics;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервисные операции по аналогии со StartManager:
/// открытие каталога, ярлык на рабочем столе, поиск «битых» файловых баз, завершение процессов 1С.
/// Класс разбит на partial-файлы по ответственности: <c>.Folders</c> (здесь), <c>.Shortcuts</c>,
/// <c>.Processes</c>, <c>.FileBase</c> и общий <c>.Shared</c>.
/// </summary>
public static partial class InfobaseMaintenanceService
{
    /// <summary>
    /// Открывает в проводнике каталог файловой базы (или выделяет файл 1Cv8.1CD).
    /// Для клиент-серверных — ничего не делает (false).
    /// </summary>
    public static bool OpenInfobaseFolder(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return false;

        var path = ib.Connection.FilePath?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            if (File.Exists(path))
            {
                // Выделить файл в проводнике
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return true;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                return true;
            }

            // Каталог родителя
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{parent}\"",
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
