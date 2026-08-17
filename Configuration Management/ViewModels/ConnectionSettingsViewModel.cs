using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel для диалога настройки подключения к информационной базе.
/// </summary>
public class ConnectionSettingsViewModel : ViewModelBase
{
    private bool _isLoading;
    private bool _hasChanges;

    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _group = string.Empty;
    private string _description = string.Empty;
    private string _configurationName = string.Empty;
    private string _configurationVersion = string.Empty;
    private string _platformVersion = string.Empty;
    private string _architecture = "32-priority";
    private string _launchMode = "Автоматический";
    private string _launchParameters = string.Empty;
    private ConnectionType _connectionType = ConnectionType.ClientServer;
    private string _server = string.Empty;
    private string _databaseName = string.Empty;
    private string _filePath = string.Empty;
    private string _webUrl = string.Empty;
    private string _user = string.Empty;
    private string _password = string.Empty;
    private AuthenticationMode _authenticationMode = AuthenticationMode.Prompt;
    private int _port = 1541;
    private Group? _selectedGroup;
    private string _connectionString = string.Empty;

    /// <summary>
    /// Создаёт ViewModel с указанным списком доступных групп.
    /// </summary>
    public ConnectionSettingsViewModel(IEnumerable<Group>? groups = null)
    {
        Groups = new ObservableCollection<Group>(groups ?? new List<Group>());
        InstalledPlatformVersions = new ObservableCollection<string>();
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Признак того, что в настройки были внесены изменения.
    /// </summary>
    public bool HasChanges => _hasChanges;

    /// <summary>
    /// Обработчик изменения свойств: помечает наличие изменений.
    /// </summary>
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoading || e.PropertyName == nameof(HasChanges))
            return;

        _hasChanges = true;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Наименование базы.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Идентификатор базы 1С (GUID из ibases.v8i).</summary>
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>Список доступных групп.</summary>
    public ObservableCollection<Group> Groups { get; }

    /// <summary>Выбранная группа.</summary>
    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                // В свойстве Group храним полный путь группы в иерархии
                // (например, «Учёт / Бухгалтерия»), чтобы сохранялась структура.
                Group = value is null
                    ? string.Empty
                    : GroupHierarchyHelper.GetFullPath(value, Groups);
                OnPropertyChanged(nameof(GroupDisplayPath));
            }
        }
    }

    /// <summary>Группа базы (полный путь в иерархии).</summary>
    public string Group
    {
        get => _group;
        set
        {
            if (SetProperty(ref _group, value))
                OnPropertyChanged(nameof(GroupDisplayPath));
        }
    }

    /// <summary>Текст для поля группы: путь или «Без группы».</summary>
    public string GroupDisplayPath =>
        string.IsNullOrWhiteSpace(_group) ? "— Без группы —" : _group;

    /// <summary>
    /// Находит группу по полному пути (например, «Учёт / Бухгалтерия»).
    /// </summary>
    private Group? FindGroupByPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        return GroupHierarchyHelper.FindByFullPath(fullPath, Groups);
    }

    /// <summary>Описание базы.</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>Версия платформы.</summary>
    public string PlatformVersion
    {
        get => _platformVersion;
        set => SetProperty(ref _platformVersion, value);
    }
    public string ConfigurationName
    {
        get => _configurationName;
        set => SetProperty(ref _configurationName, value ?? string.Empty);
    }

    public string ConfigurationVersion
    {
        get => _configurationVersion;
        set => SetProperty(ref _configurationVersion, value ?? string.Empty);
    }



    /// <summary>
    /// Разрядность запуска клиента (как в 1С:Предприятие):
    /// «32», «64», «32-priority» (по умолчанию в 1С), «64-priority».
    /// </summary>
    public string Architecture
    {
        get => _architecture;
        set
        {
            if (SetProperty(ref _architecture, NormalizeArchitecture(value)))
            {
                OnPropertyChanged(nameof(IsArchitecture32));
                OnPropertyChanged(nameof(IsArchitecture64));
                OnPropertyChanged(nameof(IsArchitecture32Priority));
                OnPropertyChanged(nameof(IsArchitecture64Priority));
                OnPropertyChanged(nameof(ArchitectureHint));
            }
        }
    }

    /// <summary>Всегда 32 (x86).</summary>
    public bool IsArchitecture32
    {
        get => Architecture == "32";
        set { if (value) Architecture = "32"; }
    }

    /// <summary>Всегда 64 (x86-64).</summary>
    public bool IsArchitecture64
    {
        get => Architecture == "64";
        set { if (value) Architecture = "64"; }
    }

    /// <summary>Приоритет 32 (x86) — режим по умолчанию в 1С.</summary>
    public bool IsArchitecture32Priority
    {
        get => Architecture == "32-priority";
        set { if (value) Architecture = "32-priority"; }
    }

    /// <summary>Приоритет 64 (x86-64).</summary>
    public bool IsArchitecture64Priority
    {
        get => Architecture == "64-priority";
        set { if (value) Architecture = "64-priority"; }
    }

    /// <summary>ОС 64-битная (иначе 64-клиент недоступен).</summary>
    public bool IsOs64Bit { get; } = Environment.Is64BitOperatingSystem;

    /// <summary>Краткая подсказка по выбранному режиму разрядности.</summary>
    public string ArchitectureHint => Architecture switch
    {
        "32" => "Всегда запускается 32-разрядный клиент. 64-битные версии игнорируются.",
        "64" => "Всегда запускается 64-разрядный клиент. 32-битные версии игнорируются.",
        "64-priority" => "Предпочитается 64-битный клиент; если есть более новая 32-битная версия — будет она.",
        _ => "Предпочитается 32-битный клиент (как в 1С по умолчанию); более новая 64-битная версия имеет приоритет."
    };

    /// <summary>Нормализация значения разрядности (совместимость со старыми «32»/«64»).</summary>
    public static string NormalizeArchitecture(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "64" or "x64" or "x86-64" or "x86_64" => "64",
            "32" or "x86" => "32",
            "64-priority" or "priority64" or "x86-64-priority" => "64-priority",
            "32-priority" or "priority32" or "x86-priority" or "" => "32-priority",
            _ => "32-priority"
        };
    }

    /// <summary>Список установленных версий платформы 1С для выбора.</summary>
    public ObservableCollection<string> InstalledPlatformVersions { get; }

    /// <summary>
    /// Устанавливает список установленных версий платформы 1С.
    /// </summary>
    public void SetInstalledPlatformVersions(IEnumerable<string> versions)
    {
        InstalledPlatformVersions.Clear();
        foreach (var version in versions)
        {
            InstalledPlatformVersions.Add(version);
        }
    }

    /// <summary>
    /// Список доступных серверов 1С (из клиент-серверных баз в списке) для выпадающего списка.
    /// </summary>
    public ObservableCollection<string> AvailableServers { get; } = new();

    /// <summary>
    /// Устанавливает список доступных серверов 1С из других баз списка.
    /// Сортируем по алфавиту и исключаем пустые значения.
    /// </summary>
    public void SetAvailableServers(IEnumerable<string>? servers)
    {
        AvailableServers.Clear();
        if (servers is null)
            return;

        foreach (var server in servers
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Select(s => s.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            AvailableServers.Add(server);
        }
    }

    /// <summary>
    /// Список доступных портов серверов 1С (из клиент-серверных баз в списке) для выпадающего списка.
    /// </summary>
    public ObservableCollection<string> AvailablePorts { get; } = new();

    /// <summary>
    /// Устанавливает список доступных портов серверов 1С из других баз списка.
    /// Сортируем по возрастанию и исключаем пустые/нулевые значения.
    /// </summary>
    public void SetAvailablePorts(IEnumerable<int>? ports)
    {
        AvailablePorts.Clear();
        if (ports is null)
            return;

        foreach (var port in ports
                     .Where(p => p > 0)
                     .Distinct()
                     .OrderBy(p => p))
        {
            AvailablePorts.Add(port.ToString());
        }
    }

    /// <summary>Режим запуска (строка: Автоматический, Тонкий клиент, Толстый клиент, Веб-клиент).</summary>
    public string LaunchMode
    {
        get => _launchMode;
        set
        {
            if (SetProperty(ref _launchMode, value))
            {
                OnPropertyChanged(nameof(IsAutoMode));
                OnPropertyChanged(nameof(IsThinClient));
                OnPropertyChanged(nameof(IsThickClient));
                OnPropertyChanged(nameof(IsWebClient));
                OnPropertyChanged(nameof(LaunchModeHint));
            }
        }
    }

    /// <summary>Автоматический режим запуска.</summary>
    public bool IsAutoMode
    {
        get => LaunchMode == "Автоматический";
        set { if (value) LaunchMode = "Автоматический"; }
    }

    /// <summary>Тонкий клиент.</summary>
    public bool IsThinClient
    {
        get => LaunchMode == "Тонкий клиент";
        set { if (value) LaunchMode = "Тонкий клиент"; }
    }

    /// <summary>Толстый клиент.</summary>
    public bool IsThickClient
    {
        get => LaunchMode == "Толстый клиент";
        set { if (value) LaunchMode = "Толстый клиент"; }
    }

    /// <summary>Веб-клиент.</summary>
    public bool IsWebClient
    {
        get => LaunchMode == "Веб-клиент";
        set { if (value) LaunchMode = "Веб-клиент"; }
    }

    /// <summary>Подсказка по выбранному режиму запуска.</summary>
    public string LaunchModeHint => LaunchMode switch
    {
        "Тонкий клиент" => "Запуск в режиме управляемого приложения (тонкий клиент 1cv8c).",
        "Толстый клиент" => "Запуск толстого клиента 1cv8 (обычное/управляемое приложение).",
        "Веб-клиент" => "Открытие базы в веб-браузере. Доступно при веб-публикации или клиент-сервере.",
        _ => "Режим выбирает платформа 1С автоматически по настройкам информационной базы."
    };

    /// <summary>Дополнительные параметры запуска платформы 1С.</summary>
    public string LaunchParameters
    {
        get => _launchParameters;
        set => SetProperty(ref _launchParameters, value);
    }

    /// <summary>Тип подключения.</summary>
    public ConnectionType ConnectionType
    {
        get => _connectionType;
        set
        {
            if (SetProperty(ref _connectionType, value))
            {
                OnPropertyChanged(nameof(IsClientServer));
                OnPropertyChanged(nameof(IsFile));
                OnPropertyChanged(nameof(IsWebServer));
                OnPropertyChanged(nameof(IsWebClientAllowed));
                // Если веб-клиент выбран, а тип подключения больше не позволяет его — сбрасываем
                if (!IsWebClientAllowed && IsWebClient)
                    LaunchMode = "Автоматический";
            }
        }
    }

    /// <summary>Признак клиент-серверного подключения.</summary>
    public bool IsClientServer
    {
        get => ConnectionType == ConnectionType.ClientServer;
        set { if (value) ConnectionType = ConnectionType.ClientServer; }
    }

    /// <summary>Признак файлового подключения.</summary>
    public bool IsFile
    {
        get => ConnectionType == ConnectionType.File;
        set { if (value) ConnectionType = ConnectionType.File; }
    }

    /// <summary>Признак подключения через веб-сервер.</summary>
    public bool IsWebServer
    {
        get => ConnectionType == ConnectionType.WebServer;
        set { if (value) ConnectionType = ConnectionType.WebServer; }
    }

    /// <summary>
    /// Веб-клиент доступен только при подключении через веб-сервер
    /// или клиент-серверном подключении (с публикацией).
    /// </summary>
    public bool IsWebClientAllowed =>
        ConnectionType == ConnectionType.WebServer || ConnectionType == ConnectionType.ClientServer;

    /// <summary>Имя сервера.</summary>
    public string Server
    {
        get => _server;
        set => SetProperty(ref _server, value);
    }

    /// <summary>Имя базы на сервере.</summary>
    public string DatabaseName
    {
        get => _databaseName;
        set => SetProperty(ref _databaseName, value);
    }

    /// <summary>Путь к файловой базе.</summary>
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    /// <summary>URL веб-публикации.</summary>
    public string WebUrl
    {
        get => _webUrl;
        set => SetProperty(ref _webUrl, value);
    }

    /// <summary>Пользователь.</summary>
    public string User
    {
        get => _user;
        set => SetProperty(ref _user, value);
    }

    /// <summary>Пароль.</summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    /// <summary>Режим аутентификации.</summary>
    public AuthenticationMode AuthenticationMode
    {
        get => _authenticationMode;
        set
        {
            if (SetProperty(ref _authenticationMode, value))
            {
                OnPropertyChanged(nameof(IsAuthPrompt));
                OnPropertyChanged(nameof(IsAuthCredentials));
                OnPropertyChanged(nameof(IsAuthWindows));
                OnPropertyChanged(nameof(IsCredentialsVisible));
            }
        }
    }

    /// <summary>Запрашивать имя и пароль.</summary>
    public bool IsAuthPrompt
    {
        get => AuthenticationMode == AuthenticationMode.Prompt;
        set { if (value) AuthenticationMode = AuthenticationMode.Prompt; }
    }

    /// <summary>Выполнять вход автоматически.</summary>
    public bool IsAuthCredentials
    {
        get => AuthenticationMode == AuthenticationMode.Credentials;
        set { if (value) AuthenticationMode = AuthenticationMode.Credentials; }
    }

    /// <summary>Аутентификация операционной системы.</summary>
    public bool IsAuthWindows
    {
        get => AuthenticationMode == AuthenticationMode.Windows;
        set { if (value) AuthenticationMode = AuthenticationMode.Windows; }
    }

    /// <summary>Видимость полей логина/пароля (только при автоматическом входе).</summary>
    public bool IsCredentialsVisible => AuthenticationMode == AuthenticationMode.Credentials;

    /// <summary>Совместимость: признак аутентификации ОС.</summary>
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

    /// <summary>Порт сервера.</summary>
    public int Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
                OnPropertyChanged(nameof(PortText));
        }
    }

    /// <summary>
    /// Текстовое представление порта для редактируемого выпадающего списка.
    /// Позволяет и выбрать порт из списка доступных, и ввести значение вручную.
    /// </summary>
    public string PortText
    {
        get => _port > 0 ? _port.ToString() : string.Empty;
        set
        {
            if (int.TryParse(value, out var parsed) && parsed > 0 && parsed <= 65535)
                Port = parsed;
        }
    }

    /// <summary>
    /// Строка подключения 1С для ввода/отображения в окне настроек базы.
    /// Может быть введена вручную или вставлена из буфера обмена.
    /// Всегда доступна в окне (не зависит от выбранной вкладки).
    /// </summary>
    public string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (SetProperty(ref _connectionString, value))
                OnPropertyChanged(nameof(HasConnectionString));
        }
    }

    /// <summary>Признак того, что строка подключения не пустая.</summary>
    public bool HasConnectionString => !string.IsNullOrWhiteSpace(_connectionString);

    /// <summary>
    /// Применяет указанную строку подключения 1С к полям ViewModel.
    /// Разбивает строку на тип подключения, сервер/порт, имя базы, путь файла или URL,
    /// пользователя и пароль. Если наименование базы не задано — подставляет имя базы (Ref)
    /// или имя каталога файловой базы.
    /// </summary>
    /// <param name="connectionString">Строка подключения 1С.</param>
    public void ApplyConnectionString(string? connectionString)
    {
        var parsed = ConnectionSettings.ParseConnectionString(connectionString);

        ConnectionType = parsed.Type;
        Server = parsed.Server;
        DatabaseName = parsed.DatabaseName;
        FilePath = parsed.FilePath;
        WebUrl = parsed.WebUrl;
        User = parsed.User;
        Password = parsed.Password;
        AuthenticationMode = parsed.AuthenticationMode;
        Port = parsed.Port;

        // Если наименование не задано — предлагаем имя базы (Ref) или имя файла.
        if (string.IsNullOrWhiteSpace(Name))
        {
            var suggestedName = parsed.Type switch
            {
                ConnectionType.File => SuggestNameFromPath(parsed.FilePath),
                ConnectionType.WebServer => parsed.WebUrl,
                _ => parsed.DatabaseName
            };
            if (!string.IsNullOrWhiteSpace(suggestedName))
            {
                Name = suggestedName;
            }
        }
    }

    /// <summary>
    /// Формирует имя базы из пути к файловой базе (имя последнего каталога).
    /// </summary>
    private static string SuggestNameFromPath(string? filePath)
    {
        var path = (filePath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    /// <summary>
    /// Заполняет ViewModel из информационной базы.
    /// </summary>
    public void LoadFrom(Infobase infobase)
    {
        _isLoading = true;
        try
        {
            Id = infobase.Id;
            Name = infobase.Name;
            Group = infobase.Group;
            SelectedGroup = FindGroupByPath(infobase.Group);
            Description = infobase.Description;
            PlatformVersion = infobase.PlatformVersion;
            ConfigurationName = infobase.ConfigurationName;
            ConfigurationVersion = infobase.ConfigurationVersion;
            Architecture = NormalizeArchitecture(infobase.Architecture);
            LaunchMode = infobase.LaunchMode;
            LaunchParameters = infobase.LaunchParameters;

            var conn = infobase.Connection;
            ConnectionType = conn.Type;
            Server = conn.Server;
            DatabaseName = conn.DatabaseName;
            FilePath = conn.FilePath;
            WebUrl = conn.WebUrl;
            User = conn.User;
            Password = conn.Password;
            // Восстанавливаем режим аутентификации (с учётом старых сохранений).
            if (conn.AuthenticationMode != AuthenticationMode.Prompt
                || !string.IsNullOrWhiteSpace(conn.User)
                || conn.UseOsAuthentication)
            {
                AuthenticationMode = conn.AuthenticationMode;
                // Старые файлы: если был только флаг ОС или логин без режима.
                if (conn.UseOsAuthentication && conn.AuthenticationMode == AuthenticationMode.Prompt
                    && string.IsNullOrWhiteSpace(conn.User))
                    AuthenticationMode = AuthenticationMode.Windows;
                else if (!string.IsNullOrWhiteSpace(conn.User) && conn.AuthenticationMode == AuthenticationMode.Prompt
                         && !conn.UseOsAuthentication)
                    AuthenticationMode = AuthenticationMode.Credentials;
            }
            else
            {
                AuthenticationMode = AuthenticationMode.Prompt;
            }
            Port = conn.Port;
            // Заполняем поле строки подключения для отображения/редактирования.
            _connectionString = conn.ToConnectionString();
        }
        finally
        {
            _isLoading = false;
        }

        _hasChanges = false;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>
    /// Применяет значения ViewModel к информационной базе.
    /// </summary>
    public void ApplyTo(Infobase infobase)
    {
        // Сохраняем идентификатор базы, чтобы не потерять его при редактировании.
        infobase.Id = Id;
        infobase.Name = Name;
        infobase.Group = Group;
        infobase.Description = Description;
        infobase.PlatformVersion = PlatformVersion;
        infobase.ConfigurationName = ConfigurationName;
        infobase.ConfigurationVersion = ConfigurationVersion;
        infobase.Architecture = NormalizeArchitecture(Architecture);
        infobase.LaunchMode = string.IsNullOrWhiteSpace(LaunchMode) ? "Автоматический" : LaunchMode;
        infobase.LaunchParameters = LaunchParameters ?? string.Empty;

        if (infobase.Connection is null)
            infobase.Connection = new ConnectionSettings();
        var conn = infobase.Connection;
        conn.Type = ConnectionType;
        conn.Server = Server;
        conn.DatabaseName = DatabaseName;
        conn.FilePath = FilePath;
        conn.WebUrl = WebUrl;
        conn.User = User;
        conn.Password = Password;
        conn.AuthenticationMode = AuthenticationMode;
        conn.Port = Port;
    }
}