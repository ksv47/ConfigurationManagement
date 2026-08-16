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
    /// Возвращает имя сервера с портом в формате 1С: «host» или «host:port».
    /// Порт добавляется, если он задан и отличается от значения по умолчанию (1541),
    /// либо если в Server уже указан порт.
    /// </summary>
    public string GetServerWithPort()
    {
        var server = (Server ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(server))
            return server;

        // Если порт уже встроен в имя сервера — не дублируем.
        if (server.Contains(':'))
            return server;

        // Стандартный порт 1541 в 1С можно не указывать.
        if (Port > 0 && Port != 1541)
            return $"{server}:{Port}";

        return server;
    }

    /// <summary>
    /// Разбирает значение Srvr из строки подключения 1С: «host» или «host:port».
    /// Заполняет Server и Port.
    /// </summary>
    public static void ParseServerAndPort(string? srvrValue, ConnectionSettings settings)
    {
        if (settings is null)
            return;

        var value = (srvrValue ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            settings.Server = string.Empty;
            return;
        }

        // IPv6 в квадратных скобках: [2001:db8::1]:1541
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close > 0)
            {
                settings.Server = value.Substring(0, close + 1);
                if (close + 1 < value.Length && value[close + 1] == ':')
                {
                    var portPart = value.Substring(close + 2).Trim();
                    if (int.TryParse(portPart, out var p) && p > 0)
                        settings.Port = p;
                }
                return;
            }
        }

        // host:port — берём последнее «:», чтобы не ломать имена с двоеточием без порта.
        var colon = value.LastIndexOf(':');
        if (colon > 0 && colon < value.Length - 1)
        {
            var host = value.Substring(0, colon).Trim();
            var portPart = value.Substring(colon + 1).Trim();
            if (int.TryParse(portPart, out var port) && port > 0 && port <= 65535)
            {
                settings.Server = host;
                settings.Port = port;
                return;
            }
        }

        settings.Server = value;
    }

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
            _ => $"Srvr=\"{GetServerWithPort()}\";Ref=\"{DatabaseName}\""
        };
    }

    /// <summary>
    /// Разбирает строку подключения 1С на отдельные поля настроек.
    /// Поддерживаются форматы:
    /// File="C:\path"  |  WS="http://server/base"  |  Srvr="host";Ref="base";Usr="user";Pwd="pass"
    /// Порт сервера (Srvr="host:port") выносится в отдельное свойство <see cref="Port"/>.
    /// Возвращает новые настройки подключения, заполненные из разобранной строки.
    /// </summary>
    /// <param name="connectionString">Строка подключения 1С.</param>
    public static ConnectionSettings ParseConnectionString(string? connectionString)
    {
        var settings = new ConnectionSettings();
        var connect = (connectionString ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connect))
            return settings;

        // Файловый режим: File="C:\path".
        var filePath = ExtractQuoted(connect, "File");
        if (filePath != null)
        {
            settings.Type = ConnectionType.File;
            settings.FilePath = filePath;
            return settings;
        }

        // Веб-публикация: WS="http://server/base".
        var wsUrl = ExtractQuoted(connect, "WS");
        if (wsUrl != null)
        {
            settings.Type = ConnectionType.WebServer;
            settings.WebUrl = wsUrl;
            return settings;
        }

        // Клиент-серверный режим: Srvr="host";Ref="base";Usr="user";Pwd="pass".
        settings.Type = ConnectionType.ClientServer;
        ParseServerAndPort(ExtractQuoted(connect, "Srvr"), settings);
        settings.DatabaseName = ExtractQuoted(connect, "Ref") ?? string.Empty;
        settings.User = ExtractQuoted(connect, "Usr") ?? string.Empty;
        settings.Password = ExtractQuoted(connect, "Pwd") ?? string.Empty;

        // Если указан логин — вход автоматический; иначе — запрос имени и пароля.
        settings.AuthenticationMode = !string.IsNullOrWhiteSpace(settings.User)
            ? AuthenticationMode.Credentials
            : AuthenticationMode.Prompt;

        return settings;
    }

    /// <summary>
    /// Извлекает значение параметра из строки подключения 1С.
    /// Например, для «Srvr="server"» вернёт «server».
    /// Поддерживает пробелы вокруг знака «=» и значения без кавычек.
    /// </summary>
    private static string? ExtractQuoted(string source, string key)
    {
        var marker = key + "=";
        var idx = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Вариант с пробелом перед «=» (например, «Srvr = "server"»).
            var spacedMarker = key + " =";
            idx = source.IndexOf(spacedMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;
            idx += spacedMarker.Length - 1; // указываем на «=»
        }
        else
        {
            idx += marker.Length - 1; // указываем на «=»
        }

        var start = idx + 1; // сразу после «=»
        if (start >= source.Length)
            return null;

        // Пропускаем пробелы.
        while (start < source.Length && source[start] == ' ')
            start++;

        if (start >= source.Length)
            return null;

        // Значение в кавычках.
        if (source[start] == '"')
        {
            var end = start + 1;
            while (end < source.Length && source[end] != '"')
                end++;

            if (end >= source.Length)
                return null;

            return source.Substring(start + 1, end - start - 1);
        }

        // Значение без кавычек — до точки с запятой или конца строки.
        var valueEnd = source.IndexOf(';', start);
        if (valueEnd < 0)
            valueEnd = source.Length;

        return source.Substring(start, valueEnd - start).Trim();
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
