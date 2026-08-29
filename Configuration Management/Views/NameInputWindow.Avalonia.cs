#if LINUX
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода произвольного названия (например, названия темы оформления).
    /// Avalonia/Linux-версия WPF-окна <see cref="NameInputWindow"/>.
    /// Раскладка лежит в NameInputWindow.axaml, здесь только поведение.
    /// </summary>
    public partial class NameInputWindow : ModalWindowBase
    {
        private readonly TextBox _nameBox;

        /// <summary>
        /// Создаёт диалог ввода названия.
        /// </summary>
        /// <param name="title">Заголовок окна.</param>
        /// <param name="label">Подпись над полем ввода.</param>
        /// <param name="okText">Текст на кнопке подтверждения.</param>
        /// <param name="initialText">Начальное значение поля ввода.</param>
        public NameInputWindow(string title, string label, string okText, string initialText = "")
        {
            AvaloniaXamlLoader.Load(this);

            Title = title;
            this.FindControl<TextBlock>("Prompt")!.Text = label;

            _nameBox = this.FindControl<TextBox>("NameBox")!;
            _nameBox.Text = initialText;
            _nameBox.KeyDown += OnNameBox_KeyDown;

            this.FindControl<ContentControl>("ButtonsHost")!.Content =
                BuildButtons(okText, 130, OnOk_Click);

            Opened += (_, _) =>
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённое название (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click()
        {
            Result = _nameBox.Text?.Trim();
        }

        private void OnNameBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click();
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
    }
}
#endif
