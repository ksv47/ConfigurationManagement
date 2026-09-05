using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Тип локального кеша платформы 1С.
/// </summary>
[Flags]
public enum OneCCacheKind
{
    /// <summary>Не очищать кеш.</summary>
    None = 0,

    /// <summary>
    /// Программный кеш: %LOCALAPPDATA%\1C\1cv8…
    /// </summary>
    Program = 1,

    /// <summary>
    /// Пользовательский кеш: %APPDATA%\1C\1cv8…
    /// </summary>
    User = 2,

    /// <summary>Программный и пользовательский кеш одновременно.</summary>
    All = Program | User
}

/// <summary>
/// Сервис очистки локального кеша платформы 1С для одной или нескольких информационных баз.
/// </summary>
public static class OneCCacheCleaner
{
    /// <summary>
    /// Очищает программный и пользовательский кеш 1С для указанной информационной базы.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase)
    {
        return Clear(infobase, OneCCacheKind.All);
    }

    /// <summary>
    /// Очищает кеш указанного типа для одной информационной базы.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase, OneCCacheKind kind)
    {
        return Clear(new[] { infobase }, kind);
    }

    /// <summary>
    /// Очищает кеш указанного типа для набора информационных баз.
    /// </summary>
    /// <param name="infobases">Набор информационных баз, кеш которых нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int Clear(IEnumerable<Infobase> infobases, OneCCacheKind kind)
    {
        if (infobases is null || kind == OneCCacheKind.None)
            return 0;

        var removed = 0;
        foreach (var infobase in infobases)
        {
            if (infobase is null)
                continue;
            removed += ClearSingle(infobase, kind);
        }

        return removed;
    }

    /// <summary>
    /// Очищает кеш 1С указанного типа для конкретной информационной базы.
    /// Каталоги кеша ищутся по карте IdConnStrMap из 1cv8u.pfl (имя каталога — собственный
    /// GUID платформы), а также по имени клиент-серверного каталога Srvr__…__Ref__…__,
    /// ID базы и имени базы. Удаляются только реально существующие и действительно удалённые
    /// каталоги.
    /// </summary>
    private static int ClearSingle(Infobase infobase, OneCCacheKind kind)
    {
        var removed = 0;
        foreach (var dir in EnumerateCacheDirectories(infobase, kind).Distinct())
        {
            if (TryDeleteDirectory(dir))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Определяет имя каталога кеша базы по старым правилам (используется как запасной
    /// вариант, если карту IdConnStrMap прочитать не удалось).
    /// Для клиент-серверной базы — имя базы на сервере, для файловой — имя файла без расширения.
    /// </summary>
    private static string GetCacheName(Infobase infobase)
    {
        var conn = infobase.Connection;
        return conn.Type switch
        {
            ConnectionType.File => Path.GetFileNameWithoutExtension(conn.FilePath),
            _ => conn.DatabaseName
        };
    }

    /// <summary>
    /// Запись карты IdConnStrMap: строка соединения, разобранная в настройки, и GUID каталога кеша.
    /// </summary>
    private sealed class IdConnStrEntry
    {
        public required ConnectionSettings Settings { get; init; }
        public required string CacheGuid { get; init; }
    }

    // Кэш разобранной карты IdConnStrMap: повторно файл 1cv8u.pfl читается, только если он
    // изменился. Доступ синхронизируется — чтение может выполняться из фоновых потоков.
    private static readonly object PflLock = new();
    private static string? _pflCachedPath;
    private static DateTime _pflCachedMtimeUtc;
    private static IReadOnlyList<IdConnStrEntry>? _pflCachedEntries;

    /// <summary>
    /// Возвращает путь к файлу карты IdConnStrMap (1cv8u.pfl) в корне пользовательского кеша,
    /// если файл существует, иначе null.
    /// </summary>
    private static string? GetPflPath()
    {
#if LINUX
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            foreach (var rel in new[] { ".1cv8/1C/1cv8/1cv8u.pfl", ".1cv8/1cv8/1cv8u.pfl" })
            {
                var p = Path.Combine(home, rel);
                if (File.Exists(p))
                    return p;
            }
        }
#else
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            var p = Path.Combine(appData, "1C", "1cv8", "1cv8u.pfl");
            if (File.Exists(p))
                return p;
        }
#endif
        return null;
    }

    /// <summary>
    /// Читает и разбирает карту IdConnStrMap из файла 1cv8u.pfl. Карта содержит пары
    /// «строка соединения → GUID каталога кеша». Результат кэшируется до изменения файла.
    /// </summary>
    private static IReadOnlyList<IdConnStrEntry>? LoadIdConnStrMap()
    {
        var path = GetPflPath();
        if (path is null)
            return null;

        lock (PflLock)
        {
            try
            {
                var mtime = File.GetLastWriteTimeUtc(path);
                if (_pflCachedPath == path && _pflCachedMtimeUtc == mtime && _pflCachedEntries is not null)
                    return _pflCachedEntries;
            }
            catch
            {
                return null;
            }

            List<IdConnStrEntry>? entries = null;
            try
            {
                // ReadAllText автоматически распознаёт BOM и декодирует UTF-8.
                var text = File.ReadAllText(path);

                // Пары «{"S","<строка соединения>"},{"S","<GUID>"}». Кавычки внутри
                // строки соединения удвоены (""), их нужно развернуть обратно в одну.
                const string pattern =
                    @"\{\s*""S""\s*,\s*""(?<conn>(?:[^""]|"""")*)""\s*\}\s*,\s*" +
                    @"\{\s*""S""\s*,\s*""(?<guid>[0-9A-Fa-f\-]{36})""\s*\}";
                var options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

                foreach (Match m in Regex.Matches(text, pattern, options))
                {
                    var connRaw = m.Groups["conn"].Value.Replace("\"\"", "\"");
                    var settings = ConnectionSettings.ParseConnectionString(connRaw);
                    if (settings is null || string.IsNullOrWhiteSpace(m.Groups["guid"].Value))
                        continue;

                    (entries ??= new List<IdConnStrEntry>()).Add(new IdConnStrEntry
                    {
                        Settings = settings,
                        CacheGuid = m.Groups["guid"].Value
                    });
                }
            }
            catch
            {
                entries = null;
            }

            if (entries is not null)
            {
                try { _pflCachedMtimeUtc = File.GetLastWriteTimeUtc(path); } catch { _pflCachedMtimeUtc = DateTime.MinValue; }
                _pflCachedPath = path;
                _pflCachedEntries = entries;
            }
            return entries;
        }
    }

    /// <summary>
    /// Проверяет, соответствует ли запись карты IdConnStrMap информационной базе.
    /// Хост сравнивается без учёта порта (одна база встречается в карте и с портом,
    /// и без него), имя базы — без учёта регистра.
    /// </summary>
    private static bool MatchesBase(IdConnStrEntry entry, Infobase infobase)
    {
        var conn = infobase.Connection;
        switch (entry.Settings.Type)
        {
            case ConnectionType.File:
                if (conn.Type != ConnectionType.File)
                    return false;
                return string.Equals(
                    NormalizePathForCompare(entry.Settings.FilePath),
                    NormalizePathForCompare(conn.FilePath),
                    StringComparison.OrdinalIgnoreCase);

            case ConnectionType.WebServer:
                if (conn.Type != ConnectionType.WebServer)
                    return false;
                return string.Equals(
                    (entry.Settings.WebUrl ?? string.Empty).Trim(),
                    (conn.WebUrl ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);

            default: // ClientServer
                if (conn.Type != ConnectionType.ClientServer)
                    return false;
                if (!string.Equals(
                        (entry.Settings.DatabaseName ?? string.Empty).Trim(),
                        (conn.DatabaseName ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                return string.Equals(
                    (entry.Settings.Server ?? string.Empty).Trim(),
                    (conn.Server ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Приводит путь к единому виду для сравнения: прямой слэш, без завершающего слэша.
    /// </summary>
    private static string NormalizePathForCompare(string path)
    {
        var s = (path ?? string.Empty).Trim();
        if (s.Length == 0)
            return s;
        s = s.Replace('\\', '/');
        while (s.EndsWith('/'))
            s = s.Substring(0, s.Length - 1);
        return s;
    }

    /// <summary>
    /// Возвращает GUID каталогов кеша, которые платформа сопоставила строке соединения базы.
    /// </summary>
    private static IEnumerable<string> GetGuidCacheNames(Infobase infobase)
    {
        var map = LoadIdConnStrMap();
        if (map is null)
            yield break;
        foreach (var entry in map)
        {
            if (MatchesBase(entry, infobase))
                yield return entry.CacheGuid;
        }
    }

    /// <summary>
    /// Возвращает имя клиент-серверного каталога кеша вида Srvr__<сервер>__Ref__<база>__
    /// (для сервера с портом и без — два варианта имени) или пустую строку для не
    /// клиент-серверной базы.
    /// </summary>
    private static IEnumerable<string> GetClientServerDirNames(Infobase infobase)
    {
        var conn = infobase.Connection;
        if (conn.Type != ConnectionType.ClientServer)
            yield break;

        var db = SanitizeDirPart(conn.DatabaseName);
        if (string.IsNullOrWhiteSpace(db))
            yield break;

        var server = SanitizeDirPart(conn.Server);
        if (!string.IsNullOrWhiteSpace(server))
            yield return $"Srvr__{server}__Ref__{db}__";

        var serverWithPort = SanitizeDirPart(conn.GetServerWithPort());
        if (!string.IsNullOrWhiteSpace(serverWithPort) &&
            !string.Equals(serverWithPort, server, StringComparison.OrdinalIgnoreCase))
            yield return $"Srvr__{serverWithPort}__Ref__{db}__";
    }

    /// <summary>
    /// Заменяет символы, недопустимые в имени каталога, на подчёркивание (как делает платформа).
    /// </summary>
    private static string SanitizeDirPart(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(IsInvalidDirChar(c) ? '_' : c);
        return sb.ToString();
    }

    private static bool IsInvalidDirChar(char c) =>
        c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
        c == '"' || c == '<' || c == '>' || c == '|';

    /// <summary>
    /// Собирает множество имён каталогов кеша, соответствующих информационной базе:
    /// GUID из карты IdConnStrMap, клиент-серверное имя Srvr__…__Ref__…__, ID базы и имя базы.
    /// </summary>
    private static IEnumerable<string> GetCacheNames(Infobase infobase)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in GetGuidCacheNames(infobase))
            set.Add(g);

        foreach (var cs in GetClientServerDirNames(infobase))
            set.Add(cs);

        // Запасные варианты: если карту не удалось прочитать, а также для баз,
        // чьи каталоги называются по ID или по имени (созданные вручную).
        if (!string.IsNullOrWhiteSpace(infobase.Id))
            set.Add(infobase.Id);

        var legacy = GetCacheName(infobase);
        if (!string.IsNullOrWhiteSpace(legacy))
            set.Add(legacy);

        return set;
    }

    /// <summary>
    /// Собирает множество «защищённых» имён каталогов кеша — имён, соответствующих текущим
    /// информационным базам (GUID из карты IdConnStrMap, имена Srvr__…__Ref__…__,
    /// ID и имена баз). Каталоги с такими именами не считаются «остатками» от удалённых
    /// баз и не подлежат автоматической очистке.
    /// </summary>
    private static HashSet<string> BuildProtectedNames(IEnumerable<Infobase> allBases)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allBases is null)
            return set;

        foreach (var ib in allBases)
        {
            if (ib is null)
                continue;
            foreach (var name in GetCacheNames(ib))
                set.Add(name);
        }

        return set;
    }

    /// <summary>
    /// Определяет, является ли имя каталога именем версии платформы (например, «8.3.24.1234»).
    /// Каталоги версий не являются каталогами кеша отдельных баз — внутри них хранятся кеши.
    /// </summary>
    private static bool IsVersionDirName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        foreach (var c in name)
        {
            if (!char.IsDigit(c) && c != '.' && c != '-')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Определяет, является ли имя каталога именем кеша информационной базы. Платформа
    /// называет такой каталог собственным GUID, а для клиент-серверной базы использует
    /// имя вида Srvr__<сервер>__Ref__<база>__. Служебные каталоги платформы (conf, logs,
    /// ExtCompT, STT, standalone-server) под эти правила не подходят и остатками
    /// не считаются.
    /// </summary>
    private static bool IsBaseCacheDirName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (Guid.TryParseExact(name, "D", out _))
            return true;

        return name.StartsWith("Srvr__", StringComparison.OrdinalIgnoreCase)
            && name.Contains("__Ref__", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Определяет, является ли имя каталога временным именем удаления (<c><имя>.deleting_<guid></c>).
    /// Такие каталоги — остатки прерванного удаления; их не нужно ни считать остатками
    /// от удалённых баз, ни переименовывать повторно (иначе имя растёт на 41 символ за раз).
    /// </summary>
    private static bool IsDeletingName(string name) =>
        name is not null && name.Contains(".deleting_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Перечисляет каталоги кеша, не принадлежащие ни одной текущей информационной базе.
    /// Это «остатки» от удалённых из списка или созданных вне приложения баз: каталоги,
    /// имя которых узнаётся как имя кеша базы (см. <see cref="IsBaseCacheDirName"/>)
    /// и не совпадает ни с одним «защищённым» именем (см. <see cref="BuildProtectedNames"/>).
    /// Каталоги версий платформы (например, «8.3.24.1234») не удаляются — они анализируются,
    /// и удаляются только их вложенные каталоги-кеши. Временные каталоги *.deleting_*
    /// не учитываются.
    /// </summary>
    private static IEnumerable<string> EnumerateOrphanDirectories(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        var protectedNames = BuildProtectedNames(allBases);

        foreach (var root in GetCacheRoots(kind, forOrphanScan: true))
        {
            if (!Directory.Exists(root))
                continue;

            string[] versionDirs;
            try { versionDirs = Directory.GetDirectories(root); }
            catch { continue; }

            foreach (var versionDir in versionDirs)
            {
                var versionName = Path.GetFileName(versionDir);

                if (IsDeletingName(versionName))
                    continue;

                if (IsVersionDirName(versionName))
                {
                    // Внутри каталога версии находятся каталоги кеша отдельных баз.
                    string[] cacheDirs;
                    try { cacheDirs = Directory.GetDirectories(versionDir); }
                    catch { continue; }

                    foreach (var cd in cacheDirs)
                    {
                        var n = Path.GetFileName(cd);
                        if (IsDeletingName(n) || protectedNames.Contains(n) || !IsBaseCacheDirName(n))
                            continue;
                        yield return cd;
                    }
                }
                else if (!protectedNames.Contains(versionName) && IsBaseCacheDirName(versionName))
                {
                    // Прямой каталог кеша в корне (без версии).
                    yield return versionDir;
                }
            }
        }
    }

    /// <summary>
    /// Вычисляет суммарный размер «остатков» кеша от удалённых баз — каталогов кеша,
    /// не принадлежащих ни одной текущей информационной базе.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="allBases">Все текущие информационные базы (для определения «защищённых» имён).</param>
    /// <returns>Суммарный размер в байтах.</returns>
    public static long GetOrphanSize(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        long total = 0;
        foreach (var dir in EnumerateOrphanDirectories(kind, allBases))
            total += GetDirectorySize(dir);
        return total;
    }

    /// <summary>
    /// Удаляет «остатки» кеша от удалённых баз — каталоги кеша, не принадлежащие ни одной
    /// текущей информационной базе.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="allBases">Все текущие информационные базы (для определения «защищённых» имён).</param>
    /// <returns>Количество фактически удалённых каталогов кеша.</returns>
    public static int ClearOrphans(OneCCacheKind kind, IEnumerable<Infobase> allBases)
    {
        if (kind == OneCCacheKind.None)
            return 0;

        var removed = 0;
        foreach (var dir in EnumerateOrphanDirectories(kind, allBases))
        {
            if (TryDeleteDirectory(dir))
                removed++;
        }

        return removed;
    }

    /// <summary>
    /// Вычисляет суммарный размер кеша указанного типа (программного и/или пользовательского)
    /// по каталогам, принадлежащим текущим информационным базам. Служебные файлы платформы
    /// в корнях кеша (helpsynt.dat, логи и т. п.) не учитываются.
    /// </summary>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <param name="infobases">Все текущие информационные базы.</param>
    /// <returns>Суммарный размер в байтах.</returns>
    public static long GetSize(OneCCacheKind kind, IEnumerable<Infobase> infobases)
    {
        if (kind == OneCCacheKind.None || infobases is null)
            return 0;

        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ib in infobases)
        {
            if (ib is null)
                continue;
            foreach (var dir in EnumerateCacheDirectories(ib, kind))
            {
                if (seen.Add(dir))
                    total += GetDirectorySize(dir);
            }
        }

        return total;
    }

    /// <summary>
    /// Вычисляет суммарный размер кеша указанного типа для одной информационной базы.
    /// Учитываются только реально существующие каталоги кеша этой базы.
    /// </summary>
    /// <param name="infobase">Информационная база, для которой вычисляется размер кеша.</param>
    /// <param name="kind">Тип кеша (программный и/или пользовательский).</param>
    /// <returns>Суммарный размер кеша базы в байтах.</returns>
    public static long GetSize(Infobase infobase, OneCCacheKind kind)
    {
        if (infobase is null || kind == OneCCacheKind.None)
            return 0;

        long total = 0;
        foreach (var dir in EnumerateCacheDirectories(infobase, kind).Distinct())
            total += GetDirectorySize(dir);

        return total;
    }

    /// <summary>
    /// Перечисляет реально существующие каталоги кеша указанной информационной базы
    /// для заданного типа кеша. Имена каталогов берутся из карты IdConnStrMap (GUID
    /// платформы), клиент-серверных имён Srvr__…__Ref__…__, а также ID и имени базы.
    /// Каталоги ищутся и непосредственно в корне кеша, и в подкаталогах версий.
    /// </summary>
    private static IEnumerable<string> EnumerateCacheDirectories(Infobase infobase, OneCCacheKind kind)
    {
        var names = GetCacheNames(infobase).ToList();
        if (names.Count == 0)
            yield break;

        foreach (var root in GetCacheRoots(kind))
        {
            if (!Directory.Exists(root))
                continue;

            // Прямые каталоги кеша в корне.
            foreach (var n in names)
            {
                var direct = Path.Combine(root, n);
                if (Directory.Exists(direct))
                    yield return direct;
            }

            // Каталоги кеша в подкаталогах версий (1cv8\<версия>\<имя>).
            string[] versionDirs;
            try { versionDirs = Directory.GetDirectories(root); }
            catch { continue; }

            foreach (var versionDir in versionDirs)
            {
                if (!IsVersionDirName(Path.GetFileName(versionDir)))
                    continue;
                foreach (var n in names)
                {
                    var cacheDir = Path.Combine(versionDir, n);
                    if (Directory.Exists(cacheDir))
                        yield return cacheDir;
                }
            }
        }
    }

    /// <summary>
    /// Рекурсивно вычисляет суммарный размер всех файлов в каталоге (в байтах).
    /// Ошибки доступа к отдельным файлам игнорируются.
    /// </summary>
    private static long GetDirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Игнорируем недоступные файлы (могут быть заняты запущенной 1С).
                }
            }
        }
        catch
        {
            // Игнорируем ошибки перечисления (каталог может исчезнуть или быть недоступен).
        }

        return total;
    }

    /// <summary>
    /// Возвращает корневые каталоги, где 1С хранит кеш, с учётом выбранного типа кеша.
    /// </summary>
    /// <param name="forOrphanScan">
    /// True, если корни запрашиваются для поиска «осиротевшего» кеша. В этом режиме
    /// удаляется всё, что не принадлежит известным базам, поэтому корни, где рядом
    /// с кешем баз лежат служебные каталоги платформы, в него не отдаются.
    /// </param>
    private static IEnumerable<string> GetCacheRoots(OneCCacheKind kind, bool forOrphanScan = false)
    {
        var roots = new List<string>();

#if LINUX
        // Linux: пользовательский кеш баз платформа держит в ~/.1cv8/1C/1cv8,
        // каталоги баз по GUID лежат прямо в нём (проверено на 8.3.27).
        // ~/.cache/1cv8 и ~/.local/share/1cv8 это данные встроенного браузера,
        // а не кеш баз; оставлены как были, вместе с прежним ~/.1cv8/1cv8
        // на случай других раскладок дистрибутива.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (kind.HasFlag(OneCCacheKind.Program))
        {
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCache))
                roots.Add(Path.Combine(xdgCache, "1cv8"));
            else if (!string.IsNullOrEmpty(home))
                roots.Add(Path.Combine(home, ".cache", "1cv8"));
        }

        if (kind.HasFlag(OneCCacheKind.User))
        {
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgData))
                roots.Add(Path.Combine(xdgData, "1cv8"));
            else if (!string.IsNullOrEmpty(home))
                roots.Add(Path.Combine(home, ".local", "share", "1cv8"));
            // Общая для всех версий каталог кэша в профиле 1С.
            if (!string.IsNullOrEmpty(home))
            {
                // В ~/.1cv8/1C/1cv8 рядом с кешем баз платформа держит свои служебные
                // каталоги (conf, logs, ExtCompT, STT, standalone-server). Остатками
                // считаются только каталоги, узнаваемые как кеш базы (см.
                // IsBaseCacheDirName), поэтому служебные под очистку не попадают
                // и корень отдаётся в обоих режимах.
                roots.Add(Path.Combine(home, ".1cv8", "1C", "1cv8"));
                roots.Add(Path.Combine(home, ".1cv8", "1cv8"));
            }
        }
#else
        // Программный кеш — %LOCALAPPDATA%\1C\1cv8.
        if (kind.HasFlag(OneCCacheKind.Program))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                roots.Add(Path.Combine(localAppData, "1C", "1cv8"));
        }

        // Пользовательский кеш — %APPDATA%\1C\1cv8.
        if (kind.HasFlag(OneCCacheKind.User))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                roots.Add(Path.Combine(appData, "1C", "1cv8"));
        }
#endif

        return roots;
    }

    /// <summary>
    /// Удаляет каталог кеша. Каталог сначала переименовывается во временное имя
    /// (<c><имя>.deleting_<guid></c>), у всех файлов рекурсивно снимается атрибут
    /// ReadOnly (платформа кладёт в кеш файлы с этим атрибутом, из-за которого обычное
    /// рекурсивное удаление падает с UnauthorizedAccessException), после чего каталог удаляется.
    /// </summary>
    /// <returns>True, если каталог был найден и удалён; иначе False.</returns>
    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            // Переименовываем каталог во временное имя — это мгновенная операция,
            // и если удаление прервётся, каталог больше не будет найден как кеш базы.
            var tempPath = path + ".deleting_" + Guid.NewGuid().ToString("N");
            Directory.Move(path, tempPath);

            ClearReadOnlyAttributes(tempPath);

            Directory.Delete(tempPath, recursive: true);
            return true;
        }
        catch
        {
            // Игнорируем ошибки (каталог может быть занят запущенной 1С или
            // отсутствовать); считаем, что каталог не удалён.
            return false;
        }
    }

    /// <summary>
    /// Рекурсивно снимает атрибут ReadOnly со всех файлов и каталогов внутри <paramref name="path"/>.
    /// </summary>
    private static void ClearReadOnlyAttributes(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* Игнорируем отдельные недоступные файлы. */ }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attributes = File.GetAttributes(dir);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
                }
                catch { /* Игнорируем отдельные недоступные каталоги. */ }
            }
        }
        catch
        {
            // Игнорируем ошибки перечисления.
        }
    }
}