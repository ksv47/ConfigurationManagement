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
        string? templatePath = null,
        string? dbms = null,
        string? dbServer = null,
        string? dbName = null,
        string? dbUser = null,
        string? dbPassword = null,
        bool createSqlDatabase = false)
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
            arguments += $" /UseTemplate\"{templatePath}\"";
        }
        // Для клиент-серверного создания базу данных на сервере СУБД создаёт сам 1С.
        if (!isFile && createSqlDatabase)
            arguments += " /CreateDatabase";
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
                return (false,
                    string.Format(LocalizationManager.T("Launcher.CreateExitCodeFormat"), process.ExitCode, err, exePath, arguments));
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            CleanupCreatedDir(createdDirPath);
            return (false, string.Format(LocalizationManager.T("Launcher.CreateCommandErrorFormat"), ex.Message, exePath, arguments));
        }
    }

    /// <summary>
    /// Экранирует значение для строки подключения 1С: кавычка внутри значения удваивается.
    /// <para>
    /// Правило то же, что уже применяется в <c>OneCComConnector.AppendParameter</c>. Без него
    /// значение закрывает само себя и дописывает в строку произвольный параметр: имя базы вида
    /// <c>base";Usr="admin</c> уходит в CREATEINFOBASE как два параметра вместо одного.
    /// Платформа разбирает командную строку сама, а не через argv, поэтому кавычки доходят
    /// до неё в исходном виде.
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
}