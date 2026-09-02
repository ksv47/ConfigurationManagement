#if WINDOWS
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Единый модальный диалог обновления (Windows/WPF). Совмещает в себе весь процесс:
/// предложение скачать новую версию, прогресс скачивания и вопрос, как применить
/// обновление («Перезапустить сейчас» или «Обновить после закрытия»). Системный
/// MessageBox здесь не используется — все этапы показаны в этом же окне.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateService _service;
    private readonly ReleaseInfo _release;

    // Путь к текущему exe и к скачанному файлу; заполняются после успешной загрузки.
    private string? _targetExe;
    private string? _newExe;

    public UpdateAvailableWindow(ReleaseInfo release, UpdateService service)
    {
        InitializeComponent();
        _release = release;
        _service = service;

        Title = LocalizationManager.T("Update.NewVersionAvailable");
        HeadingText.Text = LocalizationManager.T("Update.NewVersionAvailable");
        CurrentVersionText.Text = string.Format(
            LocalizationManager.T("Update.CurrentVersion"), VersionInfo.Display());
        NewVersionText.Text = string.Format(
            LocalizationManager.T("Update.NewVersion"), NormalizeTag(release.TagName));
        WhatsNewLabel.Text = LocalizationManager.T("Update.WhatsNew");
        BodyText.Text = string.IsNullOrWhiteSpace(release.Body)
            ? LocalizationManager.T("Update.NoDescription")
            : release.Body;

        // На старте главного окна ещё нет, и первым MainWindow становится само это
        // окно: присваивание Owner самому себе бросает ArgumentException. Без
        // владельца окно просто открывается по центру экрана.
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Loaded += (_, _) => DownloadButton.Focus();
    }

    /// <summary>
    /// Навешиваем перехватчик Win32-сообщений сразу после инициализации окна,
    /// чтобы жёстко зафиксировать его размер независимо от кастомного Window-Chrome
    /// (MaterialDesignThemes обходит ResizeMode="NoResize").
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    /// <summary>Обрезает ведущий символ «v» у тега версии для отображения.</summary>
    private static string NormalizeTag(string tag) =>
        !string.IsNullOrEmpty(tag) && (tag[0] == 'v' || tag[0] == 'V') ? tag.Substring(1) : tag;

    /// <summary>
    /// «Скачать»: скрываем предложение, показываем прогресс и запускаем загрузку.
    /// По завершении загрузки остаёмся в этом же окне и спрашиваем, как применить обновление.
    /// </summary>
    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        OfferPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        ProgressText.Text = LocalizationManager.T("Update.Downloading");

        // Подписка на прогресс скачивания (событие приходит из фонового потока).
        _service.DownloadProgressChanged += OnDownloadProgress;
        try
        {
            if (string.IsNullOrWhiteSpace(_release.DownloadUrl))
            {
                ShowError(LocalizationManager.T("Update.NoDownloadUrl"));
                return;
            }

            _targetExe = _service.ResolveTargetExe();
            if (_targetExe is null)
            {
                ShowError(LocalizationManager.T("Update.InstallFailed"));
                return;
            }

            _newExe = await _service.DownloadNewExeCoreAsync(_release.DownloadUrl!);
            if (_newExe is null)
            {
                ShowError(LocalizationManager.T("Update.DownloadFailed"));
                return;
            }

            // Скачивание завершено — переходим к вопросу о том, как применить обновление.
            ProgressPanel.Visibility = Visibility.Collapsed;
            RestartPanel.Visibility = Visibility.Visible;
            RestartHeadingText.Text = LocalizationManager.T("Update.RestartOrLater");
            RestartNowButton.Focus();
        }
        finally
        {
            _service.DownloadProgressChanged -= OnDownloadProgress;
        }
    }

    /// <summary>Обновляет индикатор прогресса внутри окна (вызывается из фонового потока).</summary>
    private void OnDownloadProgress(double percent)
    {
        Dispatcher.Invoke(() =>
        {
            if (percent < 0)
            {
                // Длина файла заранее неизвестна — индикатор переводим в неопределённый режим.
                ProgressBar.IsIndeterminate = true;
                ProgressText.Text = LocalizationManager.T("Update.Downloading");
                return;
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = percent;
            ProgressText.Text = string.Format(
                LocalizationManager.T("Update.DownloadProgressFormat"), (int)percent);
        });
    }

    /// <summary>«Перезапустить сейчас»: применяем обновление с перезапуском и закрываем приложение.</summary>
    private void OnRestartNowClick(object sender, RoutedEventArgs e)
    {
        if (_targetExe is null || _newExe is null)
        {
            Close();
            return;
        }

        RestartNowButton.IsEnabled = false;
        UpdateAfterCloseButton.IsEnabled = false;

        if (!_service.ApplyRestartNow(_targetExe, _newExe))
        {
            ShowError(LocalizationManager.T("Update.InstallFailed"));
            return;
        }

        // Помощник уже запущен. Показываем подтверждение и через короткую паузу закрываем
        // приложение, чтобы exe освободился и был заменён, а новое приложение запустилось.
        ShowDonePhase(LocalizationManager.T("Update.RestartPrompt"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _service.ShutdownNow();
        };
        timer.Start();
    }

    /// <summary>«Обновить после закрытия»: применяем обновление без перезапуска.</summary>
    private void OnUpdateAfterCloseClick(object sender, RoutedEventArgs e)
    {
        if (_targetExe is null || _newExe is null)
        {
            Close();
            return;
        }

        RestartNowButton.IsEnabled = false;
        UpdateAfterCloseButton.IsEnabled = false;

        if (!_service.ApplyAfterClose(_targetExe, _newExe))
        {
            ShowError(LocalizationManager.T("Update.InstallFailed"));
            return;
        }

        // Обновление применится при естественном закрытии программы — показываем
        // подтверждение в этом же окне (без дополнительных диалогов).
        ShowDonePhase(LocalizationManager.T("Update.AppliedLater"));
    }

    /// <summary>Закрывает окно по кнопке финального этапа.</summary>
    private void OnDoneCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Показывает финальный этап с текстом и кнопкой «Закрыть».</summary>
    private void ShowDonePhase(string text)
    {
        RestartPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        DoneText.Text = text;
        DoneCloseButton.Focus();
    }

    /// <summary>
    /// Показывает ошибку обновления прямо в этом окне (вместо закрытия окна и
    /// открытия отдельного диалога ошибки), чтобы во время обновления не появлялось
    /// несколько окон друг за другом.
    /// </summary>
    private void ShowError(string message)
    {
        OfferPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        RestartPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        ErrorCloseButton.Focus();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private const int WmGetMinMaxInfo = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct PoINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PoINT Reserved;
        public PoINT MaxSize;
        public PoINT MaxPosition;
        public PoINT MinTrackSize;
        public PoINT MaxTrackSize;
    }

    /// <summary>
    /// Перехватывает WM_GETMINMAXINFO и принудительно фиксирует минимальный и
    /// максимальный трекинг-размер равными фактическому размеру окна. Это надёжно
    /// блокирует изменение размера мышью даже при активном кастомном Window-Chrome.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

            // На момент первого сообщения RestoreBounds может быть ещё нулевым —
            // тогда берём ширину/высоту из свойств окна.
            double width = RestoreBounds.Width > 0 ? RestoreBounds.Width : ActualWidth;
            double height = RestoreBounds.Height > 0 ? RestoreBounds.Height : ActualHeight;
            if (width <= 0 && Width > 0)
                width = Width;
            if (height <= 0 && Height > 0)
                height = Height;

            int w = (int)Math.Round(width);
            int h = (int)Math.Round(height);
            mmi.MinTrackSize.X = w;
            mmi.MinTrackSize.Y = h;
            mmi.MaxTrackSize.X = w;
            mmi.MaxTrackSize.Y = h;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }
}
#endif