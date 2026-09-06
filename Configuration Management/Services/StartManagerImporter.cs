using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
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

    /// <summary>
    /// Путь к списку баз 1С (ibases.v8i), из которого взяты строки подключения,
    /// или null, если файл не найден.
    /// </summary>
    public string? IbasesPath { get; set; }

    /// <summary>
    /// Признак того, что список баз 1С (ibases.v8i) не найден. Без него импорт
    /// невозможен: строки подключения StartManager не хранит.
    /// </summary>
    public bool NoIbasesFound { get; set; }

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

    /// <summary>Код символа «0»: постоянное слагаемое шифра паролей StartManager.</summary>
    private const int ZeroDigit = '0';

    /// <summary>Имя файла списка баз StartManager.</summary>
    private const string ConfigFileName = "v8config.smc";

    /// <summary>Имя файла общих настроек StartManager.</summary>
    private const string SettingsFileName = "settings.cnf";

    // Ключи секции базы в v8config.smc. Набор сверен с файлами StartManager 1.4
    // (28 ключей, одинаковых во всех секциях): ключей строки подключения среди них нет,
    // подключение хранится в ibases.v8i платформы (issue #163).
    private const string KConfigVersion = "ConfigVersion";
    private const string KNote = "Note";
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

    /// <summary>Ключ settings.cnf с путём к исполняемому файлу платформы 1С.</summary>
    private const string KV8AppPath = "V8AppPath";

    private static readonly Encoding Ansi = CreateAnsiEncoding();

    /// <summary>
    /// Возвращает кодировку ANSI (Windows-1251) для чтения файлов StartManager.
    /// Кодовые страницы в .NET доступны только после регистрации провайдера, иначе
    /// <see cref="Encoding.GetEncoding(int)"/> бросает исключение и кодировкой становится
    /// UTF-8: тогда пароль с кириллицей не расшифровать, потому что шифр StartManager
    /// работает по однобайтовым кодам символов.
    /// </summary>
    private static Encoding CreateAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
        var candidates = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            candidates.Add(Path.Combine(appData, "StartManager14", "SMSettings"));

        // На Linux ApplicationData возвращает ~/.config; каталог, перенесённый с Windows,
        // обычно лежит прямо в домашнем каталоге, поэтому пробуем и его.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            candidates.Add(Path.Combine(home, "StartManager14", "SMSettings"));

        // Существующий каталог важнее порядка: иначе на не-Windows возвращался бы
        // несуществующий путь из ApplicationData, а домашний каталог не проверялся вовсе.
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates.FirstOrDefault();
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

        // Строки подключения в файлах StartManager нет: имя секции v8config.smc — это
        // идентификатор базы из списка 1С, а подключение хранит сама платформа
        // в ibases.v8i. Поэтому список баз читаем оттуда, а из StartManager берём
        // только его надстройки к базе (issue #163).
        var ibasesPath = IbasesV8iImporter.FindDefaultPath();
        result.IbasesPath = ibasesPath;
        if (string.IsNullOrWhiteSpace(ibasesPath))
        {
            result.NoIbasesFound = true;
            LogInfo("Импорт из StartManager: список баз 1С (ibases.v8i) не найден, импортировать нечего.");
            return result;
        }

        var basesById = new Dictionary<string, Infobase>(StringComparer.OrdinalIgnoreCase);
        foreach (var fromList in IbasesV8iImporter.ReadInfobases(ibasesPath))
        {
            var listId = (fromList.Id ?? string.Empty).Trim();
            if (listId.Length > 0)
                basesById[listId] = fromList;
        }

        var imported = new List<Infobase>();
        foreach (var section in sections)
        {
            var sectionId = SectionName(section).Trim();
            if (sectionId.Length == 0 || !basesById.TryGetValue(sectionId, out var infobase))
            {
                // Секция без пары в списке баз: база удалена из 1С, а StartManager
                // помнит её настройки. Заводить такую базу заново не нужно.
                result.Skipped++;
                continue;
            }

            ApplyStartManagerSettings(infobase, section);
            imported.Add(infobase);
        }

        // Недостающие группы из импортируемых баз создаём до добавления баз.
        EnsureGroups(imported, groups, result);

        foreach (var infobase in imported)
        {
            var existing = FindExisting(infobases, infobase);

            if (existing is null)
            {
                infobases.Add(infobase);
                result.Added++;

                // Журнал добавления по строке на базу: по нему видно, что именно приехало
                // из StartManager, без открытия карточки каждой базы (issue #163).
                LogInfo(
                    $"Добавлена база «{infobase.Name}» из StartManager: " +
                    $"подключение=[{infobase.Connection.ToConnectionString()}], " +
                    $"группа=[{(string.IsNullOrWhiteSpace(infobase.Group) ? "—" : infobase.Group)}], " +
                    $"хранилище={(infobase.Repository is { HasServer: true } ? "задано" : "—")}, " +
                    $"Предприятие={(infobase.EnterpriseAuth is { IsDefault: false } ? "задано" : "—")}, " +
                    $"Конфигуратор={(infobase.ConfiguratorAuth is { IsDefault: false } ? "задано" : "—")}");
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

        // Пути платформы 1С из settings.cnf.
        CollectPlatformPaths(Path.Combine(settingsDir, SettingsFileName), result.PlatformSearchPaths);

        LogInfo(
            $"Импорт из StartManager завершён: добавлено {result.Added}, обновлено {result.Updated}, " +
            $"пропущено секций без базы в списке 1С {result.Skipped}. " +
            $"Список баз: {ibasesPath}.");

        return result;
    }

    /// <summary>
    /// Переносит настройки импортированной базы в существующую. Источник (StartManager)
    /// считается авторитетным: заполненные значения переносятся/восстанавливаются, а пустые
    /// в источнике — сбрасывают соответствующие поля у существующей базы (issue #163).
    /// Это позволяет при повторном импорте синхронизировать базу с текущим состоянием
    /// StartManager, в т.ч. отразить удаление имени, паролей или пути к хранилищу.
    /// </summary>
    private static void Merge(Infobase target, Infobase imported)
    {
        if (!string.IsNullOrWhiteSpace(imported.Id))
            target.Id = imported.Id;

        // Имя из источника переносим целиком: если в StartManager оно очищено (стало
        // «по умолчанию» от строки подключения), обновляем его и у существующей базы.
        var importedName = (imported.Name ?? string.Empty).Trim();
        if (!string.Equals(target.Name, importedName, StringComparison.Ordinal))
        {
            var oldName = target.Name;
            target.Name = importedName;
            if (string.IsNullOrWhiteSpace(importedName))
                LogInfo($"Очищено имя базы «{oldName}»: в StartManager имя не задано.");
        }

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
        target.EnterpriseAuth = MergeAuthSettings(
            target.EnterpriseAuth, imported.EnterpriseAuth, "Предприятие", target.Name);
        target.ConfiguratorAuth = MergeAuthSettings(
            target.ConfiguratorAuth, imported.ConfiguratorAuth, "Конфигуратор", target.Name);
    }

    /// <summary>
    /// Синхронизирует настройки хранилища конфигурации с источником (StartManager).
    /// Если в источнике хранилище очищено/отключено — сбрасывает его у существующей базы
    /// (issue #163); иначе восстанавливает из StartManager (если в приложении пустое) либо
    /// приводит каждое поле к значению источника.
    /// </summary>
    private static void MergeRepository(Infobase target, RepositorySettings? imported)
    {
        // Пустой источник (хранилище в StartManager очищено или не используется) —
        // сбрасываем хранилище у существующей базы (issue #163).
        var importedEmpty = imported is null
            || (!imported.HasServer
                && string.IsNullOrWhiteSpace(imported.RepositoryName)
                && string.IsNullOrWhiteSpace(imported.User)
                && string.IsNullOrWhiteSpace(imported.Password));
        if (importedEmpty)
        {
            if (target.Repository is not null)
            {
                LogInfo($"Очистка хранилища базы «{target.Name}»: в StartManager хранилище не задано.");
                // Свойство не допускает null и само сводит его к пустому объекту — задаём
                // пустое хранилище явно (это и есть «хранилища нет»).
                target.Repository = new RepositorySettings();
            }
            return;
        }

        var dst = target.Repository;
        if (dst is null)
        {
            target.Repository = imported!;
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
            target.Repository = imported!;
            return;
        }

        // Источник авторитетен по каждому полю: пустые в StartManager очищаем, заполненные — переносим.
        dst.Server = imported!.Server ?? string.Empty;
        dst.RepositoryName = imported.RepositoryName ?? string.Empty;
        dst.User = imported.User ?? string.Empty;
        dst.Password = imported.Password ?? string.Empty;
    }

    /// <summary>
    /// Синхронизирует авторизацию («1С:Предприятие» или «Конфигуратор») с источником.
    /// Если в StartManager авторизация не задана — сбрасывает её у существующей базы
    /// (issue #163); иначе восстанавливает (если в приложении удалена/пустая) либо приводит
    /// поля к значениям источника. Возвращает итоговую авторизацию для записи в базу.
    /// </summary>
    private static InfobaseAuthSettings? MergeAuthSettings(
        InfobaseAuthSettings? target,
        InfobaseAuthSettings? imported,
        string kind,
        string baseName)
    {
        // Пустой источник — авторизация в StartManager очищена/отключена. Сбрасываем её
        // у существующей базы, чтобы удаление учётки в StartManager отражалось при импорте
        // (issue #163).
        var importedEmpty = imported is null || imported.IsDefault;
        if (importedEmpty)
        {
            if (target is not null && !target.IsDefault)
            {
                LogInfo($"Очистка авторизации «{kind}» базы «{baseName}»: в StartManager она не задана.");
                return null;
            }
            return target;
        }

        if (target is null || target.IsDefault)
        {
            // Пользователь удалил авторизацию (стала «по умолчанию») — восстанавливаем
            // из StartManager целиком (issue #163).
            return imported;
        }

        // Источник авторитетен по полям: пустые в StartManager очищаем, заполненные — переносим.
        target.User = imported!.User ?? string.Empty;
        target.Password = imported.Password ?? string.Empty;
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
    /// Накладывает надстройки StartManager на базу, прочитанную из списка 1С:
    /// авторизации «Предприятия» и «Конфигуратора», хранилище конфигурации,
    /// версию конфигурации и заметку. Подключение, имя и группа приходят
    /// из ibases.v8i и здесь не меняются.
    /// </summary>
    private static void ApplyStartManagerSettings(Infobase infobase, Dictionary<string, string> section)
    {
        infobase.Repository = BuildRepository(section) ?? new RepositorySettings();
        infobase.EnterpriseAuth = BuildEnterpriseAuth(section);
        infobase.ConfiguratorAuth = BuildConfiguratorAuth(section);

        var configVersion = Get(section, KConfigVersion);
        if (!string.IsNullOrWhiteSpace(configVersion))
            infobase.ConfigurationVersion = configVersion.Trim();

        var note = Get(section, KNote);
        if (!string.IsNullOrWhiteSpace(note))
            infobase.Description = note.Trim();
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
        IEnumerable<Infobase> importedBases,
        IList<Group> groups,
        StartManagerImportResult result)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var infobase in importedBases)
        {
            var normalized = NormalizeGroupPath(infobase.Group);
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
    /// Извлекает каталоги платформы 1С из settings.cnf и добавляет их в список
    /// <paramref name="target"/>, исключая повторы и несуществующие каталоги.
    /// </summary>
    private static void CollectPlatformPaths(string settingsPath, List<string> target)
    {
        if (!File.Exists(settingsPath))
            return;

        var candidates = new List<string>();
        foreach (var exe in ReadPlatformExecutables(settingsPath))
        {
            // Каталог самого исполняемого файла (…\bin).
            var bin = Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(bin))
                continue;

            candidates.Add(bin);
            // Родитель каталога bin — это каталог версии (если есть).
            var versionDir = Path.GetDirectoryName(bin);
            if (string.IsNullOrWhiteSpace(versionDir))
                continue;

            candidates.Add(versionDir);
            // Над каталогом версии обычно лежит корень 1cv8.
            var root = Path.GetDirectoryName(versionDir);
            if (!string.IsNullOrWhiteSpace(root)
                && string.Equals(Path.GetFileName(root), "1cv8", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(root);
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

    /// <summary>
    /// Возвращает пути к исполняемым файлам платформы 1С, заданные в settings.cnf.
    /// StartManager 1.4 хранит этот файл в XML: пути лежат в разделе «Launch»,
    /// в элементах V81AppFile…V84AppFile. Разбор INI с ключом V8AppPath оставлен
    /// запасным вариантом для файлов другого вида.
    /// </summary>
    private static IEnumerable<string> ReadPlatformExecutables(string settingsPath)
    {
        var result = new List<string>();

        if (IsXmlFile(settingsPath))
        {
            try
            {
                var document = XDocument.Load(settingsPath);
                result.AddRange(document
                    .Descendants()
                    .Where(e => e.Name.LocalName.EndsWith("AppFile", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0));
            }
            catch (Exception ex)
            {
                LogInfo($"Не удалось разобрать {Path.GetFileName(settingsPath)} как XML: {ex.Message}");
            }

            return result;
        }

        var values = ParseIniGlobalValues(settingsPath);
        if (values.TryGetValue(KV8AppPath, out var exePath) && !string.IsNullOrWhiteSpace(exePath))
            result.Add(exePath.Trim());

        return result;
    }

    /// <summary>Определяет по первому значимому символу, является ли файл XML-документом.</summary>
    private static bool IsXmlFile(string path)
    {
        try
        {
            foreach (var line in ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
                return trimmed[0] == '<';
            }
        }
        catch (IOException)
        {
            // Нечитаемый файл разбираем как INI: там ошибка обрабатывается тише.
        }

        return false;
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
    /// Читает все строки файла настроек StartManager. Кодировка определяется по BOM:
    /// StartManager 1.4 пишет свои файлы в UTF-8 с BOM, а более старые сборки — в ANSI
    /// (Windows-1251), поэтому ANSI остаётся кодировкой по умолчанию для файлов без BOM.
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        using var reader = new StreamReader(path, Ansi, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    // ---------------------------------------------------------------- расшифровка пароля

    /// <summary>
    /// Расшифровывает пароль StartManager. Метод Виженера по ASCII-символам с ключом
    /// «SLAVKA»: каждый байт шифротекста смещается назад на код соответствующего символа
    /// ключа и вперёд на код символа «0» (по модулю 256). Пустые значения возвращаются
    /// без изменений. Смещение на 48 проверено на паролях «123» (шифр «TND») и «Abc-99»
    /// (шифр «d~tSTJ»): без него расшифровка промахивается ровно на код нуля.
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
            result[i] = (byte)((data[i] - shift + ZeroDigit) & 0xFF);
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
            result[i] = (byte)((data[i] + shift - ZeroDigit) & 0xFF);
        }

        return Ansi.GetString(result);
    }

    // ---------------------------------------------------------------- вспомогательное

    private static string? Get(Dictionary<string, string> section, string key)
        => section.TryGetValue(key, out var value) ? value : null;

    /// <summary>Возвращает имя секции: в v8config.smc это идентификатор базы из списка 1С.</summary>
    private static string SectionName(Dictionary<string, string> section)
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