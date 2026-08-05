using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Configuration_Management.Models;

/// <summary>
/// Представляет информационную базу (аналог информационной базы 1С).
/// </summary>
public class Infobase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    /// <summary>
    /// Идентификатор базы 1С (GUID из файла ibases.v8i, ключ ID).
    /// Используется для точной очистки кеша 1С.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Наименование информационной базы.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Группа, к которой относится база.</summary>
    public string Group { get; set; } = string.Empty;

    private bool _isFavorite;

    /// <summary>Признак избранной базы.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    private bool _isPinned;

    /// <summary>Признак закреплённой базы (отображается вверху списка без группы).</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    /// <summary>Дата и время последнего запуска базы.</summary>
    public DateTime? LastLaunchDate { get; set; }

    /// <summary>Настройки подключения к базе.</summary>
    public ConnectionSettings Connection { get; set; } = new();

    /// <summary>Версия платформы 1С.</summary>
    public string PlatformVersion { get; set; } = string.Empty;

    /// <summary>Режим запуска (Автоматический, Тонкий клиент, Толстый клиент, Веб-клиент).</summary>
    public string LaunchMode { get; set; } = "Автоматический";

    /// <summary>Дополнительные параметры запуска платформы 1С (например, /UC, /DisableStartupMessages и др.).</summary>
    public string LaunchParameters { get; set; } = string.Empty;

    /// <summary>Тип клиента (тонкий или толстый).</summary>
    public string ClientType { get; set; } = "Тонкий";

    /// <summary>Описание базы.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Теги базы данных.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Дерево метаданных конфигурации.</summary>
    public MetadataNode? MetadataRoot { get; set; }

    /// <summary>
    /// Возвращает строку соединения для отображения.
    /// </summary>
    public string ConnectionStringDisplay => Connection.ToConnectionString();

    /// <summary>
    /// Название группы для отображения. Базы без группы отображаются в группе «Без группы».
    /// </summary>
    public string GroupDisplay => string.IsNullOrWhiteSpace(Group) ? "Без группы" : Group;

    /// <summary>
    /// Возвращает путь к файловой базе в кавычках (без префикса File=).
    /// Для клиент-серверного режима возвращает строку соединения.
    /// </summary>
    public string ConnectionPathDisplay => Connection.Type switch
    {
        ConnectionType.File => $"\"{Connection.FilePath}\"",
        _ => Connection.ToConnectionString()
    };

    /// <summary>
    /// Тип подключения для отображения (файловая или клиент-серверная).
    /// </summary>
    public string ConnectionTypeDisplay => Connection.Type switch
    {
        ConnectionType.File => "Файловая",
        _ => "Клиент-серверная"
    };

    /// <summary>
    /// Дата последнего запуска для отображения.
    /// </summary>
    public string LastLaunchDisplay =>
        LastLaunchDate.HasValue
            ? LastLaunchDate.Value.ToString("dd.MM.yyyy HH:mm")
            : "Не запускалась";

    /// <summary>
    /// Инициалы базы для отображения в аватаре списка.
    /// </summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return "1С";

            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "1С";

            if (parts.Length == 1)
                return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();

            return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpperInvariant();
        }
    }
}