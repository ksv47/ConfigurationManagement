using System.ComponentModel;

namespace Configuration_Management.Localization;

/// <summary>
/// Источник локализации для привязок XAML.
///
/// Поддерживает индексную привязку <c>{Binding Loc[Key]}</c> на обеих
/// платформах (WPF и Avalonia). Реализует <see cref="INotifyPropertyChanged"/>:
/// при смене языка вызывает <see cref="NotifyAll"/>, которое уведомляет об
/// изменении всех индексных привязок (<c>"Item[]"</c>), благодаря чему открытые
/// окна автоматически обновляют переведённый текст.
///
/// Обычно экземпляр выставляется через свойство <c>Loc</c> на ViewModel
/// (см. <see cref="ViewModels.ViewModelBase.Loc"/>) или на окне.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    private readonly LocalizationManager _manager;

    internal LocalizationSource(LocalizationManager manager)
    {
        _manager = manager;
    }

    /// <summary>Возвращает перевод ключа для текущего языка (индексная привязка).</summary>
    public string this[string key] => _manager.Translate(key);

    /// <summary>
    /// Уведомляет привязки о том, что все строки могли измениться
    /// (при смене языка).
    /// </summary>
    public void NotifyAll()
    {
        // "Item[]" уведомляет индексные привязки Path=[...] (Avalonia / {Binding Loc[Key]}).
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        // Пустая строка означает «изменились все свойства» — пересчитывает привязки
        // к объекту целиком (WPF-конвертер LocExtension).
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}