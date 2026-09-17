using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Строка диалога «Очистка истории запусков» (issue #246): состояние флажка выбора
/// и отображаемые имя базы и количество записей истории запусков.
/// Ссылается на тот же объект <see cref="Infobase"/>, что и в основном списке,
/// поэтому очистка <see cref="Infobase.LaunchHistory"/> сразу отражается в модели
/// и сохраняется через персист-метод окна настроек.
/// </summary>
public class ClearHistoryRowViewModel : ViewModelBase
{
    /// <summary>Информационная база, к которой относится строка.</summary>
    public Infobase Infobase { get; }

    /// <summary>Имя базы (неизменяемо в рамках диалога).</summary>
    public string Name => Infobase.Name;

    private bool _isChecked;

    /// <summary>Отмечена ли строка для очистки (флажок).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <param name="infobase">Информационная база. Не может быть null.</param>
    public ClearHistoryRowViewModel(Infobase infobase)
    {
        Infobase = infobase;
    }

    /// <summary>Количество записей истории запусков базы.</summary>
    public int HistoryCount => Infobase.LaunchHistory?.Count ?? 0;

    /// <summary>Есть ли у базы история для очистки.</summary>
    public bool HasHistory => HistoryCount > 0;

    /// <summary>Обновляет отображаемое количество записей из объекта <see cref="Infobase"/>.</summary>
    public void SyncFromInfobase()
    {
        OnPropertyChanged(nameof(HistoryCount));
        OnPropertyChanged(nameof(HasHistory));
    }
}