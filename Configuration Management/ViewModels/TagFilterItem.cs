#if LINUX
namespace Configuration_Management.ViewModels;

/// <summary>
/// Чип тега на панели быстрого отбора. Уведомляет контейнер при смене выбора.
/// </summary>
public class TagFilterItem : ViewModelBase
{
    private bool _isSelected;

    public TagFilterItem(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
#endif