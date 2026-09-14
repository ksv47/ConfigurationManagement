#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса определения свойств конфигурации (issue #174).
    /// Чтение через COM-коннектор занимает до нескольких секунд (до таймаута 8 с на
    /// недоступном сервере); окно показывает текущий этап, чтобы пользователь видел,
    /// что происходит, а чтение выполняется в фоне и не «замораживает» окно настроек.
    /// </summary>
    internal sealed class DetectConfigProgressWindow : Window
    {
        private readonly TextBlock _stageText;

        public DetectConfigProgressWindow()
        {
            Title = LocalizationManager.T("Connection.DetectConfigTitle");
            Width = 380;
            Height = 140;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var bar = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 6,
                Margin = new Thickness(20, 16, 20, 10)
            };
            _stageText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 0, 20, 12)
            };

            var panel = new StackPanel();
            panel.Children.Add(bar);
            panel.Children.Add(_stageText);
            Content = panel;
        }

        /// <summary>Обновляет текст текущего этапа. Можно вызывать из фонового потока.</summary>
        public void SetStage(string text)
        {
            if (Dispatcher.CheckAccess())
                _stageText.Text = text;
            else
                Dispatcher.Invoke(() => _stageText.Text = text);
        }
    }
}
#endif