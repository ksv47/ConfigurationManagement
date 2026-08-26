#if WINDOWS
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Управление учётными записями (профилями): создание, переименование, удаление,
    /// установка и снятие пароля. Каждый профиль хранит собственные настройки и список баз.
    /// </summary>
    public partial class ProfilesWindow : Window
    {
        private readonly IProfileService _profileService;
        private bool _suppressSelection;

        public ProfilesWindow(IProfileService profileService)
        {
            InitializeComponent();
            _profileService = profileService;
            RefreshList();
            ApplyCurrentAccountLabel();
            HintLabel.Text = LocalizationManager.T("Profiles.RestartHint");
        }

        private UserProfile? SelectedProfile => ProfilesList.SelectedItem as UserProfile;

        private void ApplyCurrentAccountLabel()
        {
            var name = _profileService.CurrentProfile?.Name ?? "-";
            CurrentAccountLabel.Text = string.Format(LocalizationManager.T("Profiles.CurrentAccount"), name);
        }

        /// <summary>Обновляет список учётных записей и выделяет текущую.</summary>
        private void RefreshList(string? selectId = null)
        {
            _suppressSelection = true;
            ProfilesList.ItemsSource = _profileService.Profiles.ToList();

            selectId ??= _profileService.CurrentProfile?.Id;
            var target = _profileService.Profiles.FirstOrDefault(p => p.Id == selectId);
            ProfilesList.SelectedItem = target;
            _suppressSelection = false;

            if (target == null)
                ClearEditor();
            else
                LoadEditor(target);
            ApplyCurrentAccountLabel();
        }

        private void OnProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;
            ErrorLabel.Visibility = Visibility.Collapsed;
            var profile = SelectedProfile;
            if (profile == null)
                ClearEditor();
            else
                LoadEditor(profile);
        }

        private void LoadEditor(UserProfile profile)
        {
            NameBox.Text = profile.Name;
            ProtectCheck.IsChecked = profile.HasPassword;
            PasswordInput.Clear();
        }

        private void ClearEditor()
        {
            NameBox.Text = string.Empty;
            ProtectCheck.IsChecked = false;
            PasswordInput.Clear();
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.Visibility = Visibility.Visible;
        }

        /// <summary>Создаёт новую учётную запись из полей редактора.</summary>
        private void OnCreate_Click(object sender, RoutedEventArgs e)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            var name = NameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                var password = ProtectCheck.IsChecked == true ? PasswordInput.Password : null;
                var created = _profileService.CreateProfile(name, password);
                RefreshList(created.Id);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
            }
        }

        /// <summary>Сохраняет изменения выбранной учётной записи (имя и пароль).</summary>
        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Profiles.EmptySelection"));
                return;
            }

            var name = NameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                if (!string.Equals(profile.Name, name, System.StringComparison.Ordinal))
                    _profileService.RenameProfile(profile.Id, name);

                var wantPassword = ProtectCheck.IsChecked == true;
                if (wantPassword && !string.IsNullOrEmpty(PasswordInput.Password))
                    _profileService.SetPassword(profile.Id, PasswordInput.Password);
                else if (!wantPassword && profile.HasPassword)
                    _profileService.SetPassword(profile.Id, null);

                RefreshList(profile.Id);
            }
            catch (System.Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        /// <summary>Удаляет выбранную учётную запись вместе с её данными.</summary>
        private void OnDelete_Click(object sender, RoutedEventArgs e)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            var profile = SelectedProfile;
            if (profile == null)
                return;

            var confirm = string.Format(LocalizationManager.T("Profiles.DeleteConfirm"), profile.Name);
            var dialog = AppServices.GetRequiredService<IDialogService>();
            if (!dialog.Confirm(confirm, LocalizationManager.T("Profiles.Title")))
                return;

            if (!_profileService.DeleteProfile(profile.Id))
            {
                ShowError(LocalizationManager.T("Profiles.CantDeleteLast"));
                return;
            }

            RefreshList();
        }
    }
}
#endif