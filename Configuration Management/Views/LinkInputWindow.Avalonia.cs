#if LINUX
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода ссылки на информационную базу (аналог «Перейти по ссылке»
    /// в стандартном загрузчике 1С). Avalonia/Linux-версия WPF-окна <see cref="LinkInputWindow"/>.
    /// Раскладка лежит в LinkInputWindow.axaml, здесь только поведение.
    /// </summary>
    public partial class LinkInputWindow : ModalWindowBase
    {
        private readonly TextBox _linkBox;
        private readonly Button _okButton;

        /// <summary>
        /// Создаёт диалог ввода ссылки на информационную базу.
        /// </summary>
        public LinkInputWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _linkBox = this.FindControl<TextBox>("LinkBox")!;
            _okButton = this.FindControl<Button>("OkButton")!;

            _linkBox.TextChanged += (_, _) => UpdateOkEnabled();
            _linkBox.KeyDown += OnLinkBox_KeyDown;

            this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
            _okButton.Click += (_, _) => OnOk_Click();

            UpdateOkEnabled();
            Opened += (_, _) => _linkBox.Focus();
        }

        /// <summary>
        /// Введённая ссылка на информационную базу (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void UpdateOkEnabled()
        {
            _okButton.IsEnabled = !string.IsNullOrWhiteSpace(_linkBox.Text);
        }

        private void OnOk_Click()
        {
            Result = _linkBox.Text?.Trim();
            DialogResult = true;
            Close();
        }

        private void OnLinkBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _okButton.IsEnabled)
            {
                OnOk_Click();
                e.Handled = true;
            }
        }
    }
}
#endif
