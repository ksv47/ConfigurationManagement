#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Themes;

namespace Configuration_Management.Services;

/// <summary>
/// Окно хода скачивания обновления (Linux/Avalonia). Показывается на время загрузки
/// нового исполняемого файла, как этап прогресса единого диалога обновления в
/// Windows-версии (<c>UpdateAvailableWindow</c>): без него между ответом «Скачать»
/// и вопросом о перезапуске приложение молчит несколько минут, и со стороны
/// пользователя загрузка десятков МБ неотличима от зависания (issue #225).
/// </summary>
internal sealed class UpdateProgressWindowAvalonia : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _caption;

    public UpdateProgressWindowAvalonia()
    {
        Title = LocalizationManager.T("Update.NewVersionAvailable");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;

        ThemeBrushes.Bind(this, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");

        _caption = new TextBlock
        {
            Text = LocalizationManager.T("Update.Downloading"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };
        ThemeBrushes.Bind(_caption, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = true,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        Content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16),
            Children = { _caption, _bar }
        };
    }

    /// <summary>
    /// Показывает долю скачанного. Отрицательное значение означает, что размер файла
    /// сервер не сообщил: полоса остаётся бегущей, процент не выводится. Вызывается
    /// из потока загрузки, поэтому переход в поток интерфейса выполняется здесь.
    /// </summary>
    public void SetProgress(double percent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (percent < 0)
            {
                _bar.IsIndeterminate = true;
                _caption.Text = LocalizationManager.T("Update.Downloading");
                return;
            }

            _bar.IsIndeterminate = false;
            _bar.Value = percent;
            _caption.Text = string.Format(
                LocalizationManager.T("Update.DownloadProgressFormat"), (int)percent);
        });
    }
}
#endif
