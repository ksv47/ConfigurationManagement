#if WINDOWS
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Модель представления окна управления учётными записями (профилями) для Windows/WPF.
///
/// Инкапсулирует состояние редактора (выбранный профиль, имя, признак защиты паролем,
/// сообщение об ошибке) и все бизнес-операции: построение списка профилей, выбор текущей
/// записи, создание, переименование, установку/снятие пароля, удаление (с подтверждением
/// через <see cref="IDialogService"/>), выбор активной записи и локализацию подписи
/// активной учётной записи.
///
/// Класс не ссылается на конкретные WPF-контролы и не трогает визуальное дерево.
/// Единственное исключение — пароль: <c>PasswordBox.Password</c> не является
/// DependencyProperty и не привязывается, поэтому view записывает его в свойство
/// <see cref="Password"/> из события <c>PasswordChanged</c>. Вся остальная логика
/// (валидация, вызовы <see cref="IProfileService"/>, сборка списка) живёт здесь.
/// </summary>
public sealed class ProfilesViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly IDialogService _dialogService;

    private ProfileListItem? _selectedProfile;
    private UserProfile? _currentProfile;
    private string _name = string.Empty;
    private string _password = string.Empty;
    private bool _protectWithPassword;
    private string _currentAccountLabel = string.Empty;
    private string _editingTitle = string.Empty;
    private string? _errorMessage;

    /// <summary>
    /// Создаёт модель представления окна профилей и сразу загружает список учётных записей.
    /// </summary>
    public ProfilesViewModel(IProfileService profileService, IDialogService dialogService)
    {
        _profileService = profileService;
        _dialogService = dialogService;

        CreateCommand = new RelayCommand(Create);
        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand(Delete);
        ActivateCommand = new RelayCommand(Activate);

        RefreshList();
    }

    /// <summary>Список учётных записей в порядке их создания (с признаком активности).</summary>
    public ObservableCollection<ProfileListItem> Profiles { get; } = new();

    /// <summary>
    /// Исходные модели профилей — источник элементов выпадающего меню активной учётной записи
    /// (элементы должны иметь тот же тип, что и <see cref="CurrentProfile"/>, чтобы
    /// <c>SelectedItem</c> корректно совпадал).
    /// </summary>
    public ObservableCollection<UserProfile> Accounts { get; } = new();

    /// <summary>
    /// Выбранная в списке (для редактирования) учётная запись (двусторонняя привязка).
    /// Элементы списка имеют тип <see cref="ProfileListItem"/>, поэтому и выделение тоже.
    /// </summary>
    public ProfileListItem? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
                return;
            _selectedProfile = value;
            OnPropertyChanged();
            LoadEditor(value?.Profile);
            UpdateEditingTitle();
        }
    }

    /// <summary>
    /// Активная (текущая) учётная запись. При выборе другого значения в выпадающем меню
    /// сразу вызывается <see cref="IProfileService.SetCurrentProfile"/>.
    /// </summary>
    public UserProfile? CurrentProfile
    {
        get => _currentProfile;
        set
        {
            if (value == null || ReferenceEquals(_currentProfile, value))
                return;

            if (_profileService.CurrentProfile == null
                || !string.Equals(_profileService.CurrentProfile.Id, value.Id, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _profileService.SetCurrentProfile(value.Id);
                }
                catch
                {
                    return;
                }
            }

            _currentProfile = value;
            OnPropertyChanged();
            RefreshList(value.Id);
            UpdateCurrentAccountLabel();
        }
    }

    /// <summary>Имя редактируемой учётной записи (двусторонняя привязка к TextBox).</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Пароль из PasswordBox (заполняется view; нет двухсторонней привязки).</summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    /// <summary>Признак «защитить запись паролем» (двусторонняя привязка к CheckBox).</summary>
    public bool ProtectWithPassword
    {
        get => _protectWithPassword;
        set => SetProperty(ref _protectWithPassword, value);
    }

    /// <summary>Подпись активной (текущей) учётной записи вверху окна.</summary>
    public string CurrentAccountLabel
    {
        get => _currentAccountLabel;
        private set => SetProperty(ref _currentAccountLabel, value);
    }

    /// <summary>
    /// Заголовок панели редактирования: «Редактирование записи: <имя>» или приглашение,
    /// когда запись не выбрана.
    /// </summary>
    public string EditingTitle
    {
        get => _editingTitle;
        private set => SetProperty(ref _editingTitle, value);
    }

    /// <summary>Текст ошибки валидации/операции; пустая строка — ошибок нет.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value, nameof(HasError));
    }

    /// <summary>true, если есть сообщение об ошибке для показа.</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    /// <summary>Создание новой учётной записи из полей редактора.</summary>
    public RelayCommand CreateCommand { get; }

    /// <summary>Сохранение изменений выбранной учётной записи (имя и пароль).</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Удаление выбранной учётной записи с подтверждением.</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>Делает выбранную учётную запись активной (текущей).</summary>
    public RelayCommand ActivateCommand { get; }

    /// <summary>Обновляет список учётных записей и выделяет указанную (или активную).</summary>
    /// <param name="selectId">Идентификатор профиля для выделения; если null — активный профиль.</param>
    public void RefreshList(string? selectId = null)
    {
        var current = _profileService.CurrentProfile;

        Accounts.Clear();
        Profiles.Clear();
        foreach (var p in _profileService.Profiles)
        {
            Accounts.Add(p);
            Profiles.Add(new ProfileListItem(p, current != null && string.Equals(p.Id, current.Id, StringComparison.OrdinalIgnoreCase)));
        }

        _currentProfile = current;
        OnPropertyChanged(nameof(CurrentProfile));

        selectId ??= current?.Id;
        SelectedProfile = Profiles.FirstOrDefault(i => i.Id == selectId);
        LoadEditor(SelectedProfile?.Profile);
        UpdateEditingTitle();
        UpdateCurrentAccountLabel();
    }

    /// <summary>Заполняет редактор данными выбранной учётной записи.</summary>
    private void LoadEditor(UserProfile? profile)
    {
        Name = profile?.Name ?? string.Empty;
        ProtectWithPassword = profile?.HasPassword ?? false;
        Password = string.Empty;
    }

    private void UpdateEditingTitle()
    {
        var profile = SelectedProfile?.Profile;
        EditingTitle = profile == null
            ? LocalizationManager.T("Profiles.EditingNone")
            : string.Format(LocalizationManager.T("Profiles.Editing"), profile.Name);
    }

    private void UpdateCurrentAccountLabel()
    {
        var name = _profileService.CurrentProfile?.Name ?? "-";
        CurrentAccountLabel = string.Format(LocalizationManager.T("Profiles.CurrentAccount"), name);
    }

    /// <summary>Создаёт новую учётную запись из полей редактора.</summary>
    private void Create()
    {
        ErrorMessage = null;
        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = LocalizationManager.T("Profiles.EmptyName");
            return;
        }

        try
        {
            var password = ProtectWithPassword ? Password : null;
            var created = _profileService.CreateProfile(name, password);
            RefreshList(created.Id);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Сохраняет изменения выбранной учётной записи (имя и пароль).</summary>
    private void Save()
    {
        ErrorMessage = null;
        var profile = SelectedProfile?.Profile;
        if (profile == null)
        {
            ErrorMessage = LocalizationManager.T("Profiles.EmptySelection");
            return;
        }

        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = LocalizationManager.T("Profiles.EmptyName");
            return;
        }

        try
        {
            if (!string.Equals(profile.Name, name, StringComparison.Ordinal))
                _profileService.RenameProfile(profile.Id, name);

            var wantPassword = ProtectWithPassword;
            if (wantPassword && !string.IsNullOrEmpty(Password))
                _profileService.SetPassword(profile.Id, Password);
            else if (!wantPassword && profile.HasPassword)
                _profileService.SetPassword(profile.Id, null);

            RefreshList(profile.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Делает выбранную учётную запись активной (текущей).</summary>
    private void Activate()
    {
        ErrorMessage = null;
        var profile = SelectedProfile?.Profile;
        if (profile == null)
        {
            ErrorMessage = LocalizationManager.T("Profiles.NoSelectionToActivate");
            return;
        }

        try
        {
            _profileService.SetCurrentProfile(profile.Id);
            RefreshList(profile.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Удаляет выбранную учётную запись вместе с её данными.</summary>
    private void Delete()
    {
        ErrorMessage = null;
        var profile = SelectedProfile?.Profile;
        if (profile == null)
            return;

        var confirm = string.Format(LocalizationManager.T("Profiles.DeleteConfirm"), profile.Name);
        if (!_dialogService.Confirm(confirm, LocalizationManager.T("Profiles.Title")))
            return;

        if (!_profileService.DeleteProfile(profile.Id))
        {
            ErrorMessage = LocalizationManager.T("Profiles.CantDeleteLast");
            return;
        }

        RefreshList();
    }
}
#endif