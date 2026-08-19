using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис поиска установленных версий платформы 1С:Предприятие.
/// </summary>
public static class PlatformVersionService
{
    private static readonly object _extraRootsLock = new();
    private static List<string> _extraSearchRoots = new();

    public static void SetAdditionalSearchPaths(IEnumerable<string>? paths)
    {
        lock (_extraRootsLock)
        {
            _extraSearchRoots = paths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }
    }

    public static IReadOnlyList<string> GetAdditionalSearchPaths()
    {
        lock (_extraRootsLock)
        {
            return _extraSearchRoots.ToList();
        }
    }

    public static List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
        => FindInstalledVersionInfos(additionalPaths).Select(v => v.Display).ToList();

    /// <summary>
    /// Возвращает установленные версии платформы вместе с путями к каталогам версий.
    /// </summary>
    public static List<Models.PlatformVersionInfo> FindInstalledVersionInfos(IEnumerable<string>? additionalPaths = null)
    {
        // Display → Path (при дубликатах оставляем первый найденный путь)
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddVersionsFromRoot(map, programFiles, "64");
        AddVersionsFromRoot(map, programFilesX86, "32");

        IEnumerable<string> extra;
        if (additionalPaths != null)
        {
            extra = additionalPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        else
        {
            lock (_extraRootsLock)
            {
                extra = _extraSearchRoots.ToList();
            }
        }

        foreach (var path in extra)
            AddVersionsFromCustomPath(map, path, programFiles, programFilesX86);

        return map
            .Select(kv => new Models.PlatformVersionInfo { Display = kv.Key, Path = kv.Value })
            .OrderByDescending(v => v.Display, new VersionComparer())
            .ToList();
    }

    public static IReadOnlyList<string> GetSearchRoots(string architecture)
    {
        var roots = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (architecture == "64" || architecture == "x64")
        {
            if (!string.IsNullOrEmpty(programFiles))
                roots.Add(programFiles);
        }
        else
        {
            if (!string.IsNullOrEmpty(programFilesX86))
                roots.Add(programFilesX86);
            if (roots.Count == 0 && !string.IsNullOrEmpty(programFiles))
                roots.Add(programFiles);
        }

        lock (_extraRootsLock)
        {
            foreach (var p in _extraSearchRoots)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var root = ResolveRootFromCustomPath(p);
                if (!string.IsNullOrEmpty(root) && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    roots.Add(root);
            }
        }

        return roots;
    }

    /// <summary>
    /// Разрешает каталог <c>bin</c> указанной версии платформы нужной разрядности.
    /// Использует те же правила поиска, что и при составлении списка версий
    /// (стандартные Program Files + дополнительные папки с учётом нестандартной
    /// вложенности каталогов версий). Возвращает null, если версия не найдена.
    /// </summary>
    public static string? ResolveVersionBinDirectory(string version, string architecture)
    {
        ParseVariant(version ?? string.Empty, out var cleanVersion, out _);
        if (string.IsNullOrWhiteSpace(cleanVersion))
            return null;

        var archKey = architecture == "64" ? "64" : "32";

        foreach (var root in GetSearchRoots(archKey))
        {
            // Стандартный макет: <root>\1cv8\<ver>\bin
            var standard = Path.Combine(root, "1cv8", cleanVersion, "bin");
            if (IsVersionBin(standard, archKey))
                return standard;

            // Нестандартный макет (дополнительные папки): рекурсивный поиск каталога версии.
            var flexible = FindVersionBinRecursive(root, cleanVersion, archKey, depth: 0, maxDepth: 6);
            if (flexible != null)
                return flexible;
        }

        return null;
    }

    /// <summary>
    /// Возвращает все найденные каталоги версий нужной разрядности в виде пар
    /// (имя версии, каталог bin), включая стандартные и дополнительные корни с учётом
    /// нестандартной вложенности. Используется лаунчером для выбора новейшей версии.
    /// </summary>
    public static List<(string Version, string BinDir)> FindPlatformVersionDirs(string architecture)
    {
        var archKey = architecture == "64" ? "64" : "32";
        var results = new List<(string Version, string BinDir)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSearchRoots(archKey))
        {
            AddVersionDirsFrom1cv8(results, seen, Path.Combine(root, "1cv8"), archKey);
            ScanAddVersionDirs(results, seen, root, archKey, depth: 0, maxDepth: 6);
        }

        return results;
    }

    private static bool IsVersionBin(string binDir, string archKey)
    {
        if (!Directory.Exists(binDir))
            return false;
        if (!Directory.EnumerateFiles(binDir, "1cv8*.exe").Any())
            return false;
        return archKey == "32" ? DetectArchitecture(binDir) == "32"
                               : DetectArchitecture(binDir) == "64";
    }

    private static string? FindVersionBinRecursive(string path, string version, string archKey, int depth, int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return null;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (string.Equals(name, version, StringComparison.OrdinalIgnoreCase))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (IsVersionBin(binDir, archKey))
                        return binDir;
                }

                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("common", StringComparison.OrdinalIgnoreCase))
                    continue;

                var sub = FindVersionBinRecursive(dir, version, archKey, depth + 1, maxDepth);
                if (sub != null)
                    return sub;
            }
        }
        catch
        {
            /* нет доступа */
        }

        return null;
    }

    private static void AddVersionDirsFrom1cv8(
        List<(string Version, string BinDir)> results,
        HashSet<string> seen,
        string baseDir,
        string archKey)
    {
        if (!Directory.Exists(baseDir))
            return;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!IsVersionDirectory(name))
                continue;

            var binDir = Path.Combine(dir, "bin");
            if (!IsVersionBin(binDir, archKey))
                continue;

            if (seen.Add(binDir))
                results.Add((name, binDir));
        }
    }

    private static void ScanAddVersionDirs(
        List<(string Version, string BinDir)> results,
        HashSet<string> seen,
        string path,
        string archKey,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (IsVersionDirectory(name))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (IsVersionBin(binDir, archKey) && seen.Add(binDir))
                        results.Add((name, binDir));
                }

                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("common", StringComparison.OrdinalIgnoreCase))
                    continue;

                ScanAddVersionDirs(results, seen, dir, archKey, depth + 1, maxDepth);
            }
        }
        catch
        {
            /* нет доступа */
        }
    }

    private static void TryAddVersion(Dictionary<string, string> map, string display, string versionDir)
    {
        if (!map.ContainsKey(display))
            map[display] = versionDir;
    }

    private static void AddVersionsFromRoot(Dictionary<string, string> map, string? root, string architecture)
    {
        if (string.IsNullOrEmpty(root))
            return;

        var baseDir = Path.Combine(root, "1cv8");
        if (!Directory.Exists(baseDir))
            return;

        AddVersionsFrom1cv8Dir(map, baseDir, architecture);
    }

    private static void AddVersionsFromCustomPath(
        Dictionary<string, string> map,
        string path,
        string? programFiles,
        string? programFilesX86)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        string? preferredArch = null;
        if (!string.IsNullOrEmpty(programFilesX86) &&
            path.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
            preferredArch = "32";
        else if (!string.IsNullOrEmpty(programFiles) &&
                 path.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            preferredArch = "64";

        // E:\1cPlatform\1cv8\8.3.x  или  E:\1cPlatform\8.3.x
        var oneCDir = Path.Combine(path, "1cv8");
        if (Directory.Exists(oneCDir))
        {
            AddVersionsFrom1cv8Dir(map, oneCDir, preferredArch);
            // также рекурсивно — на случай вложенных копий
            ScanForVersionDirectories(map, path, preferredArch, depth: 0, maxDepth: 4);
            return;
        }

        if (HasVersionSubdirectories(path) ||
            string.Equals(Path.GetFileName(path), "1cv8", StringComparison.OrdinalIgnoreCase))
        {
            AddVersionsFrom1cv8Dir(map, path, preferredArch);
            ScanForVersionDirectories(map, path, preferredArch, depth: 0, maxDepth: 3);
            return;
        }

        var binDir = Path.Combine(path, "bin");
        if (Directory.Exists(binDir) && IsVersionDirectory(Path.GetFileName(path)))
        {
            var arch = preferredArch ?? DetectArchitecture(binDir);
            if (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                File.Exists(Path.Combine(binDir, "1cv8x64.exe")))
            {
                TryAddVersion(map, FormatVariant(Path.GetFileName(path), arch), path);
            }
            return;
        }

        // Произвольный корень (например E:\1cPlatform) — ищем версии рекурсивно
        ScanForVersionDirectories(map, path, preferredArch, depth: 0, maxDepth: 5);
    }

    /// <summary>
    /// Рекурсивный поиск каталогов вида 8.3.x.x с bin\1cv8.exe (нестандартные корни установки).
    /// </summary>
    private static void ScanForVersionDirectories(
        Dictionary<string, string> map,
        string path,
        string? preferredArchitecture,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth || !Directory.Exists(path))
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal))
                    continue;

                if (IsVersionDirectory(name))
                {
                    var binDir = Path.Combine(dir, "bin");
                    if (Directory.Exists(binDir) &&
                        (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                         File.Exists(Path.Combine(binDir, "1cv8x64.exe"))))
                    {
                        var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                        TryAddVersion(map, FormatVariant(name, arch), dir);
                    }
                }

                // не заходим в bin/docs и т.п.
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("readme", StringComparison.OrdinalIgnoreCase))
                    continue;

                ScanForVersionDirectories(map, dir, preferredArchitecture, depth + 1, maxDepth);
            }
        }
        catch
        {
            /* нет доступа */
        }
    }

    private static void AddVersionsFrom1cv8Dir(Dictionary<string, string> map, string baseDir, string? preferredArchitecture)
    {
        if (!Directory.Exists(baseDir))
            return;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (!IsVersionDirectory(name))
                continue;

            var binDir = Path.Combine(dir, "bin");
            if (!Directory.Exists(binDir))
                continue;

            if (File.Exists(Path.Combine(binDir, "1cv8.exe")) ||
                File.Exists(Path.Combine(binDir, "1cv8x64.exe")))
            {
                var arch = preferredArchitecture ?? DetectArchitecture(binDir);
                TryAddVersion(map, FormatVariant(name, arch), dir);
            }
        }
    }

    /// <summary>
    /// Определяет разрядность по имени exe и PE-заголовку (Machine).
    /// </summary>
    private static string DetectArchitecture(string binDir)
    {
        var x64 = Path.Combine(binDir, "1cv8x64.exe");
        if (File.Exists(x64))
            return "64";

        var exe = Path.Combine(binDir, "1cv8.exe");
        if (!File.Exists(exe))
            return "64";

        try
        {
            using var fs = File.OpenRead(exe);
            if (fs.Length < 0x40) return "64";
            using var br = new BinaryReader(fs);
            // MZ
            if (br.ReadUInt16() != 0x5A4D) return "64";
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset + 6 >= fs.Length) return "64";
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return "64"; // PE\0\0
            var machine = br.ReadUInt16();
            // IMAGE_FILE_MACHINE_I386 = 0x14c, AMD64 = 0x8664
            if (machine == 0x014c) return "32";
            if (machine == 0x8664) return "64";
        }
        catch
        {
            /* ignore */
        }

        return "64";
    }

    private static bool HasVersionSubdirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path).Any(d => IsVersionDirectory(Path.GetFileName(d)));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveRootFromCustomPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        if (Directory.Exists(Path.Combine(path, "1cv8")))
            return path;

        if (string.Equals(Path.GetFileName(path), "1cv8", StringComparison.OrdinalIgnoreCase) ||
            HasVersionSubdirectories(path))
        {
            var parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent) ? path : parent;
        }

        if (IsVersionDirectory(Path.GetFileName(path)) &&
            Directory.Exists(Path.Combine(path, "bin")))
        {
            var oneC = Path.GetDirectoryName(path);
            var root = string.IsNullOrEmpty(oneC) ? null : Path.GetDirectoryName(oneC);
            return root ?? oneC ?? path;
        }

        return path;
    }

    public static string FormatVariant(string version, string architecture)
        => $"{version} ({architecture})";

    public static void ParseVariant(string variant, out string version, out string architecture)
    {
        version = variant;
        architecture = "32";

        if (string.IsNullOrWhiteSpace(variant))
            return;

        var end = variant.LastIndexOf(')');
        var start = variant.LastIndexOf('(');
        if (end < 0 || start < 0 || start > end)
            return;

        var arch = variant.Substring(start + 1, end - start - 1).Trim();
        if (arch == "64" || arch == "32")
        {
            version = variant.Substring(0, start).Trim();
            architecture = arch;
        }
    }

    /// <summary>
    /// Линия платформы — первые два числа версии: «8.3.27.1688 (64)» → «8.3».
    /// </summary>
    public static string GetVersionLine(string variant)
    {
        ParseVariant(variant, out var version, out _);
        var parts = version.Split('.');
        return parts.Length >= 2
            ? string.Join(".", parts.Take(2))
            : (string.IsNullOrEmpty(version) ? "—" : version);
    }

    /// <summary>
    /// Группа сборки — первые три числа: «8.3.27.1688 (64)» → «8.3.27».
    /// </summary>
    public static string GetVersionBuildGroup(string variant)
    {
        ParseVariant(variant, out var version, out _);
        var parts = version.Split('.');
        return parts.Length >= 3
            ? string.Join(".", parts.Take(3))
            : GetVersionLine(variant);
    }

    /// <summary>
    /// Подпись разрядности в стиле стартера 1С: «x64» / «x32».
    /// </summary>
    public static string FormatArchitectureLabel(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture)) return "";
        var a = architecture.Trim();
        if (a is "64" or "x64" or "X64") return "x64";
        if (a is "32" or "x32" or "X32" or "x86") return "x32";
        return a;
    }

    /// <summary>
    /// Дерево выбора платформы (как в стартере 1С):
    /// линия (8.3) → группа сборок (8.3.27) → полная версия «8.3.27.2214 (x64)».
    /// </summary>
    public static List<Models.PlatformVersionGroup> BuildGroupedTree(
        IEnumerable<Models.PlatformVersionInfo> infos)
    {
        var list = infos?.ToList() ?? new List<Models.PlatformVersionInfo>();
        var roots = new List<Models.PlatformVersionGroup>();

        var byLine = list
            .GroupBy(i => GetVersionLine(i.Display))
            .OrderByDescending(g => g.Key, new VersionComparer());

        foreach (var lineGroup in byLine)
        {
            var lineNode = new Models.PlatformVersionGroup { Name = lineGroup.Key, Kind = Models.PlatformNodeKind.Line };

            var byBuild = lineGroup
                .GroupBy(i => GetVersionBuildGroup(i.Display))
                .OrderByDescending(g => g.Key, new VersionComparer());

            foreach (var buildGroup in byBuild)
            {
                var buildNode = new Models.PlatformVersionGroup { Name = buildGroup.Key, Kind = Models.PlatformNodeKind.BuildGroup };

                foreach (var info in buildGroup.OrderByDescending(i => i.Display, new VersionComparer()))
                {
                    ParseVariant(info.Display, out var version, out var arch);
                    var archLabel = FormatArchitectureLabel(arch);
                    var leafName = string.IsNullOrEmpty(archLabel)
                        ? version
                        : $"{version} ({archLabel})";

                    var leafKind = archLabel switch
                    {
                        "x64" => Models.PlatformNodeKind.LeafX64,
                        "x32" => Models.PlatformNodeKind.LeafX32,
                        _ => Models.PlatformNodeKind.Leaf
                    };
                    buildNode.Children.Add(new Models.PlatformVersionGroup
                    {
                        Name = leafName,
                        Path = info.Path,
                        Variant = info.Display,
                        Kind = leafKind,
                        Versions = { info }
                    });
                    buildNode.Versions.Add(info);
                }

                lineNode.Children.Add(buildNode);
                lineNode.Versions.AddRange(buildNode.Versions);
            }

            roots.Add(lineNode);
        }

        return roots;
    }

    private static bool IsVersionDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var parts = name.Split('.');
        if (parts.Length < 3 || parts.Length > 4)
            return false;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
                return false;
        }

        return true;
    }

    private sealed class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var result = CompareCore(x, y);
            if (result != 0)
                return result;

            return GetArch(x).CompareTo(GetArch(y));
        }

        private static int CompareCore(string x, string y)
        {
            var xParts = CleanVersion(x).Split('.').Select(int.Parse).ToArray();
            var yParts = CleanVersion(y).Split('.').Select(int.Parse).ToArray();

            var length = Math.Max(xParts.Length, yParts.Length);
            for (var i = 0; i < length; i++)
            {
                var xVal = i < xParts.Length ? xParts[i] : 0;
                var yVal = i < yParts.Length ? yParts[i] : 0;
                if (xVal != yVal)
                    return xVal.CompareTo(yVal);
            }

            return 0;
        }

        private static string CleanVersion(string variant)
        {
            ParseVariant(variant, out var version, out _);
            return version;
        }

        private static int GetArch(string variant)
        {
            ParseVariant(variant, out _, out var architecture);
            return architecture == "64" ? 1 : 0;
        }
    }
}
