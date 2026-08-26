#if WINDOWS
using System.Windows;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Управление учётными записями (профилями): создание, переименование, удаление,
    /// установка и снятие пароля. Каждый профиль хранит собственные настройки и список баз.
    ///
    /// Окно — тонкая «view»: вся бизнес-логика (валидация, CRUD, построение списка,
    /// подтверждение удаления) вынесена в <see cref="ProfilesViewModel"/>. Здесь остаётся
    /// только установка контекста данных и передача пароля из <c>PasswordBox</c>,
    /// который не поддерживает двустороннюю привязку.
    /// </summary>
    public partial class ProfilesWindow : Window
    {
        private readonly ProfilesViewModel _viewModel;

        public ProfilesWindow(ProfilesViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        /// <summary>
        /// Пароль нельзя привязать через <c>{Binding}</c> (<c>PasswordBox.Password</c>
        /// не является DependencyProperty), поэтому синхронизируем его вручную в ViewModel.
        /// </summary>
        private void OnPassword_Changed(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = PasswordInput.Password;
        }
    }
}
#endif