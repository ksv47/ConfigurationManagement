using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Управление учётными записями (профилями) приложения.
///
/// Отвечает за реестр профилей (<c>profiles.json</c>), хэширование и проверку паролей,
/// пер-профильные каталоги данных и выбор активного профиля. Является общей
/// (кроссплатформенной) абстракцией для Windows/WPF и Linux/Avalonia.
/// </summary>
public interface IProfileService
{
    /// <summary>Полный список профилей (учётных записей) в порядке их создания.</summary>
    IReadOnlyList<UserProfile> Profiles { get; }

    /// <summary>Активный (выбранный на текущую сессию) профиль.</summary>
    UserProfile? CurrentProfile { get; }

    /// <summary>
    /// Каталог данных активного профиля (<c>profiles/<Id>/</c>), в котором хранятся
    /// <c>settings.json</c>, <c>infobases.json</c> и <c>groups.json</c>. Если активный профиль
    /// не выбран — возвращается общий каталог данных приложения (легаси-режим).
    /// </summary>
    string CurrentProfileDataDirectory { get; }

    /// <summary>
    /// Инициализирует сервис: загружает реестр профилей и, при первом запуске,
    /// мигрирует существующие данные из корня каталога данных в профиль по умолчанию.
    /// Должен вызываться один раз до первого обращения к профилям.
    /// </summary>
    void EnsureInitialized();

    /// <summary>Создаёт новый профиль. Имя не должно быть пустым и должно быть уникальным.</summary>
    /// <param name="name">Имя учётной записи.</param>
    /// <param name="password">Необязательный пароль (null или пустая строка — без пароля).</param>
    UserProfile CreateProfile(string name, string? password = null);

    /// <summary>
    /// Переименовывает профиль. Имя не должно быть пустым и должно быть уникальным.
    /// </summary>
    void RenameProfile(string id, string newName);

    /// <summary>
    /// Удаляет профиль вместе с его каталогом данных. Нельзя удалить последний профиль.
    /// </summary>
    /// <returns>True, если профиль удалён.</returns>
    bool DeleteProfile(string id);

    /// <summary>Задаёт или снимает пароль профиля (null/пустая строка — снять пароль).</summary>
    void SetPassword(string id, string? password);

    /// <summary>Проверяет пароль профиля.</summary>
    bool VerifyPassword(string id, string password);

    /// <summary>Делает профиль активным и запоминает его как использованный последним.</summary>
    void SetCurrentProfile(string id);
}