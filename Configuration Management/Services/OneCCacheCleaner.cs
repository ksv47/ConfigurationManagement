using System.IO;
using System.Threading.Tasks;
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
    /// <returns>Количество удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase)
    {
        return Clear(infobase, OneCCacheKind.All);
    }

    /// <summary>
    /// Очищает кеш указанного типа для одной информационной базы.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase, OneCCacheKind kind)
    {
        return Clear(new[] { infobase }, kind);
    }

    /// <summary>
    /// Очищает кеш указанного типа для набора информационных баз.
    /// </summary>
    /// <param name="infobases">Набор информационных баз, кеш которых нужно очистить.</param>
    /// <param name="kind">Тип очищаемого кеша (программный и/или пользовательский).</param>
    /// <returns>Количество удалённых каталогов кеша.</returns>
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
    /// Кеш хранится в каталогах %LOCALAPPDATA%\1C\1cv8 (программный) и %APPDATA%\1C\1cv8
    /// (пользовательский) в подкаталогах, имя которых соответствует ID базы 1С.
    /// </summary>
    private static int ClearSingle(Infobase infobase, OneCCacheKind kind)
    {
        var removed = 0;

        // Кеш может находиться в нескольких корневых каталогах.
        foreach (var root in GetCacheRoots(kind))
        {
            if (!Directory.Exists(root))
                continue;

            // Если известен ID базы — каталог кеша называется по ID базы.
            if (!string.IsNullOrWhiteSpace(infobase.Id))
            {
                var idDir = Path.Combine(root, infobase.Id);
                if (Directory.Exists(idDir))
                {
                    TryDeleteDirectory(idDir);
                    removed++;
                    continue;
                }

                // ID может храниться в нижнем регистре.
                var idDirLower = Path.Combine(root, infobase.Id.ToLowerInvariant());
                if (Directory.Exists(idDirLower))
                {
                    TryDeleteDirectory(idDirLower);
                    removed++;
                    continue;
                }
            }

            // Если ID неизвестен — ищем каталог по имени базы (для баз, созданных вручную).
            var cacheName = GetCacheName(infobase);
            if (string.IsNullOrWhiteSpace(cacheName))
                continue;

            // Ищем каталоги кеша: как в подкаталогах версий (1cv8\<версия>\<имя>),
            // так и непосредственно в корне (1cv8\<имя>).
            foreach (var versionDir in Directory.GetDirectories(root))
            {
                var cacheDir = Path.Combine(versionDir, cacheName);
                if (Directory.Exists(cacheDir))
                {
                    TryDeleteDirectory(cacheDir);
                    removed++;
                }
            }

            // Прямой каталог кеша в корне (без версии).
            var directCacheDir = Path.Combine(root, cacheName);
            if (Directory.Exists(directCacheDir))
            {
                TryDeleteDirectory(directCacheDir);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Определяет имя каталога кеша для базы.
    /// Для клиент-серверной базы — имя базы на сервере,
    /// для файловой — имя файла базы без расширения.
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
    /// Возвращает корневые каталоги, где 1С хранит кеш, с учётом выбранного типа кеша.
    /// </summary>
    private static IEnumerable<string> GetCacheRoots(OneCCacheKind kind)
    {
        var roots = new List<string>();

#if LINUX
        // Linux: программный кеш — ~/.cache/1cv8 (XDG_CACHE_HOME) и ~/.1cv8/1cv8;
        // пользовательский — ~/.local/share/1cv8/1cv8 (XDG_DATA_HOME) и ~/.1cv8/1cv8.
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
                roots.Add(Path.Combine(home, ".1cv8", "1cv8"));
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
    /// Удаляет каталог кеша. Чтобы не блокировать интерфейс при удалении
    /// большого количества файлов, каталог сначала переименовывается во временное
    /// имя (мгновенная операция), а затем удаляется асинхронно в фоновом потоке.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            // Переименовываем каталог во временное имя — это мгновенная операция,
            // не зависящая от количества файлов внутри.
            var tempPath = path + ".deleting_" + Guid.NewGuid().ToString("N");
            Directory.Move(path, tempPath);

            // Удаляем переименованный каталог в фоновом потоке.
            Task.Run(() =>
            {
                try
                {
                    Directory.Delete(tempPath, recursive: true);
                }
                catch
                {
                    // Игнорируем ошибки удаления (файлы могут быть заняты запущенной 1С).
                }
            });
        }
        catch
        {
            // Игнорируем ошибки (каталог может быть занят запущенной 1С).
        }
    }
}