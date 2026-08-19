using System.Diagnostics;
using System.IO;

namespace Configuration_Management.Services;

/// <summary>
/// Результат регистрации COM-коннектора 1С.
/// </summary>
public sealed record ComConnectorRegistrationResult(
    bool Success,
    string? PlatformVersion,
    string? BinDirectory,
    bool ProgIdVisible,
    string? VerificationNote,
    IReadOnlyList<ComConnectorRegistrationItem> Items);

/// <summary>
/// Результат регистрации отдельного COM-модуля (DLL).
/// </summary>
public sealed record ComConnectorRegistrationItem(
    string DllPath,
    bool Success,
    string? Error);

/// <summary>
/// Сервис регистрации COM-коннекторов платформы 1С (comcntr.dll / comcntr64.dll).
/// Регистрация выполняется через <c>regsvr32</c> с повышением прав (UAC).
/// Для каждой DLL используется regsvr32 нужной разрядности (System32/SysWOW64),
/// после чего выполняется проверка фактической доступности ProgID V83.COMConnector.
/// </summary>
public interface IOneCComConnectorRegistrar
{
    /// <summary>
    /// Регистрирует COM-коннекторы 1С для указанной версии платформы (или новейшей найденной).
    /// </summary>
    /// <param name="platformVersion">Версия платформы (например «8.3.27.1644») или пусто для автоматического поиска.</param>
    /// <param name="architecture">Разрядность: «32» или «64» (влияет на выбор каталога установки).</param>
    ComConnectorRegistrationResult Register(string? platformVersion, string architecture);
}

public sealed class OneCComConnectorRegistrar : IOneCComConnectorRegistrar
{
    private readonly IAppLogger _logger;

    /// <summary>Имена DLL COM-коннектора 1С в порядке приоритета (64-бит сначала — для 64-битного процесса).</summary>
    private static readonly string[] ComConnectorDllNames =
    {
        "comcntr64.dll",
        "comcntr.dll",
        "comcntr32.dll"
    };

    public OneCComConnectorRegistrar(IAppLogger logger)
    {
        _logger = logger;
    }

    public ComConnectorRegistrationResult Register(string? platformVersion, string architecture)
    {
        // 1. Определяем каталог bin платформы (ищем по обеим разрядностям).
        var (binDir, usedVersion) = FindPlatformBinDir(platformVersion);

        if (binDir == null || !Directory.Exists(binDir))
        {
            var searched = string.Join("\n", EnumerateSearchRoots());
            _logger.Warn($"Каталог платформы 1С не найден (версия={platformVersion}). Проверены корни:\n{searched}");
            return new ComConnectorRegistrationResult(false, usedVersion, null, false,
                $"Платформа 1С не найдена. Проверены каталоги:\n{searched}\n\n" +
                "Если 1С установлена в нестандартную папку — добавьте её путь в «Настройки → Платформы».",
                new List<ComConnectorRegistrationItem>());
        }

        // 2. Регистрируем существующие DLL COM-коннектора.
        var items = new List<ComConnectorRegistrationItem>();
        foreach (var dllName in ComConnectorDllNames)
        {
            var dllPath = Path.Combine(binDir, dllName);
            if (!File.Exists(dllPath))
                continue;

            var is64Bit = Is64BitDll(dllName);
            var (ok, err) = RegisterDll(dllPath, is64Bit);
            items.Add(new ComConnectorRegistrationItem(dllPath, ok, err));
            _logger.Info($"Регистрация COM-коннектора {(ok ? "успешна" : "не удалась")}: {dllPath}" +
                         (err != null ? $" — {err}" : string.Empty));
        }

        // 3. Проверяем фактическую доступность ProgID в текущем процессе (той же разрядности).
        var progIdVisible = Type.GetTypeFromProgID("V83.COMConnector") != null;
        string? note = null;
        if (!progIdVisible && items.Count > 0)
        {
            note = "DLL зарегистрированы, но ProgID V83.COMConnector не виден процессу приложения. " +
                   "Проверьте, что зарегистрирован COM-коннектор разрядности, совпадающей с приложением.";
        }

        var registered = items.Count > 0 && items.Any(i => i.Success);
        var success = progIdVisible || registered;

        return new ComConnectorRegistrationResult(success, usedVersion, binDir, progIdVisible, note, items);
    }

    /// <summary>
    /// Ищет каталог bin платформы 1С: сначала по указанной версии в обеих разрядностях,
    /// затем по любой установленной версии с DLL COM-коннектора (обе разрядности).
    /// </summary>
    private static (string? BinDir, string? Version) FindPlatformBinDir(string? platformVersion)
    {
        if (!string.IsNullOrWhiteSpace(platformVersion))
        {
            foreach (var arch in new[] { "64", "32" })
            {
                var bin = PlatformVersionService.ResolveVersionBinDirectory(platformVersion, arch);
                if (bin != null)
                    return (bin, platformVersion);
            }
        }

        foreach (var arch in new[] { "64", "32" })
        {
            foreach (var dir in PlatformVersionService.FindPlatformVersionDirs(arch))
            {
                if (ContainsComConnectorDll(dir.BinDir))
                    return (dir.BinDir, dir.Version);
            }
        }

        // Если даже без COM DLL — вернём найденный каталог с 1cv8, чтобы сообщить точнее.
        foreach (var arch in new[] { "64", "32" })
        {
            foreach (var dir in PlatformVersionService.FindPlatformVersionDirs(arch))
                return (dir.BinDir, dir.Version);
        }

        return (null, null);
    }

    /// <summary>
    /// Возвращает корневые каталоги, в которых выполняется поиск платформы
    /// (для информативного сообщения при неудаче).
    /// </summary>
    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arch in new[] { "64", "32" })
        {
            foreach (var r in PlatformVersionService.GetSearchRoots(arch))
            {
                if (!string.IsNullOrWhiteSpace(r))
                    roots.Add(r);
            }
        }

        foreach (var r in PlatformVersionService.GetAdditionalSearchPaths())
        {
            if (!string.IsNullOrWhiteSpace(r))
                roots.Add(r);
        }

        return roots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsComConnectorDll(string binDir)
    {
        if (!Directory.Exists(binDir)) return false;
        try
        {
            return Directory.EnumerateFiles(binDir, "comcntr*.dll").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Определяет разрядность DLL COM-коннектора по имени файла.
    /// comcntr64.dll — 64 бита; comcntr.dll / comcntr32.dll — 32 бита.
    /// </summary>
    private static bool Is64BitDll(string dllName)
    {
        var name = Path.GetFileNameWithoutExtension(dllName);
        return name.Contains("64", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Регистрирует одну DLL через regsvr32 нужной разрядности с повышением прав администратора.
    /// Возвращает (успех, текст ошибки).
    /// </summary>
    private (bool Success, string? Error) RegisterDll(string dllPath, bool is64Bit)
    {
        try
        {
            var regSvr32 = GetRegSvr32Path(is64Bit);

            var psi = new ProcessStartInfo
            {
                FileName = regSvr32,
                Arguments = $"/s \"{dllPath}\"",
                UseShellExecute = true,
                Verb = "runas",          // повышение прав через UAC
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Не удалось запустить regsvr32.");

            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch { /* ignore */ }
                return (false, "Превышено время ожидания regsvr32.");
            }

            var exitCode = process.ExitCode;
            // Код 0 — успех; 1223 — отмена повышения прав пользователем.
            if (exitCode == 0)
                return (true, null);
            if (exitCode == 1223)
                return (false, "Отменено пользователем (недостаточно прав).");
            return (false, $"regsvr32 вернул код {exitCode}.");
        }
        catch (System.ComponentModel.Win32Exception wex)
        {
            // Возникает при отмене UAC (1223) или недоступности regsvr32.
            if (wex.NativeErrorCode == 1223)
                return (false, "Отменено пользователем (недостаточно прав).");
            return (false, wex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Возвращает путь к regsvr32.exe нужной разрядности:
    /// 64-бит — System32, 32-бит — SysWOW64 (иначе 32-битная DLL не зарегистрируется корректно).
    /// </summary>
    private static string GetRegSvr32Path(bool is64Bit)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windowsDir))
            return "regsvr32.exe";

        var candidate = is64Bit
            ? Path.Combine(windowsDir, "System32", "regsvr32.exe")
            : Path.Combine(windowsDir, "SysWOW64", "regsvr32.exe");

        return File.Exists(candidate) ? candidate : "regsvr32.exe";
    }
}