using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private string _platformVersion = string.Empty;
    private string _architecture = "32";
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

    /// <summary>Разрядность платформы при запуске («32» или «64»).</summary>
    public string Architecture
    {
        get => _architecture;
        set => SetProperty(ref _architecture, value);
    }

    /// <summary>Использовать 32-битную платформу.</summary>
    public bool IsArchitecture32
    {
        get => Architecture == "32";
        set { if (value) Architecture = "32"; }
    }

    /// <summary>Использовать 64-битную платформу.</summary>
    public bool IsArchitecture64
    {
        get => Architecture == "64";
        set { if (value) Architecture = "64"; }
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
        set => SetProperty(ref _port, value);
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
            Architecture = infobase.Architecture;
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
        infobase.Architecture = Architecture;
        infobase.LaunchMode = LaunchMode;
        infobase.LaunchParameters = LaunchParameters;

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