using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="IDialogService"/> на базе WPF MessageBox.
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
}
