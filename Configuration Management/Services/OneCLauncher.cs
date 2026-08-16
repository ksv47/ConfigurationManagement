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
    /// Определяет фактическую разрядность клиента по настройке базы и установленным версиям.
    /// Режимы (как в 1С): 32, 64, 32-priority (по умолчанию), 64-priority.
    /// </summary>
    private static OneCArchitecture GetArchitecture(Infobase infobase)
        => ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);

    /// <summary>
    /// Выбор разрядности по правилам 1С:Предприятие.
    /// Приоритет 32/64: если у «другой» разрядности более старшая версия — берётся она.
    /// </summary>
    public static OneCArchitecture ResolveArchitecture(string? architectureSetting, string? platformVersion)
    {
        var mode = (architectureSetting ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "64" or "x64" or "x86-64" or "x86_64")
            return OneCArchitecture.x64;
        if (mode is "32" or "x86")
            return OneCArchitecture.x86;

        // Приоритетные режимы: сравниваем лучшие доступные версии 32 и 64.
        var prefer64 = mode is "64-priority" or "priority64" or "x86-64-priority";
        PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out _);

        var v32 = FindBestVersionDir("32", cleanVersion);
        var v64 = FindBestVersionDir("64", cleanVersion);

        if (v32 is null && v64 is null)
            return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
        if (v32 is null)
            return OneCArchitecture.x64;
        if (v64 is null)
            return OneCArchitecture.x86;

        var cmp = CompareVersionDirs(v32, v64);
        // Более старшая версия побеждает; при равенстве — предпочитаемая разрядность.
        if (cmp > 0)
            return OneCArchitecture.x86; // 32 новее
        if (cmp < 0)
            return OneCArchitecture.x64; // 64 новее
        return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
    }

    /// <summary>Лучший каталог версии для указанной разрядности (или null).</summary>
    private static string? FindBestVersionDir(string archKey, string preferredVersion)
    {
        var roots = PlatformVersionService.GetSearchRoots(archKey).ToList();
        string? best = null;

        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "1cv8");
            if (!Directory.Exists(baseDir))
                continue;

            // Точное совпадение версии
            if (!string.IsNullOrWhiteSpace(preferredVersion))
            {
                var exact = Path.Combine(baseDir, preferredVersion, "bin");
                if (Directory.Exists(exact) && Directory.EnumerateFiles(exact, "1cv8*.exe").Any())
                    return preferredVersion;
            }

            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "common", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Directory.Exists(Path.Combine(dir, "bin")))
                    continue;
                if (!Directory.EnumerateFiles(Path.Combine(dir, "bin"), "1cv8*.exe").Any())
                    continue;

                if (best is null || CompareVersionDirs(name, best) > 0)
                    best = name;
            }
        }

        return best;
    }

    /// <summary>Сравнение номеров версий 1С (8.3.24.1000). &gt;0 если a новее b.</summary>
    private static int CompareVersionDirs(string a, string b)
    {
        static int[] Parts(string v)
        {
            var s = v.Split(new[] { '.', ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>();
            foreach (var p in s)
            {
                if (int.TryParse(p, out var n))
                    list.Add(n);
                else
                    break;
            }
            return list.ToArray();
        }

        var pa = Parts(a);
        var pb = Parts(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var va = i < pa.Length ? pa[i] : 0;
            var vb = i < pb.Length ? pb[i] : 0;
            if (va != vb)
                return va.CompareTo(vb);
        }
        return 0;
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
            // /S "server\base" — server может быть host:port при нестандартном порте.
            _ => $" /S \"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
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

        // Корни поиска: стандартные + дополнительные пути пользователя.
        var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";
        var searchRoots = PlatformVersionService.GetSearchRoots(archKey).ToList();

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

    /// <summary>Операции DESIGNER без интерактивного UI (выгрузка, тест).</summary>
    public enum DesignerBatchOperation
    {
        DumpIB,
        DumpCfg,
        TestAndRepair
    }

    /// <summary>
    /// Запускает конфигуратор в пакетном режиме: выгрузка .dt / .cf или тестирование ИБ.
    /// Формат аргументов как у командной строки 1С (без пробела между ключом и значением в кавычках).
    /// </summary>
    public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath = null)
    {
        var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
        var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(
                "Не найден 1cv8.exe для режима Конфигуратор.\n" +
                "Укажите версию платформы у базы или проверьте установку 1С.",
                "Платформа 1С",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        if (operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return false;
            // Каталог назначения должен существовать
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Не удалось создать каталог для файла:\n{dir}\n{ex.Message}",
                        "Выгрузка",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return false;
                }
            }
        }

        var connectionArg = BuildConnectionArgument(infobase);
        var authArg = BuildAuthArgument(infobase);

        // Важно: у 1С ключи вида /DumpIB"C:\path\file.dt" (значение сразу в кавычках).
        string opArg = operation switch
        {
            DesignerBatchOperation.DumpIB => $"/DumpIB\"{outputPath}\"",
            DesignerBatchOperation.DumpCfg => $"/DumpCfg\"{outputPath}\"",
            DesignerBatchOperation.TestAndRepair => "/IBCheckAndRepair -TestOnly",
            _ => ""
        };
        if (string.IsNullOrEmpty(opArg))
            return false;

        var outLog = Path.Combine(Path.GetTempPath(), $"1c_batch_{Guid.NewGuid():N}.log");
        var arguments = $"DESIGNER {connectionArg}{authArg} {opArg} /DisableStartupDialogs /DisableStartupMessages /Out\"{outLog}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось запустить операцию.\n{ex.Message}\n\nКоманда:\n{exePath}\n{arguments}",
                "Ошибка",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Аргумент подключения в стиле 1С: /F"path", /S"srv\db", /WS"url".</summary>
    public static string BuildConnectionArgument(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            ConnectionType.File => $"/F\"{conn.FilePath.Trim().TrimEnd('\\')}\"",
            ConnectionType.WebServer => $"/WS\"{conn.WebUrl}\"",
            _ => $"/S\"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
        };
    }

    /// <summary>Аргументы /N /P при режиме Credentials.</summary>
    public static string BuildAuthArgument(Infobase infobase)
    {
        var conn = infobase.Connection;
        if (conn.AuthenticationMode != AuthenticationMode.Credentials ||
            string.IsNullOrWhiteSpace(conn.User))
            return "";
        var auth = $" /N\"{conn.User}\"";
        if (!string.IsNullOrEmpty(conn.Password))
            auth += $" /P\"{conn.Password}\"";
        return auth;
    }

    /// <summary>
    /// Путь к 1cv8.exe (толстый клиент) для ярлыка / пакетных операций.
    /// Не возвращает 1CEStart.exe.
    /// </summary>
    public static string? ResolveThickClientExe(Infobase infobase)
    {
        var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
        var path = FindExecutable(infobase.PlatformVersion, arch, OneCClientType.Thick, OneCLaunchMode.Enterprise);
        if (!string.IsNullOrEmpty(path) &&
            !path.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
            return path;

        // Повтор только для конфигуратора (тот же 1cv8.exe)
        path = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
        if (!string.IsNullOrEmpty(path) &&
            !path.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
            return path;

        return null;
    }

    /// <summary>
    /// Аргументы командной строки для ярлыка «как у стандартного стартера 1С»:
    /// ENTERPRISE /F"..." или /S"..."
    /// </summary>
    public static string BuildEnterpriseShortcutArguments(Infobase infobase)
    {
        var args = $"ENTERPRISE {BuildConnectionArgument(infobase)}{BuildAuthArgument(infobase)}";
        if (!string.IsNullOrWhiteSpace(infobase.LaunchParameters))
            args += " " + infobase.LaunchParameters.Trim();
        return args;
    }

    /// <summary>
    /// Создаёт информационную базу командой CREATEINFOBASE (пустую или из шаблона .cf/.dt).
    /// </summary>
    public static (bool Ok, string? Error) CreateInfoBase(
        string platformVersion,
        bool isFile,
        string? filePath,
        string? server,
        string? databaseName,
        string? templatePath = null)
    {
        var exePath = FindExecutable(platformVersion, OneCArchitecture.x64, OneCClientType.Thick, OneCLaunchMode.Configurator);
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            exePath = FindExecutable(platformVersion, OneCArchitecture.x86, OneCClientType.Thick, OneCLaunchMode.Configurator);
        }
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Не найден 1cv8.exe для указанной версии платформы.");
        }

        string connectionString;
        if (isFile)
        {
            var path = (filePath ?? "").Trim().TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(path))
                return (false, "Не указан каталог файловой базы.");
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                return (false, $"Не удалось создать каталог:\n{path}\n{ex.Message}");
            }
            connectionString = $"File=\"{path}\"";
        }
        else
        {
            var srv = (server ?? "").Trim();
            var db = (databaseName ?? "").Trim();
            if (string.IsNullOrEmpty(srv) || string.IsNullOrEmpty(db))
                return (false, "Не указаны сервер или имя базы.");
            connectionString = $"Srvr=\"{srv}\";Ref=\"{db}\"";
        }

        var arguments = $"CREATEINFOBASE {connectionString}";
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            if (!File.Exists(templatePath))
                return (false, $"Файл шаблона не найден:\n{templatePath}");
            arguments += $" /UseTemplate\"{templatePath}\"";
        }
        arguments += " /DisableStartupDialogs /DisableStartupMessages";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Не удалось запустить процесс 1cv8.");

            if (!process.WaitForExit(5 * 60 * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (false, "Превышено время ожидания создания базы (5 мин).");
            }

            if (process.ExitCode != 0)
            {
                var err = "";
                try { err = process.StandardError.ReadToEnd(); } catch { /* ignore */ }
                return (false,
                    $"1cv8 завершился с кодом {process.ExitCode}.\n{err}\n\nКоманда:\n{exePath}\n{arguments}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"{ex.Message}\n\nКоманда:\n{exePath}\n{arguments}");
        }
    }

}