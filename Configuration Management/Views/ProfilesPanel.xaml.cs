#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Панель управления учётными записями (профилями), размещаемая во вкладке «Учётные записи»
    /// окна настроек вместо отдельного окна. Тонкая «view»: вся бизнес-логика (валидация, CRUD,
    /// построение списка, подтверждение удаления) вынесена в <see cref="ProfilesViewModel"/>.
    /// Здесь остаётся только передача пароля из <c>PasswordBox</c>, который не поддерживает
    /// двустороннюю привязку.
    /// </summary>
    public partial class ProfilesPanel : UserControl
    {
        public ProfilesPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Пароль нельзя привязать через <c>{Binding}</c> (<c>PasswordBox.Password</c>
        /// не является DependencyProperty), поэтому синхронизируем его вручную в ViewModel.
        /// </summary>
        private void OnPassword_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfilesViewModel vm)
                vm.Password = PasswordInput.Password;
        }
    }
}
#endif