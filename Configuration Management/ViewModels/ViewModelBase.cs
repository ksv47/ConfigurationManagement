using System.ComponentModel;
using System.Runtime.CompilerServices;
using Configuration_Management.Localization;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Базовый класс для ViewModel с реализацией INotifyPropertyChanged.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// Источник локализации для привязок XAML: <c>{Binding Loc[Key]}</c>.
    /// При смене языка все открытые окна автоматически обновляют текст.
    /// </summary>
    public LocalizationSource Loc => LocalizationManager.Instance.Source;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Устанавливает значение поля и уведомляет подписчиков при изменении.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Устанавливает значение и дополнительно уведомляет о связанных свойствах
    /// (удобно для вычисляемых свойств вроде GroupByGroupText).
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, string propertyName, params string[] relatedProperties)
    {
        // Имя своего свойства передаётся явно: у вызова без него CallerMemberName
        // подставлял имя самого метода, и подписчики на имя свойства уведомления
        // не получали вовсе.
        if (!SetProperty(ref field, value, propertyName))
            return false;
        foreach (var name in relatedProperties)
            OnPropertyChanged(name);
        return true;
    }
}
