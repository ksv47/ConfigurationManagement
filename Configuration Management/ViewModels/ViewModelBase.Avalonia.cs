#if LINUX
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Configuration_Management.Localization;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Базовый класс для ViewModel с реализацией INotifyPropertyChanged (Avalonia/Linux).
/// Совместим с Avalonia-привязками.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// Источник локализации для привязок XAML: <c>{Binding Loc[Key]}</c>.
    /// При смене языка открытые окна автоматически обновляют текст.
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
    /// Устанавливает значение и дополнительно уведомляет о связанных свойствах.
    /// Имя своего свойства передаётся первым: у одноимённой перегрузки первый
    /// строковый аргумент означал бы ровно обратное, и перепутать их было бы легко.
    /// </summary>
    protected bool SetPropertyWithRelated<T>(ref T field, T value, string propertyName, params string[] relatedProperties)
    {
        // Имя передаётся явно: при вызове без него CallerMemberName подставлял
        // имя самого метода, и подписчики на имя свойства не получали ничего.
        if (!SetProperty(ref field, value, propertyName))
            return false;
        foreach (var name in relatedProperties)
            OnPropertyChanged(name);
        return true;
    }
}
#endif