using System.ComponentModel;

namespace Configuration_Management.Localization;

/// <summary>
/// Источник локализации для привязок XAML.
///
/// Поддерживает индексную привязку <c>{Binding Loc[Key]}</c> на обеих
/// платформах (WPF и Avalonia). Реализует <see cref="INotifyPropertyChanged"/>:
/// при смене языка вызывает <see cref="NotifyAll"/>, которое уведомляет об
/// изменении всех индексных привязок (<c>"Item[]"</c>) и дополнительно вызывает
/// UpdateTarget() у зарегистрированных выражений (WPF LocExtension), благодаря
/// чему открытые окна автоматически обновляют переведённый текст.
///
/// Обычно экземпляр выставляется через свойство <c>Loc</c> на ViewModel
/// (см. <see cref="ViewModels.ViewModelBase.Loc"/>) или на окне.
/// </summary>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    private readonly LocalizationManager _manager;
    private readonly object _sync = new();
    private readonly List<Registration> _registrations = new();

    // Порог, по достижении которого список регистраций принудительно чистится
    // от «мёртвых» (собранных GC) слабых ссылок, чтобы список не рос бесконечно.
    private const int CleanupThreshold = 64;

    internal LocalizationSource(LocalizationManager manager)
    {
        _manager = manager;
    }

    /// <summary>Возвращает перевод ключа для текущего языка (индексная привязка).</summary>
    public string this[string key] => _manager.Translate(key);

    /// <summary>
    /// Регистрирует объект для динамического обновления при смене языка.
    ///
    /// Хранит СЛАБУЮ ссылку на <paramref name="target"/> и делегат обновления,
    /// который не захватывает target (принимает его параметром), поэтому объект
    /// не удерживается источником и может быть собран GC. Это позволяет источнику
    /// оставаться платформонезависимым: тип объекта и способ обновления задаёт
    /// вызывающий код (например WPF-выражение привязки из <c>LocExtension</c>).
    /// </summary>
    /// <param name="target">Объект, который нужно обновлять (например BindingExpression).</param>
    /// <param name="update">Делегат, выполняющий обновление; получает target параметром.</param>
    public void RegisterForUpdate(object target, Action<object> update)
    {
        if (target is null || update is null)
            return;

        lock (_sync)
        {
            _registrations.Add(new Registration(new WeakReference(target), update));
            if (_registrations.Count >= CleanupThreshold)
                CleanupDeadRegistrationsLocked();
        }
    }

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

        // Принудительно вызываем UpdateTarget() у всех зарегистрированных выражений
        // (WPF LocExtension: конвертерная привязка без пути не реагирует на
        // PropertyChanged). Защищаемся от мёртвых/отвязанных ссылок и сбоев.
        Registration[] snapshot;
        lock (_sync)
        {
            snapshot = _registrations.ToArray();
            CleanupDeadRegistrationsLocked();
        }

        foreach (var reg in snapshot)
        {
            var target = reg.Target.IsAlive ? reg.Target.Target : null;
            if (target is null)
                continue; // выражение уже собрано GC — пропускаем

            try
            {
                reg.Update(target);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[l10n] UpdateTarget failed: " + ex.Message);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Слабая ссылка на целевой объект + делегат его обновления.</summary>
    private readonly record struct Registration(WeakReference Target, Action<object> Update);

    /// <summary>Удаляет регистрации, чьи целевые объекты уже собраны GC.</summary>
    private void CleanupDeadRegistrationsLocked()
    {
        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            if (!_registrations[i].Target.IsAlive)
                _registrations.RemoveAt(i);
        }
    }
}