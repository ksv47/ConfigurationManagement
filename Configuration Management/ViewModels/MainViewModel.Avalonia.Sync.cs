#if LINUX
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): синхронизация ibases.v8i и резервное копирование (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    public string SyncMessage
    {
        get => _syncMessage;
        set => SetProperty(ref _syncMessage, value);
    }

    // ---- Синхронизация с ibases.v8i ----
    public IbasesSyncMode IbasesSyncMode => _settings.IbasesSyncMode;
    public string IbasesSyncFilePath => _settings.IbasesSyncFilePath;
    public IbasesSyncTrigger IbasesSyncTrigger => _settings.IbasesSyncTrigger;
    public int IbasesSyncIntervalMinutes => _settings.IbasesSyncIntervalMinutes;
    public string IbasesSyncScheduleTime => _settings.IbasesSyncScheduleTime;
    public bool IbasesBackupEnabled => _settings.IbasesBackupEnabled;
    public int IbasesBackupKeepCount => _settings.IbasesBackupKeepCount;

    /// <summary>Применяет настройки синхронизации с файлом списка баз платформы.</summary>
    public void ApplyIbasesSyncSettings(IbasesSyncMode mode, string filePath, IbasesSyncTrigger trigger,
        int intervalMinutes, string scheduleTime, bool backupEnabled, int backupKeepCount)
    {
        _settings.IbasesSyncMode = mode;
        _settings.IbasesSyncFilePath = filePath ?? string.Empty;
        _settings.IbasesSyncTrigger = trigger;
        _settings.IbasesSyncIntervalMinutes = intervalMinutes > 0 ? intervalMinutes : 30;
        _settings.IbasesSyncScheduleTime = scheduleTime ?? string.Empty;
        _settings.IbasesBackupEnabled = backupEnabled;
        _settings.IbasesBackupKeepCount = backupKeepCount > 0 ? backupKeepCount : 5;

        SaveSettingsSilently();
        RestartAutoSync();
    }

    // ---- Профиль: резервное копирование и восстановление ----

    /// <summary>Каталог резервной копии профиля (настройки, базы, пользователи/пароли, ibases.v8i).</summary>
    public string ProfileBackupDirectory => _settings.ProfileBackupDirectory;

    /// <summary>Восстанавливать профиль из каталога резервной копии при каждом запуске.</summary>
    public bool ProfileRestoreOnStartup => _settings.ProfileRestoreOnStartup;

    /// <summary>Применяет настройки резервного копирования профиля из окна настроек.</summary>
    public void ApplyProfileBackupSettings(string backupDirectory, bool restoreOnStartup)
    {
        _settings.ProfileBackupDirectory = backupDirectory?.Trim() ?? string.Empty;
        _settings.ProfileRestoreOnStartup = restoreOnStartup;
        SaveSettingsSilently();
    }

    /// <summary>
    /// Сохраняет текущий профиль (настройки, список баз с пользователями и паролями,
    /// группы, ibases.v8i) в настроенный каталог. Возвращает true при успехе.
    /// </summary>
    public bool BackupProfile()
    {
        var dir = _settings.ProfileBackupDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            _dialog.ShowWarning(LocalizationManager.T("Settings.Profile.NoDirectory"));
            return false;
        }
        try
        {
            var count = ProfileBackupService.Backup(dir, _settings.IbasesSyncFilePath);
            _logger.Info($"Резервная копия профиля сохранена в {dir} ({count} файлов)");
            _dialog.ShowInfo(string.Format(LocalizationManager.T("Settings.Profile.BackupDone"), count));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка резервного копирования профиля", ex);
            _dialog.ShowError(string.Format(LocalizationManager.T("Settings.Profile.BackupFailed"), ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Восстанавливает профиль из настроенного каталога и перезагружает данные,
    /// чтобы они применились без перезапуска. Возвращает true при успехе.
    /// </summary>
    public bool RestoreProfile()
    {
        var dir = _settings.ProfileBackupDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            _dialog.ShowWarning(LocalizationManager.T("Settings.Profile.NoDirectory"));
            return false;
        }
        if (!ProfileBackupService.HasBackup(dir))
        {
            _dialog.ShowWarning(LocalizationManager.T("Settings.Profile.NoBackup"));
            return false;
        }
        try
        {
            var count = ProfileBackupService.Restore(dir, _settings.IbasesSyncFilePath);
            _logger.Info($"Профиль восстановлен из {dir} ({count} файлов)");
            // Перезагружаем настройки, список баз и группы, чтобы они применились сразу.
            Initialize();
            _dialog.ShowInfo(string.Format(LocalizationManager.T("Settings.Profile.RestoreDone"), count));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка восстановления профиля", ex);
            _dialog.ShowError(string.Format(LocalizationManager.T("Settings.Profile.RestoreFailed"), ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Читает список баз из ibases.v8i и добавляет или обновляет базы.
    /// Путь ищется сам, и только если файла нет, спрашивается у пользователя.
    /// </summary>
    public void ImportFromIbasesV8i()
    {
        var path = IbasesV8iImporter.FindDefaultPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _dialog.OpenFileDialog(
                LocalizationManager.T("Settings.Ibases.FileDialogTitle"),
                LocalizationManager.T("Main.IbasesFileFilter"));
            if (string.IsNullOrWhiteSpace(path))
                return;
        }

        ImportFromIbasesFileInteractive(path);
    }

    /// <summary>
    /// Путь для ручной операции с ibases.v8i: заданный в окне настроек, а если
    /// поле пустое, стандартный. Берётся набранное в поле, а не сохранённое:
    /// в версии для Windows так делает только восстановление из копии,
    /// а загрузка и выгрузка проверяют один путь, а читают другой.
    /// </summary>
    private static string? ResolveIbasesPathForCommand(string? filePath)
        => string.IsNullOrWhiteSpace(filePath)
            ? IbasesV8iImporter.FindDefaultPath()
            : filePath.Trim();

    /// <summary>
    /// Ручная загрузка списка баз из ibases.v8i. О результате сообщает сам
    /// импорт, включая подтверждение, если файл требует удалить базы.
    /// </summary>
    public void ImportFromIbasesFile(string? filePath)
    {
        var path = ResolveIbasesPathForCommand(filePath);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            _dialog.ShowInfo(LocalizationManager.T("Settings.Ibases.ImportFileNotFound"),
                LocalizationManager.T("Settings.Ibases.ImportTitle"));
            return;
        }

        ImportFromIbasesFileInteractive(path);
    }

    /// <summary>Ручная выгрузка списка баз в ibases.v8i.</summary>
    public void ExportToIbasesFile(string? filePath)
    {
        var path = ResolveIbasesPathForCommand(filePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            _dialog.ShowInfo(LocalizationManager.T("Settings.Ibases.ExportNoPath"),
                LocalizationManager.T("Settings.Ibases.ExportTitle"));
            return;
        }

        if (TryExportToIbases(path, backup: true, "Ручная выгрузка", out var error))
        {
            _dialog.ShowInfo(LocalizationManager.T("Settings.Ibases.ExportOk"),
                LocalizationManager.T("Settings.Ibases.ExportTitle"));
            return;
        }

        _dialog.ShowError(
            string.Format(LocalizationManager.T("Settings.Ibases.ExportFailed"), error),
            LocalizationManager.T("Settings.Ibases.ExportErrorTitle"));
    }

    /// <summary>
    /// Восстанавливает ibases.v8i из последней резервной копии рядом с файлом.
    /// Копии создаёт сама выгрузка, если включён соответствующий флажок.
    /// </summary>
    public void RestoreIbasesBackup(string? filePath)
    {
        var path = ResolveIbasesPathForCommand(filePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            _dialog.ShowWarning(LocalizationManager.T("Settings.Ibases.RestoreNoPath"),
                LocalizationManager.T("Settings.Ibases.RestoreTitle"));
            return;
        }

        var backups = IbasesBackupService.ListBackups(path);
        if (backups.Count == 0)
        {
            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("Settings.Ibases.RestoreNoBackups"), path),
                LocalizationManager.T("Settings.Ibases.RestoreTitle"));
            return;
        }

        var latest = backups[0];
        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("Settings.Ibases.RestoreConfirm"),
                    System.IO.Path.GetFileName(latest)),
                LocalizationManager.T("Settings.Ibases.RestoreConfirmTitle")))
            return;

        try
        {
            IbasesBackupService.RestoreBackup(latest, path);
            _logger.Info($"Файл {path} восстановлен из копии {latest}");
            _dialog.ShowInfo(LocalizationManager.T("Settings.Ibases.RestoreOk"),
                LocalizationManager.T("Settings.Ibases.RestoreTitle"));
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось восстановить ibases.v8i из копии", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Settings.Ibases.RestoreFailed"), ex.Message),
                LocalizationManager.T("Common.Error"));
        }
    }

    /// <summary>
    /// Разовый импорт из ibases.v8i по требованию пользователя. Импортёр не
    /// только добавляет и обновляет базы, но и удаляет те, которых в файле нет,
    /// поэтому пустой или испорченный файл вычистил бы список. Ровно та же
    /// защита стоит в автоматической синхронизации.
    /// </summary>
    private void ImportFromIbasesFileInteractive(string path)
    {
        var before = _allInfobases.Count;

        try
        {
            // Импорт идёт в копии списков: он удаляет базы, которых нет в файле,
            // и до подтверждения пользователем рабочие списки трогать нельзя.
            // Проверено исполнением: без этого разовый импорт молча заменил
            // тридцать баз восемью из файла.
            var candidateInfobases = _allInfobases.ToList();
            var candidateGroups = _groups.ToList();
            var result = _sync.Import(path, candidateInfobases, candidateGroups);

            if (before > 0 && candidateInfobases.Count == 0)
            {
                _logger.Warn($"Импорт из {path} не дал ни одной базы, список приложения не меняем");
                _dialog.ShowWarning(LocalizationManager.T("Main.ImportNoBases"),
                    LocalizationManager.T("Main.ImportIbasesTitle"));
                return;
            }

            if (result.Removed > 0 && !_dialog.Confirm(
                    string.Format(LocalizationManager.T("Main.ImportRemovesConfirm"),
                        result.Removed, result.Added, result.Updated),
                    LocalizationManager.T("Main.ImportIbasesTitle")))
                return;

            _allInfobases.Clear();
            _allInfobases.AddRange(candidateInfobases);
            // Состав списка изменился: слоты избранного пересчитываются, иначе
            // номер остаётся у удалённой базы, а её слот занят навсегда.
            SyncFavoriteHotkeys();
            _groups.Clear();
            _groups.AddRange(candidateGroups);

            var saved = SaveSilently();
            // Импорт создаёт недостающие группы, и без их записи они пропадают
            // при следующем запуске.
            saved &= SaveGroupsSilently();
            RebuildTree();

            // Выбранную базу мог удалить сам импорт.
            if (SelectedInfobase is { } selected && !_allInfobases.Contains(selected))
                SelectedInfobase = null;

            StatusBarInfo = string.Format(LocalizationManager.T("Sync.ImportedCount"), _allInfobases.Count, _groups.Count);
            _logger.Info($"Импорт из ibases.v8i: {path}, добавлено {result.Added}, обновлено {result.Updated}, удалено {result.Removed}");

            if (!saved)
            {
                _dialog.ShowError(
                    string.Format(LocalizationManager.T("Main.ErrImportFailed"),
                        LocalizationManager.T("Main.SaveFailedHint")),
                    LocalizationManager.T("Main.ImportErrorTitle"));
                return;
            }

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("Main.ImportDone"),
                    result.Added, result.Updated, result.Skipped, result.GroupsCreated),
                LocalizationManager.T("Main.ImportIbasesTitle"));
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка импорта из ibases.v8i", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrImportFailed"), ex.Message),
                LocalizationManager.T("Main.ImportErrorTitle"));
        }
    }

    /// <summary>
    /// Импорт баз и настроек платформы из программы StartManager (issue #163).
    /// Читает каталог настроек StartManager (v8config.smc и settings.cnf), добавляет
    /// и обновляет базы с авторизацией (расшифровывая пароли по алгоритму Виженера),
    /// а также добавляет найденный путь платформы 1С (V8AppPath) в дополнительные
    /// пути поиска платформы приложения.
    /// </summary>
    public void ImportFromStartManager()
    {
        var dir = StartManagerImporter.FindDefaultSettingsDir();
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir))
        {
            dir = _dialog.OpenFolderDialog(
                LocalizationManager.T("StartManager.ChooseFolder"), null);
            if (string.IsNullOrWhiteSpace(dir))
                return;
        }

        try
        {
            var candidateInfobases = _allInfobases.ToList();
            var candidateGroups = _groups.ToList();

            var result = StartManagerImporter.Import(dir, candidateInfobases, candidateGroups, ResolveIbasesFilePath());

            if (result.NoConfigFound)
            {
                _dialog.ShowInfo(
                    string.Format(LocalizationManager.T("StartManager.NoConfig"), dir),
                    LocalizationManager.T("StartManager.Title"));
                return;
            }

            if (result.NoIbasesFound)
            {
                _dialog.ShowInfo(
                    LocalizationManager.T("StartManager.NoIbases"),
                    LocalizationManager.T("StartManager.Title"));
                return;
            }

            if (result.Added == 0 && result.Updated == 0)
            {
                _dialog.ShowInfo(
                    LocalizationManager.T("StartManager.NothingImported"),
                    LocalizationManager.T("StartManager.Title"));
                return;
            }

            _allInfobases.Clear();
            _allInfobases.AddRange(candidateInfobases);
            SyncFavoriteHotkeys();
            _groups.Clear();
            _groups.AddRange(candidateGroups);

            var saved = SaveSilently();
            saved &= SaveGroupsSilently();
            RebuildTree();

            // Путь платформы 1С из settings.cnf добавляем в дополнительные пути поиска.
            var platformAdded = false;
            if (result.PlatformSearchPaths.Count > 0)
            {
                var paths = new List<string>(_settings.AdditionalPlatformSearchPaths);
                foreach (var p in result.PlatformSearchPaths)
                {
                    if (!paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    {
                        paths.Add(p);
                        platformAdded = true;
                    }
                }
                if (platformAdded)
                    ApplyPlatformSettings(paths, _settings.DefaultArchitecture);
            }

            StatusBarInfo = string.Format(
                LocalizationManager.T("Sync.ImportedCount"), _allInfobases.Count, _groups.Count);
            _logger.Info($"Импорт из StartManager: {dir}, добавлено {result.Added}, " +
                         $"обновлено {result.Updated}, пропущено {result.Skipped}");

            if (!saved)
            {
                _dialog.ShowError(
                    string.Format(LocalizationManager.T("Main.ErrImportFailed"),
                        LocalizationManager.T("Main.SaveFailedHint")),
                    LocalizationManager.T("Main.ImportErrorTitle"));
                return;
            }

            var message = string.Format(
                LocalizationManager.T("StartManager.Done"),
                result.Added, result.Updated, result.GroupsCreated, result.Skipped);
            if (result.Skipped > 0)
                message += "\n\n" + LocalizationManager.T("StartManager.SkippedHint");
            if (platformAdded)
                message += "\n" + LocalizationManager.T("StartManager.PlatformPathAdded");
            _dialog.ShowInfo(message, LocalizationManager.T("StartManager.Title"));
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка импорта из StartManager", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrImportFailed"), ex.Message),
                LocalizationManager.T("Main.ImportErrorTitle"));
        }
    }

    /// <summary>
    /// Выгружает список баз в ibases.v8i после локального изменения.
    /// Работает только если пользователь включил режим экспорта: по умолчанию
    /// IbasesSyncMode.None, то есть файл платформы не трогается вовсе.
    /// Перед записью снимается резервная копия, если она включена в настройках.
    /// </summary>
    private void ExportToIbasesAfterLocalChange()
    {
        if (_settings.IbasesSyncMode is not (IbasesSyncMode.Export or IbasesSyncMode.Both))
            return;

        var filePath = string.IsNullOrWhiteSpace(_settings.IbasesSyncFilePath)
            ? IbasesV8iImporter.FindDefaultPath()
            : _settings.IbasesSyncFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            if (_settings.IbasesBackupEnabled && System.IO.File.Exists(filePath))
            {
                try { IbasesBackupService.CreateBackup(filePath, _settings.IbasesBackupKeepCount); }
                catch (Exception ex) { _logger.Warn($"Не удалось создать резервную копию ibases.v8i: {ex.Message}"); }
            }

            _sync.Export(filePath, _allInfobases, _groups);
            _logger.Info($"Список баз выгружен в {filePath}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка выгрузки списка баз в ibases.v8i", ex);
            SyncMessage = string.Format(LocalizationManager.T("Sync.ExportError"), ex.Message);
        }
    }

    /// <summary>
    /// Перезапускает автоматическую синхронизацию по настройкам. При выключенной
    /// синхронизации и при режиме «при старте» таймер не нужен.
    /// </summary>
    private void RestartAutoSync()
    {
        StopAutoSync();

        if (_settings.IbasesSyncMode == IbasesSyncMode.None
            || _settings.IbasesSyncTrigger == IbasesSyncTrigger.OnStartup)
            return;

        if (!ComputeNextRunTime(out var nextRun))
            return;

        _nextScheduleRun = nextRun;
        _syncTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Start();
        _logger.Info($"Автосинхронизация с ibases.v8i включена, следующий запуск {nextRun:HH:mm}");
    }

    private void StopAutoSync()
    {
        if (_syncTimer is null)
            return;

        _syncTimer.Stop();
        _syncTimer.Tick -= OnSyncTimerTick;
        _syncTimer = null;
        _nextScheduleRun = null;
    }

    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        if (_settings.IbasesSyncMode == IbasesSyncMode.None)
        {
            RestartAutoSync();
            return;
        }

        if (_nextScheduleRun is null)
        {
            if (ComputeNextRunTime(out var next))
                _nextScheduleRun = next;
            return;
        }

        if (DateTime.Now < _nextScheduleRun.Value)
            return;

        SynchronizeSilently();
        if (ComputeNextRunTime(out var following))
            _nextScheduleRun = following;
    }

    /// <summary>
    /// Время следующего запуска: для интервала это «сейчас плюс интервал»,
    /// для расписания ближайшее заданное время, завтра если сегодня прошло.
    /// </summary>
    private bool ComputeNextRunTime(out DateTime nextRun)
    {
        nextRun = default;

        if (_settings.IbasesSyncTrigger == IbasesSyncTrigger.Interval)
        {
            nextRun = DateTime.Now.AddMinutes(Math.Max(1, _settings.IbasesSyncIntervalMinutes));
            return true;
        }

        if (_settings.IbasesSyncTrigger == IbasesSyncTrigger.Schedule)
        {
            // Строгий разбор времени суток: обычный TryParse принимает и локальные
            // форматы, и длительности вроде 25:00, и «9» как девять суток.
            // Час допускается с ведущим нулём и без: поле свободного ввода,
            // и «9:00» пользователь набирает не реже, чем «09:00», а раньше
            // такое расписание молча не запускалось вовсе.
            if (!TimeSpan.TryParseExact(_settings.IbasesSyncScheduleTime?.Trim(),
                    new[] { @"hh\:mm", @"h\:mm" },
                    System.Globalization.CultureInfo.InvariantCulture, out var time)
                || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                _logger.Warn($"Автосинхронизация выключена: время расписания «{_settings.IbasesSyncScheduleTime}» не распознано, ожидается ЧЧ:ММ");
                return false;
            }

            var now = DateTime.Now;
            var run = now.Date + time;
            if (run <= now)
                run = run.AddDays(1);

            nextRun = run;
            return true;
        }

        return false;
    }

    /// <summary>Путь к файлу списка баз: заданный в настройках или стандартный.</summary>
    private string? ResolveIbasesFilePath() =>
        string.IsNullOrWhiteSpace(_settings.IbasesSyncFilePath)
            ? IbasesV8iImporter.FindDefaultPath()
            : _settings.IbasesSyncFilePath;

    /// <summary>
    /// Синхронизация без диалогов: для запуска по расписанию и при старте.
    /// В двустороннем режиме сначала выгрузка, затем загрузка, как в WPF-версии.
    /// </summary>
    private void SynchronizeSilently()
    {
        if (_settings.IbasesSyncMode == IbasesSyncMode.None)
            return;

        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
        {
            _logger.Warn("Автосинхронизация: файл ibases.v8i не найден");
            return;
        }

        var done = false;

        // Выгрузка и загрузка разделены: отказ одной не должен отменять другую,
        // как это устроено в WPF-версии.
        if (_settings.IbasesSyncMode is IbasesSyncMode.Export or IbasesSyncMode.Both)
            done |= ExportToIbases(filePath);

        if (_settings.IbasesSyncMode is IbasesSyncMode.Import or IbasesSyncMode.Both)
            done |= ImportFromIbases(filePath);

        SyncMessage = done
            ? LocalizationManager.T("Sync.Completed")
            : LocalizationManager.T("Sync.Failed");
    }

    /// <summary>Выгрузка списка баз в файл платформы с резервной копией.</summary>
    private bool ExportToIbases(string filePath)
        => TryExportToIbases(filePath, backup: true, "Автосинхронизация: выгрузка", out _);

    /// <summary>
    /// Выгрузка с текстом ошибки и своей записью в журнал: по ней отличают
    /// автоматическую синхронизацию от нажатой кнопки, а текст ошибки нужен
    /// только ручной операции.
    /// </summary>
    /// <param name="backup">
    /// Создавать ли резервную копию файла. Создают обе выгрузки, и ручная тоже,
    /// хотя в версии для Windows ручная копию не делает. Причина в том, что
    /// выгрузка переписывает файл целиком: при пустом списке баз приложения
    /// она обнуляет ibases.v8i, и без копии это уже не отменить. Копия хранит
    /// состояние до выгрузки, то есть соседняя кнопка восстановления вернёт
    /// именно то, что было до ошибочного нажатия.
    /// </param>
    private bool TryExportToIbases(string filePath, bool backup, string logPrefix, out string error)
    {
        error = string.Empty;
        try
        {
            if (backup && _settings.IbasesBackupEnabled && System.IO.File.Exists(filePath))
            {
                try { IbasesBackupService.CreateBackup(filePath, _settings.IbasesBackupKeepCount); }
                catch (Exception ex) { _logger.Error("Не удалось создать резервную копию ibases.v8i", ex); }
            }

            _sync.Export(filePath, _allInfobases, _groups);
            _logger.Info($"{logPrefix} в {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.Error("Ошибка выгрузки в ibases.v8i", ex);
            return false;
        }
    }

    /// <summary>
    /// Загрузка списка баз из файла платформы. Импорт удаляет из приложения базы,
    /// которых нет в файле, поэтому отсутствующий и подозрительно пустой файл
    /// пропускаются: иначе повреждённый файл вычистил бы список без спроса.
    /// </summary>
    private bool ImportFromIbases(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            _logger.Warn($"Автосинхронизация: файла {filePath} нет, загрузка пропущена");
            return false;
        }

        var before = _allInfobases.Count;

        try
        {
            var result = _sync.Import(filePath, _allInfobases, _groups);

            if (before > 0 && _allInfobases.Count == 0)
            {
                _logger.Warn($"Автосинхронизация: файл {filePath} не дал ни одной базы, список приложения не меняем");
                _allInfobases = _repository.Load();
                // Состав списка изменился: слоты избранного пересчитываются, иначе
                // номер остаётся у удалённой базы, а её слот занят навсегда.
                SyncFavoriteHotkeys();
                RebuildTree();
                return false;
            }

            SaveSilently();
            // Импорт создаёт недостающие группы, и без этого они пропадали
            // при следующем запуске: сохраняется только список баз.
            SaveGroupsSilently();
            RebuildTree();

            // Выбранная база могла быть удалена импортом.
            if (SelectedInfobase is { } selected && !_allInfobases.Contains(selected))
                SelectedInfobase = null;

            _logger.Info($"Автосинхронизация: загрузка из {filePath}, добавлено {result.Added}, обновлено {result.Updated}, удалено {result.Removed}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка загрузки из ibases.v8i", ex);
            return false;
        }
    }

    private void SynchronizeWithIbases()
    {
        var path = _dialog.OpenFileDialog(
            LocalizationManager.T("Sync.ChooseIbasesFile"),
            LocalizationManager.T("Sync.IbasesFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        // Тот же путь, что и у кнопки на вкладке «Базы»: раньше здесь терялись
        // созданные импортом группы и оставался выбор на удалённой базе.
        ImportFromIbasesFileInteractive(path);
        SyncMessage = LocalizationManager.T("Sync.Completed");
    }
}
#endif