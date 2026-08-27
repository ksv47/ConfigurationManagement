using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Элемент списка учётных записей в окне управления профилями.
///
/// Добавляет к модели <see cref="UserProfile"/> признак активности (<see cref="IsActive"/>),
/// чтобы интерфейс мог показывать бейдж «активная» и подсветку текущей (активной) учётной
/// записи отдельно от выбранной для редактирования. Общий для Windows/WPF и Linux/Avalonia.
/// </summary>
public sealed class ProfileListItem
{
    /// <summary>Создаёт элемент списка для указанного профиля.</summary>
    public ProfileListItem(UserProfile profile, bool isActive)
    {
        Profile = profile;
        IsActive = isActive;
    }

    /// <summary>Исходная модель профиля.</summary>
    public UserProfile Profile { get; }

    /// <summary>Идентификатор профиля (удобен для сравнения при восстановлении выделения).</summary>
    public string Id => Profile.Id;

    /// <summary>Отображаемое имя учётной записи.</summary>
    public string Name => Profile.Name;

    /// <summary>True, если эта учётная запись является активной (текущей).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Обновляет признак активности (вызывается при перестроении списка).</summary>
    internal void SetActive(bool value) => IsActive = value;
}