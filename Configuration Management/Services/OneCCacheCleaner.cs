using System.IO;
using System.Threading.Tasks;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис очистки локального кеша платформы 1С для конкретной информационной базы.
/// </summary>
public static class OneCCacheCleaner
{
    /// <summary>
    /// Очищает локальный кеш 1С для указанной информационной базы.
    /// Кеш хранится в каталогах %LOCALAPPDATA%\1C\1cv8 и %APPDATA%\1C\1cv8
    /// в подкаталогах, имя которых соответствует ID базы 1С.
    /// </summary>
    /// <param name="infobase">Информационная база, кеш которой нужно очистить.</param>
    /// <returns>Количество удалённых каталогов кеша.</returns>
    public static int Clear(Infobase infobase)
    {
        if (infobase is null)
            return 0;

        var removed = 0;

        // Кеш может находиться в нескольких корневых каталогах.
        foreach (var root in GetCacheRoots())
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
    /// Возвращает корневые каталоги, где 1С хранит кеш пользователя.
    /// </summary>
    private static IEnumerable<string> GetCacheRoots()
    {
        var roots = new List<string>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrEmpty(localAppData))
            roots.Add(Path.Combine(localAppData, "1C", "1cv8"));
        if (!string.IsNullOrEmpty(appData))
            roots.Add(Path.Combine(appData, "1C", "1cv8"));

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