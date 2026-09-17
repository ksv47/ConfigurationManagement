#if LINUX
using System.Diagnostics;
using System.IO;
using System.Text;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Создание ярлыков (.desktop) и запуск родного стартера 1С (Linux-часть).
    /// </summary>
    public static partial class InfobaseMaintenanceService
    {
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
                // Файл собирается под #if LINUX, но анализатор видит и другие ОС (CA1416),
                // поэтому вызов закрыт явной проверкой ОС.
                try
                {
                    if (OperatingSystem.IsLinux())
                        File.SetUnixFileMode(desktopPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // Права можно не выставить — ярлык останется, но без флага исполняемости.
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

                LinuxProcessEnvironment.Start(new ProcessStartInfo
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
                using var p = LinuxProcessEnvironment.Start(new ProcessStartInfo
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
    }
}
#endif