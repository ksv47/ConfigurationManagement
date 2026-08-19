namespace Configuration_Management.Models;

/// <summary>
/// Настройки подключения к хранилищу конфигурации 1С
/// (адрес сервера хранилища, имя хранилища, логин и пароль).
/// Используются при работе в Конфигураторе для входа «под собой».
/// </summary>
public class RepositorySettings
{
    /// <summary>Адрес сервера хранилища конфигурации (например «tcp://server» или «tcp://server:1542»).</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Имя хранилища конфигурации на сервере.</summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>Логин пользователя хранилища.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Пароль пользователя хранилища.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Признак того, что заполнен хотя бы адрес сервера хранилища.
    /// </summary>
    public bool HasServer => !string.IsNullOrWhiteSpace(Server);

    /// <summary>
    /// Адрес хранилища для отображения: «server» или «server\name».
    /// Если имя хранилища не задано — возвращается только адрес сервера.
    /// </summary>
    public string AddressDisplay
    {
        get
        {
            var server = (Server ?? string.Empty).Trim();
            var name = (RepositoryName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(server))
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
            return string.IsNullOrWhiteSpace(name) ? server : $"{server}\\{name}";
        }
    }
}