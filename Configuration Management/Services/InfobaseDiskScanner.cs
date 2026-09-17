using System;
using System.Collections.Generic;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис поиска потерянных и забытых файловых баз 1С 8 на дисках компьютера
/// (issue #247). Чистый, без UI: перечисляет корни поиска (диски Windows /
/// точки монтирования Linux) и рекурсивно ищет файлы <c>1Cv8.1CD</c> с
/// обработкой отказов доступа, поддержкой отмены через <see cref="CancellationToken"/>
/// и дедупликацией путей. Используется обеими платформами (WPF и Avalonia).
/// </summary>
public static class InfobaseDiskScanner
{
    private const string DbFileName = "1Cv8.1CD";

    /// <summary>
    /// Перечисляет корни поиска баз:
    /// Windows — готовые логические диски; Linux — реальные точки монтирования
    /// из /proc/mounts (без виртуальных/сетевых файловых систем).
    /// </summary>
    public static IReadOnlyList<string> EnumerateSearchRoots()
    {
#if LINUX
        return EnumerateLinuxRoots();
#else
        return EnumerateWindowsRoots();
#endif
    }

    /// <summary>
    /// Выполняет рекурсивный поиск файлов <c>1Cv8.1CD</c> в заданных корнях.
    /// Работает синхронно в вызывающем (фоновом) потоке; UI-код оборачивает вызов
    /// в <see cref="System.Threading.Tasks.Task.Run{TResult}"/>.
    /// </summary>
    /// <param name="roots">Корни поиска (непустые пути к каталогам).</param>
    /// <param name="cancellationToken">Токен отмены («Прекратить»).</param>
    /// <param name="progress">Обратный вызов прогресса: (найдено, просмотрено каталогов).</param>
    /// <param name="onFound">Обратный вызов для каждой найденной базы; вызывается сразу
    /// при обнаружении, до завершения всего сканирования.</param>
    /// <returns>Список найденных баз без дубликатов по нормализованному пути.</returns>
    public static List<FoundFileBase> Scan(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken,
        Action<int, int>? progress = null,
        Action<FoundFileBase>? onFound = null)
    {
        var ctx = new ScanContext(cancellationToken, progress, onFound);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var fullRoot = GetNormalizedFullPath(root);
            if (string.IsNullOrEmpty(fullRoot) || !Directory.Exists(fullRoot))
                continue;

            Walk(fullRoot, ctx);
        }

        return ctx.Results;
    }

    // ---- Перечисление корней -------------------------------------------------

    private static IReadOnlyList<string> EnumerateWindowsRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                        continue;
                    var full = GetNormalizedFullPath(drive.RootDirectory.FullName);
                    if (!string.IsNullOrEmpty(full))
                        roots.Add(full);
                }
                catch
                {
                    // Недоступный диск пропускаем.
                }
            }
        }
        catch
        {
            // Не удалось перечислить диски — вернём пустой список.
        }
        return roots;
    }

    private static IReadOnlyList<string> EnumerateLinuxRoots()
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                var mountPoint = parts[1];
                var fsType = parts[2];

                if (IsSkippedFilesystem(fsType))
                    continue;
                if (IsSkippedPath(mountPoint))
                    continue;

                var full = GetNormalizedFullPath(UnescapeLinuxPath(mountPoint));
                if (!string.IsNullOrEmpty(full) && Directory.Exists(full))
                    roots.Add(full);
            }
        }
        catch
        {
            // Нет /proc/mounts — вернём только корень.
        }

        // Как минимум корневая ФС всегда включается.
        var rootFs = GetNormalizedFullPath("/");
        if (!string.IsNullOrEmpty(rootFs))
            roots.Add(rootFs);

        return new List<string>(roots);
    }

    /// <summary>
    /// Виртуальные и сетевые файловые системы, которые не следует сканировать
    /// (issue #247, §7). Фильтр по типу ФС из /proc/mounts.
    /// </summary>
    private static bool IsSkippedFilesystem(string fsType)
    {
        switch (fsType)
        {
            case "proc":
            case "sysfs":
            case "devtmpfs":
            case "devpts":
            case "tmpfs":
            case "ramfs":
            case "cgroup":
            case "cgroup2":
            case "securityfs":
            case "debugfs":
            case "tracefs":
            case "fusectl":
            case "configfs":
            case "pstore":
            case "binfmt_misc":
            case "autofs":
            case "mqueue":
            case "hugetlbfs":
            case "overlay":
            case "squashfs":
            case "fuse":
            case "nfs":
            case "nfs4":
            case "smbfs":
            case "cifs":
            case "9p":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Пропускает системные точки монтирования (защитная мера даже если тип ФС
    /// не распознан — например, на виртуализации без фильтра по fstype).
    /// </summary>
    private static bool IsSkippedPath(string path)
    {
        return path == "/proc" || path.StartsWith("/proc/", StringComparison.Ordinal)
            || path == "/sys" || path.StartsWith("/sys/", StringComparison.Ordinal)
            || path == "/dev" || path.StartsWith("/dev/", StringComparison.Ordinal)
            || path == "/run" || path.StartsWith("/run/", StringComparison.Ordinal);
    }

    private static string UnescapeLinuxPath(string path)
        => path.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\");

    // ---- Рекурсивный обход ---------------------------------------------------

    private static void Walk(string directory, ScanContext ctx)
    {
        if (ctx.Token.IsCancellationRequested)
            throw new OperationCanceledException(ctx.Token);

        ctx.DirsScanned++;
        ReportProgress(ctx);

        if (ShouldSkipDirectory(directory))
            return;

        // Ищем 1Cv8.1CD прямо в текущем каталоге (TopDirectoryOnly), чтобы
        // отказ доступа к подкаталогу не прерывал обход текущего.
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, DbFileName, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (IsAccessException(ex))
        {
            ctx.SkippedDirs++;
            return;
        }

        foreach (var file in files)
        {
            if (ctx.Token.IsCancellationRequested)
                throw new OperationCanceledException(ctx.Token);

            try
            {
                var key = GetNormalizedFullPath(file);
                if (key is null || !ctx.Seen.Add(key))
                    continue; // Дубликат (симлинк / повторный корень).

                var info = new FileInfo(file);
                var found = new FoundFileBase
                {
                    DbFilePath = file,
                    DirectoryPath = Path.GetDirectoryName(file) ?? directory,
                    SizeBytes = info.Length,
                    LastWriteTime = info.LastWriteTime
                };
                ctx.Results.Add(found);
                ctx.OnFound?.Invoke(found);
                ctx.FoundCount++;
                ReportProgress(ctx);
            }
            catch (Exception ex) when (IsAccessException(ex))
            {
                ctx.SkippedDirs++;
            }
        }

        // Рекурсивный обход подкаталогов с индивидуальной обработкой отказов.
        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (IsAccessException(ex))
        {
            ctx.SkippedDirs++;
            return;
        }

        foreach (var sub in subdirs)
            Walk(sub, ctx);
    }

    private static bool ShouldSkipDirectory(string directory)
    {
#if !LINUX
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
            return true;
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir) && string.Equals(directory, winDir, StringComparison.OrdinalIgnoreCase))
            return true;
#endif
        return false;
    }

    private static bool IsAccessException(Exception ex)
        => ex is UnauthorizedAccessException
            or IOException
            or DirectoryNotFoundException
            or System.Security.SecurityException;

    private static void ReportProgress(ScanContext ctx)
        => ctx.Progress?.Invoke(ctx.FoundCount, ctx.DirsScanned);

    private static string? GetNormalizedFullPath(string path)
    {
        try
        {
            // Не обрезаем завершающий разделитель: у корня «C:\» его удаление дало бы
            // некорректный путь «C:». Полный канонический путь достаточен для дедупликации
            // и проверки существования.
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Внутреннее состояние одного прохода сканирования.</summary>
    private sealed class ScanContext
    {
        public ScanContext(CancellationToken token, Action<int, int>? progress, Action<FoundFileBase>? onFound)
        {
            Token = token;
            Progress = progress;
            OnFound = onFound;
        }

        public CancellationToken Token { get; }
        public Action<int, int>? Progress { get; }
        public Action<FoundFileBase>? OnFound { get; }
        public List<FoundFileBase> Results { get; } = new();
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int FoundCount;
        public int DirsScanned;
        public int SkippedDirs;
    }
}