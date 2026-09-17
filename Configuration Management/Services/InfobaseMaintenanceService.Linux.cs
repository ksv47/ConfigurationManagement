#if LINUX
using System.Collections.Generic;
using System.Linq;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Сервисные операции на Linux (Этап 5): открытие каталога, ярлык на рабочем
    /// столе (.desktop), поиск «битых» файловых баз, завершение процессов 1С.
    /// Класс разбит на partial-файлы по ответственности: процессы (здесь),
    /// <c>.Linux.Folders</c>, <c>.Linux.Shortcuts</c>, <c>.Linux.FileBase</c> и общий <c>.Shared</c>.
    /// </summary>
    public static partial class InfobaseMaintenanceService
    {
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
    }
}
#endif