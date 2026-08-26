#if WINDOWS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (partial class split by feature blocks, see MainViewModel.*.cs).</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>
    /// Формирует текстовое описание изменений по результату импорта/экспорта.
    /// Возвращает пустую строку, если изменений не было.
    /// </summary>
    private static string BuildSyncMessage(string prefix, object result)
    {
        var parts = new List<string>();

        if (result is IbasesImportResult import)
        {
            if (import.Added > 0) parts.Add(string.Format(LocalizationManager.T("Sync.AddedBases"), import.Added));
            if (import.Updated > 0) parts.Add(string.Format(LocalizationManager.T("Sync.UpdatedBases"), import.Updated));
            if (import.Removed > 0) parts.Add(string.Format(LocalizationManager.T("Sync.RemovedBases"), import.Removed));
            if (import.Skipped > 0) parts.Add(string.Format(LocalizationManager.T("Sync.Skipped"), import.Skipped));
            if (import.GroupsCreated > 0) parts.Add(string.Format(LocalizationManager.T("Sync.GroupsCreated"), import.GroupsCreated));
        }
        else if (result is IbasesExportResult export)
        {
            if (export.Added > 0) parts.Add(string.Format(LocalizationManager.T("Sync.AddedBases"), export.Added));
            if (export.Updated > 0) parts.Add(string.Format(LocalizationManager.T("Sync.UpdatedBases"), export.Updated));
            if (export.Removed > 0) parts.Add(string.Format(LocalizationManager.T("Sync.RemovedBases"), export.Removed));
            if (export.GroupsCreated > 0) parts.Add(string.Format(LocalizationManager.T("Sync.GroupsCreated"), export.GroupsCreated));
        }

        return parts.Count == 0 ? string.Empty : $"{prefix}: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Запускает автоматическую синхронизацию в соответствии с настройками:
    /// по интервалу или по расписанию. При старте также выполняет синхронизацию
    /// (если выбран режим OnStartup или Interval/Schedule).
    /// </summary>
    public void StartAutoSync()
    {
        // При запуске приложения синхронизируемся всегда, если режим включён,
        // независимо от выбранного триггера (OnStartup — сразу, Interval/Schedule — сразу и далее).
        SynchronizeWithIbases();

        RestartAutoSync();
    }

    /// <summary>
    /// Останавливает автоматическую синхронизацию по таймеру.
    /// </summary>
    public void StopAutoSync()
    {
        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTimerTick;
            _syncTimer = null;
        }
        _nextScheduleRun = null;
        SetAutoSyncRunning(false);
    }

    /// <summary>
    /// Перезапускает таймер автоматической синхронизации в соответствии с
    /// текущими настройками. Для режима Interval таймер тикает раз в минуту и
    /// выполняет синхронизацию через заданный интервал; для режима Schedule —
    /// проверяет наступление заданного времени.
    /// </summary>
    public void RestartAutoSync()
    {
        StopAutoSync();

        if (_ibasesSyncMode == IbasesSyncMode.None ||
            _ibasesSyncTrigger is IbasesSyncTrigger.OnStartup)
        {
            return;
        }

        if (!ComputeNextRunTime(out var nextRun))
            return;

        _nextScheduleRun = nextRun;

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Start();
        SetAutoSyncRunning(true);
    }

    /// <summary>
    /// Обработчик тика таймера автоматической синхронизации.
    /// </summary>
    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        if (_ibasesSyncMode == IbasesSyncMode.None)
        {
            RestartAutoSync();
            return;
        }

        if (_nextScheduleRun is null)
        {
            if (ComputeNextRunTime(out var nextRun))
                _nextScheduleRun = nextRun;
            return;
        }

        if (DateTime.Now >= _nextScheduleRun.Value)
        {
            SynchronizeWithIbases();
            // Планируем следующий запуск.
            if (ComputeNextRunTime(out var nextRun))
                _nextScheduleRun = nextRun;
        }
    }

    /// <summary>
    /// Вычисляет время следующего запуска синхронизации для выбранного режима.
    /// Для интервала — текущее время плюс интервал; для расписания — ближайшее
    /// заданное время (завтра, если время уже прошло сегодня).
    /// </summary>
    private bool ComputeNextRunTime(out DateTime nextRun)
    {
        nextRun = default;

        if (_ibasesSyncTrigger == IbasesSyncTrigger.Interval)
        {
            var intervalMinutes = Math.Max(1, _ibasesSyncIntervalMinutes);
            nextRun = DateTime.Now.AddMinutes(intervalMinutes);
            return true;
        }

        if (_ibasesSyncTrigger == IbasesSyncTrigger.Schedule)
        {
            if (string.IsNullOrWhiteSpace(_ibasesSyncScheduleTime) ||
                !TimeSpan.TryParse(_ibasesSyncScheduleTime, out var time))
            {
                return false;
            }

            var now = DateTime.Now;
            var today = now.Date + time;
            if (today <= now)
            {
                today = today.AddDays(1);
            }

            nextRun = today;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Устанавливает признак запущенной автоматической синхронизации и уведомляет подписчиков.
    /// </summary>
    private void SetAutoSyncRunning(bool running)
    {
        if (_syncTimerRunning != running)
        {
            _syncTimerRunning = running;
            OnPropertyChanged(nameof(IsAutoSyncRunning));
        }
    }

    /// <summary>
    /// Выполняет экспорт текущего списка баз и групп в файл ibases.v8i
    /// (используется для ручного экспорта из окна настроек).
    /// </summary>
    public bool ExportToIbases()
    {
        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
            return false;

        try
        {
            if (_ibasesBackupEnabled && File.Exists(filePath))
            {
                try { IbasesBackupService.CreateBackup(filePath, _ibasesBackupKeepCount); }
                catch { /* не блокируем экспорт */ }
            }
            _ibasesSync.Export(filePath, Infobases, Groups);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Выполняет импорт баз из файла ibases.v8i в приложение
    /// (используется для ручного импорта из окна настроек).
    /// </summary>
    public bool ImportFromIbases()
    {
        var filePath = ResolveIbasesFilePath();
        if (filePath is null || !File.Exists(filePath))
            return false;

        try
        {
            _ibasesSync.Import(filePath, Infobases, Groups);
            InfobasesView.Refresh();
            Save();
            SaveGroups();
            RebuildGroupTree();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Определяет путь к файлу ibases.v8i: пользовательский путь из настроек,
    /// либо стандартный путь 1С, если пользовательский не задан.
    /// </summary>
    private string? ResolveIbasesFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_ibasesSyncFilePath))
            return _ibasesSyncFilePath;

        return IbasesV8iImporter.FindDefaultPath();
    }

    /// <summary>
    /// Применяет изменения списка групп, внесённые в окне настроек.
    /// </summary>
    public void ApplyGroupChanges(IEnumerable<Group> groups)
    {
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
        SaveGroups();
        InfobasesView.Refresh();
        RebuildGroupTree();
    }
}
#endif
