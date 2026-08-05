using System.Windows;
using System.Windows.Input;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода названия тега.
    /// </summary>
    public partial class TagInputWindow : Window
    {
        /// <summary>
        /// Создаёт диалог ввода тега.
        /// </summary>
        public TagInputWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                TagBox.Focus();
                TagBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённое название тега (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = TagBox.Text?.Trim();
            DialogResult = true;
        }

        private void OnTagBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}