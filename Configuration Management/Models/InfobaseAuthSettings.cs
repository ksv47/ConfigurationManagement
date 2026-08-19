namespace Configuration_Management.Models;

/// <summary>
/// Настройки авторизации пользователя при запуске информационной базы
/// (для «1С:Предприятие» или для «Конфигуратора»): режим аутентификации,
/// логин и пароль. Значения по умолчанию — запрос имени и пароля.
/// </summary>
public class InfobaseAuthSettings
{
    /// <summary>Режим аутентификации.</summary>
    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Prompt;

    /// <summary>Пользователь для подключения.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Пароль для подключения.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Использовать аутентификацию ОС (совместимость со старыми настройками).</summary>
    public bool UseOsAuthentication
    {
        get => AuthenticationMode == AuthenticationMode.Windows;
        set
        {
            if (value)
                AuthenticationMode = AuthenticationMode.Windows;
            else if (AuthenticationMode == AuthenticationMode.Windows)
                AuthenticationMode = AuthenticationMode.Prompt;
        }
    }

    /// <summary>
    /// Признак того, что настройки не изменялись относительно значений по умолчанию
    /// (запрос имени и пароля, без пользователя). Используется, чтобы понять,
    /// задана ли авторизация явно или нужно подставить авторизацию базы.
    /// </summary>
    public bool IsDefault => AuthenticationMode == AuthenticationMode.Prompt
        && string.IsNullOrWhiteSpace(User)
        && string.IsNullOrWhiteSpace(Password);
}