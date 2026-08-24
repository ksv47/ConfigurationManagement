using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Резервное копирование и восстановление «профиля» приложения в произвольный каталог.
///
/// Профиль включает всё, что нужно, чтобы после переустановки системы сразу получить
/// привычно настроенное приложение:
///   • settings.json      — настройки интерфейса и поведения;
///   • infobases.json     — список информационных баз вместе с пользователями и паролями
///                          для запуска (модель <c>InfobaseAuthSettings</c> хранится здесь);
///   • groups.json        — дерево групп;
///   • ibases.v8i         — стандартный файл списка баз 1С (если найден).
///
/// Файлы копируются поимённо (не весь каталог), чтобы не тащить логи и прочие служебные
/// файлы. Восстановление выполняется тем же набором файлов обратно в каталог данных.
/// </summary>
public static class ProfileBackupService
{
    /// <summary>
    /// Имена файлов профиля, которые хранятся в каталоге данных приложения
    /// (<see cref="PlatformPaths.AppDataDirectory"/>) и копируются в резервный каталог.
    /// </summary>
    private static readonly string[] DataFileNames =
    {
        "settings.json",
        "infobases.json",
        "groups.json"
    };

    /// <summary>Имя файла списка баз 1С.</summary>
    public const string IbasesFileName = "ibases.v8i";

    /// <summary>
    /// Определяет путь к файлу ibases.v8i, который входит в профиль: пользовательский путь
    /// из настроек, если задан, иначе стандартный путь 1С.
    /// </summary>
    /// <param name="configuredPath">Путь, заданный пользователем в настройках (может быть пустым).</param>
    /// <returns>Полный путь к файлу или пустая строка, если файл определить/найти нельзя.</returns>
    public static string ResolveIbasesSourcePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var defaultPath = IbasesV8iImporter.FindDefaultPath();
        return string.IsNullOrWhiteSpace(defaultPath) ? string.Empty : defaultPath;
    }

    /// <summary>
    /// Проверяет, есть ли в каталоге резервная копия профиля (хотя бы один файл данных).
    /// </summary>
    public static bool HasBackup(string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            return false;

        foreach (var name in DataFileNames)
        {
            if (File.Exists(Path.Combine(backupDirectory, name)))
                return true;
        }
        return File.Exists(Path.Combine(backupDirectory, IbasesFileName));
    }

    /// <summary>
    /// Копирует текущий профиль приложения в указанный каталог.
    /// Каталог создаётся при необходимости. Существующие файлы перезаписываются.
    /// </summary>
    /// <param name="backupDirectory">Каталог, куда сохранить копию профиля.</param>
    /// <param name="configuredIbasesPath">
    /// Путь к файлу ibases.v8i из настроек (может быть пустым). Если задан и существует —
    /// копируется он, иначе стандартный файл 1С.
    /// </param>
    /// <returns>Число скопированных файлов.</returns>
    public static int Backup(string backupDirectory, string? configuredIbasesPath = null)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            throw new ArgumentException("Не указан каталог резервной копии профиля.", nameof(backupDirectory));

        Directory.CreateDirectory(backupDirectory);
        var copied = 0;

        // Файлы каталога данных приложения.
        foreach (var name in DataFileNames)
        {
            var source = Path.Combine(PlatformPaths.AppDataDirectory, name);
            if (!File.Exists(source))
                continue;
            File.Copy(source, Path.Combine(backupDirectory, name), overwrite: true);
            copied++;
        }

        // Файл списка баз 1С (стандартный или пользовательский путь).
        var ibasesSource = ResolveIbasesSourcePath(configuredIbasesPath);
        if (!string.IsNullOrWhiteSpace(ibasesSource) && File.Exists(ibasesSource))
        {
            File.Copy(ibasesSource, Path.Combine(backupDirectory, IbasesFileName), overwrite: true);
            copied++;
        }

        return copied;
    }

    /// <summary>
    /// Восстанавливает профиль из указанного каталога обратно в каталог данных приложения,
    /// а файл ibases.v8i — в стандартное (или заданное пользователем) место 1С.
    /// </summary>
    /// <param name="backupDirectory">Каталог с резервной копией профиля.</param>
    /// <param name="configuredIbasesPath">
    /// Целевой путь к файлу ibases.v8i из настроек. Если пуст — восстанавливается в стандартное
    /// место 1С (<see cref="IbasesV8iImporter.FindDefaultPath"/>).
    /// </param>
    /// <returns>Число восстановленных файлов.</returns>
    public static int Restore(string backupDirectory, string? configuredIbasesPath = null)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
            throw new ArgumentException("Не указан каталог резервной копии профиля.", nameof(backupDirectory));
        if (!Directory.Exists(backupDirectory))
            throw new DirectoryNotFoundException(
                "Каталог резервной копии профиля не найден: " + backupDirectory);

        var restored = 0;

        // Файлы каталога данных приложения.
        foreach (var name in DataFileNames)
        {
            var source = Path.Combine(backupDirectory, name);
            if (!File.Exists(source))
                continue;
            var targetDir = PlatformPaths.AppDataDirectory;
            Directory.CreateDirectory(targetDir);
            File.Copy(source, Path.Combine(targetDir, name), overwrite: true);
            restored++;
        }

        // Файл списка баз 1С — обратно в его целевое место.
        var ibasesSource = Path.Combine(backupDirectory, IbasesFileName);
        if (File.Exists(ibasesSource))
        {
            var ibasesTarget = ResolveIbasesTargetPath(configuredIbasesPath);
            if (!string.IsNullOrWhiteSpace(ibasesTarget))
            {
                var targetDir = Path.GetDirectoryName(ibasesTarget);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);
                File.Copy(ibasesSource, ibasesTarget, overwrite: true);
                restored++;
            }
        }

        return restored;
    }

    /// <summary>Целевой путь для файла ibases.v8i при восстановлении.</summary>
    private static string ResolveIbasesTargetPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var defaultPath = IbasesV8iImporter.FindDefaultPath();
        return string.IsNullOrWhiteSpace(defaultPath) ? string.Empty : defaultPath;
    }
}