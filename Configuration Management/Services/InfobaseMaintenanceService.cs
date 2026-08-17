using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервисные операции по аналогии со StartManager:
/// открытие каталога, ярлык на рабочем столе, поиск «битых» файловых баз, завершение процессов 1С.
/// </summary>
public static class InfobaseMaintenanceService
{
    private static readonly string[] OneCProcessNames =
    {
        "1cv8", "1cv8c", "1cv8s", "1cv8a", "ragent", "rmngr", "rphost"
    };

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
    /// Создаёт ярлык .lnk на рабочем столе как стандартный лаунчер 1С:
    /// цель — 1cv8.exe, аргументы ENTERPRISE /F"..." или /S"...".
    /// </summary>
    public static bool CreateDesktopShortcut(Infobase ib, string? appExecutablePath = null)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                return false;

            var safeName = string.Join("_", (ib.Name ?? "База 1С").Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "База_1С";
            var lnkPath = Path.Combine(desktop, $"{safeName}.lnk");

            var target = OneCLauncher.ResolveThickClientExe(ib);
            if (string.IsNullOrEmpty(target))
                target = FindOneCStartExe();
            if (string.IsNullOrEmpty(target))
                return false;

            var args = OneCLauncher.BuildEnterpriseShortcutArguments(ib);
            return CreateShortcutCom(lnkPath, target, args, ib.Name ?? safeName, target);
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Запускает родной стартер 1С (1CEStart.exe) для сверки списка баз.
    /// </summary>
    public static bool OpenNativeStarter()
    {
        try
        {
            var path = FindOneCStartExe();
            if (string.IsNullOrEmpty(path))
            {
                System.Windows.MessageBox.Show(
                    "Не найден 1CEStart.exe.\nОжидаемый путь: Program Files\\1cv8\\common\\1CEStart.exe",
                    "Стартер 1С",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось запустить стартер 1С.\n{ex.Message}",
                "Стартер 1С",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    public static string? FindOneCStartExe()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            var p = Path.Combine(root!, "1cv8", "common", "1CEStart.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Создание .lnk через WScript.Shell (COM), как у стандартного стартера 1С.</summary>
    private static bool CreateShortcutCom(
        string lnkPath,
        string targetPath,
        string arguments,
        string description,
        string? iconPath = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.Description = description;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                shortcut.IconLocation = iconPath + ",0";
            shortcut.Save();
            return File.Exists(lnkPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Завершает процессы платформы 1С (1cv8, 1cv8c, …).
    /// Возвращает число завершённых процессов.
    /// </summary>
    public static int KillOneCProcesses()
    {
        var killed = 0;
        foreach (var name in OneCProcessNames)
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var p in list)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                    }
                }
                catch
                {
                    // нет прав / уже завершён
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return killed;
    }

    /// <summary>Число запущенных процессов 1С.</summary>
    public static int CountOneCProcesses()
    {
        var count = 0;
        foreach (var name in OneCProcessNames)
        {
            try
            {
                count += Process.GetProcessesByName(name).Length;
            }
            catch
            {
                // ignore
            }
        }
        return count;
    }

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

    /// <summary>
    /// Физически удаляет каталог файловой базы (или файл 1Cv8.1CD и соседние файлы в каталоге).
    /// Для не-файловых баз ничего не делает. Возвращает null при успехе или текст ошибки.
    /// </summary>
    public static string? TryDeleteFileBasePhysically(Infobase ib)
    {
        if (ib.Connection.Type != ConnectionType.File)
            return "Физическое удаление доступно только для файловых баз.";

        var dir = GetFileBaseDirectory(ib);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return "Каталог файловой базы не найден на диске.";

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
                    return $"Отказ: нельзя удалить системный или корневой каталог ({full}).";
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
            return $"Не удалось удалить каталог:\n{dir}\n\n{ex.Message}";
        }
    }

    /// <summary>Проверяет наличие маркера блокировки.</summary>
    public static bool IsFileBaseBlocked(Infobase ib)
    {
        var dir = GetFileBaseDirectory(ib);
        if (dir is null) return false;
        return File.Exists(Path.Combine(dir, BlockMarkerFileName));
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
                catch { /* skip locked */ }
            }
        }
        catch
        {
            // partial
        }
        return total;
    }
}
