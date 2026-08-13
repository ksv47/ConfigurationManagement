namespace Configuration_Management.Models;

/// <summary>
/// Настройки подключения к информационной базе.
/// </summary>
public class ConnectionSettings
{
    /// <summary>Тип подключения (файловый или клиент-серверный).</summary>
    public ConnectionType Type { get; set; } = ConnectionType.ClientServer;

    /// <summary>Имя сервера 1С (для клиент-серверного режима).</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Имя базы на сервере (для клиент-серверного режима).</summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Путь к файловой базе (для файлового режима).</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Пользователь для подключения.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Пароль для подключения.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Режим аутентификации (как в стандартном списке баз 1С):
    /// Prompt — запрашивать имя и пароль;
    /// Credentials — выполнять вход автоматически (логин/пароль);
    /// Windows — аутентификация операционной системы.
    /// </summary>
    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Prompt;

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

    /// <summary>Порт сервера (для клиент-серверного режима).</summary>
    public int Port { get; set; } = 1541;

    /// <summary>
    /// URL веб-публикации (для подключения через веб-сервер).
    /// Например: http://server/base или https://server/base.
    /// </summary>
    public string WebUrl { get; set; } = string.Empty;

    /// <summary>
    /// Возвращает строку соединения для отображения без параметров запуска,
    /// логина и пароля: только путь к файловой базе, сервер и имя базы или URL веб-публикации.
    /// </summary>
    public string ToConnectionString()
    {
        return Type switch
        {
            ConnectionType.File => $"File=\"{FilePath}\"",
            ConnectionType.WebServer => string.IsNullOrWhiteSpace(WebUrl) ? "WS=\"\"" : $"WS=\"{WebUrl}\"",
            _ => $"Srvr=\"{Server}\";Ref=\"{DatabaseName}\""
        };
    }
}

/// <summary>
/// Тип подключения информационной базы.
/// </summary>
public enum ConnectionType
{
    /// <summary>Файловый режим (файловая база на диске).</summary>
    File,

    /// <summary>Клиент-серверный режим (сервер 1С + СУБД).</summary>
    ClientServer,

    /// <summary>Подключение через веб-сервер (веб-публикация).</summary>
    WebServer
}

/// <summary>
/// Режим аутентификации при запуске информационной базы (аналог настроек стандартного лаунчера 1С).
/// </summary>
public enum AuthenticationMode
{
    /// <summary>Запрашивать имя и пароль при каждом запуске.</summary>
    Prompt,

    /// <summary>Выполнять вход автоматически по сохранённым логину и паролю.</summary>
    Credentials,

    /// <summary>Аутентификация операционной системы (Windows).</summary>
    Windows
}
