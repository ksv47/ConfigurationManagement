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
    /// завершение — через Process.Kill() с последующим pkill -f 1cv8.
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
        /// Перечисляет запущенные процессы 1С: те, чьё имя (comm) совпадает с
        /// известными именами 1С либо чья командная строка содержит «1cv8».
        /// </summary>
        public static IEnumerable<(int Pid, string Name, string? CmdLine)> Enumerate1C()
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

                // Признак это имя самого исполняемого файла, а не упоминание
                // «1cv8» где-нибудь в командной строке: под прежнее правило
                // попадал любой процесс, у которого в аргументах есть путь
                // с 1cv8, вплоть до терминала с открытым каталогом платформы.
                var isOneC = MatchesOneCName(comm) || MatchesOneCName(ExecutableName(cmd));

                if (isOneC)
                    yield return (pid, comm, cmd);
            }
        }

        /// <summary>Имя исполняемого файла из командной строки, без пути и аргументов.</summary>
        private static string ExecutableName(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine))
                return string.Empty;

            var first = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrEmpty(first) ? string.Empty : Path.GetFileName(first);
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
        public static void KillProcess(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                    p.Kill();
            }
            catch
            {
                // нет прав или процесс уже завершён
            }
        }

        /// <summary>
        /// Завершает все процессы 1С, найденные в /proc. Возвращает число завершённых.
        /// </summary>
        public static int KillAllOneC()
        {
            var pids = Enumerate1C().Select(x => x.Pid).Distinct().ToList();
            var killed = 0;
            foreach (var pid in pids)
            {
                if (!IsAlive(pid))
                    continue;
                KillProcess(pid);
                killed++;
            }

            // Прежняя страховка `pkill -f 1cv8` убрана: она била по подстроке
            // в командной строке и уносила чужие процессы, у которых в аргументах
            // просто встречается путь платформы.
            return killed;
        }

        /// <summary>Число запущенных процессов 1С.</summary>
        public static int CountOneC()
            => Enumerate1C().Select(x => x.Pid).Distinct().Count();

    }
}
#endif