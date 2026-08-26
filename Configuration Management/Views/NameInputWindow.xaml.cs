#if WINDOWS
using System.Windows;
using System.Windows.Input;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода произвольного названия (например, названия темы оформления)
    /// в стиле Material Design: с подписью, полем ввода и кнопками «Отмена»/«ОК».
    /// </summary>
    public partial class NameInputWindow : Window
    {
        /// <summary>
        /// Создаёт диалог ввода названия.
        /// </summary>
        /// <param name="title">Заголовок окна.</param>
        /// <param name="label">Подпись над полем ввода.</param>
        /// <param name="okText">Текст на кнопке подтверждения.</param>
        /// <param name="initialText">Начальное значение поля ввода.</param>
        public NameInputWindow(string title, string label, string okText, string initialText = "")
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = label;
            OkTextBlock.Text = okText;
            NameBox.Text = initialText;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённое название (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = NameBox.Text?.Trim();
            DialogResult = true;
        }

        private void OnNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif