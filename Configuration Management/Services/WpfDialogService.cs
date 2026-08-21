using System.Windows;
using Configuration_Management.Localization;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IDialogService"/> на базе WPF MessageBox и стандартных файловых диалогов.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string message, string title = "") =>
        MessageBox.Show(message, DefaultTitle(title, "Common.Information"),
            MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "") =>
        MessageBox.Show(message, DefaultTitle(title, "Common.Warning"),
            MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "") =>
        MessageBox.Show(message, DefaultTitle(title, "Common.Error"),
            MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "") =>
        MessageBox.Show(message, DefaultTitle(title, "Common.Confirm"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = DefaultTitle(title, "Dialog.OpenFile"),
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = DefaultTitle(title, "Dialog.SaveFile"),
            FileName = defaultFileName,
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFolderDialog(string title = "", string? initialDirectory = null)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = DefaultTitle(title, "Dialog.SelectFolder"),
            SelectedPath = NormalizeDirectory(initialDirectory)
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>Подставляет локализованный заголовок по умолчанию, если переданный пуст.</summary>
    private static string DefaultTitle(string title, string fallbackKey) =>
        string.IsNullOrWhiteSpace(title) ? LocalizationManager.T(fallbackKey) : title;

    /// <summary>Приводит фильтр к формату OpenFileDialog (разделитель '|'). Пустое значение → все файлы.</summary>
    private static string BuildFilter(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? $"{LocalizationManager.T("Common.AllFiles")}|*.*" : filter;

    /// <summary>Пустой/несуществующий каталог → пустая строка (диалог откроется в стандартном месте).</summary>
    private static string NormalizeDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? string.Empty : directory;
}
