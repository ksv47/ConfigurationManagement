using System.Windows;
using Configuration_Management.Localization;
using Microsoft.Win32;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IDialogService"/> в стиле Material Design.
/// Предупреждения, подтверждения и ошибки показываются через собственное
/// модальное окно <see cref="MaterialMessageWindow"/> (а не стандартный MessageBox),
/// чтобы единообразно выглядеть в обеих темах приложения.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Info);

    public void ShowWarning(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Warning);

    public void ShowError(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Error);

    public bool Confirm(string message, string title = "")
    {
        var win = new MaterialMessageWindow(message, DefaultTitle(title, "Common.Confirm"), MaterialMessageKind.Question)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
        return win.Confirmed;
    }

    private static void Show(string message, string title, MaterialMessageKind kind)
    {
        var win = new MaterialMessageWindow(message, DefaultTitle(title, "Common.Information"), kind)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

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
