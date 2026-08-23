#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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

        /// <summary>Первый аргумент командной строки, то есть путь запуска.</summary>
        public static string? ReadFirstArgument(int pid)
        {
            try
            {
                var bytes = File.ReadAllBytes($"/proc/{pid}/cmdline");
                var end = Array.IndexOf(bytes, (byte)0);
                if (end < 0)
                    end = bytes.Length;
                return end == 0 ? null : Encoding.UTF8.GetString(bytes, 0, end);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Время старта процесса из /proc/&lt;pid&gt;/stat. Номер процесса ядро
        /// переиспользует, а пара «номер плюс время старта» уникальна, и по ней
        /// перед сигналом видно, тот ли это процесс, что показывали.
        /// </summary>
        public static string? ReadStartTime(int pid)
        {
            try
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                // Имя процесса в скобках может содержать пробелы, поэтому поля
                // считаются от закрывающей скобки.
                var tail = stat[(stat.LastIndexOf(')') + 1)..];
                var fields = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // starttime это 22-е поле записи, то есть 20-е после имени.
                return fields.Length >= 20 ? fields[19] : null;
            }
            catch
            {
                return null;
            }
        }

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

            var platformRoots = PlatformRoots();

            foreach (var dir in Directory.EnumerateDirectories(proc))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid) || pid <= 0)
                    continue;

                var comm = ReadComm(pid) ?? string.Empty;
                var cmd = ReadCmdLine(pid) ?? string.Empty;
                // У чужого пользователя ссылка exe не читается, а командная
                // строка читается: её первый аргумент и даёт путь запуска.
                var executable = ReadExePath(pid) ?? ReadFirstArgument(pid) ?? string.Empty;

                if (!IsOneCProcess(comm, executable, platformRoots))
                    continue;

                var name = comm.Length > 0 ? comm : Path.GetFileName(executable);
                yield return new OneCProcess(pid, name, cmd, executable, ReadStartTime(pid));
            }
        }

        /// <summary>Запущенный процесс платформы.</summary>
        public readonly record struct OneCProcess(
            int Pid, string Name, string CmdLine, string Executable, string? StartTime);

        private static bool IsOneCProcess(string comm, string executable, IReadOnlyList<string> platformRoots)
        {
            if (MatchesOneCName(comm))
                return true;

            if (string.IsNullOrEmpty(executable))
                return false;

            return MatchesOneCName(Path.GetFileName(executable)) || IsUnderPlatformRoot(executable, platformRoots);
        }

        /// <summary>
        /// Исполняемый файл лежит внутри найденной установки платформы.
        /// Сравниваются реальные корни, а не имя каталога: по одному лишь
        /// началу «1cv8» под правило попадал и домашний каталог вида
        /// /home/1cv8admin, то есть чужие процессы этого пользователя.
        /// </summary>
        private static bool IsUnderPlatformRoot(string executable, IReadOnlyList<string> roots)
        {
            var full = TryGetFullPath(executable);
            if (full is null)
                return false;

            foreach (var root in roots)
            {
                var normalized = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(normalized, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string? TryGetFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return null; }
        }

        /// <summary>
        /// Корни установленных платформ: каталоги версий, которые нашёл поиск,
        /// и их родители. Собираются один раз на перечисление, потому что поиск
        /// ходит по файловой системе.
        /// </summary>
        private static IReadOnlyList<string> PlatformRoots()
        {
            var roots = new List<string>();
            try
            {
                // Разрядность здесь кодируется как «64» и «32»: именно так её
                // принимает Linux-версия поиска платформы.
                foreach (var (_, binDir) in PlatformVersionService.FindPlatformVersionDirs("64")
                             .Concat(PlatformVersionService.FindPlatformVersionDirs("32")))
                {
                    var full = TryGetFullPath(binDir);
                    if (full is not null)
                        roots.Add(full);
                }
            }
            catch
            {
                // Поиск платформы не должен ронять перечисление процессов.
            }

            return roots.Distinct(StringComparer.Ordinal).ToList();
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
        /// <summary>Отправляет сигнал завершения вместе с деревом потомков.</summary>
        private static bool SendKill(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    return true;

                p.Kill(entireProcessTree: true);
                return true;
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

        /// <summary>Сколько ждать выхода процессов после сигналов, всего.</summary>
        private const int KillWaitMilliseconds = 3000;

        /// <summary>Шаг опроса при ожидании выхода.</summary>
        private const int KillPollMilliseconds = 50;

        /// <summary>
        /// Завершает процессы из переданного снимка. Возвращает, сколько
        /// действительно завершилось и сколько не удалось: у процессов чужого
        /// пользователя прав нет, и без разделения пользователь видел бы
        /// «завершено» при живом сервере.
        /// </summary>
        public static (int Killed, int Failed) KillOneC(IEnumerable<(int Pid, string? StartTime)> processes)
        {
            var targets = new List<int>();
            var failed = 0;

            foreach (var (pid, startTime) in processes.DistinctBy(p => p.Pid))
            {
                if (!IsAlive(pid))
                {
                    // Процесс ушёл сам или вместе с чужим деревом: это успех,
                    // иначе сумма не сходится с числом в вопросе.
                    targets.Add(pid);
                    continue;
                }

                // Номер процесса ядро переиспользует: перед сигналом сверяем,
                // что это тот самый экземпляр, который показывали пользователю.
                // Пустое время старта означает, что подтвердить личность нечем,
                // и тогда сигнал не посылается вовсе: номер процесса ядро
                // переиспользует, и под удаление попал бы чужой процесс.
                if (startTime is null || ReadStartTime(pid) != startTime)
                {
                    failed++;
                    continue;
                }

                if (SendKill(pid))
                    targets.Add(pid);
                else
                    failed++;
            }

            // Сигналы разосланы всем, теперь одно общее ожидание: иначе окно
            // стояло бы по три секунды на каждый зависший процесс.
            var deadline = Environment.TickCount64 + KillWaitMilliseconds;
            while (targets.Any(IsAlive) && Environment.TickCount64 < deadline)
                Thread.Sleep(KillPollMilliseconds);

            var killed = targets.Count(pid => !IsAlive(pid));
            failed += targets.Count - killed;

            return (killed, failed);
        }

    }
}
#endif