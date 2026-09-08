using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IProfileService"/>: управление реестром профилей (<c>profiles.json</c>),
/// пер-профильными каталогами данных, хэширование/проверка паролей (PBKDF2) и миграция
/// существующих данных в профиль по умолчанию при первом запуске.
///
/// Структура каталога данных приложения после включения профилей:
/// <code>
///   ~/.config/ConfigurationManagement (Linux) / %APPDATA%\ConfigurationManagement (Windows)
///   ├── profiles.json                  — реестр учётных записей
///   └── profiles\
///       └── <Id>\
///           ├── settings.json
///           ├── infobases.json
///           └── groups.json
/// </code>
/// </summary>
public class ProfileService : IProfileService
{
    private const string RegistryFileName = "profiles.json";
    private const int RegistrySchemaVersion = 1;
    private const string DefaultProfileName = "Пользователь";

    // Имена «легаси»-файлов, которые до введения профилей лежали в корне каталога данных.
    private static readonly string[] LegacyDataFileNames =
    {
        "settings.json",
        "infobases.json",
        "groups.json"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Имена профилей могут содержать кириллицу — записываем их читаемыми UTF-8,
        // а не \uXXXX-последовательностями (issue #170). Влияет только на запись.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<UserProfile> _profiles = new();
    private readonly IAppLogger _logger;
    private string? _lastProfileId;
    private UserProfile? _currentProfile;
    private bool _initialized;

    private string RegistryPath => Path.Combine(PlatformPaths.AppDataDirectory, RegistryFileName);

    public ProfileService(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>Каталог данных конкретного профиля.</summary>
    public string GetProfileDataDirectory(string id) =>
        Path.Combine(PlatformPaths.AppDataDirectory, "profiles", id);

    public IReadOnlyList<UserProfile> Profiles
    {
        get
        {
            EnsureInitialized();
            return _profiles;
        }
    }

    public UserProfile? CurrentProfile
    {
        get
        {
            EnsureInitialized();
            return _currentProfile;
        }
    }

    public string CurrentProfileDataDirectory =>
        _currentProfile != null
            ? GetProfileDataDirectory(_currentProfile.Id)
            : PlatformPaths.AppDataDirectory;

    /// <summary>
    /// Загружает реестр профилей. Если реестра ещё нет, а в корне каталога данных есть
    /// легаси-файлы (settings/infobases/groups.json) — выполняет миграцию в профиль по умолчанию,
    /// чтобы существующие данные не потерялись. Повторные вызовы безопасны.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_initialized)
            return;

        if (File.Exists(RegistryPath))
        {
            LoadRegistry();
        }
        else
        {
            MigrateLegacyData();
            SaveRegistry();
        }

        // Активным делаем профиль, использованный последним; иначе — единственный профиль.
        if (_lastProfileId != null)
        {
            _currentProfile = _profiles.FirstOrDefault(p => p.Id == _lastProfileId);
        }
        _currentProfile ??= _profiles.FirstOrDefault();

        _initialized = true;
    }

    public UserProfile CreateProfile(string name, string? password = null)
    {
        EnsureInitialized();

        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Имя профиля не может быть пустым.", nameof(name));
        if (_profiles.Any(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Профиль с именем «{trimmed}» уже существует.", nameof(name));

        var profile = new UserProfile { Name = trimmed };
        if (!string.IsNullOrEmpty(password))
            profile.PasswordHash = HashPassword(password);

        _profiles.Add(profile);
        SaveRegistry();
        return profile;
    }

    public void RenameProfile(string id, string newName)
    {
        EnsureInitialized();

        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Имя профиля не может быть пустым.", nameof(newName));

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        if (_profiles.Any(p => p.Id != id && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Профиль с именем «{trimmed}» уже существует.", nameof(newName));

        profile.Name = trimmed;
        SaveRegistry();
    }

    public bool DeleteProfile(string id)
    {
        EnsureInitialized();

        // Нельзя удалить последний профиль — иначе приложение останется без данных.
        if (_profiles.Count <= 1)
            return false;

        var profile = FindProfile(id);
        if (profile == null)
            return false;

        var index = _profiles.IndexOf(profile);
        var wasCurrent = _currentProfile?.Id == id;
        var wasLast = _lastProfileId == id;

        _profiles.RemoveAt(index);
        if (wasCurrent)
            _currentProfile = _profiles.FirstOrDefault();
        if (wasLast)
            _lastProfileId = null;

        // Сначала сохраняем реестр — только при успешной записи удаляем каталог данных.
        // Так запись, исключённая из списка, не «оживёт» на следующем запуске из старого
        // файла profiles.json, когда её каталог уже удалён (issue #209).
        if (!SaveRegistry())
        {
            // Откатываем удаление, чтобы состояние на диске и в памяти не разошлось молча:
            // возвращаем профиль в список и восстанавливаем ссылки на текущий/последний.
            _profiles.Insert(index, profile);
            if (wasCurrent)
                _currentProfile = profile;
            if (wasLast)
                _lastProfileId = id;

            var message = string.Format(
                LocalizationManager.T("Profiles.DeleteFailedSave"), RegistryPath);
            _logger.Error(message);
            throw new InvalidOperationException(message);
        }

        // Реестр сохранён успешно — теперь удаляем каталог данных профиля.
        // Сбой здесь не критичен: запись уже исчезла из реестра, а каталог будет
        // перезаписан при пересоздании профиля с тем же Id.
        try
        {
            var dir = GetProfileDataDirectory(profile.Id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Оставляем каталог — он будет перезаписан при пересоздании профиля с тем же Id.
        }

        return true;
    }

    public void SetPassword(string id, string? password)
    {
        EnsureInitialized();

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        profile.PasswordHash = string.IsNullOrEmpty(password) ? string.Empty : HashPassword(password);
        SaveRegistry();
    }

    public bool VerifyPassword(string id, string password)
    {
        EnsureInitialized();

        var profile = FindProfile(id);
        if (profile == null || !profile.HasPassword)
            return true;

        if (string.IsNullOrEmpty(password))
            return false;

        return VerifyPasswordHash(password, profile.PasswordHash);
    }

    public void SetCurrentProfile(string id)
    {
        EnsureInitialized();

        var profile = FindProfile(id) ?? throw new InvalidOperationException("Профиль не найден.");
        _currentProfile = profile;
        _lastProfileId = profile.Id;
        SaveRegistry();
    }

    // ---------------------------------------------------------------- internals

    private UserProfile? FindProfile(string id) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Миграция легаси-данных: если реестра профилей нет, а в корне каталога данных есть
    /// файлы настроек/баз/групп — создаёт профиль по умолчанию и переносит эти файлы в его
    /// подкаталог. Легаси-файлы в корне при этом удаляются, чтобы не дублироваться.
    /// </summary>
    private void MigrateLegacyData()
    {
        var legacyDir = PlatformPaths.AppDataDirectory;
        var hadLegacyData = LegacyDataFileNames.Any(f => File.Exists(Path.Combine(legacyDir, f)));

        var defaultProfile = new UserProfile { Name = DefaultProfileName };
        _profiles.Add(defaultProfile);
        _lastProfileId = defaultProfile.Id;

        if (!hadLegacyData)
            return;

        var profileDir = GetProfileDataDirectory(defaultProfile.Id);
        Directory.CreateDirectory(profileDir);

        foreach (var name in LegacyDataFileNames)
        {
            var source = Path.Combine(legacyDir, name);
            if (!File.Exists(source))
                continue;
            try
            {
                var target = Path.Combine(profileDir, name);
                if (File.Exists(target))
                    File.Delete(target);
                File.Move(source, target);
            }
            catch
            {
                // Сбой переноса не должен блокировать запуск: файл останется в корне.
            }
        }
    }

    private void LoadRegistry()
    {
        try
        {
            var json = File.ReadAllText(RegistryPath);
            var registry = JsonSerializer.Deserialize<ProfileRegistry>(json, JsonOptions);
            if (registry?.Profiles is { Count: > 0 })
            {
                _profiles.Clear();
                _profiles.AddRange(registry.Profiles);
                _lastProfileId = registry.LastProfileId;
            }
        }
        catch
        {
            // Повреждённый реестр: пересоздаём чистый список. Каталоги данных профилей не трогаем.
            _profiles.Clear();
            _lastProfileId = null;
        }
    }

    /// <summary>
    /// Сохраняет реестр профилей в <c>profiles.json</c>.
    /// </summary>
    /// <returns>True, если запись выполнена успешно; иначе false.</returns>
    private bool SaveRegistry()
    {
        try
        {
            Directory.CreateDirectory(PlatformPaths.AppDataDirectory);
            var registry = new ProfileRegistry
            {
                SchemaVersion = RegistrySchemaVersion,
                LastProfileId = _lastProfileId,
                Profiles = _profiles.ToList()
            };
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(registry, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            // Ошибку больше не подавляем молча: фиксируем её в журнале. Возвращаем false,
            // чтобы вызывающий код (например, удаление профиля) мог откатить изменения
            // и сообщить пользователю, — иначе расхождение между реестром и диском останется
            // незамеченным до следующего запуска.
            _logger.Error($"Не удалось сохранить реестр профилей ({RegistryPath}): {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Хэширует пароль алгоритмом PBKDF2-SHA256 с случайной солью. Формат результата:
    /// «<c>итерации.сольBase64.хэшBase64</c>». Соль и число итераций хранятся вместе с хэшем,
    /// поэтому для проверки достаточно самого хэша.
    /// </summary>
    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPasswordHash(string password, string stored)
    {
        try
        {
            var parts = stored.Split('.');
            if (parts.Length != 3)
                return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Модель файла-реестра <c>profiles.json</c>.</summary>
    private sealed class ProfileRegistry
    {
        public int SchemaVersion { get; set; }
        public string? LastProfileId { get; set; }
        public List<UserProfile> Profiles { get; set; } = new();
    }
}