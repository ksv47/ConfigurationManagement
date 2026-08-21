namespace Configuration_Management.Services;

/// <summary>
/// Абстракция диалогов для соблюдения MVVM и упрощения тестирования ViewModel.
/// </summary>
public interface IDialogService
{
    /// <summary>Показывает информационное сообщение.</summary>
    void ShowInfo(string message, string title = "");

    /// <summary>Показывает предупреждение.</summary>
    void ShowWarning(string message, string title = "");

    /// <summary>Показывает ошибку.</summary>
    void ShowError(string message, string title = "");

    /// <summary>
    /// Запрашивает подтверждение у пользователя.
    /// </summary>
    /// <returns>True, если пользователь подтвердил действие.</returns>
    bool Confirm(string message, string title = "");

    /// <summary>
    /// Открывает диалог выбора одного файла.
    /// </summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="filter">Фильтр файлов (например "Конфигурация (*.cf)|*.cf"). Пустая строка — все файлы.</param>
    /// <param name="initialDirectory">Начальный каталог. Пустая строка — по умолчанию.</param>
    /// <returns>Полный путь выбранного файла или null при отмене.</returns>
    string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null);

    /// <summary>
    /// Открывает диалог сохранения файла.
    /// </summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="defaultFileName">Предлагаемое имя файла.</param>
    /// <param name="filter">Фильтр файлов. Пустая строка — все файлы.</param>
    /// <param name="initialDirectory">Начальный каталог. Пустая строка — по умолчанию.</param>
    /// <returns>Полный путь для сохранения или null при отмене.</returns>
    string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null);

    /// <summary>
    /// Открывает диалог выбора каталога.
    /// </summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="initialDirectory">Начальный каталог. Пустая строка — по умолчанию.</param>
    /// <returns>Полный путь выбранного каталога или null при отмене.</returns>
    string? OpenFolderDialog(string title = "", string? initialDirectory = null);
}
