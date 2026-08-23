#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Вспомогательные операции с процессами 1С на Linux.
    /// Командная строка читается из /proc/<pid>/cmdline (аналог Win32_Process),
    /// завершение — через Process.Kill() вместе с деревом потомков.
    /// </summary>
    internal static class LinuxProc
    {
        /// <summary>Имена процессов платформы 1С (без .exe).</summary>
        public static readonly string[] OneCProcessNames =
        {
            "1cv8", "1cv8c", "1cv8s", "1cv8a", "ragent", "rmngr", "rphost"
        };

        /// <summary>Читает командную строку процесса из /proc/<pid>/cmdline (аргументы разделены NUL).</summary>
        public static string? ReadCmdLine(int pid)
        {
            try
            {
                var path = $"/proc/{pid}/cmdline";
                if (!File.Exists(path))
                    return null;
                var bytes = File.ReadAllBytes(path);
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(b == 0 ? ' ' : (char)b);
                return sb.ToString().Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Читает имя процесса из /proc/<pid>/comm.</summary>
        public static string? ReadComm(int pid)
        {
            try
            {
                var path = $"/proc/{pid}/comm";
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Перечисляет запущенные процессы 1С. Процессом платформы считается
        /// тот, чей исполняемый файл лежит в каталоге установки 1С либо носит
        /// известное имя. Упоминание «1cv8» в аргументах признаком не служит:
        /// по нему в список попадал любой процесс с путём платформы в строке
        /// запуска, вплоть до терминала с открытым каталогом.
        /// </summary>
        public static IEnumerable<OneCProcess> Enumerate1C()
        {
            const string proc = "/proc";
            if (!Directory.Exists(proc))
                yield break;

            foreach (var dir in Directory.EnumerateDirectories(proc))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid) || pid <= 0)
                    continue;

                var comm = ReadComm(pid) ?? string.Empty;
                var cmd = ReadCmdLine(pid) ?? string.Empty;
                // У чужого пользователя ссылка exe не читается, а командная
                // строка читается: её первый аргумент и даёт путь запуска.
                var executable = ReadExePath(pid) ?? FirstArgument(cmd);

                if (!IsOneCProcess(comm, executable))
                    continue;

                var name = comm.Length > 0 ? comm : Path.GetFileName(executable);
                yield return new OneCProcess(pid, name, cmd, executable);
            }
        }

        /// <summary>Запущенный процесс платформы.</summary>
        public readonly record struct OneCProcess(int Pid, string Name, string CmdLine, string Executable);

        private static bool IsOneCProcess(string comm, string executable)
        {
            if (MatchesOneCName(comm) || comm.StartsWith("1cv8", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(executable))
                return false;

            return MatchesOneCName(Path.GetFileName(executable)) || IsPlatformPath(executable);
        }

        /// <summary>
        /// Путь ведёт внутрь установки платформы: у каталогов 1С есть сегмент
        /// вида 1cv8. Сегмент, а не подстрока: путь домашнего каталога вроде
        /// /home/1cv8admin признаком не является.
        /// </summary>
        private static bool IsPlatformPath(string executable) =>
            executable
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .SkipLast(1)
                .Any(segment => segment.StartsWith("1cv8", StringComparison.OrdinalIgnoreCase));

        /// <summary>Путь запуска: первый аргумент командной строки.</summary>
        private static string FirstArgument(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine))
                return string.Empty;

            var first = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first ?? string.Empty;
        }

        /// <summary>Куда указывает /proc/&lt;pid&gt;/exe. Для чужого процесса недоступно.</summary>
        private static string? ReadExePath(int pid)
        {
            try
            {
                var link = new FileInfo($"/proc/{pid}/exe").LinkTarget;
                return string.IsNullOrEmpty(link) ? null : link;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Имя принадлежит платформе 1С. Сравнение точное: comm в Linux урезан
        /// до пятнадцати символов, но все имена платформы короче.
        /// </summary>
        private static bool MatchesOneCName(string name) =>
            !string.IsNullOrEmpty(name)
            && OneCProcessNames.Any(n => string.Equals(name, n, StringComparison.OrdinalIgnoreCase));

        /// <summary>Жив ли процесс с указанным pid.</summary>
        public static bool IsAlive(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Завершает процесс по pid (SIGTERM/Kill). Ошибки игнорируются.</summary>
        /// <summary>
        /// Завершает процесс вместе с потомками. Возвращает, действительно ли
        /// он завершился: сигнал асинхронный, а прав на чужой процесс может
        /// не быть, поэтому ответ даётся по факту, а не по отправке сигнала.
        /// </summary>
        public static bool KillProcess(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    return true;

                p.Kill(entireProcessTree: true);
                p.WaitForExit(KillWaitMilliseconds);
                return p.HasExited;
            }
            catch (ArgumentException)
            {
                // процесса уже нет
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Сколько ждать выхода процесса после сигнала.</summary>
        private const int KillWaitMilliseconds = 3000;

        /// <summary>
        /// Завершает все процессы 1С, найденные в /proc. Возвращает число завершённых.
        /// </summary>
        /// <summary>
        /// Завершает процессы из переданного снимка. Возвращает, сколько
        /// действительно завершилось и сколько не удалось: у процессов чужого
        /// пользователя прав нет, и без разделения пользователь видел бы
        /// «завершено» при живом сервере.
        /// </summary>
        public static (int Killed, int Failed) KillOneC(IEnumerable<int> pids)
        {
            var killed = 0;
            var failed = 0;

            foreach (var pid in pids.Distinct())
            {
                if (!IsAlive(pid))
                    continue;

                if (KillProcess(pid))
                    killed++;
                else
                    failed++;
            }

            return (killed, failed);
        }

        /// <summary>Число запущенных процессов 1С.</summary>
        public static int CountOneC()
            => Enumerate1C().Select(x => x.Pid).Distinct().Count();
    }
}
#endif