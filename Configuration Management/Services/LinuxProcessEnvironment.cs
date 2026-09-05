#if LINUX
using System;
using System.Diagnostics;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Подготовка окружения для внешних процессов, запускаемых из приложения на Linux.
    /// <para>
    /// Когда программа работает из пакета AppImage, её окружение содержит переменные
    /// пакета (<c>APPIMAGE</c>, <c>APPDIR</c>, <c>OWD</c>, <c>ARGV0</c>) и пути внутрь
    /// разового монтирования в <c>PATH</c> и <c>LD_LIBRARY_PATH</c>. Дочерние процессы
    /// наследуют это целиком, и получаются два следствия: 1С:Предприятие ищет библиотеки
    /// и вспомогательные программы внутри чужого пакета, а рабочий стол считает её окна
    /// окнами этой программы, потому что переменную <c>APPIMAGE</c> он читает прямо из
    /// окружения процесса. В панели задач окна платформы при этом показываются значком
    /// этой программы или пропадают из панели вовсе.
    /// </para>
    /// </summary>
    internal static class LinuxProcessEnvironment
    {
        /// <summary>Переменные, которые выставляет сам пакет AppImage.</summary>
        private static readonly string[] AppImageVariables =
        {
            "APPIMAGE", "APPDIR", "OWD", "ARGV0", "APPIMAGE_UUID"
        };

        /// <summary>
        /// Запускает процесс с очищенным от следов пакета окружением. Заменяет прямой
        /// вызов <see cref="Process.Start(ProcessStartInfo)"/> во всех местах, где
        /// приложение запускает стороннюю программу: 1С:Предприятие, файловый менеджер,
        /// браузер, вспомогательные утилиты рабочего стола.
        /// </summary>
        public static Process? Start(ProcessStartInfo psi)
        {
            Sanitize(psi);
            return Process.Start(psi);
        }

        /// <summary>
        /// Убирает из окружения запускаемого процесса следы пакета AppImage: сами
        /// переменные пакета и пути внутрь его монтирования в <c>PATH</c> и
        /// <c>LD_LIBRARY_PATH</c>. Вне AppImage не делает ничего.
        /// </summary>
        public static void Sanitize(ProcessStartInfo psi)
        {
            var appDir = Environment.GetEnvironmentVariable("APPDIR");

            foreach (var name in AppImageVariables)
                psi.Environment.Remove(name);

            if (string.IsNullOrWhiteSpace(appDir))
                return;

            RemovePathEntriesUnder(psi, "PATH", appDir);
            RemovePathEntriesUnder(psi, "LD_LIBRARY_PATH", appDir);
        }

        /// <summary>
        /// Удаляет из списка путей переменной <paramref name="variable"/> элементы,
        /// лежащие внутри каталога <paramref name="prefix"/>. Пустую переменную убирает
        /// целиком, чтобы не отдавать процессу пустой список путей.
        /// </summary>
        private static void RemovePathEntriesUnder(ProcessStartInfo psi, string variable, string prefix)
        {
            if (!psi.Environment.TryGetValue(variable, out var value) || string.IsNullOrEmpty(value))
                return;

            var kept = new System.Collections.Generic.List<string>();
            foreach (var entry in value.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!entry.StartsWith(prefix, StringComparison.Ordinal))
                    kept.Add(entry);
            }

            if (kept.Count == 0)
                psi.Environment.Remove(variable);
            else
                psi.Environment[variable] = string.Join(':', kept);
        }
    }
}
#endif
