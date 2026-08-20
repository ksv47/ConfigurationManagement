#if LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    // ========================================================================
    // Linux-версии статических сервисов 1С, на которые опираются портированные
    // диалоговые окна (Этап 3). WPF-реализации (PlatformVersionService.cs,
    // OneCLauncher.cs, InfobaseMaintenanceService.cs) исключены из Linux-сборки,
    // поэтому здесь предоставляются функциональные Linux-аналоги минимального
    // набора методов, используемых окнами. Полный порт платформенных сервисов —
    // Этап 5.
    // ========================================================================

    /// <summary>Поиск и разбор установленных версий платформы 1С (Linux).</summary>
    public static class PlatformVersionService
    {
        private static List<string> _additionalPaths = new();

        public static void SetAdditionalSearchPaths(IEnumerable<string>? paths)
        {
            _additionalPaths = paths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public static IReadOnlyList<string> GetAdditionalSearchPaths() => _additionalPaths;

        /// <summary>Корни установки платформы 1С на Linux.</summary>
        private static IEnumerable<string> GetInstallRoots(string architecture)
        {
            var roots = new List<string>
            {
                "/opt/1cv8", "/usr/local/1cv8", "/opt/1cv8/x86_64", "/opt/1cv8/i386"
            };

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                roots.Add(Path.Combine(home, ".1cv8"));
                roots.Add(Path.Combine(home, "1cv8"));
                roots.Add(Path.Combine(home, ".local", "share", "1cv8"));
            }

            roots.AddRange(_additionalPaths);
            return roots.Where(Directory.Exists);
        }

        /// <summary>Находит установленные версии платформы 1С на Linux (по каталогам версий).</summary>
        public static List<PlatformVersionInfo> FindInstalledVersionInfos(IEnumerable<string>? additionalPaths = null)
        {
            var result = new List<PlatformVersionInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (additionalPaths != null)
            {
                foreach (var p in additionalPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
                    ScanVersionDir(p, result, seen);
            }

            foreach (var root in GetInstallRoots("64"))
                ScanVersionDir(root, result, seen);

            return result
                .OrderByDescending(v => v.Display, new VersionDisplayComparer())
                .ToList();
        }

        private static void ScanVersionDir(string root, List<PlatformVersionInfo> result, HashSet<string> seen)
        {
            if (!Directory.Exists(root))
                return;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var name = Path.GetFileName(dir);
                    if (!LooksLikeVersion(name))
                        continue;
                    if (!seen.Add(name))
                        continue;

                    var arch = DetectArchitecture(dir);
                    result.Add(new PlatformVersionInfo
                    {
                        Display = FormatVariant(name, arch),
                        Path = dir
                    });
                }
            }
            catch
            {
                // Игнорируем недоступные каталоги.
            }
        }

        private static bool LooksLikeVersion(string name)
        {
            var parts = name.Split('.');
            return parts.Length >= 3
                && parts.All(p => int.TryParse(p, out _));
        }

        private static string DetectArchitecture(string dir)
        {
            if (dir.Contains("i386", StringComparison.OrdinalIgnoreCase) ||
                dir.Contains("x86-32", StringComparison.OrdinalIgnoreCase))
                return "32";
            return "64";
        }

        public static List<string> FindInstalledVersions(IEnumerable<string>? additionalPaths = null)
            => FindInstalledVersionInfos(additionalPaths).Select(v => v.Display).ToList();

        /// <summary>Корни поиска исполняемого файла 1cv8 для указанной разрядности.</summary>
        public static IReadOnlyList<string> GetSearchRoots(string architecture)
        {
            var list = new List<string>();
            foreach (var root in GetInstallRoots(architecture))
            {
                list.Add(Path.Combine(root, "bin"));
            }
            return list;
        }

        /// <summary>Каталог bin для версии (если найден), иначе null.</summary>
        public static string? ResolveVersionBinDirectory(string version, string architecture)
        {
            foreach (var root in GetInstallRoots(architecture))
            {
                var bin = Path.Combine(root, version, "bin");
                if (Directory.Exists(bin))
                    return bin;
            }
            return null;
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

        /// <summary>Линия платформы: «8.3.27.1688 (64)» → «8.3».</summary>
        public static string GetVersionLine(string variant)
        {
            ParseVariant(variant, out var version, out _);
            var parts = version.Split('.');
            return parts.Length >= 2
                ? string.Join(".", parts.Take(2))
                : (string.IsNullOrEmpty(version) ? "—" : version);
        }

        /// <summary>Группа сборки: «8.3.27.1688 (64)» → «8.3.27».</summary>
        public static string GetVersionBuildGroup(string variant)
        {
            ParseVariant(variant, out var version, out _);
            var parts = version.Split('.');
            return parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : GetVersionLine(variant);
        }

        /// <summary>Подпись разрядности в стиле стартера 1С: «x64» / «x32».</summary>
        public static string FormatArchitectureLabel(string? architecture)
        {
            if (string.IsNullOrWhiteSpace(architecture)) return "";
            var a = architecture.Trim();
            if (a is "64" or "x64" or "X64") return "x64";
            if (a is "32" or "x32" or "X32" or "x86") return "x32";
            return a;
        }

        /// <summary>Дерево выбора платформы: линия (8.3) → группа сборок (8.3.27) → версия.</summary>
        public static List<PlatformVersionGroup> BuildGroupedTree(IEnumerable<PlatformVersionInfo> infos)
        {
            var list = infos?.ToList() ?? new List<PlatformVersionInfo>();
            var roots = new List<PlatformVersionGroup>();

            var byLine = list
                .GroupBy(i => GetVersionLine(i.Display))
                .OrderByDescending(g => g.Key, new VersionDisplayComparer());

            foreach (var lineGroup in byLine)
            {
                var lineNode = new PlatformVersionGroup { Name = lineGroup.Key, Kind = PlatformNodeKind.Line };

                var byBuild = lineGroup
                    .GroupBy(i => GetVersionBuildGroup(i.Display))
                    .OrderByDescending(g => g.Key, new VersionDisplayComparer());

                foreach (var buildGroup in byBuild)
                {
                    var buildNode = new PlatformVersionGroup { Name = buildGroup.Key, Kind = PlatformNodeKind.BuildGroup };

                    foreach (var info in buildGroup.OrderByDescending(i => i.Display, new VersionDisplayComparer()))
                    {
                        var leaf = new PlatformVersionGroup
                        {
                            Name = info.Display,
                            Variant = info.Display,
                            Path = info.Path,
                            Kind = ParseVariantKind(info.Display),
                            Versions = { info }
                        };
                        buildNode.Children.Add(leaf);
                    }
                    lineNode.Children.Add(buildNode);
                }
                roots.Add(lineNode);
            }

            return roots;
        }

        private static PlatformNodeKind ParseVariantKind(string variant)
        {
            ParseVariant(variant, out _, out var arch);
            return arch == "64" ? PlatformNodeKind.LeafX64
                : arch == "32" ? PlatformNodeKind.LeafX32
                : PlatformNodeKind.Leaf;
        }

        /// <summary>Сравнивает строки версий по старшинству чисел.</summary>
        private sealed class VersionDisplayComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x is null && y is null) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                ParseVariant(x, out var vx, out _);
                ParseVariant(y, out var vy, out _);
                var px = vx.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
                var py = vy.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
                for (var i = 0; i < Math.Max(px.Length, py.Length); i++)
                {
                    var a = i < px.Length ? px[i] : 0;
                    var b = i < py.Length ? py[i] : 0;
                    if (a != b) return a.CompareTo(b);
                }
                return 0;
            }
        }
    }

    /// <summary>Запуск CREATEINFOBASE (Linux): поиск 1cv8 и выполнение команды.</summary>
    public static class OneCLauncher
    {
        /// <summary>Создаёт информационную базу командой CREATEINFOBASE (пустую или из шаблона .cf/.dt).</summary>
        public static (bool Ok, string? Error) CreateInfoBase(
            string platformVersion,
            bool isFile,
            string? filePath,
            string? server,
            string? databaseName,
            string? templatePath = null)
        {
            PlatformVersionService.ParseVariant(platformVersion, out var version, out var arch);
            var exe = FindExecutable(version, arch);
            if (string.IsNullOrEmpty(exe))
            {
                return (false, "Не найден исполняемый файл 1cv8 для указанной версии платформы.");
            }

            string connectionString;
            if (isFile)
            {
                var path = (filePath ?? "").Trim().TrimEnd('\\', '/');
                if (string.IsNullOrEmpty(path))
                    return (false, "Не указан каталог файловой базы.");
                try
                {
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    return (false, $"Не удалось создать каталог:\n{path}\n{ex.Message}");
                }
                connectionString = $"File=\"{path}\"";
            }
            else
            {
                var srv = (server ?? "").Trim();
                var db = (databaseName ?? "").Trim();
                if (string.IsNullOrEmpty(srv) || string.IsNullOrEmpty(db))
                    return (false, "Не указаны сервер или имя базы.");
                connectionString = $"Srvr=\"{srv}\";Ref=\"{db}\"";
            }

            var args = new List<string> { "CREATEINFOBASE", connectionString };
            if (!string.IsNullOrWhiteSpace(templatePath))
                args.Add($"/UseTemplate\"{templatePath}\"");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc is null)
                    return (false, "Не удалось запустить 1cv8 (CREATEINFOBASE).");
                proc.WaitForExit();
                return proc.ExitCode == 0
                    ? (true, null)
                    : (false, $"1cv8 завершился с кодом {proc.ExitCode}.");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка запуска CREATEINFOBASE:\n{ex.Message}");
            }
        }

        /// <summary>Находит исполняемый файл 1cv8 для версии/разрядности (Linux).</summary>
        private static string? FindExecutable(string version, string architecture)
        {
            var bin = PlatformVersionService.ResolveVersionBinDirectory(version, architecture);
            if (bin is null)
                return null;

            var candidates = new[] { "1cv8", "1cv8c" };
            foreach (var name in candidates)
            {
                var path = Path.Combine(bin, name);
                if (File.Exists(path))
                    return path;
            }

            // Системные каталоги: /usr/bin/1cv8 (симлинк) и ~/.1cv8.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var dir in new[]
                     {
                         "/usr/bin",
                         string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".1cv8")
                     })
            {
                if (dir is null) continue;
                foreach (var name in candidates)
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }
    }

    /// <summary>Обслуживание файловых баз (Linux): определение каталога и наличия базы.</summary>
    public static class InfobaseMaintenanceService
    {
        /// <summary>Каталог файловой базы (родитель 1Cv8.1CD или сам путь-каталог).</summary>
        public static string? GetFileBaseDirectory(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return null;
            var path = ib.Connection.FilePath?.Trim() ?? "";
            if (string.IsNullOrEmpty(path))
                return null;
            if (Directory.Exists(path))
                return path;
            if (File.Exists(path))
                return Path.GetDirectoryName(path);
            var parent = Path.GetDirectoryName(path);
            return Directory.Exists(parent) ? parent : null;
        }

        /// <summary>Проверяет наличие файловой базы на диске.</summary>
        public static bool FileBaseExists(Infobase ib)
        {
            if (ib.Connection.Type != ConnectionType.File)
                return true;

            var path = ib.Connection.FilePath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
                return false;

            if (File.Exists(path))
                return true;
            if (Directory.Exists(path))
            {
                if (File.Exists(Path.Combine(path, "1Cv8.1CD")))
                    return true;
                return Directory.EnumerateFiles(path, "1Cv8.1CD", SearchOption.TopDirectoryOnly).Any();
            }
            return false;
        }
    }
}
#endif