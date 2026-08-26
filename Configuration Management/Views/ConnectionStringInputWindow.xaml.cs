#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог ввода строки подключения к информационной базе 1С.
    /// Позволяет вставить строку из буфера обмена (если она удовлетворяет
    /// критериям ссылки на базу) или ввести её вручную, после чего применить.
    /// </summary>
    public partial class ConnectionStringInputWindow : Window
    {
        /// <summary>
        /// Создаёт диалог ввода строки подключения.
        /// </summary>
        /// <param name="initialValue">Текущее значение строки подключения базы (может быть пустым).</param>
        public ConnectionStringInputWindow(string? initialValue = null)
        {
            InitializeComponent();
            OkButton.IsEnabled = false;

            Loaded += (_, _) =>
            {
                // Если уже есть значение — заполняем им, иначе пробуем взять ссылку из буфера обмена.
                if (!string.IsNullOrWhiteSpace(initialValue))
                {
                    InputBox.Text = initialValue!.Trim();
                }
                else
                {
                    TryPrefillFromClipboard();
                }
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        /// <summary>
        /// Введённая строка подключения (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        /// <summary>
        /// Определяет, является ли текст ссылкой на информационную базу 1С
        /// (подходит для автоподстановки из буфера обмена).
        /// </summary>
        private static bool LooksLikeLink(string? text)
        {
            var value = (text ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Ссылка-протокол или веб-адрес.
            if (value.StartsWith("e1c:", System.StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
                return true;

            // Строка подключения 1С с параметрами.
            string[] keys = { "srvr=", "ref=", "file=", "ws=", "usr=", "pwd=" };
            foreach (var key in keys)
            {
                if (value.Contains(key, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Путь к файловой базе или клиент-серверная база «server\base» / «server:port\base».
            if (value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0)
                return true;

            return false;
        }

        /// <summary>
        /// Пытается подставить строку из буфера обмена, если она удовлетворяет критериям ссылки.
        /// </summary>
        private void TryPrefillFromClipboard()
        {
            string text;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            }
            catch
            {
                return;
            }

            if (LooksLikeLink(text))
            {
                InputBox.Text = text;
            }
        }

        /// <summary>
        /// Вставляет текст из буфера обмена в поле ввода (без проверки критериев).
        /// </summary>
        private void OnPasteClipboard_Click(object sender, RoutedEventArgs e)
        {
            string text;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            }
            catch
            {
                MessageBox.Show(LocalizationManager.T("ConnectionStringInput.ClipboardReadError"),
                    LocalizationManager.T("ConnectionStringInput.PasteTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(LocalizationManager.T("ConnectionStringInput.ClipboardEmpty"),
                    LocalizationManager.T("ConnectionStringInput.PasteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            InputBox.Text = text;
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        }

        private void OnInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(InputBox.Text);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = InputBox.Text?.Trim();
            DialogResult = true;
        }

        private void OnInputBox_KeyDown(object sender, KeyEventArgs e)
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