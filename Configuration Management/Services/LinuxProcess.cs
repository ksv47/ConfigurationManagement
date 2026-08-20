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

                var isOneC = OneCProcessNames.Any(n =>
                                 string.Equals(comm, n, StringComparison.OrdinalIgnoreCase) ||
                                 comm.StartsWith(n, StringComparison.OrdinalIgnoreCase)) ||
                             cmd.Contains("1cv8", StringComparison.OrdinalIgnoreCase);

                if (isOneC)
                    yield return (pid, comm, cmd);
            }
        }

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

            // Страховка: pkill -f 1cv8 на случай процессов, невидимых в /proc (др. пользователь).
            TryRunPkill();
            return killed;
        }

        /// <summary>Число запущенных процессов 1С.</summary>
        public static int CountOneC()
            => Enumerate1C().Select(x => x.Pid).Distinct().Count();

        private static void TryRunPkill()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "pkill",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { "-f", "1cv8" }
                });
                p?.WaitForExit(2000);
            }
            catch
            {
                // pkill может отсутствовать
            }
        }
    }
}
#endif