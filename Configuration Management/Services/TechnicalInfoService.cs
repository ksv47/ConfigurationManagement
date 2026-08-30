using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Configuration_Management.Localization;

namespace Configuration_Management.Services;

/// <summary>
/// Собирает обезличенную техническую информацию о системе и приложении для диагностики.
/// Работает одинаково в Windows (WPF) и Linux (Avalonia): использует только кроссплатформенные
/// API .NET (<see cref="RuntimeInformation"/>, <see cref="Environment"/>, <see cref="GC"/>).
/// Отчёт намеренно не содержит персональных данных: имени пользователя, имени компьютера,
/// домена, сетевых адресов и путей к профилям — только те данные, которые нужны разработчику,
/// чтобы понять, в чём проблема.
/// </summary>
public static class TechnicalInfoService
{
    /// <summary>
    /// Возвращает многострочный отчёт с технической информацией для копирования в буфер обмена.
    /// Метки строк локализованы под текущий язык интерфейса.
    /// </summary>
    public static string Collect()
    {
        var sb = new StringBuilder();
        Append(sb, "Settings.About.TechInfo.App", ProductName());
        Append(sb, "Settings.About.TechInfo.AppVersion", AppVersion());
        Append(sb, "Settings.About.TechInfo.Ui", UiFlavor());
        Append(sb, "Settings.About.TechInfo.Os", Safe(() => RuntimeInformation.OSDescription.Trim()));
        Append(sb, "Settings.About.TechInfo.Architecture", $"{RuntimeInformation.OSArchitecture} / {ProcessArchLabel()}");
        Append(sb, "Settings.About.TechInfo.Runtime", Safe(() => RuntimeInformation.FrameworkDescription));
        Append(sb, "Settings.About.TechInfo.Processors", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        Append(sb, "Settings.About.TechInfo.Memory", TryGetTotalPhysicalMemory());
        Append(sb, "Settings.About.TechInfo.WorkingSet", FormatBytes((ulong)Math.Max(0, Environment.WorkingSet)));
        Append(sb, "Settings.About.TechInfo.Language", LocalizationManager.Instance.CurrentLanguage);
        Append(sb, "Settings.About.TechInfo.Culture", CultureInfo.CurrentCulture.Name);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string key, string value)
        => sb.Append(LocalizationManager.T(key)).Append(": ").AppendLine(value);

    private static string ProductName()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title
               ?? asm.GetName().Name ?? "";
    }

    private static string AppVersion()
    {
        // Только номер версии без суффикса «+<sha>» из InformationalVersion.
        return VersionInfo.Display();
    }

    private static string UiFlavor()
    {
#if WINDOWS
        return "WPF (Windows)";
#else
        return "Avalonia (Linux)";
#endif
    }

    private static string ProcessArchLabel()
        => Environment.Is64BitProcess ? "64-bit" : "32-bit";

    /// <summary>
    /// Объём физической памяти, доступной системе. Лучший из возможных на платформе:
    /// Windows — GlobalMemoryStatusEx; Linux — /proc/meminfo. При сбое возвращает «н/д».
    /// </summary>
    private static string TryGetTotalPhysicalMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                    return FormatBytes(status.ullTotalPhys);
            }
            else if (File.Exists("/proc/meminfo"))
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                        continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
                        return FormatBytes((ulong)kb * 1024);
                    break;
                }
            }
        }
        catch
        {
            // ignore — вернём «н/д»
        }

        return LocalizationManager.T("Settings.About.TechInfo.Na");
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string Safe(Func<string> getter)
    {
        try
        {
            var value = getter();
            return string.IsNullOrWhiteSpace(value) ? LocalizationManager.T("Settings.About.TechInfo.Na") : value;
        }
        catch
        {
            return LocalizationManager.T("Settings.About.TechInfo.Na");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}