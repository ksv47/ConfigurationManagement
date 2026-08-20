using System.Windows;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IDialogService"/> на базе WPF MessageBox и стандартных файловых диалогов.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string message, string title = "Информация") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "Внимание") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "Ошибка") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "Подтверждение") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? OpenFileDialog(string title = "Открыть файл", string filter = "", string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFileDialog(string title = "Сохранить файл", string defaultFileName = "", string filter = "", string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFolderDialog(string title = "Выбор папки", string? initialDirectory = null)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            SelectedPath = NormalizeDirectory(initialDirectory)
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>Приводит фильтр к формату OpenFileDialog (разделитель '|'). Пустое значение → все файлы.</summary>
    private static string BuildFilter(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? "Все файлы (*.*)|*.*" : filter;

    /// <summary>Пустой/несуществующий каталог → пустая строка (диалог откроется в стандартном месте).</summary>
    private static string NormalizeDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? string.Empty : directory;
}
