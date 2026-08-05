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

    /// <summary>Использовать аутентификацию ОС.</summary>
    public bool UseOsAuthentication { get; set; } = true;

    /// <summary>Порт сервера (для клиент-серверного режима).</summary>
    public int Port { get; set; } = 1541;

    /// <summary>
    /// Возвращает строку соединения для отображения без параметров запуска,
    /// логина и пароля: только путь к файловой базе или сервер и имя базы.
    /// Значения сервера и имени базы заключаются в кавычки.
    /// </summary>
    public string ToConnectionString()
    {
        return Type switch
        {
            ConnectionType.File => $"File=\"{FilePath}\"",
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
    ClientServer
}