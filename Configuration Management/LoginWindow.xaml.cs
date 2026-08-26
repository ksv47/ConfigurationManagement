#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Окно выбора учётной записи (профиля) по аналогии со списком пользователей 1С.
    /// Показывается при запуске, если в приложении создано несколько профилей.
    /// Для профиля с паролем запрашивается пароль; для незащищённого — вход сразу.
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly IProfileService _profileService;

        /// <summary>Идентификатор выбранного профиля (null, если вход не выполнен).</summary>
        public string? SelectedProfileId { get; private set; }

        /// <summary>
        /// Создаёт окно авторизации и, если пользователь вошёл, возвращает идентификатор
        /// выбранного профиля; иначе — null (приложение завершает работу).
        /// </summary>
        public static string? ShowLogin(IProfileService profileService)
        {
            var window = new LoginWindow(profileService) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
            return window.SelectedProfileId;
        }

        public LoginWindow(IProfileService profileService)
        {
            InitializeComponent();
            _profileService = profileService;
            ProfilesList.ItemsSource = profileService.Profiles;

            // Если защищённых нет, выбираем первую запись по умолчанию.
            if (ProfilesList.Items.Count > 0)
                ProfilesList.SelectedIndex = 0;

            Loaded += (_, _) => ApplySelectionState();
        }

        private UserProfile? SelectedProfile => ProfilesList.SelectedItem as UserProfile;

        private void OnProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            ApplySelectionState();
        }

        /// <summary>Показывает поле пароля только для защищённого выбранного профиля.</summary>
        private void ApplySelectionState()
        {
            var profile = SelectedProfile;
            var hasPassword = profile?.HasPassword == true;
            PasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
            if (hasPassword)
                PasswordInput.Focus();
        }

        private void OnPasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
                e.Handled = true;
            }
        }

        private void OnLogin_Click(object sender, RoutedEventArgs e)
        {
            TryLogin();
        }

        private void TryLogin()
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Auth.EmptySelection"));
                return;
            }

            if (profile.HasPassword && !_profileService.VerifyPassword(profile.Id, PasswordInput.Password))
            {
                ShowError(LocalizationManager.T("Auth.WrongPassword"));
                PasswordInput.Clear();
                PasswordInput.Focus();
                return;
            }

            SelectedProfileId = profile.Id;
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorLabel.Text = message;
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }
}
#endif