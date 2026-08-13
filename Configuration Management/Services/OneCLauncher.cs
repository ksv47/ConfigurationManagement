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
    /// Разрядность берётся из настройки базы (Architecture), версия — из PlatformVersion.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode)
    {
        // В режиме «Конфигуратор» тип клиента не применяется.
        if (mode == OneCLaunchMode.Configurator)
            return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase));

        // Веб-клиент запускается через браузер.
        if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
            return LaunchWebClient(infobase);

        // Автоматический режим — платформа сама выбирает клиент (без /RunMode).
        if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, null, GetArchitecture(infobase));

        // Толстый клиент.
        if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, GetArchitecture(infobase));

        // По умолчанию — тонкий клиент.
        return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase));
    }

    /// <summary>
    /// Возвращает разрядность запуска из настройки базы. «64» — 64-битная платформа,
    /// любое другое значение (в т.ч. пустое) — 32-битная.
    /// </summary>
    private static OneCArchitecture GetArchitecture(Infobase infobase)
        => string.Equals(infobase.Architecture, "64", StringComparison.OrdinalIgnoreCase)
            ? OneCArchitecture.x64
            : OneCArchitecture.x86;

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
        var exePath = FindExecutable(infobase.PlatformVersion, architecture, clientType, mode);
        if (string.IsNullOrEmpty(exePath))
        {
            var archLabel = architecture == OneCArchitecture.x64 ? "64-бит" : "32-бит";
            var versionHint = string.IsNullOrWhiteSpace(infobase.PlatformVersion)
                ? "Укажите версию платформы в настройках базы или установите 1С."
                : $"Запрошена версия: {infobase.PlatformVersion}";
            System.Windows.MessageBox.Show(
                $"Не удалось найти платформу 1С ({archLabel}).\n{versionHint}\n\n" +
                "Имена файлов:\n" +
                "• 64-бит (совр.): 1cv8.exe / 1cv8c.exe в Program Files\\1cv8\\<ver>\\bin\\\n" +
                "• 64-бит (стар.): 1cv8x64.exe\n" +
                "• 32-бит: 1cv8.exe / 1cv8c.exe в Program Files (x86)\\1cv8\\<ver>\\bin\\",
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
            ConnectionType.WebServer => $" /WS \"{conn.WebUrl}\"",
            _ => $" /S \"{conn.Server}\\{conn.DatabaseName}\""
        };

        // Режим аутентификации — как в стандартном лаунчере 1С:
        // Prompt — не передаём /N /P (платформа сама запросит);
        // Credentials — /N и /P с сохранёнными данными;
        // Windows — /WA+ (аутентификация ОС).
        string authArg = conn.AuthenticationMode switch
        {
            AuthenticationMode.Credentials when !string.IsNullOrWhiteSpace(conn.User)
                => $" /N\"{conn.User}\" /P\"{conn.Password}\"",
            AuthenticationMode.Windows
                => " /WA+",
            _ => ""
        };

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
        string url;

        if (conn.Type == ConnectionType.WebServer)
        {
            if (string.IsNullOrWhiteSpace(conn.WebUrl))
            {
                System.Windows.MessageBox.Show(
                    "Не указан URL веб-публикации.",
                    "Веб-клиент недоступен",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }
            url = conn.WebUrl;
        }
        else if (conn.Type == ConnectionType.ClientServer)
        {
            url = $"http://{conn.Server}/{conn.DatabaseName}";
        }
        else
        {
            System.Windows.MessageBox.Show(
                "Веб-клиент доступен только для клиент-серверных баз и баз на веб-сервере.",
                "Веб-клиент недоступен",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

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
    /// Ищет исполняемый файл платформы 1С нужной разрядности и типа клиента.
    /// <list type="bullet">
    /// <item>64-бит: %ProgramFiles%\1cv8\&lt;версия&gt;\bin\ — современные версии: 1cv8.exe / 1cv8c.exe; старые: 1cv8x64.exe</item>
    /// <item>32-бит: %ProgramFiles(x86)%\1cv8\&lt;версия&gt;\bin\ — 1cv8.exe / 1cv8c.exe</item>
    /// </list>
    /// Версия вида «8.3.25.1234 (64)» очищается от суффикса разрядности.
    /// </summary>
    private static string? FindExecutable(
        string version,
        OneCArchitecture architecture,
        OneCClientType? clientType = null,
        OneCLaunchMode mode = OneCLaunchMode.Enterprise)
    {
        // Очищаем версию от суффикса «(32)» / «(64)», если он попал в поле PlatformVersion.
        PlatformVersionService.ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
        if (string.IsNullOrWhiteSpace(cleanVersion))
            cleanVersion = string.Empty;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Корни поиска: 64-бит → Program Files, 32-бит → Program Files (x86).
        var searchRoots = new List<string>();
        if (architecture == OneCArchitecture.x64)
        {
            if (!string.IsNullOrEmpty(programFiles))
                searchRoots.Add(programFiles);
            // На 32-битной ОС Program Files (x86) может совпадать с Program Files — не дублируем.
            if (!string.IsNullOrEmpty(programFilesX86)
                && !string.Equals(programFiles, programFilesX86, StringComparison.OrdinalIgnoreCase)
                && architecture == OneCArchitecture.x64)
            {
                // 64-битные клиенты в x86-каталог не ставятся — не добавляем.
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(programFilesX86))
                searchRoots.Add(programFilesX86);
            // Одноразрядная система: x86-каталога нет.
            if (searchRoots.Count == 0 && !string.IsNullOrEmpty(programFiles))
                searchRoots.Add(programFiles);
        }

        // Имена exe в порядке приоритета.
        // Конфигуратор всегда требует 1cv8.exe (толстый).
        // Тонкий клиент — предпочтительно 1cv8c.exe, иначе 1cv8.exe с /RunModeManagedApplication.
        // 64-бит старых версий — 1cv8x64.exe.
        string[] exeNames;
        if (mode == OneCLaunchMode.Configurator)
        {
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8.exe" };
        }
        else if (clientType == OneCClientType.Thin)
        {
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8c.exe", "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8c.exe", "1cv8.exe" };
        }
        else
        {
            // Толстый клиент или авто: 1cv8.exe (современный 64) / 1cv8x64.exe (старый 64).
            exeNames = architecture == OneCArchitecture.x64
                ? new[] { "1cv8.exe", "1cv8x64.exe" }
                : new[] { "1cv8.exe" };
        }

        // 1. Конкретная версия в bin\.
        if (!string.IsNullOrWhiteSpace(cleanVersion))
        {
            foreach (var root in searchRoots)
            {
                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(root, "1cv8", cleanVersion, "bin", exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        // 2. Любая установленная версия нужной разрядности (новейшая по имени каталога).
        var found = new List<(string Path, string VersionDir)>();
        foreach (var root in searchRoots)
        {
            var baseDir = Path.Combine(root, "1cv8");
            if (!Directory.Exists(baseDir))
                continue;

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var verName = Path.GetFileName(dir);
                // Пропускаем common, conf и т.п.
                if (string.Equals(verName, "common", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(dir, "bin", exeName);
                    if (File.Exists(candidate))
                    {
                        found.Add((candidate, verName));
                        break; // один exe на каталог версии (первый по приоритету)
                    }
                }
            }
        }

        if (found.Count > 0)
        {
            // Берём «наибольшую» версию по строковому сравнению сегментов.
            return found
                .OrderByDescending(f => f.VersionDir, StringComparer.OrdinalIgnoreCase)
                .Select(f => f.Path)
                .First();
        }

        // 3. Общий лаунчер 1CEStart.exe (разрядность выбирает сам, но лучше, чем ничего).
        foreach (var root in new[] { programFiles, programFilesX86 }.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            var launcherPath = Path.Combine(root!, "1cv8", "common", "1CEStart.exe");
            if (File.Exists(launcherPath))
                return launcherPath;
        }

        return null;
    }

}