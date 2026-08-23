namespace Configuration_Management.Models;

/// <summary>
/// Глобальная настройка: что делать с окном приложения сразу после успешного запуска
/// информационной базы или конфигуратора 1С.
/// </summary>
public enum AfterLaunchAction
{
    /// <summary>Ничего не делать — окно остаётся в текущем состоянии.</summary>
    None,
    /// <summary>Свернуть главное окно (в трей/панель задач).</summary>
    MinimizeToTray,
    /// <summary>Закрыть (увести в трей) главное окно, оставив приложение работать в фоне.</summary>
    Close
}

/// <summary>Вспомогательные методы работы с <see cref="AfterLaunchAction"/>.</summary>
public static class AfterLaunchActionHelper
{
    /// <summary>Строковое представление для сохранения в настройках.</summary>
    public static string ToSettingString(this AfterLaunchAction action)
        => action switch
        {
            AfterLaunchAction.MinimizeToTray => "MinimizeToTray",
            AfterLaunchAction.Close => "Close",
            _ => "None"
        };

    /// <summary>Разбирает строковое значение из настроек (по умолчанию — <see cref="AfterLaunchAction.None"/>).</summary>
    public static AfterLaunchAction Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AfterLaunchAction.None;

        if (value.Trim().Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase) ||
            value.Trim().Equals("Minimize", StringComparison.OrdinalIgnoreCase))
            return AfterLaunchAction.MinimizeToTray;

        if (value.Trim().Equals("Close", StringComparison.OrdinalIgnoreCase))
            return AfterLaunchAction.Close;

        return AfterLaunchAction.None;
    }
}