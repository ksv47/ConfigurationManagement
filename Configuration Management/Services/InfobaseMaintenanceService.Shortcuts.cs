using System.Diagnostics;
using System.IO;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Создание ярлыков (.lnk) и запуск родного стартера 1С (WPF-часть).
/// </summary>
public static partial class InfobaseMaintenanceService
{
    /// <summary>
    /// Создаёт ярлык .lnk на рабочем столе как стандартный лаунчер 1С:
    /// цель — 1cv8.exe, аргументы ENTERPRISE /F"..." или /S"...".
    /// </summary>
    public static bool CreateDesktopShortcut(Infobase ib, string? appExecutablePath = null)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                return false;

            var safeName = string.Join("_", (ib.Name ?? LocalizationManager.T("Maint.DefaultBaseName")).Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = string.Join("_", LocalizationManager.T("Maint.DefaultBaseName").Split(Path.GetInvalidFileNameChars()));
            var lnkPath = Path.Combine(desktop, $"{safeName}.lnk");

            var target = OneCLauncher.ResolveThickClientExe(ib);
            if (string.IsNullOrEmpty(target))
                target = FindOneCStartExe();
            if (string.IsNullOrEmpty(target))
                return false;

            var args = OneCLauncher.BuildEnterpriseShortcutArguments(ib);
            return CreateShortcutCom(lnkPath, target, args, ib.Name ?? safeName, target);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Логгер из DI (без создания жёсткой зависимости).</summary>
    private static IAppLogger? GetLogger()
    {
        try { return AppServices.GetRequiredService<IAppLogger>(); }
        catch { return null; }
    }

    /// <summary>
    /// Запускает родной стартер 1С (1CEStart.exe) для сверки списка баз.
    /// Сервис не знает об UI: при неудаче логирует причину и возвращает false,
    /// а сообщение пользователю показывает вызывающая ViewModel через IDialogService.
    /// </summary>
    public static bool OpenNativeStarter()
    {
        try
        {
            var path = FindOneCStartExe();
            if (string.IsNullOrEmpty(path))
            {
                GetLogger()?.Warn(LocalizationManager.T("Maint.StarterNotFound"));
                return false;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            GetLogger()?.Error(
                string.Format(LocalizationManager.T("Maint.StarterLaunchFailedFormat"), ex.Message), ex);
            return false;
        }
    }

    public static string? FindOneCStartExe()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            var p = Path.Combine(root!, "1cv8", "common", "1CEStart.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Создание .lnk через WScript.Shell (COM), как у стандартного стартера 1С.</summary>
    private static bool CreateShortcutCom(
        string lnkPath,
        string targetPath,
        string arguments,
        string description,
        string? iconPath = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.Description = description;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                shortcut.IconLocation = iconPath + ",0";
            shortcut.Save();
            return File.Exists(lnkPath);
        }
        catch
        {
            return false;
        }
    }
}