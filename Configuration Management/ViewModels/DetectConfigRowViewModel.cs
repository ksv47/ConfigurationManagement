using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка диалога «Определение конфигураций всех баз» (issue #236): состояние
/// флажка выбора, признак обработки и отображаемые значения имени конфигурации
/// и номера релиза. Ссылается на тот же объект <see cref="Infobase"/>, что и в
/// основном списке, поэтому правки, внесённые <see cref="Services.ConfigurationInfoService.ReadAndApply"/>,
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
    private string _errorText = string.Empty;

    /// <param name="infobase">Информационная база. Не может быть null.</param>
    public DetectConfigRowViewModel(Infobase infobase)
    {
        Infobase = infobase;
        _configurationName = infobase.ConfigurationName ?? string.Empty;
        _configurationVersion = infobase.ConfigurationVersion ?? string.Empty;

        // Требование #2: если у базы уже заполнены и имя конфигурации, и номер релиза —
        // флажок не проставляется автоматически; если хоть одно свойство пустое —
        // флажок проставляется (это целевые базы для определения).
        _isChecked = string.IsNullOrWhiteSpace(_configurationName)
            || string.IsNullOrWhiteSpace(_configurationVersion);
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

    /// <summary>Текст ошибки определения по строке (пусто, если ошибки нет).</summary>
    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value ?? string.Empty);
    }

    /// <summary>
    /// Обновляет отображаемые имя конфигурации и версию из объекта <see cref="Infobase"/>
    /// после того, как <see cref="Services.ConfigurationInfoService.ReadAndApply"/> применил
    /// прочитанные значения. Ошибку строки при этом сбрасываем.
    /// </summary>
    public void SyncFromInfobase()
    {
        ConfigurationName = Infobase.ConfigurationName ?? string.Empty;
        ConfigurationVersion = Infobase.ConfigurationVersion ?? string.Empty;
        ErrorText = string.Empty;
    }
}