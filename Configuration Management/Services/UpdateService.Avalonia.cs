#if LINUX
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Подсистема автоматического обновления (Linux/Avalonia). Запускает фоновую проверку
    /// новых версий через <see cref="GitHubReleaseService"/>, показывает диалог «Доступна новая
    /// версия» (через <see cref="IDialogService"/>) и по подтверждению скачивает self-contained
    /// single-file бинарник <c>ConfigurationManagement</c>. Установка выполняется отдельным
    /// сценарием-помощником, который дожидается завершения основного процесса, заменяет
    /// целевой исполняемый файл и при необходимости перезапускает приложение (issue #161).
    /// </summary>
    public sealed class UpdateService
    {
        /// <summary>Каталог (относительно %TEMP%) для загрузки и временных скриптов обновления.</summary>
        private const string UpdateTempDir = "ConfigurationManagement/update";

        private readonly GitHubReleaseService _gitHub;
        private readonly IDialogService _dialogs;
        private readonly HttpClient _http;

        /// <summary>
        /// Флаг «автоматически обновлять приложение без подтверждения». Как и в Windows-версии,
        /// при обнаружении новой версии всегда показывается диалог с вопросом о применении;
        /// значение сохраняется из настроек при старте (в App.OnFrameworkInitializationCompleted).
        /// </summary>
        public bool AutoUpdateEnabled { get; set; } = true;

        public UpdateService(GitHubReleaseService gitHub, IDialogService dialogs)
        {
            _gitHub = gitHub;
            _dialogs = dialogs;

            _http = new HttpClient();
            // GitHub требует корректный User-Agent.
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ConfigurationManagement/1.0");
            // Self-contained single-file бинарник может весить десятки МБ — таймаут больше, чем у API.
            _http.Timeout = TimeSpan.FromMinutes(20);
        }

        /// <summary>
        /// Проверяет наличие новой версии приложения. Если версия новее — показывает диалог
        /// обновления. Вызывается из фона; переход в UI-поток выполняется внутри через
        /// Dispatcher. Ошибки сети/парсинга и отображения диалога не всплывают наружу.
        /// </summary>
        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var release = await _gitHub.GetLatestReleaseAsync().ConfigureAwait(false);
                if (release is null)
                    return;

                if (!GitHubReleaseService.IsNewerThan(release, VersionInfo.Display()))
                    return;

                await Dispatcher.UIThread.InvokeAsync(() => ShowUpdateDialog(release));
            }
            catch
            {
                // Фоновая проверка не должна ронять приложение.
            }
        }

        /// <summary>
        /// Ручная проверка обновлений (кнопка «Проверить обновления» во вкладке «О программе»).
        /// Явно сообщает результат: ошибку проверки, «версия актуальна» или показывает диалог.
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

                await Dispatcher.UIThread.InvokeAsync(() => ShowUpdateDialog(release));
            }
            catch
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.CheckFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
            }
        }

        /// <summary>
        /// Показывает единый диалог обновления: спрашивает подтверждение скачивания, скачивает
        /// бинарник, затем предлагает применить обновление (перезапустить сейчас или после
        /// закрытия). Все ошибки обрабатываются внутри и не роняют приложение.
        /// </summary>
        private void ShowUpdateDialog(ReleaseInfo release)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(release.DownloadUrl))
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.NoDownloadUrl"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                // Скачиваем новый бинарник в фоне (без UI-прогресса, но надёжно).
                var newBinary = DownloadNewBinaryAsync(release.DownloadUrl!)
                    .GetAwaiter()
                    .GetResult();
                if (newBinary is null)
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.DownloadFailed"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                var target = ResolveTargetBinary();
                if (target is null)
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.InstallFailed"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                // Спрашиваем, как применить обновление: перезапустить сейчас или после закрытия.
                var restartNow = _dialogs.Confirm(
                    LocalizationManager.T("Update.RestartNowPrompt"),
                    LocalizationManager.T("Update.NewVersionAvailable"));

                if (restartNow)
                {
                    if (!ApplyRestartNow(target, newBinary))
                    {
                        ShowOnUi(() => _dialogs.ShowError(
                            LocalizationManager.T("Update.InstallFailed"),
                            LocalizationManager.T("Update.NewVersionAvailable")));
                        return;
                    }
                    // Помощник запущен — закрываем приложение, чтобы замена прошла после выхода.
                    ShutdownNow();
                }
                else
                {
                    // Обновление применится при следующем естественном закрытии приложения.
                    if (!ApplyAfterClose(target, newBinary))
                    {
                        ShowOnUi(() => _dialogs.ShowError(
                            LocalizationManager.T("Update.InstallFailed"),
                            LocalizationManager.T("Update.NewVersionAvailable")));
                        return;
                    }
                    ShowOnUi(() => _dialogs.ShowInfo(
                        LocalizationManager.T("Update.WillApplyOnExit"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                }
            }
            catch
            {
                ShowOnUi(() => _dialogs.ShowError(
                    LocalizationManager.T("Update.InstallFailed"),
                    LocalizationManager.T("Update.NewVersionAvailable")));
            }
        }

        /// <summary>Возвращает путь к текущему исполняемому файлу приложения или null.</summary>
        internal string? ResolveTargetBinary()
        {
            var target = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }

        /// <summary>
        /// Скачивает новый бинарник по прямой ссылке во временный каталог. Возвращает путь
        /// к файлу или null при сетевой ошибке / пустом файле. Временный файл удаляется при неудаче.
        /// </summary>
        private async Task<string?> DownloadNewBinaryAsync(string url)
        {
            var dir = Path.Combine(Path.GetTempPath(), UpdateTempDir);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "ConfigurationManagement.new");

            try
            {
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using (var target = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(target).ConfigureAwait(false);
                    await target.FlushAsync().ConfigureAwait(false);
                }

                return new FileInfo(dest).Length > 0 ? dest : null;
            }
            catch
            {
                TryDelete(dest);
                return null;
            }
        }

        /// <summary>Применяет обновление режимом «Перезапустить сейчас». Возвращает true при успехе.</summary>
        internal bool ApplyRestartNow(string target, string newBinary)
        {
            var script = CreateUpdaterScript(target, newBinary, Environment.ProcessId, restart: true);
            return LaunchUpdater(script);
        }

        /// <summary>Применяет обновление режимом «Обновить после закрытия». Возвращает true при успехе.</summary>
        internal bool ApplyAfterClose(string target, string newBinary)
        {
            var script = CreateUpdaterScript(target, newBinary, Environment.ProcessId, restart: false);
            return LaunchUpdater(script);
        }

        /// <summary>Закрывает текущее приложение (вызывается после успешного запуска помощника).</summary>
        internal void ShutdownNow()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                }
                catch
                {
                    // При любом сбое завершаем процесс явно, чтобы помощник мог заменить бинарник.
                    Environment.Exit(0);
                }
            });
        }

        /// <summary>
        /// Создаёт временный bash-сценарий, который дожидается завершения основного процесса
        /// (по PID), заменяет текущий исполняемый файл скачанным (chmod +x) и удаляет сам скрипт.
        /// Если <paramref name="restart"/> равен true — после замены перезапускает приложение.
        /// Возвращает путь к созданному скрипту.
        /// </summary>
        private static string CreateUpdaterScript(string target, string newBinary, int currentPid, bool restart)
        {
            var scriptPath = Path.Combine(
                Path.GetTempPath(), UpdateTempDir, $"apply-update-{Guid.NewGuid():N}.sh");

            var relaunchBlock = restart
                ? "nohup \"$TARGET\" >/dev/null 2>&1 &"
                : "# Перезапуск не требуется — обновление применится при следующем закрытии.";

            var script = $@"#!/usr/bin/env bash
set -u
TARGET='{Bq(target)}'
NEW='{Bq(newBinary)}'
PID_TARGET={currentPid}
RESTART={(restart ? 1 : 0)}

# Ожидание завершения основного процесса, чтобы не было гонки при замене файла.
i=0
while kill -0 ""$PID_TARGET"" 2>/dev/null && [ $i -lt 300 ]; do
  sleep 1
  i=$((i+1))
done
sleep 1

# Заменяем текущий исполняемый файл новым и делаем его исполняемым.
if ! cp -f ""$NEW"" ""$TARGET"" 2>/dev/null && ! mv -f ""$NEW"" ""$TARGET""; then
  exit 1
fi
chmod +x ""$TARGET"" 2>/dev/null || true

# Перезапуск приложения (только по явному запросу пользователя).
if [ ""$RESTART"" = ""1"" ]; then
  {relaunchBlock}
fi

# Убираем временный бинарник и сам скрипт.
rm -f ""$NEW""
rm -f ""$0""
";

            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, script);
            return scriptPath;
        }

        /// <summary>
        /// Запускает временный bash-помощник detached, без ожидания его завершения.
        /// Возвращает true, если процесс удалось запустить.
        /// </summary>
        private static bool LaunchUpdater(string scriptPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
                // Запускаем bash на сценарии; CreateNoWindow уводит процесс с терминала,
                // поэтому он продолжит работу и после выхода основного процесса.
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/env",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add(scriptPath);
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Экранирует строку для одинарных кавычек bash: ' → '\''.</summary>
        private static string Bq(string value) => value.Replace("'", "'\\''");

        /// <summary>Выполняет действие в UI-потоке, если вызывающий поток — не UI.</summary>
        private static void ShowOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }
            Dispatcher.UIThread.InvokeAsync(action);
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
}
#endif