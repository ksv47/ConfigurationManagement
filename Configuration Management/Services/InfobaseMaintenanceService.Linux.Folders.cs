#if LINUX
using System.Diagnostics;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Открытие каталога файловой базы в файловом менеджере (Linux-часть).
    /// </summary>
    public static partial class InfobaseMaintenanceService
    {
        /// <summary>
        /// Открывает каталог файловой базы в файловом менеджере. Если путь указывает
        /// на файл 1Cv8.1CD (или каталог базы содержит его) — файл выделяется
        /// (менеджером, родным для текущего окружения, иначе gio open). Иначе каталог открывается
        /// через xdg-open.
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
                // Прямой путь к файлу базы 1Cv8.1CD → выделить файл в менеджере.
                if (File.Exists(path))
                    return SelectFileInManager(path);

                // Путь — каталог базы: выделяем 1Cv8.1CD внутри, если он есть,
                // иначе просто открываем каталог.
                if (Directory.Exists(path))
                {
                    var dbf = Path.Combine(path, "1Cv8.1CD");
                    if (File.Exists(dbf))
                        return SelectFileInManager(dbf);
                    return OpenDirectory(path);
                }

                // Путь не существует — открываем родительский каталог.
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    return OpenDirectory(parent);
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Показывает файл в файловом менеджере. Порядок задаёт окружение рабочего
        /// стола: родной менеджер первым, остальные запасными. Способ показа
        /// у менеджеров разный, см. <see cref="LinuxDesktopEnvironment.FileManagers"/>.
        /// Если ни один не подошёл, пробуется gio open, затем xdg-open каталога.
        /// </summary>
        private static bool SelectFileInManager(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return OpenDirectory(dir);

            // Порядок задаёт текущее окружение рабочего стола: родной менеджер идёт
            // первым, чтобы на KDE не открывался файловый менеджер GNOME и наоборот.
            // Список уже отфильтрован по наличию в PATH, поэтому запуск не выбирает
            // первый попавшийся установленный менеджер вслепую.
            foreach (var (manager, argument, passFile) in LinuxDesktopEnvironment.FileManagers())
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = manager,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    if (!string.IsNullOrEmpty(argument))
                        startInfo.ArgumentList.Add(argument);
                    // Менеджеру, который не умеет выделять файл, даём каталог:
                    // от пути к файлу он открыл бы сам файл приложением по умолчанию.
                    startInfo.ArgumentList.Add(passFile ? filePath : dir);

                    using var process = LinuxProcessEnvironment.Start(startInfo);
                    if (process is null)
                        continue;

                    // Менеджер остаётся работать, поэтому ждём недолго: интересует
                    // только случай, когда он завершился сразу с ошибкой.
                    if (process.WaitForExit(700) && process.ExitCode != 0)
                        continue;

                    return true;
                }
                catch
                {
                    // менеджер не запустился — пробуем следующий
                }
            }

            try
            {
                LinuxProcessEnvironment.Start(new ProcessStartInfo
                {
                    FileName = "gio",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { "open", filePath }
                });
                return true;
            }
            catch
            {
                // fallback ниже
            }

            return OpenDirectory(dir);
        }

        /// <summary>Открывает каталог в файловом менеджере по умолчанию (xdg-open).</summary>
        private static bool OpenDirectory(string? dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;
            try
            {
                LinuxProcessEnvironment.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { dir }
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif