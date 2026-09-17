namespace Configuration_Management.Models;

/// <summary>
/// Параметры создания информационной базы (файловой или клиент-серверной).
/// Собирается из полей окна создания и передаётся в <c>ICreateInfobaseService</c>.
/// Только примитивные значения — сервис не зависит от UI-контролов WPF/Avalonia.
/// </summary>
public sealed class CreateInfobaseRequest
{
    /// <summary>Наименование информационной базы.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Признак создания из шаблона (.cf/.dt) вместо пустой базы.</summary>
    public bool FromTemplate { get; set; }

    /// <summary>Путь к файлу шаблона (.cf/.dt); используется при <see cref="FromTemplate"/>.</summary>
    public string? TemplatePath { get; set; }

    /// <summary>Выбранная версия платформы 1С (может содержать суффикс разрядности « (32)/(64)»).</summary>
    public string PlatformVersion { get; set; } = string.Empty;

    /// <summary>Признак файловой базы (false — клиент-серверная).</summary>
    public bool IsFile { get; set; }

    /// <summary>Путь к каталогу файловой базы (для файлового режима).</summary>
    public string? FilePath { get; set; }

    /// <summary>Имя сервера 1С (для клиент-серверного режима).</summary>
    public string? Server { get; set; }

    /// <summary>Имя базы на сервере (для клиент-серверного режима).</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Тип СУБД (например, MSSQLServer, PostgreSQL).</summary>
    public string? Dbms { get; set; }

    /// <summary>Сервер СУБД.</summary>
    public string? DbServer { get; set; }

    /// <summary>Имя базы данных на сервере СУБД.</summary>
    public string? DbName { get; set; }

    /// <summary>Пользователь СУБД.</summary>
    public string? DbUser { get; set; }

    /// <summary>Пароль пользователя СУБД.</summary>
    public string? DbPassword { get; set; }

    /// <summary>Создавать базу данных на сервере СУБД.</summary>
    public bool CreateSqlDatabase { get; set; }

    /// <summary>Блокировать фоновые задания (SchJobDn="Y").</summary>
    public bool BlockScheduledJobs { get; set; }

    /// <summary>Путь группы, в которую добавляется созданная база (может быть пустым).</summary>
    public string GroupPath { get; set; } = string.Empty;
}