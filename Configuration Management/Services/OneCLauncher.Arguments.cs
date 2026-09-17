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
/// Сервис запуска платформы 1С:Предприятие.
/// </summary>
public static partial class OneCLauncher
{
    /// <summary>Аргумент подключения в стиле 1С: /F"path", /S"srv\db", /WS"url".</summary>
    public static string BuildConnectionArgument(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            // Значение заключается в кавычки по грамматике ключа 1С (/F"…"). Это НЕ строка
            // подключения: кавычку внутри значения экранировать удвоением нельзя — иначе 1С
            // получит неверный путь. Поэтому небезопасное значение (содержит «"») не подставляется,
            // иначе возможна инъекция дополнительного /ключа (см. IsSafeCliValue).
            ConnectionType.File => IsSafeCliValue(conn.FilePath)
                ? $"/F\"{conn.FilePath.Trim().TrimEnd('\\')}\""
                : "",
            ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl)
                ? $"/WS\"{conn.WebUrl}\""
                : "",
            _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                ? $"/S\"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                : ""
        };
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
        // Сервис не знает об UI: ошибки логируются через IAppLogger, а сообщение
        // пользователю показывает вызывающая ViewModel через IDialogService.
        var parsed = ParseLink(link);
        if (parsed is null)
        {
            GetLogger()?.Warn(LocalizationManager.T("Launcher.LinkParseFailed"));
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
                GetLogger()?.Error(
                    string.Format(LocalizationManager.T("Launcher.WebClientOpenFailedFormat"), ex.Message), ex);
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
            GetLogger()?.Warn(LocalizationManager.T("Launcher.PlatformExeNotFound"));
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
            GetLogger()?.Error(
                string.Format(LocalizationManager.T("Launcher.LaunchFailedFormat"), ex.Message), ex);
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
        //    Кавычка внутри значения экранируется удвоением, поэтому шаблон допускает «""»
        //    внутри и разворачивает его обратно (см. UnescapeConnectValue).
        var srvrMatch = System.Text.RegularExpressions.Regex.Match(
            value, @"Srvr\s*=\s*""(?<s>(?:[^""]|"""")*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (srvrMatch.Success)
        {
            var refMatch = System.Text.RegularExpressions.Regex.Match(
                value, @"Ref\s*=\s*""(?<r>(?:[^""]|"""")*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var server = UnescapeConnectValue(srvrMatch.Groups["s"].Value).Trim();
            var database = refMatch.Success ? UnescapeConnectValue(refMatch.Groups["r"].Value).Trim() : string.Empty;
            // Значения идут в /S"…" по грамматике ключа (не строки подключения): значение с «"»
            // недопустимо (см. IsSafeCliValue) — отказываемся от запуска вместо инъекции /ключа.
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) ||
                !IsSafeCliValue(server) || !IsSafeCliValue(database))
                return null;
            return new ParsedLink { Arguments = $" /S \"{server}\\{database}\"" };
        }

        // 3. Файловая база: File="..." или File=...
        //    Кавычка внутри пути экранируется удвоением (симметрично записи), поэтому шаблон
        //    допускает «""» внутри и разворачивает его обратно (см. UnescapeConnectValue).
        var fileMatch = System.Text.RegularExpressions.Regex.Match(
            value, @"File\s*=\s*""(?<f>(?:[^""]|"""")*)""|File\s*=\s*(?<f>[^;]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fileMatch.Success)
        {
            var path = UnescapeConnectValue(fileMatch.Groups["f"].Value).Trim();
            if (string.IsNullOrWhiteSpace(path) || !IsSafeCliValue(path))
                return null;
            return new ParsedLink { Arguments = $" /F \"{path}\"" };
        }

        // 4. Клиент-серверная: server\База (обратный слэш, но не существующий каталог)
        if (value.Contains('\\'))
        {
            // Если это существующий каталог — трактуем как файловую базу.
            if (Directory.Exists(value))
                return IsSafeCliValue(value)
                    ? new ParsedLink { Arguments = $" /F \"{value}\"" }
                    : null;

            var separator = value.IndexOf('\\');
            var server = value.Substring(0, separator).Trim();
            var database = value.Substring(separator + 1).Trim();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database) ||
                !IsSafeCliValue(server) || !IsSafeCliValue(database))
                return null;
            return new ParsedLink { Arguments = $" /S \"{server}\\{database}\"" };
        }

        // 5. Простой путь к каталогу файловой базы (существует на диске).
        if (Directory.Exists(value))
            return IsSafeCliValue(value)
                ? new ParsedLink { Arguments = $" /F \"{value}\"" }
                : null;

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
        string? templatePath = null,
        string? dbms = null,
        string? dbServer = null,
        string? dbName = null,
        string? dbUser = null,
        string? dbPassword = null,
        bool createSqlDatabase = false,
        bool blockScheduledJobs = false)
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

            // Параметры СУБД добавляются в строку подключения только если заданы.
            // Для клиент-серверного создания платформе нужны DBSrvr/DB/DBMS/DBUID/DBPwd,
            // иначе команда собирается неполной (issue #77).
            var csb = new System.Text.StringBuilder(
                $"Srvr=\"{EscapeConnectValue(srv)}\";Ref=\"{EscapeConnectValue(db)}\"");
            if (!string.IsNullOrWhiteSpace(dbms))
                csb.Append($";DBMS=\"{EscapeConnectValue(dbms)}\"");
            if (!string.IsNullOrWhiteSpace(dbServer))
                csb.Append($";DBSrvr=\"{EscapeConnectValue(dbServer)}\"");
            if (!string.IsNullOrWhiteSpace(dbName))
                csb.Append($";DB=\"{EscapeConnectValue(dbName)}\"");
            if (!string.IsNullOrWhiteSpace(dbUser))
                csb.Append($";DBUID=\"{EscapeConnectValue(dbUser)}\"");
            if (!string.IsNullOrWhiteSpace(dbPassword))
                csb.Append($";DBPwd=\"{EscapeConnectValue(dbPassword)}\"");
            // Создание базы данных на сервере СУБД задаётся параметром строки
            // подключения, а не ключом командной строки: с «/CreateDatabase»
            // платформа базу не создаёт и падает на попытке подключиться
            // к несуществующей. Проверено запуском на PostgreSQL и на MS SQL.
            if (createSqlDatabase)
                csb.Append(";CrSQLDB=\"Y\"");
            // SchJobDn действует только в CREATEINFOBASE: он задаёт состояние создаваемой
            // клиент-серверной базы и не должен попадать в обычную строку подключения.
            if (blockScheduledJobs)
                csb.Append(";SchJobDn=\"Y\"");
            connectionString = csb.ToString();
        }

        var arguments = $"CREATEINFOBASE {connectionString}";
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
            {
                CleanupCreatedDir(createdDirPath);
                return (false, LocalizationManager.T("Launcher.CreateProcessFailed"));
            }

            if (!process.WaitForExit(5 * 60 * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                CleanupCreatedDir(createdDirPath);
                return (false, LocalizationManager.T("Launcher.CreateTimeout"));
            }

            if (process.ExitCode != 0)
            {
                var err = "";
                try { err = process.StandardError.ReadToEnd(); } catch { /* ignore */ }
                CleanupCreatedDir(createdDirPath);
                var message = string.Format(
                    LocalizationManager.T("Launcher.CreateExitCodeFormat"),
                    process.ExitCode,
                    err,
                    exePath,
                    arguments);
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
                exePath,
                arguments);
            return (false, SensitiveDataMasker.MaskDbPassword(message));
        }
    }

}
