using System.IO;
using System.Text.Json;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Репозиторий для сохранения и загрузки настроек информационных баз в JSON-файл.
/// Файлы данных хранятся в каталоге активного профиля (<see cref="IProfileService.CurrentProfileDataDirectory"/>),
/// поэтому у каждой учётной записи свой список баз, групп и настроек.
/// </summary>
public class InfobaseRepository : IInfobaseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Без отступов — заметно быстрее сериализация/запись при большом списке баз.
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Текущая версия схемы файла <c>settings.json</c>. Увеличивается при изменении модели
    /// настроек, несовместимом со старыми файлами. Файлы с большей версией схемы (созданные
    /// более новой версией приложения) не читаются: они откладываются в резервную копию,
    /// а приложение стартует с настройками по умолчанию.
    /// </summary>
    private const int ConfigSchemaVersion = 1;

    private readonly IProfileService? _profileService;
    private readonly string? _explicitDirectory;

    /// <summary>
    /// Создаёт репозиторий. Если передан явный <paramref name="directory"/> — файлы данных
    /// читаются/пишутся в нём (режим совместимости/тестов). Иначе используется каталог данных
    /// активного профиля (<see cref="IProfileService.CurrentProfileDataDirectory"/>), поэтому
    /// у каждой учётной записи свой список баз, групп и настроек. Если сервис профилей не задан —
    /// общий каталог данных приложения (легаси-режим).
    /// </summary>
    public InfobaseRepository(IProfileService? profileService = null, string? directory = null)
    {
        _profileService = profileService;
        _explicitDirectory = directory;
    }

    /// <summary>Каталог, в котором репозиторий хранит файлы данных.</summary>
    private string DataDirectory =>
        !string.IsNullOrEmpty(_explicitDirectory)
            ? _explicitDirectory!
            : (_profileService?.CurrentProfileDataDirectory ?? PlatformPaths.AppDataDirectory);

    private string InfobasesPath => Path.Combine(DataDirectory, "infobases.json");
    private string GroupsPath => Path.Combine(DataDirectory, "groups.json");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>
    /// Загружает список информационных баз из файла. Если файл отсутствует — возвращает пустой список.
    /// </summary>
    public List<Infobase> Load()
    {
        if (!File.Exists(InfobasesPath))
            return new List<Infobase>();
        try
        {
            var json = File.ReadAllText(InfobasesPath);
            return JsonSerializer.Deserialize<List<Infobase>>(json, JsonOptions) ?? new List<Infobase>();
        }
        catch (Exception ex)
        {
            // При ошибке десериализации возвращаем пустой список, а повреждённый файл
            // откладываем в резервную копию, чтобы не спотыкаться о него на каждом старте.
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки баз: {ex.Message}");
            QuarantineCorruptFile(InfobasesPath, "corrupt");
            return new List<Infobase>();
        }
    }

    /// <summary>
    /// Сохраняет список информационных баз в файл.
    /// </summary>
    public void Save(List<Infobase> infobases)
    {
        WriteAtomic(InfobasesPath, JsonSerializer.Serialize(infobases, JsonOptions));
    }

    /// <summary>
    /// Загружает список групп из файла. Если файл отсутствует — возвращает пустой список.
    /// Повреждённые данные групп (пустые или дублирующиеся идентификаторы) автоматически
    /// восстанавливаются, чтобы иерархия «группа в группе» не терялась.
    /// </summary>
    public List<Group> LoadGroups()
    {
        if (!File.Exists(GroupsPath))
            return new List<Group>();
        try
        {
            var json = File.ReadAllText(GroupsPath);
            var groups = JsonSerializer.Deserialize<List<Group>>(json, JsonOptions) ?? new List<Group>();
            var hadInvalidIds = groups.Any(g => string.IsNullOrWhiteSpace(g.Id));
            var hadDuplicateIds = groups.GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1);
            var normalized = NormalizeGroups(groups);
            // Если при загрузке были исправлены идентификаторы, устранены дубликаты или
            // разорваны циклические ссылки — сразу сохраняем, чтобы иерархия групп
            // гарантированно восстановилась на диске и не ломала последующие запуски.
            if (hadInvalidIds || hadDuplicateIds || normalized)
            {
                SaveGroups(groups);
            }
            return groups;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки групп: {ex.Message}");
            QuarantineCorruptFile(GroupsPath, "corrupt");
            return new List<Group>();
        }
    }

    /// <summary>
    /// Восстанавливает корректность списка групп: генерирует недостающие идентификаторы,
    /// устраняет дубликаты и разрывает циклические ссылки на родителя (A→B→A), сохраняя
    /// корректные ссылки на родителя. Возвращает <c>true</c>, если список был изменён.
    /// </summary>
    private static bool NormalizeGroups(List<Group> groups)
    {
        var changed = false;

        // Идентификаторы, которые уже корректно используются группами.
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Первый проход: присваиваем идентификаторы группам с пустым Id.
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id))
            {
                group.Id = Guid.NewGuid().ToString();
                changed = true;
            }
        }

        // Второй проход: устраняем дубликаты идентификаторов.
        foreach (var group in groups)
        {
            if (usedIds.Contains(group.Id))
            {
                // Обнаружен дубликат Id — назначаем новый уникальный идентификатор
                // и переносим на него ссылки детей.
                var oldId = group.Id;
                var newId = Guid.NewGuid().ToString();
                while (usedIds.Contains(newId))
                {
                    newId = Guid.NewGuid().ToString();
                }
                group.Id = newId;
                changed = true;
                foreach (var child in groups)
                {
                    if (string.Equals(child.ParentId, oldId, StringComparison.OrdinalIgnoreCase))
                    {
                        child.ParentId = newId;
                    }
                }
            }
            usedIds.Add(group.Id);
        }

        // Третий проход: разрываем циклические ссылки на родителя. Циклическая цепочка
        // (A→B→A) в плоском списке приводит к бесконечной вложенности при построении дерева
        // и могла вызывать зависание/переполнение стека на повреждённых или легаси-файлах
        // (issue #64). Разрывается ровно та ссылка, которая замыкает цикл: для группы,
        // чей ParentId указывает на узел, уже присутствующий в цепочке предков, ссылка
        // очищается, и группа становится корневой — как это и делает построитель дерева.
        var idToGroup = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            idToGroup[group.Id] = group;
        }

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.ParentId))
                continue;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = group;
            while (current is not null && !string.IsNullOrEmpty(current.ParentId))
            {
                if (!visited.Add(current.Id))
                    break; // Уже прошли этот узел — цепочка замкнулась.

                if (!idToGroup.TryGetValue(current.ParentId, out var parent))
                    break; // Родитель не найден — это обычный корень.

                if (visited.Contains(parent.Id))
                {
                    // Переход к родителю замкнул бы цепочку в цикл — разрываем эту ссылку.
                    current.ParentId = string.Empty;
                    changed = true;
                    break;
                }

                current = parent;
            }
        }

        return changed;
    }

    /// <summary>
    /// Сохраняет список групп в файл.
    /// </summary>
    public void SaveGroups(List<Group> groups)
    {
        WriteAtomic(GroupsPath, JsonSerializer.Serialize(groups, JsonOptions));
    }

    /// <summary>
    /// Загружает настройки интерфейса из файла. Если файл отсутствует — возвращает настройки по умолчанию.
    /// </summary>
    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
#if DEBUG
            Console.Error.WriteLine("[l10n-debug] LoadSettings: file missing (" + SettingsPath + ")");
#endif
            return new AppSettings();
        }
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Восстанавливаем null-поля, которые могли прийти из легаси/повреждённого файла,
            // чтобы конструкторы не спотыкались о них при старте (issue #64).
            loaded.NormalizeForLoad();

            // Файл создан более новой версией приложения, чем текущая: его схема нам может
            // быть незнакома, безопасно прочитать его нельзя. Откладываем файл в резервную
            // копию и стартуем с чистыми настройками — вместо зависания на непонятных данных.
            if (loaded.SchemaVersion > ConfigSchemaVersion)
            {
                System.Diagnostics.Debug.WriteLine($"Схема настроек {loaded.SchemaVersion} новее поддерживаемой {ConfigSchemaVersion}: сброс к настройкам по умолчанию.");
                QuarantineCorruptFile(SettingsPath, "future-schema");
                return new AppSettings();
            }

#if DEBUG
            Console.Error.WriteLine("[l10n-debug] LoadSettings: Language=" + loaded.Language + ", file=" + SettingsPath);
#endif
            return loaded;
        }
        catch (Exception ex)
        {
            // Повреждённый или несовместимый файл настроек: делаем резервную копию и стартуем
            // с настройками по умолчанию, чтобы приложение гарантированно открылось.
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            QuarantineCorruptFile(SettingsPath, "corrupt");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки интерфейса в файл.
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
#if DEBUG
        Console.Error.WriteLine("[l10n-debug] SaveSettings: Language=" + settings.Language + ", file=" + SettingsPath);
#endif
        settings.SchemaVersion = ConfigSchemaVersion;
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }


    public async Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(infobases, JsonOptions);
        await WriteAtomicAsync(InfobasesPath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(groups, JsonOptions);
        await WriteAtomicAsync(GroupsPath, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.SchemaVersion = ConfigSchemaVersion;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await WriteAtomicAsync(SettingsPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Откладывает повреждённый или несовместимый конфигурационный файл в резервную копию,
    /// чтобы он не мешал запуску. Оригинал переименовывается в «<имя>.<причина>.<метка времени>.bak»
    /// в том же каталоге. Любые ошибки глушатся — карантин не должен блокировать запуск.
    /// </summary>
    private static void QuarantineCorruptFile(string path, string reason)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(directory ?? "", $"{name}.{reason}.{stamp}{ext}.bak");
            File.Move(path, backupPath);
        }
        catch
        {
            // Игнорируем: карантин — вспомогательная операция и не должен ломать запуск.
        }
    }

    /// <summary>
    /// Атомарная запись: сначала во временный файл, затем замена целевого.
    /// Снижает риск повреждения данных при сбое/отключении питания.
    /// </summary>
    private static void WriteAtomic(string targetPath, string content)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        File.WriteAllText(tempPath, content);
        // File.Replace надёжнее на Windows при существующем файле; иначе Move.
        if (File.Exists(targetPath))
            File.Replace(tempPath, targetPath, null);
        else
            File.Move(tempPath, targetPath);
    }

    /// <summary>
    /// Асинхронная атомарная запись через временный файл.
    /// </summary>
    private static async Task WriteAtomicAsync(string targetPath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
        if (File.Exists(targetPath))
            File.Replace(tempPath, targetPath, null);
        else
            File.Move(tempPath, targetPath);
    }
}