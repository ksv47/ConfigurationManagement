#if LINUX
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода названия тега. Avalonia/Linux-версия WPF-окна <see cref="TagInputWindow"/>.
    /// Раскладка лежит в TagInputWindow.axaml, здесь только поведение.
    /// </summary>
    public partial class TagInputWindow : ModalWindowBase
    {
        private readonly TextBox _tagBox;

        /// <summary>
        /// Создаёт диалог ввода тега.
        /// </summary>
        public TagInputWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _tagBox = this.FindControl<TextBox>("TagBox")!;
            _tagBox.KeyDown += OnTagBox_KeyDown;

            // Кнопки строит базовый класс: у них общий вид и поведение
            // на все модальные окна, поэтому в разметке стоит только место.
            var buttonsHost = this.FindControl<ContentControl>("ButtonsHost")!;
            buttonsHost.Content = BuildButtons(Localization.LocalizationManager.T("Common.Add"), 110, OnOk_Click,
                cancelWidth: 90, okIconKey: "IconAdd");

            Opened += (_, _) =>
            {
                _tagBox.Focus();
                _tagBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённое название тега (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click()
        {
            Result = _tagBox.Text?.Trim();
        }

        private void OnTagBox_KeyDown(object? sender, KeyEventArgs e)
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
