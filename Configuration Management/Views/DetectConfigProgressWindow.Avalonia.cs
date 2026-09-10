#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;

namespace Configuration_Management
{
    /// <summary>
    /// Модальный диалог прогресса определения свойств конфигурации (issue #174).
    /// Avalonia/Linux-версия WPF-окна <see cref="DetectConfigProgressWindow"/>. Чтение
    /// через конфигуратор/COM-агент занимает до нескольких секунд; окно показывает
    /// текущий этап, чтобы пользователь видел, что происходит, а чтение выполняется
    /// в фоне и не «замораживает» окно настроек.
    /// </summary>
    internal sealed class DetectConfigProgressWindow : ModalWindowBase
    {
        private readonly TextBlock _stageText;

        public DetectConfigProgressWindow()
        {
            Title = LocalizationManager.T("Connection.DetectConfigTitle");
            Width = 380;
            Height = 140;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // Индетерминантный индикатор держит рендер-цикл занятым и на программном
            // рендере/в виртуализации даёт постоянную перерисовку (issue #153). Там
            // рисуем статичную заполненную полосу, как в главном окне.
            var disableAnimations = Services.LinuxRendering.DisableAnimations;
            var bar = new ProgressBar
            {
                IsIndeterminate = !disableAnimations,
                Value = disableAnimations ? 100 : 0,
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
            Dispatcher.UIThread.Post(() => _stageText.Text = text);
        }
    }
}
#endif