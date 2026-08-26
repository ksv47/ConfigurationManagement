#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода ссылки на информационную базу (аналог «Перейти по ссылке»
    /// в стандартном загрузчике 1С).
    /// </summary>
    public partial class LinkInputWindow : Window
    {
        /// <summary>
        /// Создаёт диалог ввода ссылки на информационную базу.
        /// </summary>
        public LinkInputWindow()
        {
            InitializeComponent();
            OkButton.IsEnabled = false;
            Loaded += (_, _) =>
            {
                LinkBox.Focus();
            };
        }

        /// <summary>
        /// Введённая ссылка на информационную базу (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnLinkBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(LinkBox.Text);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = LinkBox.Text?.Trim();
            DialogResult = true;
        }

        private void OnLinkBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && OkButton.IsEnabled)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif