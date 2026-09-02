#if LINUX
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    // ========================================================================
    // Режимы/типы запуска (общие для обеих платформ; в Windows определены в
    // OneCLauncher.cs, в Linux — здесь, т.к. WPF-файл исключён из сборки).
    // ========================================================================

    /// <summary>Режим запуска платформы 1С.</summary>
    public enum OneCLaunchMode
    {
        /// <summary>Режим «1С:Предприятие» (клиент).</summary>
        Enterprise,
        /// <summary>Режим «Конфигуратор» (разработка).</summary>
        Configurator
    }

    /// <summary>Тип клиента 1С:Предприятие.</summary>
    public enum OneCClientType
    {
        /// <summary>Тонкий клиент (управляемое приложение).</summary>
        Thin,
        /// <summary>Толстый клиент (обычное приложение).</summary>
        Thick
    }

    /// <summary>Режим форм приложения 1С:Предприятие.</summary>
    public enum OneCRunMode
    {
        /// <summary>Управляемые формы (/RunModeManagedApplication).</summary>
        Managed,
        /// <summary>Обычные формы (/RunModeOrdinaryApplication).</summary>
        Ordinary
    }

    /// <summary>Разрядность исполняемого файла платформы 1С.</summary>
    public enum OneCArchitecture
    {
        /// <summary>32-битная версия.</summary>
        x86,
        /// <summary>64-битная версия.</summary>
        x64
    }

    /// <summary>
    /// Сервис запуска платформы 1С:Предприятие на Linux.
    /// Запуск — через /opt/1cv8/<вер>/bin/1cv8 (или 1cv8c) через Process.Start
    /// без UseShellExecute. Командная строка 1С совместима с Windows.
    /// </summary>
    public static class OneCLauncher
    {
        /// <summary>
        /// Режим глобальной «Разрядности по умолчанию» («Настройки → Платформы»):
        /// "X64" — всегда 64-бит, "X86" — всегда 32-бит, либо "Priority"
        /// («Использовать приоритет базы») — брать явную настройку разрядности базы.
        /// </summary>
        public static string DefaultArchitectureMode { get; set; } = "X64";

        private static readonly ConcurrentDictionary<string, Process> _activeBatchProcesses =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Возникает при запуске пакетной операции DESIGNER.</summary>
        public static event EventHandler<DesignerBatchInfo>? DesignerBatchStarted;

        /// <summary>Возникает при завершении пакетной операции DESIGNER.</summary>
        public static event EventHandler<DesignerBatchInfo>? DesignerBatchCompleted;

        // ====================================================================
        // Запуск базы
        // ====================================================================

        public static bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false)
        {
            if (mode == OneCLaunchMode.Configurator)
                return Launch(infobase, mode, OneCClientType.Thin, GetArchitecture(infobase), runAsAdmin);

            if (string.Equals(infobase.LaunchMode, "Веб-клиент", StringComparison.OrdinalIgnoreCase))
                return LaunchWebClient(infobase);

            if (string.Equals(infobase.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, null, GetArchitecture(infobase), runAsAdmin);

            if (string.Equals(infobase.LaunchMode, "Толстый клиент (обычные формы)", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Ordinary, GetArchitecture(infobase), runAsAdmin);

            if (string.Equals(infobase.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
                return Launch(infobase, mode, OneCClientType.Thick, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin);

            return Launch(infobase, mode, OneCClientType.Thin, OneCRunMode.Managed, GetArchitecture(infobase), runAsAdmin);
        }

        public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false)
            => Launch(infobase, mode, clientType, null, architecture, runAsAdmin);

        public static bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false)
        {
            // База, расположенная на веб-сервере, подключается только тонким клиентом (/WS).
            // 1cv8 (толстый клиент) не понимает /WS и при запуске открывает стандартное
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
                var logger = GetLogger();
                logger?.Warn(string.Format("{0} ({1}). {2}",
                    LocalizationManager.T("Launcher.PlatformNotFoundTitle"),
                    archLabel,
                    versionHint));
                return false;
            }

            // Запуск идёт через ProcessStartInfo.Arguments (строку), а не ArgumentList: .NET сам
            // разбивает строку по правилам CommandLineToArgvW, снимая внешние кавычки у /N"user"
            // и /F"path". Для 1С это и есть рабочий, проверенный формат. Перевод на ArgumentList
            // передавал бы кавычки платформе дословно — поведение 1С при этом не гарантировано,
            // поэтому осознанно не переводим (защита от инъекции реализована на уровне значений,
            // см. IsSafeCliValue).
            var arguments = BuildArguments(infobase, mode, clientType, runMode);

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
                infobase.LastLaunchDate = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                GetLogger()?.Error(string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message), ex);
                return false;
            }
        }

        /// <summary>Определяет режим форм по режиму запуска базы.</summary>
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

        private static OneCArchitecture GetArchitecture(Infobase infobase)
            => ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);

        /// <summary>
        /// Выбор разрядности по правилам 1С:Предприятие.
        /// Порядок приоритетов (issue #146, комментарий 7OH):
        /// 1. «Текущая сессия» — выбранное значение в группе «Текущая сессия»
        ///    (обрабатывается в вызывающем коде до этого метода).
        /// 2. Суффикс разрядности в выбранной версии платформы («8.3.27.1688 (64)»).
        /// 3. Глобальная настройка «Разрядность по умолчанию»: X64 / X86 либо
        ///    новый режим «Использовать приоритет базы» (Priority).
        /// 4. Если выбрано «Использовать приоритет базы» — явная настройка
        ///    разрядности базы (вкладка «Разрядность») и приоритетные режимы
        ///    32-priority / 64-priority по стилю 1С.
        /// </summary>
        public static OneCArchitecture ResolveArchitecture(string? architectureSetting, string? platformVersion)
        {
            // 2. Если в версии платформы явно указан суффикс разрядности («8.3.27.1688 (64)») —
            //    пользователь выбрал конкретную сборку. Это следующий по приоритету шаг
            //    после «текущей сессии» и перебивает глобальную настройку.
            PlatformVersionService.ParseVariant(platformVersion ?? string.Empty, out var cleanVersion, out var versionArch);
            if (!string.IsNullOrWhiteSpace(cleanVersion) && (versionArch == "32" || versionArch == "64"))
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
            var v32 = FindBestVersionDir("32", cleanVersion);
            var v64 = FindBestVersionDir("64", cleanVersion);

            if (v32 is null && v64 is null)
                return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
            if (v32 is null)
                return OneCArchitecture.x64;
            if (v64 is null)
                return OneCArchitecture.x86;

            var cmp = PlatformVersionService.CompareVersionStrings(v32, v64);
            if (cmp > 0)
                return OneCArchitecture.x86;
            if (cmp < 0)
                return OneCArchitecture.x64;
            return prefer64 ? OneCArchitecture.x64 : OneCArchitecture.x86;
        }

        private static string? FindBestVersionDir(string archKey, string preferredVersion)
        {
            var entries = PlatformVersionService.FindPlatformVersionDirs(archKey);
            string? best = null;
            foreach (var (version, _) in entries)
            {
                // Полная версия — только точные совпадения; частичная — по префиксу (issue #142).
                if (!string.IsNullOrWhiteSpace(preferredVersion) &&
                    !VersionMatches(preferredVersion, version))
                    continue;
                if (best is null || PlatformVersionService.CompareVersionStrings(version, best) > 0)
                    best = version;
            }
            return best;
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
        /// Проверяет, можно ли безопасно подставить значение внутрь кавычек ключа командной строки
        /// 1С вида /Key"value".
        /// ВАЖНО: грамматика таких ключей — НЕ грамматика строки подключения. Внутри значения кавычку
        /// экранировать удвоением («""») НЕЛЬЗЯ: для ключа командной строки это неверно, и 1С получит
        /// искажённое значение. Поэтому «"» внутри значения — единственный реальный вектор инъекции
        /// дополнительного /ключа 1cv8 (можно «вырваться» из кавычек). Пробелы внутри значения
        /// безопасны (остаются внутри кавычек и не создают новых аргументов). Также отклоняются
        /// управляющие символы (CR/LF/…), способные нарушить разбор командной строки.
        /// Если метод вернул false, корректно представить значение в этой грамматике невозможно —
        /// такой аргумент нужно отбросить/отказаться, а НЕ «экранировать».
        /// </summary>
        private static bool IsSafeCliValue(string? value)
            => !string.IsNullOrEmpty(value) &&
               value!.IndexOf('"') < 0 &&
               !value.Any(c => char.IsControl(c));

        /// <summary>
        /// Собирает /N"user" /P"password". Небезопасное значение (содержит «"» или управляющий символ)
        /// опускается, чтобы не допустить инъекции аргумента — см. <see cref="IsSafeCliValue"/>.
        /// </summary>
        private static string BuildCredentialsArg(string user, string password)
        {
            if (!IsSafeCliValue(user))
                return "";
            var auth = $" /N\"{user}\"";
            if (!string.IsNullOrEmpty(password) && IsSafeCliValue(password))
                auth += $" /P\"{password}\"";
            return auth;
        }

        /// <summary>Формирует аргументы командной строки для запуска 1С.</summary>
        private static string BuildArguments(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode)
        {
            var modeArg = mode switch
            {
                OneCLaunchMode.Enterprise => "ENTERPRISE",
                _ => "DESIGNER"
            };

            var clientArg = mode == OneCLaunchMode.Enterprise && (runMode.HasValue || clientType.HasValue)
                ? (runMode ?? (clientType == OneCClientType.Thin ? OneCRunMode.Managed : OneCRunMode.Ordinary)) switch
                {
                    OneCRunMode.Managed => " /RunModeManagedApplication",
                    _ => " /RunModeOrdinaryApplication"
                }
                : "";

            var conn = infobase.Connection;
            // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
            // кавычку внутри значения удвоением не экранируют — поэтому небезопасное значение
            // (с «"») не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
            string connectionArg = conn.Type switch
            {
                ConnectionType.File => IsSafeCliValue(conn.FilePath) ? $" /F \"{conn.FilePath}\"" : "",
                ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl) ? $" /WS \"{conn.WebUrl}\"" : "",
                _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                    ? $" /S \"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                    : ""
            };

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

            var extraArg = string.IsNullOrWhiteSpace(infobase.LaunchParameters)
                ? ""
                : " " + infobase.LaunchParameters.Trim();

            return $"{modeArg}{clientArg}{connectionArg}{authArg}{repositoryArg}{extraArg}";
        }

        private static bool LaunchWebClient(Infobase infobase)
        {
            var conn = infobase.Connection;
            string url;
            if (conn.Type == ConnectionType.WebServer)
            {
                if (string.IsNullOrWhiteSpace(conn.WebUrl))
                    return false;
                url = conn.WebUrl;
            }
            else if (conn.Type == ConnectionType.ClientServer)
            {
                url = $"http://{conn.Server}/{conn.DatabaseName}";
            }
            else
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false, ArgumentList = { url } });
                infobase.LastLaunchDate = DateTime.Now;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ====================================================================
        // Поиск исполняемого файла
        // ====================================================================

        /// <summary>Имена бинарников 1С на Linux (без .exe).</summary>
        private static string[] GetBinaryNames(OneCArchitecture architecture, OneCClientType? clientType, OneCLaunchMode mode)
        {
            if (mode == OneCLaunchMode.Configurator)
                return new[] { "1cv8" };
            if (clientType == OneCClientType.Thin)
                return new[] { "1cv8c", "1cv8" };
            return new[] { "1cv8" };
        }

        /// <summary>Ищет исполняемый файл 1cv8/1cv8c нужной версии и разрядности.</summary>
        private static string? FindExecutable(
            string version,
            OneCArchitecture architecture,
            OneCClientType? clientType = null,
            OneCLaunchMode mode = OneCLaunchMode.Enterprise)
        {
            PlatformVersionService.ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
            if (string.IsNullOrWhiteSpace(cleanVersion))
                cleanVersion = string.Empty;

            var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";
            var exeNames = GetBinaryNames(architecture, clientType, mode);

            // 1. Конкретная версия в bin\.
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

            // 2. Любая установленная версия нужной разрядности (новейшая).
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
                        break;
                    }
                }
                if (chosen is null)
                    continue;
                if (best is null || PlatformVersionService.CompareVersionStrings(verName, bestDir) > 0)
                {
                    best = chosen;
                    bestDir = verName;
                }
            }

            if (best != null)
                return best;

            // 3. Симлинк /usr/bin/1cv8 или ~/.1cv8/1cv8.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var dir in new[] { "/usr/bin", string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".1cv8") })
            {
                if (dir is null)
                    continue;
                foreach (var exeName in exeNames)
                {
                    var path = Path.Combine(dir, exeName);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        /// <summary>Путь к 1cv8 (толстый клиент) для ярлыка / пакетных операций.</summary>
        public static string? ResolveThickClientExe(Infobase infobase)
        {
            var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            return FindExecutable(infobase.PlatformVersion, arch, OneCClientType.Thick, OneCLaunchMode.Enterprise)
                ?? FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
        }

        // ====================================================================
        // Пакетные операции DESIGNER
        // ====================================================================

        /// <summary>Операции DESIGNER без интерактивного UI (выгрузка, тест).</summary>
        public enum DesignerBatchOperation
        {
            DumpIB,
            DumpCfg,
            TestAndRepair
        }

        /// <summary>Информация о запущенной пакетной операции DESIGNER.</summary>
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

            public DesignerBatchOperation Operation { get; }
            public string InfobaseName { get; }
            public string? OutputPath { get; }
            public string? LogPath { get; }
            public string? CommandLine { get; }
            public int ExitCode { get; set; } = -1;
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }

            public string OperationLabel => Operation switch
            {
                DesignerBatchOperation.DumpIB => LocalizationManager.T("Launcher.OperationDumpIB"),
                DesignerBatchOperation.DumpCfg => LocalizationManager.T("Launcher.OperationDumpCfg"),
                DesignerBatchOperation.TestAndRepair => LocalizationManager.T("Launcher.OperationTestAndRepair"),
                _ => LocalizationManager.T("Launcher.OperationGeneric")
            };
        }

        /// <summary>Запускает конфигуратор в пакетном режиме (выгрузка .dt/.cf или тест).</summary>
        public static bool RunDesignerBatch(Infobase infobase, DesignerBatchOperation operation, string? outputPath = null)
        {
            var arch = ResolveArchitecture(infobase.Architecture, infobase.PlatformVersion);
            var exePath = FindExecutable(infobase.PlatformVersion, arch, null, OneCLaunchMode.Configurator);
            if (string.IsNullOrEmpty(exePath))
            {
                var otherArch = arch == OneCArchitecture.x64 ? OneCArchitecture.x86 : OneCArchitecture.x64;
                exePath = FindExecutable(infobase.PlatformVersion, otherArch, null, OneCLaunchMode.Configurator);
            }
            if (string.IsNullOrEmpty(exePath))
                return false;

            if (IsDesignerBlocked(infobase, out _))
                return false;

            if (operation is DesignerBatchOperation.DumpIB or DesignerBatchOperation.DumpCfg)
            {
                if (string.IsNullOrWhiteSpace(outputPath))
                    return false;
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { return false; }
                }
            }

            var connectionArg = BuildConnectionArgument(infobase);
            var authArg = BuildAuthArgument(infobase);

            // Ключи вида /DumpIB"path" — по грамматике ключа, НЕ строки подключения: кавычку внутри
            // пути удвоением не экранируют, поэтому путь с «"» недопустим (см. IsSafeCliValue) —
            // безопасно выгрузить его невозможно, отказываемся.
            string opArg = operation switch
            {
                DesignerBatchOperation.DumpIB when IsSafeCliValue(outputPath) => $"/DumpIB\"{outputPath}\"",
                DesignerBatchOperation.DumpCfg when IsSafeCliValue(outputPath) => $"/DumpCfg\"{outputPath}\"",
                DesignerBatchOperation.TestAndRepair => "/IBCheckAndRepair -TestOnly",
                _ => ""
            };
            if (string.IsNullOrEmpty(opArg))
                return false;

            // /Out — путь к временному логу, всегда системный GUID-файл (без пользовательских
            // данных), поэтому экранирование не требуется (вектора инъекции нет).
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
                GetLogger()?.Error(string.Format(LocalizationManager.T("Launcher.OperationStartFailedFormat"), ex.Message, exePath, arguments), ex);
                return false;
            }
        }

        /// <summary>Токен подключения базы для сопоставления с командной строкой процесса.</summary>
        public static string GetBaseConnectionToken(Infobase infobase)
        {
            var conn = infobase.Connection;
            return conn.Type switch
            {
                ConnectionType.File => (conn.FilePath ?? string.Empty).Trim().TrimEnd('\\', '/'),
                ConnectionType.WebServer => (conn.WebUrl ?? string.Empty).Trim(),
                _ => $"{conn.GetServerWithPort()}\\{conn.DatabaseName}".Trim()
            };
        }

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
                    try { CompleteDesignerBatch(process, info); }
                    catch { }
                    DesignerBatchCompleted?.Invoke(null, info);
                };
            }
            catch
            {
                // процесс мог уже завершиться
            }
        }

        private static void CompleteDesignerBatch(Process process, DesignerBatchInfo info)
        {
            try { info.ExitCode = process.HasExited ? process.ExitCode : -1; }
            catch { info.ExitCode = -1; }

            var logText = ReadLogFile(info.LogPath);

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
            info.ErrorMessage = sb.ToString();
        }

        private static string ReadLogFile(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath))
                return string.Empty;
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
                            break;
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
                // занят
            }
            finally
            {
                try { File.Delete(logPath); } catch { }
            }
            return string.Empty;
        }

        private static string TruncateLogTail(string text, int maxChars)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length <= maxChars)
                return text;
            return "…" + text.Substring(text.Length - maxChars);
        }

        /// <summary>Проверяет блокировку запуска конфигуратора перед пакетной операцией.</summary>
        public static bool IsDesignerBlocked(Infobase infobase, out string? reason)
        {
            reason = null;
            PruneDeadBatchProcesses();

            if (_activeBatchProcesses.Count > 0)
            {
                var otherName = _activeBatchProcesses.First().Value?.ProcessName ?? "1cv8";
                reason = string.Format(LocalizationManager.T("Launcher.AnotherOperationRunningFormat"), otherName);
                return true;
            }

            var token = GetBaseConnectionToken(infobase);
            if (!string.IsNullOrWhiteSpace(token) && IsConfiguratorRunningForBase(token))
            {
                reason = LocalizationManager.T("Launcher.ConfiguratorForBaseRunning");
                return true;
            }

            return false;
        }

        private static void PruneDeadBatchProcesses()
        {
            foreach (var kvp in _activeBatchProcesses)
            {
                if (kvp.Value == null || kvp.Value.HasExited)
                    _activeBatchProcesses.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>Ищет запущенный конфигуратор (1cv8) для базы по командной строке из /proc.</summary>
        private static bool IsConfiguratorRunningForBase(string baseToken)
        {
            try
            {
                foreach (var process in LinuxProc.Enumerate1C())
                {
                    var name = process.Name;
                    var cmd = process.CmdLine;
                    var n = name ?? string.Empty;
                    if (!n.StartsWith("1cv8", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var c = cmd ?? string.Empty;
                    if (c.Contains("DESIGNER", StringComparison.OrdinalIgnoreCase) &&
                        c.Contains(baseToken, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // /proc недоступен
            }
            return false;
        }

        // ====================================================================
        // Аргументы подключения / авторизации
        // ====================================================================

        public static string BuildConnectionArgument(Infobase infobase)
        {
            var conn = infobase.Connection;
            // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
            // кавычку внутри значения удвоением не экранируют — небезопасное значение (с «"»)
            // не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
            return conn.Type switch
            {
                ConnectionType.File => IsSafeCliValue(conn.FilePath)
                    ? $"/F\"{conn.FilePath.Trim().TrimEnd('\\', '/')}\""
                    : "",
                ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl)
                    ? $"/WS\"{conn.WebUrl}\""
                    : "",
                _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                    ? $"/S\"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                    : ""
            };
        }

        public static string BuildAuthArgument(Infobase infobase)
        {
            if (infobase.ConfiguratorAuth is { } cfgAuth &&
                cfgAuth.AuthenticationMode == AuthenticationMode.Credentials &&
                !string.IsNullOrWhiteSpace(cfgAuth.User))
            {
                return BuildCredentialsArg(cfgAuth.User, cfgAuth.Password);
            }

            var conn = infobase.Connection;
            if (conn.AuthenticationMode != AuthenticationMode.Credentials ||
                string.IsNullOrWhiteSpace(conn.User))
                return "";
            return BuildCredentialsArg(conn.User, conn.Password);
        }

        /// <summary>Аргументы командной строки для ярлыка «как у стартера 1С».</summary>
        public static string BuildEnterpriseShortcutArguments(Infobase infobase)
        {
            var args = $"ENTERPRISE {BuildConnectionArgument(infobase)}{BuildAuthArgument(infobase)}";
            if (!string.IsNullOrWhiteSpace(infobase.LaunchParameters))
                args += " " + infobase.LaunchParameters.Trim();
            return args;
        }

        // ====================================================================
        // Запуск по ссылке / открытие URL
        // ====================================================================

        /// <summary>
        /// Запускает 1С по ссылке на информационную базу (аналог «Перейти по ссылке»).
        /// Поддерживаемые форматы:
        /// <list type="bullet">
        /// <item>Ссылка-протокол «e1c://…» — открывается системным обработчиком (xdg-open);</item>
        /// <item>Веб-клиент «http(s)://…» — в браузере по умолчанию (xdg-open);</item>
        /// <item>Файловая база «/path» или «File="/path"» — через платформу 1cv8;</item>
        /// <item>Клиент-сервер «server\База» или «Srvr="...";Ref="..."» — через 1cv8.</item>
        /// </list>
        /// </summary>
        /// <param name="link">Ссылка на информационную базу.</param>
        /// <returns>true, если запуск успешно инициирован.</returns>
        public static bool LaunchByLink(string link)
        {
            var value = (link ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Веб-ссылки и ссылки-протоколы обрабатывает системный обработчик (xdg-open).
            if (value.StartsWith("e1c:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return OpenUrl(value);
            }

            // Файловая / клиент-серверная база — запускаем через платформу 1С.
            var args = ParseLinkArguments(value);
            if (args is null)
                return false;

            var exe = FindExecutable(string.Empty, OneCArchitecture.x64, OneCClientType.Thick, OneCLaunchMode.Enterprise)
                      ?? FindExecutable(string.Empty, OneCArchitecture.x86, OneCClientType.Thick, OneCLaunchMode.Enterprise);
            if (string.IsNullOrEmpty(exe))
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"ENTERPRISE {args}",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Открывает URL в приложении по умолчанию (xdg-open).</summary>
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList = { url }
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Разбирает ссылку на файловую/клиент-серверную базу в аргументы командной
        /// строки 1С (/F /S). Возвращает null, если формат не распознан.
        /// </summary>
        private static string? ParseLinkArguments(string value)
        {
            // 1. Строка подключения: Srvr="...";Ref="..."
            //    Кавычка внутри значения экранируется удвоением, поэтому шаблон допускает «""»
            //    внутри и разворачивает его обратно (см. UnescapeConnectValue).
            var srvr = Regex.Match(value, @"Srvr\s*=\s*""(?<s>(?:[^""]|"""")*)""", RegexOptions.IgnoreCase);
            if (srvr.Success)
            {
                var re = Regex.Match(value, @"Ref\s*=\s*""(?<r>(?:[^""]|"""")*)""", RegexOptions.IgnoreCase);
                var server = UnescapeConnectValue(srvr.Groups["s"].Value).Trim();
                var db = re.Success ? UnescapeConnectValue(re.Groups["r"].Value).Trim() : string.Empty;
                // Значения идут в /S"…" по грамматике ключа (не строки подключения): значение с «"»
                // недопустимо (см. IsSafeCliValue) — отказываемся от запуска вместо инъекции /ключа.
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(db) ||
                    !IsSafeCliValue(server) || !IsSafeCliValue(db))
                    return null;
                return $"/S \"{server}\\{db}\"";
            }

            // 2. Файловая база: File="..." или File=...
            //    Кавычка внутри пути экранируется удвоением (симметрично записи), поэтому шаблон
            //    допускает «""» внутри и разворачивает его обратно (см. UnescapeConnectValue).
            var file = Regex.Match(value,
                @"File\s*=\s*""(?<f>(?:[^""]|"""")*)""|File\s*=\s*(?<f>[^;]+)", RegexOptions.IgnoreCase);
            if (file.Success)
            {
                var path = UnescapeConnectValue(file.Groups["f"].Value).Trim();
                if (string.IsNullOrWhiteSpace(path) || !IsSafeCliValue(path))
                    return null;
                return $"/F \"{path}\"";
            }

            // 3. Клиент-серверная: server\База (обратный слэш, но не существующий каталог).
            if (value.Contains('\\'))
            {
                if (Directory.Exists(value))
                    return IsSafeCliValue(value) ? $"/F \"{value}\"" : null;
                var sep = value.IndexOf('\\');
                var server = value.Substring(0, sep).Trim();
                var db = value.Substring(sep + 1).Trim();
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(db) ||
                    !IsSafeCliValue(server) || !IsSafeCliValue(db))
                    return null;
                return $"/S \"{server}\\{db}\"";
            }

            // 4. Простой путь к существующему каталогу файловой базы.
            if (Directory.Exists(value))
                return IsSafeCliValue(value) ? $"/F \"{value}\"" : null;

            return null;
        }

        // ====================================================================
        // Создание информационной базы
        // ====================================================================

        /// <summary>
        /// Экранирует значение для строки подключения 1С: кавычка внутри значения удваивается.
        /// <para>
        /// Тот же помощник есть в <c>OneCLauncher.Arguments.cs</c> для Windows. Под Linux csproj
        /// исключает из компиляции три части Windows-класса, включая её, поэтому переиспользовать
        /// метод оттуда нельзя и правило приходится повторить. Экранирование здесь так же
        /// необходимо, как в Windows, только причина другая: аргументы уходят через
        /// <c>ArgumentList</c>, то есть значение попадает к платформе дословно по построению,
        /// а не потому, что она сама разбирает командную строку.
        /// </para>
        /// </summary>
        private static string EscapeConnectValue(string value) => value.Replace("\"", "\"\"");

        /// <summary>
        /// Разворачивает экранирование строки подключения 1С: удвоенная кавычка «""» снова
        /// становится одной. Обратная операция к <see cref="EscapeConnectValue"/>.
        /// </summary>
        private static string UnescapeConnectValue(string value) => value.Replace("\"\"", "\"");

        /// <summary>
        /// Удаляет только что созданный пустой каталог файловой базы, если CREATEINFOBASE не удался.
        /// Затрагивает лишь каталог, созданный в этой попытке, и только если он остался пустым.
        /// </summary>
        private static void CleanupCreatedDir(string? dirPath)
        {
            if (string.IsNullOrEmpty(dirPath))
                return;
            try
            {
                if (Directory.Exists(dirPath) &&
                    !Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    Directory.Delete(dirPath);
                }
            }
            catch
            {
                /* Не критично: каталог мог быть занят или уже удалён. */
            }
        }

        public static (bool Ok, string? Error) CreateInfoBase(
            string platformVersion,
            bool isFile,
            string? filePath,
            string? server,
            string? databaseName,
            string? templatePath = null,
            string? dbms = null,
            string? dbServer = null,
            string? dbName = null,
            string? dbUser = null,
            string? dbPassword = null,
            bool createSqlDatabase = false,
            bool blockScheduledJobs = false)
        {
            PlatformVersionService.ParseVariant(platformVersion, out var version, out var arch);
            var exe = FindExecutable(version, arch == "64" ? OneCArchitecture.x64 : OneCArchitecture.x86,
                OneCClientType.Thick, OneCLaunchMode.Configurator);
            if (string.IsNullOrEmpty(exe))
                return (false, LocalizationManager.T("Launcher.CreateExeNotFoundLinux"));

            string connectionString;
            // Каталог, созданный только что под файловую базу. Запоминаем его, чтобы удалить
            // при неудачной попытке создания ИБ (issue #77): иначе пустой каталог остаётся на диске.
            string? createdDirPath = null;
            if (isFile)
            {
                var path = (filePath ?? "").Trim().TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(path))
                    return (false, LocalizationManager.T("Launcher.CreateFileDirNotSpecified"));
                try
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                        createdDirPath = path;
                    }
                }
                catch (Exception ex)
                {
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateDirCreateFailedFormat"), path, ex.Message));
                }
                connectionString = $"File=\"{EscapeConnectValue(path)}\"";
            }
            else
            {
                var srv = (server ?? "").Trim();
                var db = (databaseName ?? "").Trim();
                if (string.IsNullOrEmpty(srv) || string.IsNullOrEmpty(db))
                    return (false, LocalizationManager.T("Launcher.CreateServerOrDbNotSpecified"));

                // Параметры СУБД добавляются только если заданы (см. issue #77).
                var cs = $"Srvr=\"{EscapeConnectValue(srv)}\";Ref=\"{EscapeConnectValue(db)}\"";
                if (!string.IsNullOrWhiteSpace(dbms))
                    cs += $";DBMS=\"{EscapeConnectValue(dbms)}\"";
                if (!string.IsNullOrWhiteSpace(dbServer))
                    cs += $";DBSrvr=\"{EscapeConnectValue(dbServer)}\"";
                if (!string.IsNullOrWhiteSpace(dbName))
                    cs += $";DB=\"{EscapeConnectValue(dbName)}\"";
                if (!string.IsNullOrWhiteSpace(dbUser))
                    cs += $";DBUID=\"{EscapeConnectValue(dbUser)}\"";
                if (!string.IsNullOrWhiteSpace(dbPassword))
                    cs += $";DBPwd=\"{EscapeConnectValue(dbPassword)}\"";
                // Создание базы данных на сервере СУБД задаётся параметром строки
                // подключения, а не ключом командной строки: с «/CreateDatabase»
                // платформа базу не создаёт и падает на попытке подключиться
                // к несуществующей. Проверено запуском на PostgreSQL 8.3.27.
                if (createSqlDatabase)
                    cs += ";CrSQLDB=\"Y\"";
                // SchJobDn действует только в CREATEINFOBASE: он задаёт состояние создаваемой
                // клиент-серверной базы и не должен попадать в обычную строку подключения.
                if (blockScheduledJobs)
                    cs += ";SchJobDn=\"Y\"";
                connectionString = cs;
            }

            var args = new List<string> { "CREATEINFOBASE", connectionString };
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                if (!File.Exists(templatePath))
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateTemplateNotFoundFormat"), templatePath));
                }
                // /UseTemplate"…" — это ключ командной строки, а не строка подключения: кавычку внутри
                // пути здесь экранировать удвоением нельзя (см. IsSafeCliValue), поэтому при «"» в пути
                // отказываемся от создания, а не пытаемся «экранировать».
                if (!IsSafeCliValue(templatePath))
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, string.Format(LocalizationManager.T("Launcher.CreateTemplateInvalidPathFormat"), templatePath));
                }
                args.Add($"/UseTemplate\"{templatePath}\"");
            }
            args.Add("/DisableStartupDialogs");
            args.Add("/DisableStartupMessages");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                };
                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    CleanupCreatedDir(createdDirPath);
                    return (false, LocalizationManager.T("Launcher.CreateProcessFailed"));
                }

                if (!proc.WaitForExit(5 * 60 * 1000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    CleanupCreatedDir(createdDirPath);
                    return (false, LocalizationManager.T("Launcher.CreateTimeout"));
                }

                if (proc.ExitCode != 0)
                {
                    var err = "";
                    try { err = proc.StandardError.ReadToEnd(); } catch { }
                    CleanupCreatedDir(createdDirPath);
                    var message = string.Format(
                        LocalizationManager.T("Launcher.CreateExitCodeFormat"),
                        proc.ExitCode,
                        err,
                        exe,
                        string.Join(" ", args));
                    return (false, SensitiveDataMasker.MaskDbPassword(message));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                CleanupCreatedDir(createdDirPath);
                var message = string.Format(
                    LocalizationManager.T("Launcher.CreateCommandErrorFormat"),
                    ex.Message,
                    exe,
                    string.Join(" ", args));
                return (false, SensitiveDataMasker.MaskDbPassword(message));
            }
        }

        /// <summary>Логгер из DI (без создания жёсткой зависимости).</summary>
        private static IAppLogger? GetLogger()
        {
            try { return AppServices.GetRequiredService<IAppLogger>(); }
            catch { return null; }
        }
    }

    /// <summary>Реализация <see cref="IOneCLauncher"/> для Linux.</summary>
    public sealed class OneCLauncherService : IOneCLauncher
    {
        public bool Launch(Infobase infobase, OneCLaunchMode mode, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, runAsAdmin);

        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCArchitecture architecture, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, clientType, architecture, runAsAdmin);

        public bool Launch(Infobase infobase, OneCLaunchMode mode, OneCClientType? clientType, OneCRunMode? runMode, OneCArchitecture architecture, bool runAsAdmin = false)
            => OneCLauncher.Launch(infobase, mode, clientType, runMode, architecture, runAsAdmin);
    }
}
#endif
