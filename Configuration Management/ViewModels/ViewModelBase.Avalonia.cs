#if LINUX
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Базовый класс для ViewModel с реализацией INotifyPropertyChanged (Avalonia/Linux).
/// Совместим с Avalonia-привязками.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
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
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, params string[] relatedProperties)
    {
        if (!SetProperty(ref field, value))
            return false;
        foreach (var name in relatedProperties)
            OnPropertyChanged(name);
        return true;
    }
}
#endif