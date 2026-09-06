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
        /// Запуск из пакета AppImage самообновлению не поддаётся: исполняемый файл там лежит
        /// внутри разового монтирования, поэтому в этом случае показывается только сообщение.
        /// </summary>
        private void ShowUpdateDialog(ReleaseInfo release)
        {
            try
            {
                var target = ResolveTargetBinary();
                if (target is null)
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.InstallFailed"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                // Способ установки проверяется раньше наличия файла в выпуске: если
                // заменить себя нельзя, пользователю не важно, какие в выпуске файлы.
                var blocker = GetSelfUpdateBlocker(target);
                if (blocker is not null)
                {
                    var manual = blocker;
                    if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
                        manual += Environment.NewLine + Environment.NewLine + release.HtmlUrl;
                    ShowOnUi(() => _dialogs.ShowInfo(
                        manual, LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                if (string.IsNullOrWhiteSpace(release.DownloadUrl))
                {
                    ShowOnUi(() => _dialogs.ShowError(
                        LocalizationManager.T("Update.NoDownloadUrl"),
                        LocalizationManager.T("Update.NewVersionAvailable")));
                    return;
                }

                // Спрашиваем разрешение до скачивания: отказ прекращает обновление целиком.
                var current = string.Format(
                    LocalizationManager.T("Update.CurrentVersion"), VersionInfo.Display());
                var offered = string.Format(
                    LocalizationManager.T("Update.NewVersion"), NormalizeTag(release.TagName));
                var accepted = _dialogs.Confirm(
                    current + Environment.NewLine + offered + Environment.NewLine + Environment.NewLine
                        + LocalizationManager.T("Update.DownloadPrompt"),
                    LocalizationManager.T("Update.NewVersionAvailable"));
                if (!accepted)
                    return;

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

        /// <summary>
        /// Возвращает текст объяснения, почему самозамена невозможна, или <c>null</c>,
        /// если приложение вправе заменить свой исполняемый файл. Проверяются два
        /// случая: запуск из пакета AppImage и установка в каталог, недоступный
        /// пользователю на запись (так лежит бинарник из deb-пакета, в <c>/usr/bin</c>).
        /// </summary>
        private static string? GetSelfUpdateBlocker(string target)
        {
            if (IsRunningFromAppImage(target))
                return LocalizationManager.T("Update.PackageManualUpdate");

            return IsDirectoryWritable(Path.GetDirectoryName(target))
                ? null
                : LocalizationManager.T("Update.TargetNotWritable");
        }

        /// <summary>
        /// Признак запуска из пакета AppImage: переменные <c>APPIMAGE</c> и <c>APPDIR</c>
        /// выставляет сам пакет, а исполняемый файл лежит внутри разового монтирования,
        /// доступного только на чтение. Одной переменной мало: её наследует любой
        /// дочерний процесс, запущенный из пакета, поэтому дополнительно проверяется,
        /// что текущий исполняемый файл действительно находится внутри <c>APPDIR</c>.
        /// Заменять сам пакет скачанным бинарником нельзя: обёртка AppImage теряется.
        /// </summary>
        private static bool IsRunningFromAppImage(string target)
        {
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            var appDir = Environment.GetEnvironmentVariable("APPDIR");
            if (string.IsNullOrWhiteSpace(appImage) || string.IsNullOrWhiteSpace(appDir))
                return false;

            try
            {
                if (!File.Exists(appImage))
                    return false;

                var mount = Path.GetFullPath(appDir);
                if (!mount.EndsWith(Path.DirectorySeparatorChar))
                    mount += Path.DirectorySeparatorChar;

                return Path.GetFullPath(target).StartsWith(mount, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Проверяет каталог на запись созданием и удалением временного файла. Замена
        /// исполняемого файла идёт переименованием внутри его каталога, поэтому прав
        /// на сам файл недостаточно, нужны права на каталог.
        /// </summary>
        private static bool IsDirectoryWritable(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            var probe = Path.Combine(directory, $".cm-update-probe-{Guid.NewGuid():N}");
            try
            {
                using (File.Create(probe))
                {
                }
                File.Delete(probe);
                return true;
            }
            catch
            {
                TryDelete(probe);
                return false;
            }
        }

        /// <summary>Обрезает ведущий символ «v» у тега версии для отображения.</summary>
        private static string NormalizeTag(string tag) =>
            !string.IsNullOrEmpty(tag) && (tag[0] == 'v' || tag[0] == 'V') ? tag.Substring(1) : tag;

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
        /// <para>
        /// Замена идёт переименованием внутри каталога цели, а не копированием поверх:
        /// <c>cp</c> при занятом файле удаляет его и создаёт заново, поэтому обрыв на
        /// середине оставил бы обрезанный исполняемый файл. Ожидание завершения процесса
        /// в режиме «после закрытия» безусловное, как в версии для Windows: обещание
        /// «применится при закрытии» не должно нарушаться по истечении таймаута.
        /// </para>
        /// </summary>
        private static string CreateUpdaterScript(string target, string newBinary, int currentPid, bool restart)
        {
            var scriptPath = Path.Combine(
                Path.GetTempPath(), UpdateTempDir, $"apply-update-{Guid.NewGuid():N}.sh");

            // Пустое тело if недопустимо в bash, поэтому в режиме «после закрытия»
            // подставляется команда-заглушка, а не один комментарий.
            var relaunchBlock = restart
                ? "nohup \"$TARGET\" >/dev/null 2>&1 &"
                : ": # Перезапуск не требуется, обновление применится при следующем закрытии.";

            // Предел ожидания задаётся только режиму «перезапустить сейчас»: там
            // пользователь ждёт приложение обратно. В режиме «после закрытия» помощник
            // ждёт столько, сколько работает приложение.
            var waitCondition = restart
                ? "kill -0 \"$PID_TARGET\" 2>/dev/null && [ $i -lt 300 ]"
                : "kill -0 \"$PID_TARGET\" 2>/dev/null";

            var script = $@"#!/usr/bin/env bash
set -u
TARGET='{Bq(target)}'
NEW='{Bq(newBinary)}'
STAGED=""$TARGET.cm-update-$$""
PID_TARGET={currentPid}
RESTART={(restart ? 1 : 0)}

# Ожидание завершения основного процесса, чтобы не было гонки при замене файла.
i=0
while {waitCondition}; do
  sleep 1
  i=$((i+1))
done
sleep 1

# Замена в два шага: сначала копия рядом с целью, затем атомарное переименование.
# Так недокачанный или недокопированный файл никогда не окажется на месте рабочего.
if ! cp -f ""$NEW"" ""$STAGED""; then
  rm -f ""$STAGED""
  exit 1
fi
if ! chmod +x ""$STAGED""; then
  rm -f ""$STAGED""
  exit 1
fi
if ! mv -f ""$STAGED"" ""$TARGET""; then
  rm -f ""$STAGED""
  exit 1
fi

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