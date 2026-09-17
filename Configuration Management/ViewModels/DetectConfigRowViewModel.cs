using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка диалога «Определение конфигураций всех баз» (issue #236): состояние
/// флажка выбора, признак обработки и отображаемые значения имени конфигурации,
/// номера релиза и версии платформы, а также признак заполненности логина/пароля.
/// Ссылается на тот же объект <see cref="Infobase"/>, что и в основном списке,
/// поэтому правки, внесённые <see cref="Services.ConfigurationInfoService.ReadAndApply"/>,
/// сразу отражаются в модели и сохраняются через персист-метод окна настроек.
/// </summary>
public class DetectConfigRowViewModel : ViewModelBase
{
    /// <summary>Информационная база, к которой относится строка.</summary>
    public Infobase Infobase { get; }

    /// <summary>Имя базы (неизменяемо в рамках диалога).</summary>
    public string Name => Infobase.Name;

    private bool _isChecked;
    private bool _isProcessing;
    private string _configurationName;
    private string _configurationVersion;
    private string _platformVersion;
    private bool _hasCredentials;
    private string _errorText = string.Empty;

    /// <param name="infobase">Информационная база. Не может быть null.</param>
    public DetectConfigRowViewModel(Infobase infobase)
    {
        Infobase = infobase;
        _configurationName = infobase.ConfigurationName ?? string.Empty;
        _configurationVersion = infobase.ConfigurationVersion ?? string.Empty;
        _platformVersion = infobase.PlatformVersion ?? string.Empty;
        _hasCredentials = ComputeHasCredentials();

        // Автоотметка (issue #236): флажок проставляется только для баз, которые нужно
        // определить (пустое имя конфигурации или номер релиза) И при этом у базы заданы
        // платформа (п.4) и логин/пароль (п.5) — иначе чтение почти наверняка завершится
        // ошибкой. Пользователь всегда может отметить такую строку вручную.
        _isChecked = (string.IsNullOrWhiteSpace(_configurationName)
                || string.IsNullOrWhiteSpace(_configurationVersion))
            && HasPlatform
            && _hasCredentials;
    }

    /// <summary>Отмечена ли строка для определения (флажок).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Идёт ли сейчас обработка строки (флажок неактивен).</summary>
    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    /// <summary>Текущее имя конфигурации базы.</summary>
    public string ConfigurationName
    {
        get => _configurationName;
        set => SetProperty(ref _configurationName, value ?? string.Empty);
    }

    /// <summary>Текущий номер релиза (версия) конфигурации базы.</summary>
    public string ConfigurationVersion
    {
        get => _configurationVersion;
        set => SetProperty(ref _configurationVersion, value ?? string.Empty);
    }

    /// <summary>Версия платформы 1С, заданная для базы (пусто — не задана).</summary>
    public string PlatformVersion
    {
        get => _platformVersion;
        set
        {
            if (SetProperty(ref _platformVersion, value ?? string.Empty))
                OnPropertyChanged(nameof(HasPlatform));
        }
    }

    /// <summary>Задана ли платформа для базы (issue #236, п.4).</summary>
    public bool HasPlatform => !string.IsNullOrWhiteSpace(_platformVersion);

    /// <summary>
    /// Заполнены ли логин и пароль для чтения конфигурации (issue #236, п.5).
    /// Учитываются и основные учётные данные подключения, и отдельная авторизация Конфигуратора.
    /// </summary>
    public bool HasCredentials
    {
        get => _hasCredentials;
        private set => SetProperty(ref _hasCredentials, value);
    }

    /// <summary>Текст ошибки определения по строке (пусто, если ошибки нет).</summary>
    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value ?? string.Empty);
    }

    /// <summary>
    /// Обновляет отображаемые имя конфигурации, версию, платформу и признак логина/пароля
    /// из объекта <see cref="Infobase"/> после того, как
    /// <see cref="Services.ConfigurationInfoService.ReadAndApply"/> применил прочитанные
    /// значения, либо после редактирования свойств базы через кнопку под курсором.
    /// Ошибку строки при этом сбрасываем.
    /// </summary>
    public void SyncFromInfobase()
    {
        ConfigurationName = Infobase.ConfigurationName ?? string.Empty;
        ConfigurationVersion = Infobase.ConfigurationVersion ?? string.Empty;
        PlatformVersion = Infobase.PlatformVersion ?? string.Empty;
        HasCredentials = ComputeHasCredentials();
        ErrorText = string.Empty;
    }

    private bool ComputeHasCredentials()
    {
        var conn = Infobase.Connection;
        if (conn != null
            && !string.IsNullOrWhiteSpace(conn.User)
            && !string.IsNullOrWhiteSpace(conn.Password))
            return true;

        var cfgAuth = Infobase.ConfiguratorAuth;
        if (cfgAuth != null
            && !string.IsNullOrWhiteSpace(cfgAuth.User)
            && !string.IsNullOrWhiteSpace(cfgAuth.Password))
            return true;

        return false;
    }
}