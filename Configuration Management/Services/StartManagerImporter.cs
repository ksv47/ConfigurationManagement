using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Результат импорта настроек из программы StartManager (issue #163).
/// </summary>
public class StartManagerImportResult
{
    /// <summary>Количество добавленных новых баз.</summary>
    public int Added { get; set; }

    /// <summary>Количество обновлённых существующих баз.</summary>
    public int Updated { get; set; }

    /// <summary>Количество пропущенных (отключённых в StartManager) баз.</summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Каталоги установки платформы 1С, определённые из settings.cnf (V8AppPath).
    /// Подходят для добавления в дополнительные пути поиска платформы приложения.
    /// </summary>
    public List<string> PlatformSearchPaths { get; } = new();

    /// <summary>Путь к каталогу, из которого выполнен импорт (для сообщений пользователю).</summary>
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>Признак того, что файл v8config.smc отсутствует (импорт ничего не сделал).</summary>
    public bool NoConfigFound { get; set; }
}

/// <summary>
/// Сервис импорта настроек из программы «StartManager» — альтернативного стартера 1С.
/// Читает два файла из каталога настроек StartManager (%APPDATA%\StartManager14\SMSettings):
/// <list type="bullet">
///   <item><c>settings.cnf</c> — общие настройки, включая путь к платформе 1С (V8AppPath);</item>
///   <item><c>v8config.smc</c> — список баз с путями подключения и авторизацией.</item>
/// </list>
/// Пароли в StartManager зашифрованы методом Виженера по ASCII-символам с ключом «SLAVKA» —
/// здесь реализована их расшифровка. Файлы имеют кодировку Windows-1251 (ANSI).
/// </summary>
/// <remarks>
/// Класс не зависит от UI и компилируется в обеих сборках (WPF и Avalonia).
/// </remarks>
public static class StartManagerImporter
{
    /// <summary>Ключ шифрования паролей StartManager (метод Виженера по ASCII).</summary>
    private const string VigenereKey = "SLAVKA";

    /// <summary>Имя файла списка баз StartManager.</summary>
    private const string ConfigFileName = "v8config.smc";

    /// <summary>Имя файла общих настроек StartManager.</summary>
    private const string SettingsFileName = "settings.cnf";

    // Ключи секции базы в v8config.smc (см. описание issue #163).
    private const string KEnable = "Enable";
    private const string KFile = "SPath";
    private const string KServer = "SRVS";
    private const string KDbName = "DBName";
    private const string KIbName = "IBName";
    private const string KName = "Name";
    private const string KUrl = "WS";
    private const string KUserStorage = "UserStorage";
    private const string KStorageDir = "StorageDir";
    private const string KStorageUser = "StorageUser";
    private const string KStoragePassword = "StoragePassword";
    private const string KUserLoginEnt = "UserLoginEnt";
    private const string KEntUser = "EntUser";
    private const string KEntPassword = "EntPassword";
    private const string KUserLoginCnf = "UserLoginCnf";
    private const string KCfgUser = "CfgUser";
    private const string KCfgPassword = "CfgPassword";
    private const string KFolder = "Folder";

    /// <summary>Ключ settings.cnf с путём к исполняемому файлу платформы 1С.</summary>
    private const string KV8AppPath = "V8AppPath";

    private static readonly Encoding Ansi = CreateAnsiEncoding();

    /// <summary>
    /// Возвращает кодировку ANSI (Windows-1251) для чтения файлов StartManager.
    /// Если кодовые страницы недоступны (Linux без пакета CodePages) — кодировка по умолчанию.
    /// </summary>
    private static Encoding CreateAnsiEncoding()
    {
        try
        {
            return Encoding.GetEncoding(1251);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    /// <summary>
    /// Возвращает стандартный каталог настроек StartManager:
    /// <c>%APPDATA%\StartManager14\SMSettings</c>. На Linux, где %APPDATA% нет,
    /// используется каталог пользователя с тем же именем (обычно копия с Windows).
    /// </summary>
    public static string? FindDefaultSettingsDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var win = Path.Combine(appData, "StartManager14", "SMSettings");
            if (Directory.Exists(win) || !OperatingSystem.IsWindows())
                return win;
        }

#if LINUX
        // На Linux ApplicationData возвращает ~/.config; также пробуем обычный домашний каталог.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "StartManager14", "SMSettings");
#endif

        return null;
    }

    /// <summary>
    /// Импортирует базы из StartManager в коллекции приложения: добавляет новые базы,
    /// обновляет существующие (по совпадению имени) и создаёт недостающие группы.
    /// Также читает settings.cnf и возвращает каталоги платформы 1С (V8AppPath).
    /// </summary>
    /// <param name="settingsDir">Каталог настроек StartManager (SMSettings).</param>
    /// <param name="infobases">Коллекция баз приложения, в которую выполняется импорт.</param>
    /// <param name="groups">Коллекция групп приложения, в которую добавляются недостающие группы.</param>
    /// <returns>Результат импорта.</returns>
    public static StartManagerImportResult Import(
        string settingsDir,
        IList<Infobase> infobases,
        IList<Group> groups)
    {
        var result = new StartManagerImportResult { SourceDirectory = settingsDir };

        var configPath = Path.Combine(settingsDir, ConfigFileName);
        if (!File.Exists(configPath))
        {
            result.NoConfigFound = true;
            return result;
        }

        var sections = ParseSmcFile(configPath);

        // Недостающие группы из импортируемых баз создаём до добавления баз.
        EnsureGroups(sections, groups, result);

        foreach (var section in sections)
        {
            // Секция отключена, если ключ Enable задан и не равен истине.
            var enabled = true;
            if (section.TryGetValue(KEnable, out var enableRaw))
                enabled = IsTrue(enableRaw);
            if (!enabled)
            {
                result.Skipped++;
                continue;
            }

            var infobase = ToInfobase(section);
            if (infobase is null)
            {
                // Секция без строки подключения — пропускаем (не является базой).
                result.Skipped++;
                continue;
            }

            var existing = FindExisting(infobases, infobase);

            if (existing is null)
            {
                infobases.Add(infobase);
                result.Added++;
            }
            else
            {
                // Режим слияния при повторном импорте (issue #163): дополняем/перезаписываем
                // настройки подключения, группу и авторизации существующей базы, а не только
                // добавляем новые. Логин/пароль существующей базы не затираем, если в
                // StartManager они не заданы. Так «удалённые вручную» авторизации (хранилище /
                // Предприятие / Конфигуратор) восстанавливаются из StartManager.
                Merge(existing, infobase);
                result.Updated++;

                // Лог слияния (issue #163): по журналу видно, какие авторизации были
                // восстановлены, а какие остались пустыми — ускоряет подтверждение на
                // машине пользователя без угадывания.
                LogInfo(
                    $"Слияние базы «{infobase.Name}» из StartManager: " +
                    $"группа=[{(string.IsNullOrWhiteSpace(existing.Group) ? "—" : existing.Group)}], " +
                    $"хранилище={(existing.Repository is { HasServer: true } ? "задано" : "—")}, " +
                    $"Предприятие={(existing.EnterpriseAuth is { IsDefault: false } ? "задано" : "—")}, " +
                    $"Конфигуратор={(existing.ConfiguratorAuth is { IsDefault: false } ? "задано" : "—")}");
            }
        }

        // Пути платформы 1С из settings.cnf (V8AppPath).
        CollectPlatformPaths(Path.Combine(settingsDir, SettingsFileName), result.PlatformSearchPaths);

        return result;
    }

    /// <summary>
    /// Переносит настройки импортированной базы в существующую, не затирая заполненные
    /// значения, но восстанавливая удалённые вручную авторизации (issue #163).
    /// <para>
    /// Ключевое правило: если авторизация в приложении пустая (пользователь удалил
    /// имя/пароль/путь к хранилищу), а в StartManager запись существует — она
    /// восстанавливается целиком или дополняется по незаполненным полям. Прежняя логика
    /// отбрасывала запись целиком по <c>HasServer</c>/<c>IsDefault</c>, из-за чего
    /// «пустые» в приложении, но заполненные в StartManager авторизации не возвращались.
    /// </para>
    /// </summary>
    private static void Merge(Infobase target, Infobase imported)
    {
        if (!string.IsNullOrWhiteSpace(imported.Id))
            target.Id = imported.Id;

        if (!string.IsNullOrWhiteSpace(imported.Group))
            target.Group = imported.Group;

        if (imported.Connection.Type != ConnectionType.ClientServer
            || !string.IsNullOrWhiteSpace(imported.Connection.Server)
            || !string.IsNullOrWhiteSpace(imported.Connection.FilePath)
            || !string.IsNullOrWhiteSpace(imported.Connection.WebUrl))
        {
            // Переносим строку подключения (кроме случая, когда она полностью пустая).
            var prevUser = target.Connection.User;
            var prevPassword = target.Connection.Password;
            target.Connection = imported.Connection;
            if (string.IsNullOrWhiteSpace(target.Connection.User) && !string.IsNullOrWhiteSpace(prevUser))
                target.Connection.User = prevUser;
            if (string.IsNullOrWhiteSpace(target.Connection.Password) && !string.IsNullOrWhiteSpace(prevPassword))
                target.Connection.Password = prevPassword;
        }

        MergeRepository(target, imported.Repository);
        target.EnterpriseAuth = MergeAuthSettings(target.EnterpriseAuth, imported.EnterpriseAuth);
        target.ConfiguratorAuth = MergeAuthSettings(target.ConfiguratorAuth, imported.ConfiguratorAuth);
    }

    /// <summary>
    /// Восстанавливает/дополняет настройки хранилища конфигурации. Если хранилище в
    /// приложении пустое, а в StartManager задано — берётся целиком; иначе заполняются
    /// только незаполненные поля.
    /// </summary>
    private static void MergeRepository(Infobase target, RepositorySettings? imported)
    {
        if (imported is null)
            return; // В StartManager хранилище не задано — ничего не трогаем.

        var dst = target.Repository;
        if (dst is null)
        {
            target.Repository = imported;
            return;
        }

        // Хранилище в приложении пустое (пользователь удалил его вручную) — восстанавливаем
        // из StartManager целиком, включая пустой пароль, чтобы не осталось «полуудалённых»
        // полей (issue #163).
        var dstEmpty = !dst.HasServer
                       && string.IsNullOrWhiteSpace(dst.RepositoryName)
                       && string.IsNullOrWhiteSpace(dst.User)
                       && string.IsNullOrWhiteSpace(dst.Password);
        if (dstEmpty)
        {
            target.Repository = imported;
            return;
        }

        // Иначе дополняем только те поля, которые в приложении остались пустыми.
        if (string.IsNullOrWhiteSpace(dst.Server) && !string.IsNullOrWhiteSpace(imported.Server))
            dst.Server = imported.Server;
        if (string.IsNullOrWhiteSpace(dst.RepositoryName) && !string.IsNullOrWhiteSpace(imported.RepositoryName))
            dst.RepositoryName = imported.RepositoryName;
        if (string.IsNullOrWhiteSpace(dst.User) && !string.IsNullOrWhiteSpace(imported.User))
            dst.User = imported.User;
        if (string.IsNullOrWhiteSpace(dst.Password) && !string.IsNullOrWhiteSpace(imported.Password))
            dst.Password = imported.Password;
    }

    /// <summary>
    /// Восстанавливает/дополняет авторизацию («1С:Предприятие» или «Конфигуратор»).
    /// Если авторизация в приложении по умолчанию (пустая), а в StartManager задана —
    /// берётся целиком; иначе заполняются только незаполненные поля и режим аутентификации.
    /// Возвращает итоговую авторизацию для записи в базу.
    /// </summary>
    private static InfobaseAuthSettings? MergeAuthSettings(
        InfobaseAuthSettings? target,
        InfobaseAuthSettings? imported)
    {
        if (imported is null)
        {
            // В StartManager авторизация не задана — сохраняем текущее состояние.
            return target;
        }

        if (target is null || target.IsDefault)
        {
            // Пользователь удалил авторизацию (стала «по умолчанию») — восстанавливаем
            // из StartManager целиком (issue #163).
            return imported;
        }

        // Частично заполнена и в приложении, и в StartManager — дополняем незаполненные поля.
        if (string.IsNullOrWhiteSpace(target.User) && !string.IsNullOrWhiteSpace(imported.User))
            target.User = imported.User;
        if (string.IsNullOrWhiteSpace(target.Password) && !string.IsNullOrWhiteSpace(imported.Password))
            target.Password = imported.Password;
        if (target.AuthenticationMode == AuthenticationMode.Prompt
            && imported.AuthenticationMode != AuthenticationMode.Prompt)
            target.AuthenticationMode = imported.AuthenticationMode;

        return target;
    }

    /// <summary>Пишет информационное сообщение импорта в файловый лог (issue #163).</summary>
    private static void LogInfo(string message)
    {
        try
        {
            AppServices.GetRequiredService<IAppLogger>().Info(message);
        }
        catch
        {
            // Логирование не должно ломать импорт.
        }
    }

    /// <summary>
    /// Находит существующую базу приложения для слияния с импортируемой записью
    /// (режим слияния при повторном импорте, issue #163). Поиск выполняется
    /// последовательно по нескольким критериям, чтобы не создавать дубликаты и
    /// корректно обновлять авторизации существующих баз:
    /// <list type="number">
    ///   <item>точное совпадение имени (как было ранее);</item>
    ///   <item>совпадение идентификатора StartManager / приложения (ID);</item>
    ///   <item>совпадение нормализованной строки подключения (путь / сервер+база / URL).</item>
    /// </list>
    /// Возвращает null, если подходящая база не найдена (тогда добавляется новая).
    /// </summary>
    private static Infobase? FindExisting(IList<Infobase> infobases, Infobase imported)
    {
        var name = (imported.Name ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var byName = infobases.FirstOrDefault(b =>
                string.Equals((b.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        var id = (imported.Id ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(id))
        {
            var byId = infobases.FirstOrDefault(b =>
                !string.IsNullOrWhiteSpace(b.Id)
                && string.Equals((b.Id ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        var conn = imported.Connection?.ToConnectionString();
        if (!string.IsNullOrWhiteSpace(conn))
        {
            var byConn = infobases.FirstOrDefault(b =>
                b.Connection is not null
                && !string.IsNullOrWhiteSpace(b.Connection.ToConnectionString())
                && string.Equals(b.Connection.ToConnectionString(), conn, StringComparison.OrdinalIgnoreCase));
            if (byConn is not null)
                return byConn;
        }

        return null;
    }

    /// <summary>
    /// Преобразует секцию v8config.smc в модель <see cref="Infobase"/>.
    /// Возвращает null, если в секции нет строки подключения (не база).
    /// </summary>
    private static Infobase? ToInfobase(Dictionary<string, string> section)
    {
        var connection = BuildConnection(section);
        if (connection is null)
            return null;

        var name = FirstValue(section, KName, KIbName)
                   ?? sectionSectionName(section);
        if (string.IsNullOrWhiteSpace(name))
            name = connection.DatabaseName;
        if (string.IsNullOrWhiteSpace(name))
            name = connection.FilePath;

        return new Infobase
        {
            Name = name.Trim(),
            Group = NormalizeGroupPath(Get(section, KFolder)),
            Connection = connection,
            Repository = BuildRepository(section) ?? new RepositorySettings(),
            EnterpriseAuth = BuildEnterpriseAuth(section),
            ConfiguratorAuth = BuildConfiguratorAuth(section),
            Description = string.Empty,
            Id = Get(section, "ID") ?? string.Empty
        };
    }

    /// <summary>
    /// Строит строку подключения из секции: файловая база (SPath), клиент-сервер
    /// (SRVS + DBName) или веб-публикация (WS). Возвращает null, если подключение не задано.
    /// </summary>
    private static ConnectionSettings? BuildConnection(Dictionary<string, string> section)
    {
        var file = Get(section, KFile);
        if (!string.IsNullOrWhiteSpace(file))
        {
            return new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = file.Trim()
            };
        }

        var url = FirstValue(section, KUrl, "URL", "WebUrl");
        if (!string.IsNullOrWhiteSpace(url))
        {
            return new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = url.Trim()
            };
        }

        var server = FirstValue(section, KServer, "Srvr");
        var db = FirstValue(section, KDbName, "Ref");
        if (!string.IsNullOrWhiteSpace(server) || !string.IsNullOrWhiteSpace(db))
        {
            var settings = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                DatabaseName = (db ?? string.Empty).Trim()
            };
            ConnectionSettings.ParseServerAndPort(server, settings);
            return settings;
        }

        return null;
    }

    /// <summary>
    /// Строит настройки хранилища конфигурации (UserStorage / StorageDir / ...).
    /// Возвращает null, если авторизация в хранилище не используется или не заполнена.
    /// </summary>
    private static RepositorySettings? BuildRepository(Dictionary<string, string> section)
    {
        if (!HasTrue(section, KUserStorage))
            return null;

        var dir = Get(section, KStorageDir);
        var user = Get(section, KStorageUser);
        var password = DecryptPassword(Get(section, KStoragePassword));

        if (string.IsNullOrWhiteSpace(dir) && string.IsNullOrWhiteSpace(user))
            return null;

        var repo = new RepositorySettings
        {
            Server = dir?.Trim() ?? string.Empty,
            User = user ?? string.Empty,
            Password = password
        };

        // StorageDir вида «tcp://server:1542\ИмяХранилища» — выделяем имя хранилища.
        var dirValue = dir?.Trim();
        if (!string.IsNullOrEmpty(dirValue))
        {
            var idx = dirValue.LastIndexOf('\\');
            if (idx >= 0 && idx < dirValue.Length - 1)
            {
                repo.RepositoryName = dirValue.Substring(idx + 1).Trim();
                repo.Server = dirValue.Substring(0, idx).Trim();
            }
        }

        return repo;
    }

    /// <summary>Строит авторизацию «1С:Предприятие» (UserLoginEnt / EntUser / EntPassword).</summary>
    private static InfobaseAuthSettings? BuildEnterpriseAuth(Dictionary<string, string> section)
    {
        if (!HasTrue(section, KUserLoginEnt))
            return null;

        var user = Get(section, KEntUser);
        var password = DecryptPassword(Get(section, KEntPassword));
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password))
            return null;

        return new InfobaseAuthSettings
        {
            AuthenticationMode = string.IsNullOrWhiteSpace(user)
                ? AuthenticationMode.Prompt
                : AuthenticationMode.Credentials,
            User = user ?? string.Empty,
            Password = password
        };
    }

    /// <summary>Строит авторизацию «Конфигуратора» (UserLoginCnf / CfgUser / CfgPassword).</summary>
    private static InfobaseAuthSettings? BuildConfiguratorAuth(Dictionary<string, string> section)
    {
        if (!HasTrue(section, KUserLoginCnf))
            return null;

        var user = Get(section, KCfgUser);
        var password = DecryptPassword(Get(section, KCfgPassword));
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password))
            return null;

        return new InfobaseAuthSettings
        {
            AuthenticationMode = string.IsNullOrWhiteSpace(user)
                ? AuthenticationMode.Prompt
                : AuthenticationMode.Credentials,
            User = user ?? string.Empty,
            Password = password
        };
    }

    /// <summary>
    /// Создаёт недостающие группы из путей Folder импортируемых секций,
    /// выстраивая иерархию (родительские группы до вложенных).
    /// </summary>
    private static void EnsureGroups(
        IEnumerable<Dictionary<string, string>> sections,
        IList<Group> groups,
        StartManagerImportResult result)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            var folder = Get(section, KFolder);
            if (string.IsNullOrWhiteSpace(folder))
                continue;
            var normalized = NormalizeGroupPath(folder);
            if (!string.IsNullOrWhiteSpace(normalized))
                paths.Add(normalized);
        }

        foreach (var path in paths)
            CreateGroupWithParents(path, groups, result);
    }

    /// <summary>Создаёт группу по полному пути (например «Учёт\Бухгалтерия»), при необходимости — родителей.</summary>
    private static void CreateGroupWithParents(string groupPath, IList<Group> groups, StartManagerImportResult result)
    {
        var segments = SplitGroupPath(groupPath);
        if (segments.Count == 0)
            return;

        string? parentId = null;
        var parts = new List<string>(segments.Count);

        foreach (var segment in segments)
        {
            parts.Add(segment);
            var pathSoFar = string.Join(GroupHierarchyHelper.PathSeparator, parts);

            var existing = GroupHierarchyHelper.FindByFullPath(pathSoFar, groups);
            if (existing is null)
            {
                existing = new Group
                {
                    Name = segment,
                    Id = Guid.NewGuid().ToString(),
                    ParentId = parentId ?? string.Empty
                };
                groups.Add(existing);
            }
            else if (string.IsNullOrWhiteSpace(existing.ParentId) && !string.IsNullOrEmpty(parentId))
            {
                existing.ParentId = parentId;
            }

            parentId = existing.Id;
        }
    }

    /// <summary>
    /// Извлекает каталоги платформы 1С из settings.cnf (ключ V8AppPath) и добавляет их
    /// в список <paramref name="target"/>, исключая повторы и несуществующие каталоги.
    /// </summary>
    private static void CollectPlatformPaths(string settingsPath, List<string> target)
    {
        if (!File.Exists(settingsPath))
            return;

        var values = ParseIniGlobalValues(settingsPath);
        if (!values.TryGetValue(KV8AppPath, out var exePath) || string.IsNullOrWhiteSpace(exePath))
            return;

        var exe = exePath.Trim();
        var candidates = new List<string>();

        // Каталог самого исполняемого файла (…\bin).
        var bin = Path.GetDirectoryName(exe);
        if (!string.IsNullOrWhiteSpace(bin))
        {
            candidates.Add(bin);
            // Родитель каталога bin — это каталог версии (если есть).
            var versionDir = Path.GetDirectoryName(bin);
            if (!string.IsNullOrWhiteSpace(versionDir))
            {
                candidates.Add(versionDir);
                // Над каталогом версии обычно лежит корень 1cv8.
                var root = Path.GetDirectoryName(versionDir);
                if (!string.IsNullOrWhiteSpace(root)
                    && string.Equals(Path.GetFileName(root), "1cv8", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(root);
                }
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var full = dir.Trim();
            if (seen.Add(full) && Directory.Exists(full))
                target.Add(full);
        }
    }

    // ---------------------------------------------------------------- разбор файлов

    /// <summary>Разбирает v8config.smc на список секций: [Имя] → словарь ключ/значение.</summary>
    private static List<Dictionary<string, string>> ParseSmcFile(string path)
    {
        var sections = new List<Dictionary<string, string>>();
        Dictionary<string, string>? current = null;

        foreach (var rawLine in ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Сохраняем имя секции под служебным ключом.
                current["__section__"] = line.Substring(1, line.Length - 2).Trim();
                sections.Add(current);
                continue;
            }

            if (current is null)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            current[key] = value;
        }

        return sections;
    }

    /// <summary>Разбирает простой INI-файл глобальных настроек (без секций) в словарь.</summary>
    private static Dictionary<string, string> ParseIniGlobalValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim().Trim('"');
            values[key] = value;
        }

        return values;
    }

    /// <summary>
    /// Читает все строки файла в кодировке ANSI (Windows-1251) с запасной кодировкой по умолчанию.
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        var text = File.ReadAllText(path, Ansi);
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    // ---------------------------------------------------------------- расшифровка пароля

    /// <summary>
    /// Расшифровывает пароль StartManager. Метод Виженера по ASCII-символам с ключом
    /// «SLAVKA»: каждый байт шифротекста смещается назад на код соответствующего символа
    /// ключа (по модулю 256). Пустые значения возвращаются без изменений.
    /// </summary>
    public static string DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;

        var data = Ansi.GetBytes(encrypted);
        var key = Encoding.ASCII.GetBytes(VigenereKey);
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var shift = key[i % key.Length];
            result[i] = (byte)((data[i] - shift) & 0xFF);
        }

        return Ansi.GetString(result);
    }

    /// <summary>
    /// Шифрует пароль методом Виженера с ключом «SLAVKA» (обратная операция к
    /// <see cref="DecryptPassword"/>). Используется для тестов и отладки.
    /// </summary>
    public static string EncryptPassword(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
            return string.Empty;

        var data = Ansi.GetBytes(plain);
        var key = Encoding.ASCII.GetBytes(VigenereKey);
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var shift = key[i % key.Length];
            result[i] = (byte)((data[i] + shift) & 0xFF);
        }

        return Ansi.GetString(result);
    }

    // ---------------------------------------------------------------- вспомогательное

    private static string? Get(Dictionary<string, string> section, string key)
        => section.TryGetValue(key, out var value) ? value : null;

    private static string? FirstValue(Dictionary<string, string> section, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (section.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string sectionSectionName(Dictionary<string, string> section)
        => Get(section, "__section__") ?? string.Empty;

    private static bool HasTrue(Dictionary<string, string> section, string key)
    {
        var value = Get(section, key);
        return !string.IsNullOrWhiteSpace(value) && IsTrue(value);
    }

    private static bool IsTrue(string value)
    {
        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(value.Trim(), out var b) && b
        };
    }

    /// <summary>Нормализует путь группы к внутреннему разделителю приложения « / ».</summary>
    private static string NormalizeGroupPath(string? group)
    {
        var segments = SplitGroupPath(group ?? string.Empty);
        return string.Join(GroupHierarchyHelper.PathSeparator, segments);
    }

    private static List<string> SplitGroupPath(string path)
    {
        return path
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}