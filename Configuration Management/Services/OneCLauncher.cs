using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using Configuration_Management.Localization;
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
/// Режим форм приложения 1С:Предприятие (независим от типа клиента,
/// как в стандартном списке баз 1С — «Толстый клиент (управляемое приложение)»
/// и «Толстый клиент (обычное приложение)»).
/// </summary>
public enum OneCRunMode
{
    /// <summary>Управляемые формы (/RunModeManagedApplication).</summary>
    Managed,

    /// <summary>Обычные формы (/RunModeOrdinaryApplication).</summary>
    Ordinary
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
    /// Глобальная разрядность по умолчанию, используемая при запуске, когда
    /// у информационной базы не указана собственная разрядность.
    /// Задаётся в «Настройки → Платформы».
    /// </summary>
    public static OneCArchitecture DefaultArchitecture { get; set; } = OneCArchitecture.x64;

    /// <summary>
    /// Активные пакетные операции DESIGNER (выгрузка .dt/.cf, тест), запущенные приложением.
    /// Ключ — токен подключения базы. Используется для блокировки параллельных выгрузок
    /// и обнаружения уже запущенного конфигуратора этой же базы.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Process> _activeBatchProcesses =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Возникает при запуске пакетной операции DESIGNER (выгрузка .dt/.cf или тест).
    /// Используется главным окном для показа анимированного индикатора выгрузки.
    /// </summary>
    public static event EventHandler<DesignerBatchInfo>? DesignerBatchStarted;

    /// <summary>
    /// Возникает при завершении пакетной операции DESIGNER (в т.ч. по ошибке).
    /// Используется главным окном для скрытия индикатора выгрузки.
    /// </summary>
    public static event EventHandler<DesignerBatchInfo>? DesignerBatchCompleted;

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы в заданном режиме.
    /// Тип клиента определяется из режима запуска базы (LaunchMode):
    /// «Автоматический», «Тонкий клиент», «Толстый клиент» или «Веб-клиент».
    /// Разрядность берётся из настройки базы (Architecture), версия — из PlatformVersion.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false)
    {
        // В режиме «Конфигуратор» тип клиента не применяется.
        if (mode == OneCLaunchMode.Configurator)
            return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase), runAsAdmin);

        // Веб-клиент запускается через браузер.
        if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
            return LaunchWebClient(infobase);

        // Автоматический режим — платформа сама выбирает клиент (без /RunMode).
        if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, null, GetArchitecture(infobase), runAsAdmin);

        // Толстый клиент в обычных формах.
        if (string.Equals(infobase.LaunchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Ordinary, GetArchitecture(infobase), runAsAdmin);

        // Толстый клиент (управляемые формы) — по умолчанию «Толстый клиент».
        if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin);

        // По умолчанию — тонкий клиент (управляемые формы).
        return Launch(infobase, mode, OneCClientType.Thin, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin);
    }

    /// <summary>
    /// Определяет режим форм по режиму запуска базы (LaunchMode).
    /// Только толстый клиент может работать и в управляемых, и в обычных формах:
    /// «Толстый клиент» → управляемые, «Толстый клиент (обычные формы)» → обычные,
    /// «Тонкий клиент» → управляемые; иначе — null (Авто).
    /// </summary>
    public static OneCRunMode? GetRunModeFromLaunchMode(string? launchMode)
    {
        if (string.Equals(launchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Ordinary;
        if (string.Equals(launchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Managed;
        if (string.Equals(launchMode, "Тонкий клиент", StringComparison.OrdinalIgnoreCase))
            return OneCRunMode.Managed;
        return null;
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

        // Если в версии платформы явно указан суффикс разрядности («8.3.27.1688 (64)») —
        // он имеет приоритет над настройкой разрядности: пользователь выбрал конкретную сборку.
        PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out var versionArch);
        if (!string.IsNullOrWhiteSpace(cleanVersion) && (versionArch == "32" || versionArch == "64"))
            return versionArch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86;

        if (mode is "64" or "x64" or "x86-64" or "x86_64")
            return OneCArchitecture.x64;
        if (mode is "32" or "x86")
            return OneCArchitecture.x86;

        // Разрядность в базе не указана — используем глобальную настройку
        // по умолчанию (Настройки → Платформы → «Разрядность по умолчанию»).
        if (string.IsNullOrWhiteSpace(mode))
            return DefaultArchitecture;

        // Приоритетные режимы: сравниваем лучшие доступные версии 32 и 64.
        var prefer64 = mode is "64-priority" or "priority64" or "x86-64-priority";
        if (string.IsNullOrWhiteSpace(cleanVersion))
            cleanVersion = string.Empty;

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

    /// <summary>
    /// Лучший каталог версии для указанной разрядности (или null).
    /// Использует гибкий поиск, покрывающий и нестандартные корни из дополнительных папок.
    /// </summary>
    private static string? FindBestVersionDir(string archKey, string preferredVersion)
    {
        var entries = PlatformVersionService.FindPlatformVersionDirs(archKey);
        string? best = null;

        foreach (var (version, _) in entries)
        {
            // Точное совпадение версии
            if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                string.Equals(version, preferredVersion, StringComparison.OrdinalIgnoreCase))
                return version;

            if (best is null || CompareVersionDirs(version, best) > 0)
                best = version;
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
    /// типом клиента и разрядностью. Режим форм выводится из типа клиента
    /// (тонкий → управляемые, толстый → обычные).
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="clientType">Тип клиента (тонкий или толстый). null — автоматический выбор платформой.</param>
    /// <param name="architecture">Разрядность (32 или 64 бита).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false)
        => Launch(infobase, mode, clientType, null, architecture, runAsAdmin);

    /// <summary>
    /// Запускает платформу 1С для указанной информационной базы с заданным
    /// типом клиента, режимом форм и разрядностью. Если <paramref name="runMode"/>
    /// задан, он имеет приоритет над режимом, выводимым из типа клиента,
    /// что позволяет запускать управляемые формы толстым клиентом и наоборот.
    /// </summary>
    /// <param name="infobase">Информационная база.</param>
    /// <param name="mode">Режим запуска (Предприятие или Конфигуратор).</param>
    /// <param name="clientType">Тип клиента (тонкий или толстый). null — автоматический выбор платформой.</param>
    /// <param name="runMode">Режим форм (управляемые/обычные). null — из типа клиента.</param>
    /// <param name="architecture">Разрядность (32 или 64 бита).</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false)
    {
        var exePath = FindExecutable(infobase.PlatformVersion, architecture, clientType, mode);
        if (string.IsNullOrEmpty(exePath))
        {
            var archLabel = architecture == OneCArchitecture.x64
                ? LocalizationManager.T("Launcher.Bit64")
                : LocalizationManager.T("Launcher.Bit32");
            var versionHint = string.IsNullOrWhiteSpace(infobase.PlatformVersion)
                ? LocalizationManager.T("Launcher.PlatformVersionHint")
                : string.Format(LocalizationManager.T("Launcher.RequestedVersionFormat"), infobase.PlatformVersion);
            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.T("Launcher.PlatformNotFoundFormat"), archLabel, versionHint),
                LocalizationManager.T("Launcher.PlatformNotFoundTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var arguments = BuildArguments(infobase, mode, clientType, runMode);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = runAsAdmin, // runas требует ShellExecute
                Verb = runAsAdmin ? "runas" : string.Empty
            };
            Process.Start(psi);

            // Обновляем дату последнего запуска базы.
            infobase.LastLaunchDate = DateTime.Now;

            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message),
                LocalizationManager.T("Launcher.LaunchErrorTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Формирует аргументы командной строки для запуска 1С.
    /// </summary>
    private static string BuildArguments(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode)
    {
        var modeArg = mode switch
        {
            OneCLaunchMode.Enterprise => "ENTERPRISE",
            _ => "DESIGNER"
        };

        // Параметр режима форм применяется только в режиме «Предприятие».
        // Явно заданный runMode имеет приоритет; иначе режим выводится из типа клиента
        // (тонкий → управляемые, толстый → обычные). Если задано и runMode, и clientType —
        // они независимы, что соответствует 1С («толстый клиент в управляемом приложении»).
        // null (автоматический выбор платформы) — параметр /RunMode не передаётся.
        var clientArg = mode == OneCLaunchMode.Enterprise && (runMode.HasValue || clientType.HasValue)
            ? (runMode ?? (clientType == OneCClientType.Thin ? OneCRunMode.Managed : OneCRunMode.Ordinary)) switch
            {
                OneCRunMode.Managed => " /RunModeManagedApplication",
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
        //
        // «1С:Предприятие» использует отдельную авторизацию (EnterpriseAuth), если она
        // задана; «Конфигуратор» — отдельную авторизацию (ConfiguratorAuth), если она
        // задана; иначе — авторизацию информационной базы (Connection, обратная совместимость).
        AuthenticationMode authMode;
        string authUser;
        string authPassword;
        if (mode == OneCLaunchMode.Enterprise && infobase.EnterpriseAuth is { } entAuth)
        {
            authMode = entAuth.AuthenticationMode;
            authUser = entAuth.User;
            authPassword = entAuth.Password;
        }
        else if (mode == OneCLaunchMode.Configurator && infobase.ConfiguratorAuth is { } cfgAuth)
        {
            authMode = cfgAuth.AuthenticationMode;
            authUser = cfgAuth.User;
            authPassword = cfgAuth.Password;
        }
        else
        {
            authMode = conn.AuthenticationMode;
            authUser = conn.User;
            authPassword = conn.Password;
        }

        string authArg = authMode switch
        {
            AuthenticationMode.Credentials when !string.IsNullOrWhiteSpace(authUser)
                => $" /N\"{authUser}\" /P\"{authPassword}\"",
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
                    LocalizationManager.T("Launcher.WebUrlNotSpecified"),
                    LocalizationManager.T("Launcher.WebClientUnavailableTitle"),
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
                LocalizationManager.T("Launcher.WebClientOnlyClientServer"),
                LocalizationManager.T("Launcher.WebClientUnavailableTitle"),
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
                string.Format(LocalizationManager.T("Launcher.WebClientOpenFailedFormat"), ex.Message),
                LocalizationManager.T("Launcher.LaunchErrorTitle"),
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

        var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";

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

        // 1. Конкретная версия в bin\. Используем гибкое разрешение каталога версии,
        //    покрывающее и нестандартные корни из дополнительных папок в настройках.
        if (!string.IsNullOrWhiteSpace(cleanVersion))
        {
            var versionBinDir = PlatformVersionService.ResolveVersionBinDirectory(cleanVersion, archKey);
            if (versionBinDir != null)
            {
                foreach (var exeName in exeNames)
                {
                    var candidate = Path.Combine(versionBinDir, exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        // 2. Любая установленная версия нужной разрядности (новейшая по имени каталога).
        //    Гибкий поиск учитывает стандартные и дополнительные корни.
        string? best = null;
        string bestDir = string.Empty;
        foreach (var (verName, binDir) in PlatformVersionService.FindPlatformVersionDirs(archKey))
        {
            string? chosen = null;
            foreach (var exeName in exeNames)
            {
                var candidate = Path.Combine(binDir, exeName);
                if (File.Exists(candidate))
                {
                    chosen = candidate;
                    break; // один exe на каталог версии (первый по приоритету)
                }
            }

            if (chosen is null)
                continue;

            // Берём «наибольшую» версию по числовому сравнению сегментов
            // (8.3.10 > 8.3.9 — строковое сравнение давало бы неверный результат).
            if (best is null || CompareVersionDirs(verName, bestDir) > 0)
            {
                best = chosen;
                bestDir = verName;
            }
        }

        if (best != null)
            return best;

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
    /// Информация о запущенной пакетной операции DESIGNER (выгрузка .dt/.cf или тест),
    /// передаваемая через события <see cref="OneCLauncher.DesignerBatchStarted"/> /
    /// <see cref="OneCLauncher.DesignerBatchCompleted"/>.
    /// </summary>
    public sealed class DesignerBatchInfo
    {
        public DesignerBatchInfo(DesignerBatchOperation operation, string infobaseName, string? outputPath,
            string? logPath = null, string? commandLine = null)
        {
            Operation = operation;
            InfobaseName = infobaseName;
            OutputPath = outputPath;
            LogPath = logPath;
            CommandLine = commandLine;
        }

        /// <summary>Тип выполняемой операции.</summary>
        public DesignerBatchOperation Operation { get; }

        /// <summary>Имя информационной базы, для которой выполняется операция.</summary>
        public string InfobaseName { get; }

        /// <summary>Путь к файлу выгрузки (.dt/.cf); может быть пустым для тестирования.</summary>
        public string? OutputPath { get; }

        /// <summary>Путь к временному файлу лога операции (/Out), заполняется по завершении.</summary>
        public string? LogPath { get; }

        /// <summary>Командная строка запуска 1cv8 (для диагностики).</summary>
        public string? CommandLine { get; }

        /// <summary>Код возврата процесса 1cv8 (заполняется по завершении).</summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>Успешно ли завершилась операция (код 0 и файл создан).</summary>
        public bool Success { get; set; }

        /// <summary>Сообщение об ошибке с текстом лога 1С (при неуспехе).</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Человекочитаемое название операции для индикатора и подсказки.</summary>
        public string OperationLabel => Operation switch
        {
            DesignerBatchOperation.DumpIB => LocalizationManager.T("Launcher.OperationDumpIB"),
            DesignerBatchOperation.DumpCfg => LocalizationManager.T("Launcher.OperationDumpCfg"),
            DesignerBatchOperation.TestAndRepair => LocalizationManager.T("Launcher.OperationTestAndRepair"),
            _ => LocalizationManager.T("Launcher.OperationGeneric")
        };
    }

    /// <summary>
    /// Запускает конфигуратор в пакетном режиме: выгрузка .dt / .cf или тестирование ИБ.
    /// Формат аргументов как у командной строки 1С (без пробела между ключом и значением в кавычках).
    /// </summary>
    public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath = null)
    {
        var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
        var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);

        // Платформа может быть установлена только в одной разрядности (например,
        // 32-бит в Program Files (x86) при глобальной настройке по умолчанию «64»).
        // Если для выбранной разрядности 1cv8.exe не найден — пробуем противоположную.
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
            var fallback = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            if (!string.IsNullOrEmpty(fallback) &&
                !fallback.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
                exePath = fallback;
        }

        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.T("Launcher.ConfiguratorExeNotFound"),
                LocalizationManager.T("Launcher.PlatformTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // Проверка блокировки запуска конфигуратора: уже запущен конфигуратор этой базы
        // (в т.ч. открытый вручную вне приложения) или идёт другая выгрузка/операция DESIGNER.
        if (IsDesignerBlocked(infobase, out var blockReason))
        {
            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.T("Launcher.ConfiguratorBlockedFormat"), blockReason),
                LocalizationManager.T("Launcher.ConfiguratorAlreadyRunningTitle"),
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
                        string.Format(LocalizationManager.T("Launcher.CreateDirFailedFormat"), dir, ex.Message),
                        LocalizationManager.T("Launcher.DumpTitle"),
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
            var process = Process.Start(psi);
            var info = new DesignerBatchInfo(operation, infobase.Name, outputPath, outLog, $"{exePath} {arguments}");
            RegisterBatchProcess(infobase, process, info);
            DesignerBatchStarted?.Invoke(null, info);
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.T("Launcher.OperationStartFailedFormat"), ex.Message, exePath, arguments),
                LocalizationManager.T("Launcher.OperationErrorTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Токен подключения базы для сопоставления с командной строкой процесса конфигуратора.</summary>
    public static string GetBaseConnectionToken(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            ConnectionType.File => (conn.FilePath ?? string.Empty).Trim().TrimEnd('\\'),
            ConnectionType.WebServer => (conn.WebUrl ?? string.Empty).Trim(),
            _ => $"{conn.GetServerWithPort()}\\{conn.DatabaseName}".Trim()
        };
    }

    /// <summary>Регистрирует запущенный процесс пакетной операции и удаляет его по завершении.</summary>
    private static void RegisterBatchProcess(Infobase infobase, Process? process, DesignerBatchInfo info)
    {
        var token = GetBaseConnectionToken(infobase);
        if (process is null || string.IsNullOrWhiteSpace(token))
            return;

        _activeBatchProcesses[token] = process;
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                _activeBatchProcesses.TryRemove(token, out _);
                // Читаем лог, определяем успех операции и формируем сообщение об ошибке.
                try { CompleteDesignerBatch(process, info); }
                catch { /* не должны ронять поток обработчика */ }
                // Оповещаем об окончании операции (индикатор выгрузки в главном окне).
                DesignerBatchCompleted?.Invoke(null, info);
            };
        }
        catch
        {
            /* процесс мог уже завершиться */
        }
    }

    /// <summary>
    /// По завершении процесса 1cv8 читает лог /Out, определяет успех операции
    /// (код возврата 0 и наличие файла выгрузки) и заполняет <paramref name="info"/>.
    /// При неуспехе формирует человекочитаемое сообщение с текстом лога 1С.
    /// </summary>
    private static void CompleteDesignerBatch(Process process, DesignerBatchInfo info)
    {
        try { info.ExitCode = process.HasExited ? process.ExitCode : -1; }
        catch { info.ExitCode = -1; }

        // Читаем лог операции (файл мог ещё дописываться — ждём стабилизации размера).
        var logText = ReadLogFile(info.LogPath);

        // Успех: код возврата 0 и (для выгрузки) создан и не пуст файл назначения.
        bool ok = info.ExitCode == 0;
        if (ok && info.Operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg)
        {
            ok = !string.IsNullOrWhiteSpace(info.OutputPath) &&
                 File.Exists(info.OutputPath) &&
                 new FileInfo(info.OutputPath).Length > 0;
        }

        info.Success = ok;
        if (ok)
            return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(LocalizationManager.T("Launcher.OperationFailedFormat"), info.OperationLabel));
        sb.AppendLine(string.Format(LocalizationManager.T("Launcher.ExitCodeFormat"), info.ExitCode));
        if (!string.IsNullOrWhiteSpace(info.OutputPath))
            sb.AppendLine(string.Format(LocalizationManager.T("Launcher.FileFormat"), info.OutputPath));
        if (!string.IsNullOrWhiteSpace(logText))
        {
            sb.AppendLine();
            sb.AppendLine(LocalizationManager.T("Launcher.MessageHeader1C"));
            sb.Append(TruncateLogTail(logText, 3000));
        }
        if (!string.IsNullOrWhiteSpace(info.CommandLine))
        {
            sb.AppendLine();
            sb.AppendLine(LocalizationManager.T("Launcher.CommandLineHeader"));
            sb.AppendLine(info.CommandLine);
        }
        info.ErrorMessage = sb.ToString();
    }

    /// <summary>Читает содержимое временного лога 1С и удаляет файл.</summary>
    private static string ReadLogFile(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
            return string.Empty;

        // Ждём, пока файл перестанет расти (1С дописывает лог даже после выхода процесса).
        for (var i = 0; i < 30; i++)
        {
            try
            {
                if (!File.Exists(logPath))
                    break;
                var f = new FileInfo(logPath);
                if (f.Length > 0)
                {
                    var len1 = f.Length;
                    Thread.Sleep(120);
                    var len2 = new FileInfo(logPath).Length;
                    if (len1 == len2)
                        break; // размер стабилен — можно читать
                }
            }
            catch
            {
                break;
            }
            Thread.Sleep(80);
        }

        try
        {
            if (File.Exists(logPath))
                return File.ReadAllText(logPath);
        }
        catch
        {
            /* занят другим процессом — пропускаем */
        }
        finally
        {
            try { File.Delete(logPath); } catch { /* ignore */ }
        }
        return string.Empty;
    }

    /// <summary>Возвращает хвост текста (последние <paramref name="maxChars"/> символов).</summary>
    private static string TruncateLogTail(string text, int maxChars)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length <= maxChars)
            return text;
        return "…" + text.Substring(text.Length - maxChars);
    }

    /// <summary>Удаляет завершившиеся процессы из реестра активных операций.</summary>
    private static void PruneDeadBatchProcesses()
    {
        foreach (var kvp in _activeBatchProcesses)
        {
            if (kvp.Value == null || kvp.Value.HasExited)
                _activeBatchProcesses.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Проверяет блокировку запуска конфигуратора перед выгрузкой .dt/.cf или тестированием.
    /// Возвращает true, если запуск нужно заблокировать; <paramref name="reason"/> описывает причину.
    /// </summary>
    public static bool IsDesignerBlocked(Infobase infobase, out string? reason)
    {
        reason = null;
        PruneDeadBatchProcesses();

        // 1. Уже идёт другая выгрузка / пакетная операция DESIGNER, запущенная приложением.
        if (_activeBatchProcesses.Count > 0)
        {
            var otherName = _activeBatchProcesses.First().Value?.ProcessName ?? "1cv8.exe";
            reason = string.Format(LocalizationManager.T("Launcher.AnotherOperationRunningFormat"), otherName);
            return true;
        }

        // 2. Конфигуратор этой базы уже запущен (в т.ч. открыт вручную вне приложения).
        var token = GetBaseConnectionToken(infobase);
        if (!string.IsNullOrWhiteSpace(token) && IsConfiguratorRunningForBase(token))
        {
            reason = LocalizationManager.T("Launcher.ConfiguratorForBaseRunning");
            return true;
        }

        return false;
    }

    /// <summary>Ищет запущенный процесс конфигуратора (1cv8.exe) для указанной базы по командной строке.</summary>
    private static bool IsConfiguratorRunningForBase(string baseToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process " +
                "WHERE Name='1cv8.exe' OR Name='1cv8x64.exe'");
            foreach (var obj in searcher.Get())
            {
                var cmd = obj["CommandLine"] as string ?? string.Empty;
                if (cmd.Contains("DESIGNER", StringComparison.OrdinalIgnoreCase) &&
                    cmd.Contains(baseToken, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Нет прав на чтение командной строки процессов других пользователей или WMI недоступен.
        }
        return false;
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
        // Для пакетных операций конфигуратора (выгрузка .dt/.cf) в приоритете
        // отдельная авторизация конфигуратора, если она задана.
        if (infobase.ConfiguratorAuth is { } cfgAuth &&
            cfgAuth.AuthenticationMode == AuthenticationMode.Credentials &&
            !string.IsNullOrWhiteSpace(cfgAuth.User))
        {
            var cAuth = $" /N\"{cfgAuth.User}\"";
            if (!string.IsNullOrEmpty(cfgAuth.Password))
                cAuth += $" /P\"{cfgAuth.Password}\"";
            return cAuth;
        }

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
    /// Запускает 1С по ссылке на информационную базу (аналог «Перейти по ссылке»
    /// в стандартном загрузчике 1С). Поддерживаются форматы:
    /// <list type="bullet">
    /// <item>Ссылка-протокол: «e1c://...» (передаётся стандартному загрузчику 1С — обработчику протокола)</item>
    /// <item>Файловая база: путь к каталогу базы, напр. «C:\1C\База» или «File="C:\1C\База"»</item>
    /// <item>Клиент-серверная база: «server\База», «server:1541\База» или «Srvr="server";Ref="База"»</item>
    /// <item>Веб-клиент: «http://server/base» или «https://server/base»</item>
    /// </list>
    /// </summary>
    /// <param name="link">Ссылка на информационную базу.</param>
    /// <returns>true, если запуск успешно инициирован.</returns>
    public static bool LaunchByLink(string link)
    {
        var parsed = ParseLink(link);
        if (parsed is null)
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.T("Launcher.LinkParseFailed"),
                LocalizationManager.T("Launcher.BaseLinkTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        // Веб-клиент открывается в браузере по умолчанию.
        if (parsed.IsWeb)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = parsed.WebUrl!,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    string.Format(LocalizationManager.T("Launcher.WebClientOpenFailedFormat"), ex.Message),
                    LocalizationManager.T("Launcher.LaunchErrorTitle"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        // Файловая / клиент-серверная база запускается через платформу 1С.
        var exePath = FindExecutable(string.Empty, OneCArchitecture.x64, OneCClientType.Thick, OneCLaunchMode.Enterprise);
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            exePath = FindExecutable(string.Empty, OneCArchitecture.x86, OneCClientType.Thick, OneCLaunchMode.Enterprise);
        }
        if (string.IsNullOrEmpty(exePath) ||
            exePath.EndsWith("1CEStart.exe", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(
                LocalizationManager.T("Launcher.PlatformExeNotFound"),
                LocalizationManager.T("Launcher.PlatformNotFoundTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var arguments = $"ENTERPRISE {parsed.Arguments}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message),
                LocalizationManager.T("Launcher.LaunchErrorTitle"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Результат разбора ссылки на информационную базу.
    /// </summary>
    private sealed class ParsedLink
    {
        public bool IsWeb;
        public string? WebUrl;
        public string Arguments = string.Empty;
    }

    /// <summary>
    /// Разбирает ссылку на информационную базу в аргументы командной строки 1С.
    /// Возвращает null, если формат не распознан.
    /// </summary>
    private static ParsedLink? ParseLink(string link)
    {
        var value = (link ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // 1. Ссылка-URI, обрабатываемая ОС (зарегистрированным обработчиком протокола):
        //    e1c://... — стандартный загрузчик 1С; http:// / https:// — веб-клиент в браузере.
        if (value.StartsWith("e1c:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedLink { IsWeb = true, WebUrl = value };
        }

        // 2. Строка подключения 1С: Srvr="...";Ref="..."
        var srvrMatch = System.Text.RegularExpressions.Regex.Match(
            value, @"Srvr\s*=\s*""(?<s>[^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (srvrMatch.Success)
        {
            var refMatch = System.Text.RegularExpressions.Regex.Match(
                value, @"Ref\s*=\s*""(?<r>[^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var server = srvrMatch.Groups["s"].Value.Trim();
            var database = refMatch.Success ? refMatch.Groups["r"].Value.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
                return null;
            return new ParsedLink { Arguments = $" /S \"{server}\\{database}\"" };
        }

        // 3. Файловая база: File="..." или File=...
        var fileMatch = System.Text.RegularExpressions.Regex.Match(
            value, @"File\s*=\s*""?(?<f>[^"";]*)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fileMatch.Success)
        {
            var path = fileMatch.Groups["f"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return new ParsedLink { Arguments = $" /F \"{path}\"" };
        }

        // 4. Клиент-серверная: server\База (обратный слэш, но не существующий каталог)
        if (value.Contains('\\'))
        {
            // Если это существующий каталог — трактуем как файловую базу.
            if (Directory.Exists(value))
                return new ParsedLink { Arguments = $" /F \"{value}\"" };

            var separator = value.IndexOf('\\');
            var server = value.Substring(0, separator).Trim();
            var database = value.Substring(separator + 1).Trim();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
                return null;
            return new ParsedLink { Arguments = $" /S \"{server}\\{database}\"" };
        }

        // 5. Простой путь к каталогу файловой базы (существует на диске).
        if (Directory.Exists(value))
            return new ParsedLink { Arguments = $" /F \"{value}\"" };

        return null;
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
            return (false, LocalizationManager.T("Launcher.CreateExeNotFound"));
        }

        string connectionString;
        if (isFile)
        {
            var path = (filePath ?? "").Trim().TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(path))
                return (false, LocalizationManager.T("Launcher.CreateFileDirNotSpecified"));
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                return (false, string.Format(LocalizationManager.T("Launcher.CreateDirCreateFailedFormat"), path, ex.Message));
            }
            connectionString = $"File=\"{path}\"";
        }
        else
        {
            var srv = (server ?? "").Trim();
            var db = (databaseName ?? "").Trim();
            if (string.IsNullOrEmpty(srv) || string.IsNullOrEmpty(db))
                return (false, LocalizationManager.T("Launcher.CreateServerOrDbNotSpecified"));
            connectionString = $"Srvr=\"{srv}\";Ref=\"{db}\"";
        }

        var arguments = $"CREATEINFOBASE {connectionString}";
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            if (!File.Exists(templatePath))
                return (false, string.Format(LocalizationManager.T("Launcher.CreateTemplateNotFoundFormat"), templatePath));
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
                return (false, LocalizationManager.T("Launcher.CreateProcessFailed"));

            if (!process.WaitForExit(5 * 60 * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (false, LocalizationManager.T("Launcher.CreateTimeout"));
            }

            if (process.ExitCode != 0)
            {
                var err = "";
                try { err = process.StandardError.ReadToEnd(); } catch { /* ignore */ }
                return (false,
                    string.Format(LocalizationManager.T("Launcher.CreateExitCodeFormat"), process.ExitCode, err, exePath, arguments));
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, string.Format(LocalizationManager.T("Launcher.CreateCommandErrorFormat"), ex.Message, exePath, arguments));
        }
    }

}