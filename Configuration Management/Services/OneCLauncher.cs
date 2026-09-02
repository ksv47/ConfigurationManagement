using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
public static partial class OneCLauncher
{
    /// <summary>
    /// Режим глобальной «Разрядности по умолчанию» («Настройки → Платформы»):
    /// "X64" — всегда 64-бит, "X86" — всегда 32-бит, либо "Priority"
    /// («Использовать приоритет базы») — брать явную настройку разрядности
    /// информационной базы (вкладка «Разрядность»). По умолчанию — X64.
    /// </summary>
    public static string DefaultArchitectureMode { get; set; } = "X64";

    /// <summary>
    /// Разрядность для «текущей сессии» (блок «Текущая сессия» в окне списка баз).
    /// Наивысший (первый) шаг приоритета выбора разрядности в <see cref="ResolveArchitecture"/>:
    /// если здесь задан конкретный режим (не Auto), он побеждает и суффикс версии,
    /// и глобальную настройку, и настройку базы. Auto — выбираем по шагам 2–4.
    /// Значение передаётся из <c>MainViewModel</c> (изменяется вместе с выбором в UI).
    /// </summary>
    public static SessionArchitectureMode SessionArchitecture { get; set; } = SessionArchitectureMode.Auto;

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
    /// Порядок приоритетов (issue #146, комментарий 7OH):
    /// 1. «Текущая сессия» — выбранное значение в группе «Текущая сессия»
    ///    (свойство <see cref="SessionArchitecture"/>, передаётся из MainViewModel).
    /// 2. Суффикс разрядности в выбранной версии платформы («8.3.27.1688 (64)»).
    /// 3. Глобальная настройка «Разрядность по умолчанию»: X64 / X86 либо
    ///    новый режим «Использовать приоритет базы» (Priority).
    /// 4. Если выбрано «Использовать приоритет базы» — явная настройка
    ///    разрядности базы (вкладка «Разрядность») и приоритетные режимы
    ///    32-priority / 64-priority по стилю 1С.
    /// </summary>
    public static OneCArchitecture ResolveArchitecture(string? architectureSetting, string? platformVersion)
    {
        // 1. «Текущая сессия» — первый (наивысший) шаг приоритета (issue #146).
        //    Явный выбор разрядности в блоке «Текущая сессия» побеждает суффикс
        //    версии, глобальную настройку и настройку базы.
        if (SessionArchitecture == SessionArchitectureMode.X86)
            return OneCArchitecture.x86;
        if (SessionArchitecture == SessionArchitectureMode.X64)
            return OneCArchitecture.x64;

        // 2. Если в версии платформы явно указан суффикс разрядности («8.3.27.1688 (64)») —
        //    пользователь выбрал конкретную сборку. Это следующий по приоритету шаг
        //    после «текущей сессии» и перебивает глобальную настройку.
        PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out var versionArch);
        var hasSuffix = !string.IsNullOrWhiteSpace(platformVersion)
            && platformVersion.Contains("(") && platformVersion.Contains(")");
        if (hasSuffix && !string.IsNullOrWhiteSpace(cleanVersion) && (versionArch == "32" || versionArch == "64"))
            return versionArch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86;

        // 3. Глобальная настройка «Разрядность по умолчанию»
        //    (Настройки → Платформы → «Разрядность по умолчанию»).
        var defaultMode = string.IsNullOrWhiteSpace(DefaultArchitectureMode) ? "X64" : DefaultArchitectureMode.Trim();
        if (string.Equals(defaultMode, "X86", StringComparison.OrdinalIgnoreCase))
            return OneCArchitecture.x86;
        if (string.Equals(defaultMode, "X64", StringComparison.OrdinalIgnoreCase))
            return OneCArchitecture.x64;
        // defaultMode == "Priority" («Использовать приоритет базы») → шаг 4.

        // 4. Явная настройка разрядности базы (вкладка «Разрядность») и приоритетные режимы.
        var mode = (architectureSetting ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "64" or "x64" or "x86-64" or "x86_64")
            return OneCArchitecture.x64;
        if (mode is "32" or "x86")
            return OneCArchitecture.x86;

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
    /// Полная версия ограничивает поиск только ею, частичная (префикс) — новейшей
    /// установленной версией с таким префиксом (issue #142).
    /// </summary>
    private static string? FindBestVersionDir(string archKey, string preferredVersion)
    {
        var entries = PlatformVersionService.FindPlatformVersionDirs(archKey);
        string? best = null;
        string bestVersion = string.Empty;

        foreach (var (version, _) in entries)
        {
            // Полная версия — только точные совпадения; частичная — по префиксу (issue #142).
            if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                !VersionMatches(preferredVersion, version))
                continue;

            if (best is null || CompareVersionDirs(version, bestVersion) > 0)
            {
                best = version;
                bestVersion = version;
            }
        }

        return best;
    }

    /// <summary>Сравнение номеров версий 1С (8.3.24.1000). >0 если a новее b.</summary>
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
    /// Проверяет, соответствует ли фактическая версия запрошенной.
    /// Полная версия (4 сегмента) — точное совпадение; частичная («8.5», «8.3.27») —
    /// по числовому префиксу (issue #142).
    /// </summary>
    private static bool VersionMatches(string requested, string actual)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return true;

        var reqParts = requested.Split('.');
        // Полная версия — только точное совпадение (как раньше).
        if (reqParts.Length >= 4)
            return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase);

        // Частичная версия — префиксное сопоставление сегментов.
        var actParts = actual.Split('.');
        if (actParts.Length < reqParts.Length)
            return false;
        for (var i = 0; i < reqParts.Length; i++)
        {
            if (!string.Equals(actParts[i].Trim(), reqParts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
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
        // База, расположенная на веб-сервере, подключается только тонким клиентом (/WS).
        // Толстый клиент 1cv8.exe не понимает /WS и при запуске открывает стандартное
        // окно со списком информационных баз вместо подключения к базе.
        if (infobase.Connection.Type == ConnectionType.WebServer)
        {
            clientType = OneCClientType.Thin;
            runMode ??= OneCRunMode.Managed;
        }

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
            // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
            // кавычку внутри значения удвоением не экранируют — поэтому небезопасное значение
            // (с «"») не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
            ConnectionType.File => IsSafeCliValue(conn.FilePath) ? $" /F \"{conn.FilePath}\"" : "",
            ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl) ? $" /WS \"{conn.WebUrl}\"" : "",
            // /S "server\base" — server может быть host:port при нестандартном порте.
            _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                ? $" /S \"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                : ""
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
                => BuildCredentialsArg(authUser, authPassword),
            AuthenticationMode.Windows
                => " /WA+",
            _ => ""
        };

        // Подключение к хранилищу конфигурации (только в режиме «Конфигуратор»):
        // /ConfigurationRepositoryF "<путь>" — путь к хранилищу. Для серверного хранилища
        // путь имеет вид tcp://сервер:порт/имяХранилища (из Repository.Server + RepositoryName);
        // /ConfigurationRepositoryN — пользователь хранилища; /ConfigurationRepositoryP — пароль.
        // Аргументы добавляются, только если задан адрес сервера хранилища.
        string repositoryArg = "";
        var repo = infobase.Repository;
        if (mode == OneCLaunchMode.Configurator && repo.HasServer)
        {
            var server = repo.Server.Trim().TrimEnd('/');
            var name = (repo.RepositoryName ?? string.Empty).Trim();
            var repoPath = string.IsNullOrWhiteSpace(name) ? server : $"{server}/{name}";
            // Значения /ConfigurationRepository* тоже идут по грамматике ключа (не строки
            // подключения): небезопасное значение (с «"») не подставляется (см. IsSafeCliValue).
            if (IsSafeCliValue(repoPath))
                repositoryArg = $" /ConfigurationRepositoryF \"{repoPath}\"";
            if (IsSafeCliValue(repo.User))
            {
                repositoryArg += $" /ConfigurationRepositoryN \"{repo.User}\"";
                if (IsSafeCliValue(repo.Password))
                    repositoryArg += $" /ConfigurationRepositoryP \"{repo.Password}\"";
            }
        }

        // Дополнительные параметры запуска, заданные пользователем
        // (например, /UC, /DisableStartupMessages и др.).
        var extraArg = string.IsNullOrWhiteSpace(infobase.LaunchParameters)
            ? ""
            : " " + infobase.LaunchParameters.Trim();

        return $"{modeArg}{clientArg}{connectionArg}{authArg}{repositoryArg}{extraArg}";
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
    /// <item>64-бит: %ProgramFiles%\1cv8\<версия>\bin\ — современные версии: 1cv8.exe / 1cv8c.exe; старые: 1cv8x64.exe</item>
    /// <item>32-бит: %ProgramFiles(x86)%\1cv8\<версия>\bin\ — 1cv8.exe / 1cv8c.exe</item>
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
        //    Для частичной версии (префикс, issue #142) ResolveVersionBinDirectory
        //    возвращает каталог новейшей установленной версии с таким префиксом.
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

        // 2. Установленные версии нужной разрядности (новейшая по имени каталога).
        //    Гибкий поиск учитывает стандартные и дополнительные корни.
        //    Если запрошена конкретная версия, запасной поиск ограничивается ТОЛЬКО
        //    соответствующими ей вариантами (полная — точным совпадением, частичная —
        //    префиксом с выбором новейшей) и не выбирает произвольную новейшую —
        //    иначе запускалась бы совсем не та версия (issues #29, #142, #28).
        string? best = null;
        string bestDir = string.Empty;
        foreach (var (verName, binDir) in PlatformVersionService.FindPlatformVersionDirs(archKey))
        {
            if (!string.IsNullOrWhiteSpace(cleanVersion) &&
                !VersionMatches(cleanVersion, verName))
            {
                continue;
            }

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

        // 3. Общий лаунчер 1CEStart.exe (разрядность и версию выбирает сам). Применяется
        //    ТОЛЬКО при автоматическом выборе клиента (clientType == null): он открывает
        //    стартер со списком баз, а не подключается к конкретной базе. Для явного
        //    тонкого/толстого клиента такой откат запустил бы «обычное приложение»
        //    вместо запрошенного режима (issue #28), поэтому возвращаем null и
        //    показываем понятное предупреждение «платформа не найдена».
        if (clientType is null && mode == OneCLaunchMode.Enterprise)
        {
            foreach (var root in new[] { programFiles, programFilesX86 }.Where(r => !string.IsNullOrEmpty(r)).Distinct())
            {
                var launcherPath = Path.Combine(root!, "1cv8", "common", "1CEStart.exe");
                if (File.Exists(launcherPath))
                    return launcherPath;
            }
        }

        return null;
    }
}