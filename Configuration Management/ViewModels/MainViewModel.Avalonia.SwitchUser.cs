#if LINUX
using Configuration_Management.Localization;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): смена пользователя (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>
    /// true, если учётных записей больше одной — тогда кнопка «Смена пользователя»
    /// показывается на верхней панели (при одной записи переключать нечего).
    /// </summary>
    public bool SwitchUserVisible
    {
        get
        {
            try
            {
                return AppServices.GetRequiredService<IProfileService>().Profiles.Count > 1;
            }
            catch
            {
                // Сервис профилей в изолированном/тестовом контексте может отсутствовать.
                return false;
            }
        }
    }

    /// <summary>
    /// Открывает окно выбора учётной записи и, если пользователь вошёл в другую
    /// запись, переключает активный профиль и перезагружает данные главного окна.
    /// Отмена или выбор той же записи ничего не меняют.
    /// </summary>
    private void SwitchUser()
    {
        try
        {
            var profileService = AppServices.GetRequiredService<IProfileService>();
            if (profileService.Profiles.Count <= 1)
                return;

            var current = profileService.CurrentProfile;
            var selectedId = LoginWindow.ShowLogin(profileService);
            if (selectedId == null)
                return; // Вход отменён — остаёмся как есть.

            if (current != null &&
                string.Equals(current.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                return; // Та же запись — перезагрузка не нужна.

            profileService.SetCurrentProfile(selectedId);
            ReloadAfterProfileSwitch();
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка смены пользователя: " + ex.Message);
            _dialog.ShowError(string.Format(LocalizationManager.T("Auth.LoginError"), ex.Message));
        }
    }

    /// <summary>
    /// Перезагружает данные главного окна после смены активного профиля в работающем
    /// приложении: повторно выполняет <see cref="Initialize"/> (список баз, группы,
    /// тема, избранное, горячие клавиши), обновляет язык интерфейса профиля и
    /// сообщает окну о переназначении сочетаний.
    /// </summary>
    private void ReloadAfterProfileSwitch()
    {
        try
        {
            Initialize();

            // Локализация выбранного профиля.
            try
            {
                var s = _repository.LoadSettings();
                LocalizationManager.Instance.Initialize(s.Language);
            }
            catch
            {
                // Локализация не должна ломать смену пользователя.
            }

            OnPropertyChanged(nameof(SwitchUserVisible));
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка перезагрузки данных после смены пользователя", ex);
        }
    }
}
#endif