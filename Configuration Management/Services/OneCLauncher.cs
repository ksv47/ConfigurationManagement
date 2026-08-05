using System.Diagnostics;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Режим запуска платформы 1С.
/// </summary>
public enum OneCLaunchMode
{
    /// <summary>Режим «1С:Предприятие» (клиент).</summary>
    Enterprise,

    /// <summary>Режим «Конфигуратор» (разработка).</summary>
    Configurator
}

/// <summary>
/// Тип клиента 1С:Предприятие.
/// </summary>
public enum OneCClientType
{
    /// <summary>Тонкий клиент (управляемое приложение).</summary>
    Thin,

    /// <summary>Толстый клиент (обычное приложение).</summary>
    Thick
}

/// <summary>
/// Разрядность исполняемого файла платформы 1С.
/// </summary>
public enum OneCArchitecture
{
    /// <summary>32-битная версия.</summary>
    x86,

    /// <summary>64-битная версия.</summary>
    x64
}

/// <summary>
/// Сервис запуска платформы 1С:Предприятие.
/// </summary>
public static class OneCLauncher
{
    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы в заданном режиме.
    /// Тип клиента определяется из режима запуска базы (LaunchMode):
    /// «Автоматический», «Тонкий клиент», «Толстый клиент» или «Веб-клиент».
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode)
    {
        // В режиме «Конфигуратор» тип клиента не применяется.
        if (mode == OneCLaunchMode.Configurator)
            return Launch(infobase, mode, OneCClientType.Thin, OneCArchitecture.x86);

        // Веб-клиент запускается через браузер.
        if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
            return LaunchWebClient(infobase);

        // Автоматический режим — платформа сама выбирает клиент (без /RunMode).
        if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, null, OneCArchitecture.x86);

        // Толстый клиент.
        if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, OneCArchitecture.x86);

        // По умолчанию — тонкий клиент.
        return Launch(infobase, mode, OneCClientType.Thin, OneCArchitecture.x86);
    }

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы с заданным
    /// типом клиента и разрядностью.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="clientType">Тип клиента (тонкий или толстый). null — автоматический выбор платформой.</param>
    /// <param name="architecture">Разрядность (32 или 64 бита).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture)
    {
        var exePath = FindExecutable(architecture);
        if (string.IsNullOrEmpty(exePath))
        {
            System.Windows.MessageBox.Show(
                "Не удалось найти платформу 1С.\n" +
                "Убедитесь, что платформа 1С установлена.",
                "Платформа 1С не найдена",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var arguments = BuildArguments(infobase, mode, clientType);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false
            };
            Process.Start(psi);

            // Обновляем дату последнего запуска базы.
            infobase.LastLaunchDate = DateTime.Now;

            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось запустить платформу 1С.\n{ex.Message}",
                "Ошибка запуска",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Формирует аргументы командной строки для запуска 1С.
    /// </summary>
    private static string BuildArguments(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType)
    {
        var modeArg = mode switch
        {
            OneCLaunchMode.Enterprise => "ENTERPRISE",
            _ => "DESIGNER"
        };

        // Параметр типа клиента применяется только в режиме «Предприятие».
        // null — автоматический режим: параметр /RunMode не передаётся,
        // платформа сама выбирает подходящий клиент.
        var clientArg = mode == OneCLaunchMode.Enterprise && clientType.HasValue
            ? clientType.Value switch
            {
                OneCClientType.Thin => " /RunModeManagedApplication",
                _ => " /RunModeOrdinaryApplication"
            }
            : "";

        var conn = infobase.Connection;
        string connectionArg = conn.Type switch
        {
            ConnectionType.File => $" /F \"{conn.FilePath}\"",
            _ => $" /S \"{conn.Server}\\{conn.DatabaseName}\""
        };

        // Если указан логин — запускаем с параметрами аутентификации,
        // иначе — автоматически (аутентификация ОС).
        var authArg = string.IsNullOrWhiteSpace(conn.User)
            ? ""
            : $" /Usr:\"{conn.User}\" /Pwd:\"{conn.Password}\"";

        // Дополнительные параметры запуска, заданные пользователем
        // (например, /UC, /DisableStartupMessages и др.).
        var extraArg = string.IsNullOrWhiteSpace(infobase.LaunchParameters)
            ? ""
            : " " + infobase.LaunchParameters.Trim();

        return $"{modeArg}{clientArg}{connectionArg}{authArg}{extraArg}";
    }

    /// <summary>
    /// Запускает веб-клиент 1С в браузере по умолчанию.
    /// Для клиент-серверной базы формируется адрес http://сервер/имя_базы,
    /// для файловой базы веб-клиент недоступен — выводится предупреждение.
    /// </summary>
    private static bool LaunchWebClient(Infobase infobase)
    {
        var conn = infobase.Connection;
        if (conn.Type != ConnectionType.ClientServer)
        {
            System.Windows.MessageBox.Show(
                "Веб-клиент доступен только для клиент-серверных информационных баз.",
                "Веб-клиент недоступен",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var url = $"http://{conn.Server}/{conn.DatabaseName}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            infobase.LastLaunchDate = DateTime.Now;
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось открыть веб-клиент.\n{ex.Message}",
                "Ошибка запуска",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Ищет исполняемый файл платформы 1С нужной разрядности.
    /// Сначала ищет стандартный лаунчер 1CEStart.exe (общий для всех версий),
    /// который автоматически подбирает версию платформы. Если лаунчер не найден —
    /// ищет 1cv8.exe / 1cv8x64.exe в каталогах установленных версий.
    /// </summary>
    private static string? FindExecutable(OneCArchitecture architecture)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var searchRoots = new List<string>();
        if (!string.IsNullOrEmpty(programFiles))
            searchRoots.Add(programFiles);
        if (!string.IsNullOrEmpty(programFilesX86) && programFilesX86 != programFiles)
            searchRoots.Add(programFilesX86);

        // 1. Ищем стандартный лаунчер 1CEStart.exe в каталоге common.
        //    Лаунчер подбирает версию автоматически, но не позволяет выбрать разрядность.
        foreach (var root in searchRoots)
        {
            var launcherPath = Path.Combine(root, "1cv8", "common", "1CEStart.exe");
            if (File.Exists(launcherPath))
                return launcherPath;
        }

        // 2. Если лаунчер не найден — ищем конкретный exe нужной разрядности.
        var exeName = architecture == OneCArchitecture.x64 ? "1cv8x64.exe" : "1cv8.exe";
        var candidates = new List<string>();
        foreach (var root in searchRoots)
        {
            var baseDir = Path.Combine(root, "1cv8");
            if (!Directory.Exists(baseDir))
                continue;

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                candidates.Add(Path.Combine(dir, "bin", exeName));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}