#if LINUX
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Сервисные операции на Linux (Этап 5): открытие каталога, ярлык на рабочем
    /// столе (.desktop), поиск «битых» файловых баз, завершение процессов 1С.
    /// </summary>
    public static class InfobaseMaintenanceService
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

                    using var process = Process.Start(startInfo);
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
                Process.Start(new ProcessStartInfo
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
                Process.Start(new ProcessStartInfo
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

        /// <summary>Что известно о файле базы на диске.</summary>
        public enum FileBaseState
        {
            /// <summary>База на месте.</summary>
            Exists,

            /// <summary>Ни каталога, ни файла базы нет.</summary>
            Missing,

            /// <summary>Проверить не удалось: нет прав, ресурс недоступен.</summary>
            Unknown
        }

        /// <summary>
        /// Состояние файловой базы. Отдельный ответ для «проверить не удалось»
        /// нужен потому, что Directory.Exists возвращает ложь и при отказе
        /// в доступе, и при отвалившемся сетевом ресурсе: без этого базы
        /// с недоступного диска попадали бы в список на удаление.
        /// </summary>
        public static FileBaseState GetFileBaseState(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return FileBaseState.Exists;

            var path = ib.Connection.FilePath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
                return FileBaseState.Missing;

            try
            {
                if (File.Exists(path))
                    return FileBaseState.Exists;

                if (!Directory.Exists(path))
                {
                    // Directory.Exists отвечает ложью и когда каталога нет,
                    // и когда его не прочитать. Различает только запрос
                    // атрибутов: он бросает разные исключения.
                    try
                    {
                        File.GetAttributes(path);
                    }
                    catch (FileNotFoundException)
                    {
                        return FileBaseState.Missing;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return FileBaseState.Missing;
                    }
                    catch (Exception)
                    {
                        return FileBaseState.Unknown;
                    }

                    return FileBaseState.Missing;
                }

                // Имена файлов на Linux регистрозависимы, поэтому маска
                // сравнивается своими силами, а не через EnumerateFiles.
                var found = Directory
                    .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                    .Any(f => string.Equals(Path.GetFileName(f), "1Cv8.1CD", StringComparison.OrdinalIgnoreCase));

                return found ? FileBaseState.Exists : FileBaseState.Missing;
            }
            catch (Exception)
            {
                // Нет прав на чтение каталога, ресурс исчез посреди проверки.
                return FileBaseState.Unknown;
            }
        }

        /// <summary>Существует ли файловая база. «Проверить не удалось» считается существованием.</summary>
        public static bool FileBaseExists(Infobase ib) => GetFileBaseState(ib) != FileBaseState.Missing;

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
        /// Создаёт ярлык .desktop (вместо .lnk) на рабочем столе или в
        /// ~/.local/share/applications для запуска базы «как у стартера 1С».
        /// </summary>
        public static bool CreateDesktopShortcut(Infobase ib, string? appExecutablePath = null)
        {
            try
            {
                var desktop = GetDesktopDirectory();
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                    desktop = GetApplicationsDirectory();
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                    return false;

                var safeName = SanitizeFileName(ib.Name);
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = SanitizeFileName(LocalizationManager.T("Maint.DefaultBaseName"));

                var target = appExecutablePath;
                if (string.IsNullOrEmpty(target))
                    target = OneCLauncher.ResolveThickClientExe(ib);
                if (string.IsNullOrEmpty(target))
                    return false;

                var args = OneCLauncher.BuildEnterpriseShortcutArguments(ib);

                var desktopPath = Path.Combine(desktop, $"{safeName}.desktop");
                var sb = new StringBuilder();
                sb.AppendLine("[Desktop Entry]");
                sb.AppendLine("Type=Application");
                sb.AppendLine($"Name={EscapeDesktopValue(ib.Name ?? safeName)}");
                sb.AppendLine($"Comment={EscapeDesktopValue(LocalizationManager.T("Maint.DesktopShortcutComment"))}");
                // В Exec % — управляющие коды полей (%%), поэтому экранируем их.
                sb.AppendLine($"Exec={QuoteExec(target)} {args.Replace("%", "%%")}");
                // Иконка: путь к исполняемому 1cv8 (если есть) либо имя темы.
                sb.AppendLine($"Icon={EscapeDesktopValue(target)}");
                sb.AppendLine($"Path={EscapeDesktopValue(Path.GetDirectoryName(target) ?? "")}");
                sb.AppendLine("Terminal=false");
                sb.AppendLine("Categories=Office;");
                sb.AppendLine("StartupNotify=true");
                sb.AppendLine("StartupWMClass=1cv8");
                File.WriteAllText(desktopPath, sb.ToString(), new UTF8Encoding(false));

                // На большинстве DE ярлык на рабочем столе должен быть исполняемым.
                try
                {
                    File.SetUnixFileMode(desktopPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // Linux-only; на не-Unix (не бывает под #if LINUX) — пропускаем.
                }

                return File.Exists(desktopPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Запускает родной стартер 1С (1cestart) для сверки списка баз
        /// (аналог 1CEStart.exe на Windows). Ищет общий стартер установленной
        /// платформы и системные пути.
        /// </summary>
        public static bool OpenNativeStarter()
        {
            try
            {
                var path = FindOneCStart();
                if (string.IsNullOrEmpty(path))
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? ""
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Ищет исполняемый файл стартера 1С (1cestart) на Linux.</summary>
        public static string? FindOneCStart()
        {
            // 1. Общий стартер установленной платформы: /opt/1cv8/<вер>/common/1cestart.
            foreach (var (_, binDir) in PlatformVersionService.FindPlatformVersionDirs("64"))
            {
                if (string.IsNullOrEmpty(binDir))
                    continue;
                // binDir это либо <версия>/bin, либо сам каталог версии: раскладка
                // зависит от дистрибутива, поэтому проверяются оба варианта.
                foreach (var baseDir in new[] { binDir, Path.GetDirectoryName(binDir) })
                {
                    if (string.IsNullOrEmpty(baseDir))
                        continue;
                    var common = Path.Combine(baseDir, "common", "1cestart");
                    if (File.Exists(common))
                        return common;
                }
            }

            // 2. Известные системные и пользовательские пути.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                "/opt/1cv8/common/1cestart",
                "/opt/1cv8.x86_64/common/1cestart",
                "/usr/bin/1cestart",
                "/usr/local/bin/1cestart",
                string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".1cv8", "1CEStart", "1cestart")
            };
            foreach (var root in roots)
            {
                if (!string.IsNullOrEmpty(root) && File.Exists(root))
                    return root;
            }

            return null;
        }

        private static string GetDesktopDirectory()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-user-dir",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    ArgumentList = { "DESKTOP" }
                });
                var dir = p?.StandardOutput.ReadToEnd()?.Trim();
                p?.WaitForExit(2000);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            catch
            {
                // xdg-user-dir может отсутствовать
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var desktop = Path.Combine(home, "Desktop");
            if (Directory.Exists(desktop))
                return desktop;
            desktop = Path.Combine(home, LocalizationManager.T("Common.DesktopFolder"));
            return Directory.Exists(desktop) ? desktop : string.Empty;
        }

        private static string GetApplicationsDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "applications");
        }

        private static string SanitizeFileName(string? name)
        {
            var chars = (name ?? string.Empty).ToCharArray();
            var sb = new StringBuilder(chars.Length);
            foreach (var c in chars)
            {
                sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or ' '
                    ? (c == ' ' ? '_' : c)
                    : '_');
            }
            return sb.ToString();
        }

        private static string EscapeDesktopValue(string value)
            => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\n", "\\n");

        private static string QuoteExec(string path)
        {
            var v = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return v.Contains(' ') ? $"\"{v}\"" : v;
        }

        /// <summary>
        /// Запущенный процесс платформы. Время старта хранится вместе с номером:
        /// номер ядро переиспользует, и без сверки можно завершить чужой процесс,
        /// занявший номер, пока открыт вопрос пользователю.
        /// </summary>
        public readonly record struct OneCProcessInfo(int Pid, string Name, string? StartTime);

        /// <summary>Снимок запущенных процессов платформы.</summary>
        public static IReadOnlyList<OneCProcessInfo> SnapshotOneCProcesses() =>
            LinuxProc.Enumerate1C()
                .Select(p => new OneCProcessInfo(p.Pid, p.Name, p.StartTime))
                .ToList();

        /// <summary>
        /// Завершает процессы из снимка. Работа идёт по снимку, показанному
        /// пользователю: иначе между вопросом и действием множество процессов
        /// успевает измениться.
        /// </summary>
        public static (int Killed, int Failed) KillOneCProcesses(IEnumerable<OneCProcessInfo> snapshot) =>
            LinuxProc.KillOneC(snapshot.Select(p => (p.Pid, p.StartTime)));


        /// <summary>Разбивка снимка по именам процессов.</summary>
        public static IReadOnlyList<(string Name, int Count)> DescribeProcesses(
            IEnumerable<OneCProcessInfo> snapshot) =>
            snapshot
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Count: g.Select(p => p.Pid).Distinct().Count()))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Имя маркера блокировки файловой базы в её каталоге.</summary>
        public const string BlockMarkerFileName = "1Cv8.blocked";

        /// <summary>Физически удаляет каталог файловой базы. Возвращает null при успехе или текст ошибки.</summary>
        public static string? TryDeleteFileBasePhysically(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return LocalizationManager.T("Maint.PhysicalDeleteOnlyFile");

            var dir = GetFileBaseDirectory(ib);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return LocalizationManager.T("Maint.FileBaseDirNotFound");

            // Защита от удаления слишком «корневых» путей.
            try
            {
                var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var roots = new List<string>();
                foreach (var p in new[] { Path.GetPathRoot(full), "/", home })
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    var rr = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(full, rr, StringComparison.OrdinalIgnoreCase))
                        return string.Format(LocalizationManager.T("Maint.CannotDeleteSystemRootFormat"), full);
                }
            }
            catch
            {
                // продолжаем с осторожностью
            }

            // Каталог по ссылке удалять нельзя: на Linux удалилась бы сама ссылка,
            // а файлы базы остались бы на месте, и пользователь считал бы,
            // что база удалена.
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget is not null)
                return string.Format(LocalizationManager.T("Maint.CannotDeleteSymlinkFormat"), dir);

            // Признак файловой базы перепроверяется прямо перед удалением:
            // между проверкой в диалоге и этим шагом каталог мог смениться.
            if (!File.Exists(Path.Combine(dir, "1Cv8.1CD")))
                return LocalizationManager.T("Maint.FileBaseDirNotFound");

            try
            {
                Directory.Delete(dir, recursive: true);
                return null;
            }
            catch (Exception ex)
            {
                return string.Format(LocalizationManager.T("Maint.DeleteFailedFormat"), dir, ex.Message);
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
                            $"Blocked by Configuration Management at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
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
                    catch { }
                }
            }
            catch
            {
                // partial
            }
            return total;
        }
    }
}
#endif