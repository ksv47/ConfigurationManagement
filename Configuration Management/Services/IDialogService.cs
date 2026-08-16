namespace Configuration_Management.Services;

/// <summary>
/// Абстракция диалогов для соблюдения MVVM и упрощения тестирования ViewModel.
/// </summary>
public interface IDialogService
{
    /// <summary>Показывает информационное сообщение.</summary>
    void ShowInfo(string message, string title = "Информация");

    /// <summary>Показывает предупреждение.</summary>
    void ShowWarning(string message, string title = "Внимание");

    /// <summary>Показывает ошибку.</summary>
    void ShowError(string message, string title = "Ошибка");

    /// <summary>
    /// Запрашивает подтверждение у пользователя.
    /// </summary>
    /// <returns>True, если пользователь подтвердил действие.</returns>
    bool Confirm(string message, string title = "Подтверждение");
}
