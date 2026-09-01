#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Подсистема автоматического обновления (Windows/WPF). Запускает фоновую проверку
/// новых версий через GitHub Releases, показывает диалог «Доступна новая версия»
/// и обрабатывает выбор пользователя. При подтверждении скачивает self-contained
/// single-file <c>ConfigurationManagement.exe</c> и заменяет текущий исполняемый файл
/// через временный PowerShell-помощник, после чего перезапускает приложение.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Репозиторий GitHub, откуда берётся последний выпуск.</summary>
    private const string RepoBaseUrl = "https://github.com/sivatorov/ConfigurationManagement";

    /// <summary>Каталог (относительно %TEMP%) для загрузки и временных скриптов обновления.</summary>
    private const string UpdateTempDir = @"ConfigurationManagement\update";

    private readonly GitHubReleaseService _gitHub;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http;

    /// <summary>
    /// Флаг «автоматически обновлять приложение без подтверждения». Когда включён,
    /// фоновая проверка при обнаружении новой версии сама скачивает, устанавливает
    /// и перезапускает приложение, не показывая диалог «Скачать/Отмена». Значение
    /// устанавливается из настроек при старте приложения в <c>App.OnStartup</c>.
    /// Ручная проверка («Проверить обновления») всегда показывает диалог/результат,
    /// независимо от этого флага.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// Событие прогресса скачивания exe. Значение — проценты 0–100. Если длина файла
    /// заранее неизвестна (сервер не отдал Content-Length), передаётся −1, что означает
    /// «неопределённый прогресс» (индикатор переводится в режим IsIndeterminate).
    /// Вызывается из фонового потока, поэтому подписчик должен сам перейти в UI-поток.
    /// </summary>
    public event Action<double>? DownloadProgressChanged;

    /// <summary>
    /// Событие окончания попытки скачивания (успех или неудача). Нужно, чтобы скрыть
    /// индикатор прогресса в строке состояния главного окна. Также вызывается из фона.
    /// </summary>
    public event Action? DownloadFinished;

    public UpdateService(GitHubReleaseService gitHub, IDialogService dialogs)
    {
        _gitHub = gitHub;
        _dialogs = dialogs;

        _http = new HttpClient();
        // GitHub требует корректный User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ConfigurationManagement/1.0");
        // Self-contained single-file exe может весить десятки МБ — таймаут больше, чем у API.
        _http.Timeout = TimeSpan.FromMinutes(20);
    }

    /// <summary>
    /// Проверяет наличие новой версии приложения. Если версия новее и включён
    /// автоматический режим (<see cref="AutoUpdateEnabled"/>) — сразу запускает
    /// скачивание, установку и перезапуск без диалога. Иначе показывает диалог
    /// «Доступна новая версия». Вызывается из фона; переход в UI-поток выполняется
    /// внутри через Dispatcher, поэтому сам метод не блокирует интерфейс. Ошибки
    /// сети/парсинга и отображения диалога не всплывают наружу.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await _gitHub.GetLatestReleaseAsync().ConfigureAwait(false);
            if (release is null)
                return;

            // Сравниваем с текущей версией приложения.
            if (!GitHubReleaseService.IsNewerThan(release, VersionInfo.Display()))
                return;

            var app = Application.Current;
            if (app is null)
                return;

            if (AutoUpdateEnabled)
            {
                // Автоматический режим: не спрашиваем пользователя, а сразу скачиваем,
                // устанавливаем и перезапускаем приложение. Выполняем в UI-потоке через
                // Dispatcher; все ошибки обрабатываются внутри DownloadAndInstallAsync.
                await app.Dispatcher.InvokeAsync(() => _ = DownloadAndInstallAsync(release));
                return;
            }

            await app.Dispatcher.InvokeAsync(() => ShowUpdateDialog(release));
        }
        catch
        {
            // Фоновая проверка не должна ронять приложение.
        }
    }

    /// <summary>
    /// Ручная проверка обновлений (кнопка «Проверить обновления» во вкладке «О программе»).
    /// В отличие от фоновой, явно сообщает пользователю результат: ошибку проверки,
    /// «версия актуальна» или показывает диалог о доступной новой версии.
    /// </summary>
    public async Task CheckForUpdatesManualAsync()
    {
        try
        {
            var release = await _gitHub.GetLatestReleaseAsync().ConfigureAwait(false);
            if (release is null)
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.CheckFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            if (!GitHubReleaseService.IsNewerThan(release, VersionInfo.Display()))
            {
                ShowOnUi(() => _dialogs.ShowInfo(
                    LocalizationManager.T("Update.UpToDate"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            var app = Application.Current;
            if (app is null)
                return;

            await app.Dispatcher.InvokeAsync(() => ShowUpdateDialog(release));
        }
        catch
        {
            ShowOnUi(() => _dialogs.ShowError(
                LocalizationManager.T("Update.CheckFailed"),
                LocalizationManager.T("Update.NewVersionAvailable")));
        }
    }

    /// <summary>
    /// Показывает модальный диалог «Доступна новая версия» и при подтверждении
    /// пользователем запускает скачивание и установку.
    /// </summary>
    private void ShowUpdateDialog(ReleaseInfo release)
    {
        try
        {
            var window = new UpdateAvailableWindow(release);
            window.ShowDialog();
            if (window.DownloadRequested)
                // Загрузка выполняется в фоне; все ошибки обрабатываются внутри.
                _ = DownloadAndInstallAsync(release);
        }
        catch
        {
            // Диалог не должен ронять приложение.
        }
    }

    /// <summary>
    /// Скачивает Windows-версию (single-file exe) из GitHub releases во временный файл
    /// с отображением прогресса в строке состояния главного окна (событие
    /// <see cref="DownloadProgressChanged"/>), а после успешной загрузки предлагает
    /// пользователю выбрать «Перезапустить сейчас» или «Обновить после закрытия».
    /// При выборе «Перезапустить сейчас» запускается PowerShell-помощник, который после
    /// завершения основного процесса заменяет exe и перезапускает приложение, после чего
    /// текущее приложение закрывается. При выборе «Обновить после закрытия» помощник
    /// (в режиме без перезапуска) дожидается естественного завершения процесса и заменяет
    /// exe без автоматического перезапуска и принудительного закрытия приложения.
    /// Программа всегда сама скачивает exe по прямой ссылке; окно/страница GitHub не
    /// открывается. Ошибки сети/парсинга и установки не роняют приложение — показывается
    /// локализованное сообщение.
    /// </summary>
    public async Task DownloadAndInstallAsync(ReleaseInfo release)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                // Прямой ссылки на exe нет. Браузер/GitHub не открываем — просто
                // сообщаем пользователю, что загрузить обновление невозможно.
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.NoDownloadUrl"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            var targetExe = Environment.ProcessPath
                            ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(targetExe))
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.InstallFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            // Скачивание выполняется в фоне; прогресс передаётся в строку состояния
            // главного окна через событие DownloadProgressChanged.
            var newExe = await DownloadAsync(release.DownloadUrl!);

            // Сообщаем подписчикам (главному окну), что попытка скачивания завершена,
            // чтобы скрыть индикатор прогресса в любом случае (успех или неудача).
            RaiseDownloadFinished();

            if (newExe is null)
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.DownloadFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            // После успешной загрузки спрашиваем, как применить обновление.
            var restartNow = AskRestartChoice();

            if (!restartNow)
            {
                // Режим «Обновить после закрытия»: помощник ждёт завершения процесса
                // (без таймаута и без перезапуска) и заменяет exe при естественном
                // закрытии приложения. Само приложение не закрываем и не перезапускаем.
                var deferredUpdater = CreateUpdaterScript(
                    targetExe, newExe, Environment.ProcessId, restart: false);
                if (!LaunchUpdater(deferredUpdater))
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.InstallFailed"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                }
                return;
            }

            // Режим «Перезапустить сейчас»: помощник дожидается завершения процесса,
            // заменяет exe, перезапускает приложение, после чего закрываем текущее.
            var updaterScript = CreateUpdaterScript(
                targetExe, newExe, Environment.ProcessId, restart: true);
            if (!LaunchUpdater(updaterScript))
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.InstallFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
                return;
            }

            ShowOnUi(() => _dialogs.ShowInfo(
                LocalizationManager.T("Update.RestartPrompt"),
                LocalizationManager.T("Update.NewVersionAvailable")));

            var app = Application.Current;
            app?.Dispatcher.Invoke(app.Shutdown);
        }
        catch (Exception ex)
        {
            ShowOnUi(() => _dialogs.ShowError(
                LocalizationManager.T("Update.InstallFailed") + "\n" + ex.Message,
                LocalizationManager.T("Update.NewVersionAvailable")));
        }
    }

    /// <summary>
    /// Скачивает exe по прямой ссылке во временный каталог. Возвращает путь к файлу
    /// или null при сетевой ошибке / пустом файле. Временный файл удаляется при неудаче.
    /// Во время записи отчитывается о прогрессе через <see cref="DownloadProgressChanged"/>.
    /// </summary>
    private async Task<string?> DownloadAsync(string url)
    {
        var dest = Path.Combine(Path.GetTempPath(), UpdateTempDir, "ConfigurationManagement.new.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            // Длина тела нужна для расчёта процентов. Если сервер не отдал Content-Length,
            // прогресс передаём как −1 (индикатор переключается в «неопределённый» режим).
            var totalBytes = response.Content.Headers.ContentLength ?? 0L;

            await using var source = await response.Content.ReadAsStreamAsync();
            await using (var target = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long readBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    readBytes += read;
                    ReportDownloadProgress(totalBytes, readBytes);
                }
            }

            return new FileInfo(dest).Length > 0 ? dest : null;
        }
        catch
        {
            TryDelete(dest);
            return null;
        }
    }

    /// <summary>
    /// Публикует прогресс скачивания. Если общий размер известен заранее — проценты 0–100,
    /// иначе −1 (означает «неопределённый прогресс»).
    /// </summary>
    private void ReportDownloadProgress(long totalBytes, long readBytes)
    {
        if (DownloadProgressChanged is null)
            return;

        var progress = totalBytes > 0
            ? Math.Min(100, readBytes * 100.0 / totalBytes)
            : -1;

        DownloadProgressChanged(progress);
    }

    /// <summary>Уведомляет подписчиков о завершении попытки скачивания (успех или неудача).</summary>
    private void RaiseDownloadFinished()
    {
        try
        {
            DownloadFinished?.Invoke();
        }
        catch
        {
            // Подписчик не должен ронять процесс скачивания.
        }
    }

    /// <summary>
    /// Запрашивает у пользователя, как применить скачанное обновление: «Перезапустить сейчас»
    /// (вернёт true) или «Обновить после закрытия» (вернёт false). Выполняется в UI-потоке.
    /// </summary>
    private bool AskRestartChoice()
    {
        var restartNow = true;
        ShowOnUi(() =>
        {
            var result = System.Windows.MessageBox.Show(
                LocalizationManager.T("Update.RestartOrLater"),
                LocalizationManager.T("Update.NewVersionAvailable"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            restartNow = result == MessageBoxResult.Yes;
        });
        return restartNow;
    }

    /// <summary>
    /// Создаёт временный PowerShell-скрипт, который дожидается завершения основного процесса
    /// (по PID), заменяет текущий исполняемый файл скачанным и удаляет сам скрипт. Если
    /// параметр <paramref name="restart"/> равен true — после замены перезапускает приложение
    /// (режим «Перезапустить сейчас») и ждёт процесс с защитным таймаутом; если false —
    /// перезапуск не выполняется (режим «Обновить после закрытия») и ожидание длится до
    /// естественного завершения приложения. Возвращает путь к созданному скрипту.
    /// </summary>
    private static string CreateUpdaterScript(string targetExe, string newExe, int currentPid, bool restart)
    {
        var scriptPath = Path.Combine(
            Path.GetTempPath(), UpdateTempDir, $"apply-update-{Guid.NewGuid():N}.ps1");

        // Ожидание завершения основного процесса: с таймаутом для перезапуска «сейчас»,
        // без ограничения — для режима «после закрытия».
        var waitBlock = restart
            ? @"
$maxWait = 120
$elapsed = 0
while ($elapsed -lt $maxWait) {
    if (-not (Get-Process -Id $pidTarget -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
    $elapsed++
}
Start-Sleep -Seconds 1"
            : @"
while (Get-Process -Id $pidTarget -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds 1
}
Start-Sleep -Seconds 1";

        // Перезапуск приложения выполняем только по явному запросу пользователя.
        var relaunchBlock = restart
            ? "Start-Process -FilePath $target"
            : "# Перезапуск не требуется — обновление применится при следующем закрытии приложения.";

        var script = $@"
$ErrorActionPreference = 'Stop'
$target = '{Pq(targetExe)}'
$new = '{Pq(newExe)}'
$pidTarget = {currentPid}

# Ожидание завершения основного процесса, чтобы exe не был заблокирован.
{waitBlock}

# Заменяем текущий exe новым.
Move-Item -Path $new -Destination $target -Force
{relaunchBlock}

# Убираем временный скрипт.
Remove-Item -LiteralPath '{Pq(scriptPath)}' -Force -ErrorAction SilentlyContinue
";

        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        return scriptPath;
    }

    /// <summary>
    /// Запускает временный PowerShell-помощник скрытно, без ожидания его завершения.
    /// Возвращает true, если процесс удалось запустить.
    /// </summary>
    private static bool LaunchUpdater(string scriptPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Экранирует строку для одинарных кавычек PowerShell.</summary>
    private static string Pq(string value) => value.Replace("'", "''");

    /// <summary>Выполняет действие в UI-потоке, если вызывающий поток — не UI.</summary>
    private static void ShowOnUi(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        app.Dispatcher.Invoke(action);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Не критично — временный файл останется в %TEMP%.
        }
    }
}
#endif