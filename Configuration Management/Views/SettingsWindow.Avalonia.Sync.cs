#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux), часть «Резервное копирование»
    /// и синхронизация ibases.v8i.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строит строку состояния блока синхронизации по тем значениям, что
        /// сейчас в полях окна, а не по сохранённым. Набор ключей и порядок
        /// частей те же, что у BuildStatusText в ViewModels/SettingsViewModel.cs:
        /// сам метод лежит в файле, который в Linux-сборку не входит.
        /// </summary>
        private static string BuildSyncStatus(IbasesSyncMode mode, string? filePath,
            IbasesSyncTrigger trigger, string? interval, string? scheduleTime)
        {
            if (mode == IbasesSyncMode.None)
                return LocalizationManager.T("Settings.Ibases.StatusDisabled");

            var path = string.IsNullOrWhiteSpace(filePath)
                ? Services.IbasesV8iImporter.FindDefaultPath()
                : filePath.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return LocalizationManager.T("Settings.Ibases.StatusFileNotFound");

            var modeText = mode switch
            {
                IbasesSyncMode.Import => LocalizationManager.T("Settings.Ibases.ModeImportShort"),
                IbasesSyncMode.Export => LocalizationManager.T("Settings.Ibases.ModeExportShort"),
                _ => LocalizationManager.T("Settings.Ibases.ModeBothShort")
            };
            var triggerText = trigger switch
            {
                IbasesSyncTrigger.Interval => string.Format(
                    LocalizationManager.T("Settings.Ibases.TriggerIntervalShort"),
                    int.TryParse(interval, out var minutes) && minutes > 0 ? minutes : 30),
                IbasesSyncTrigger.Schedule => string.Format(
                    LocalizationManager.T("Settings.Ibases.TriggerScheduleShort"), scheduleTime),
                _ => LocalizationManager.T("Settings.Ibases.TriggerStartupShort")
            };
            return string.Format(LocalizationManager.T("Settings.Ibases.StatusFormat"),
                path, modeText, triggerText);
        }
    }
}
#endif