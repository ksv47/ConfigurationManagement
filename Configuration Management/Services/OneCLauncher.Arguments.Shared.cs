using System.IO;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Платформенно-нейтральное построение аргументов командной строки 1С.
/// Используется обеими реализациями <see cref="OneCLauncher"/> — Windows (WPF)
/// и Linux (Avalonia) — чтобы не дублировать идентичную логику сборки строки
/// запуска. Поведение и порядок ключей НЕ должны отличаться от прежних
/// платформенных версий.
/// </summary>
public static partial class OneCLauncher
{
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
        // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
        // кавычку внутри значения удвоением не экранируют — поэтому небезопасное значение
        // (с «"») не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
        // /S "server\base" — server может быть host:port при нестандартном порте.
        string connectionArg = conn.Type switch
        {
            ConnectionType.File => IsSafeCliValue(conn.FilePath) ? $" /F \"{conn.FilePath}\"" : "",
            ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl) ? $" /WS \"{conn.WebUrl}\"" : "",
            _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                ? $" /S \"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                : ""
        };

        // Учётные данные выбираются единым резолвингом (issue #236): раздельная авторизация
        // Конфигуратора/Предприятия (EnterpriseAuth/ConfiguratorAuth), иначе авторизация базы.
        InfobaseAuthResolver.Resolve(infobase, mode, out var authMode, out var authUser, out var authPassword);

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

    /// <summary>Аргументы /N /P при режиме Credentials.</summary>
    public static string BuildAuthArgument(Infobase infobase)
    {
        // Пакетные операции конфигуратора (выгрузка .dt/.cf) выполняются в режиме
        // «Конфигуратор»: единый резолвинг учётных данных (issue #236) сам возьмёт
        // ConfiguratorAuth, если она задана, иначе авторизацию информационной базы.
        InfobaseAuthResolver.Resolve(infobase, OneCLaunchMode.Configurator,
            out var authMode, out var authUser, out var authPassword);
        if (authMode != AuthenticationMode.Credentials || string.IsNullOrWhiteSpace(authUser))
            return "";
        return BuildCredentialsArg(authUser, authPassword);
    }

    /// <summary>Аргументы командной строки для ярлыка «как у стандартного стартера 1С».</summary>
    public static string BuildEnterpriseShortcutArguments(Infobase infobase)
    {
        var args = $"ENTERPRISE {BuildConnectionArgument(infobase)}{BuildAuthArgument(infobase)}";
        if (!string.IsNullOrWhiteSpace(infobase.LaunchParameters))
            args += " " + infobase.LaunchParameters.Trim();
        return args;
    }

    /// <summary>
    /// Экранирует значение для строки подключения 1С: кавычка внутри значения удваивается.
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
}