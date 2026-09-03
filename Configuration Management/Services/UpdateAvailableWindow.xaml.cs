#if WINDOWS
using System;
using System.IO;
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
        // Размер окна должен перестроиться под компактный этап скачивания (issue #157):
        // не наследуем габариты этапа предложения с длинным описанием изменений.
        RefreshWindowSize();

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

            try
            {
                _newExe = await _service.DownloadNewExeCoreAsync(_release.DownloadUrl!);
            }
            catch (Exception ex)
            {
                LogDiagnostic("Ошибка при скачивании обновления: " + ex);
                _newExe = null;
            }

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
            RefreshWindowSize();
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
        RefreshWindowSize();
    }

    /// <summary>
    /// Показывает ошибку обновления прямо в этом окне (вместо закрытия окна и
    /// открытия отдельного диалога ошибки), чтобы во время обновления не появлялось
    /// несколько окон друг за другом.
    /// </summary>
    private void ShowError(string message)
    {
        LogDiagnostic("Ошибка в диалоге обновления: " + message);
        OfferPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        RestartPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        ErrorCloseButton.Focus();
        RefreshWindowSize();
    }

    /// <summary>
    /// Принудительно пересчитывает размер окна по содержимому текущего этапа
    /// (issue #157). Кастомный Window-Chrome MaterialDesign может «запоминать» прежние
    /// габариты после смены видимого блока; сброс SizeToContent в Manual и обратно в Height
    /// заставляет окно подстроиться под новый этап (прогресс, вопрос о применении, ошибка),
    /// чтобы прогресс-бар и кнопки не обрезались и не оставалось пустоты.
    /// </summary>
    private void RefreshWindowSize()
    {
        try
        {
            if (!IsLoaded)
                return;

            // Сброс в Manual и обратно в Height заставляет окно пересчитать высоту
            // по содержимому текущего этапа (предложение → скачивание → применение → ошибка).
            SizeToContent = SizeToContent.Manual;
            UpdateLayout();
            SizeToContent = SizeToContent.Height;
            UpdateLayout();
            InvalidateMeasure();
        }
        catch
        {
            // Сбой пересчёта размера не должен ломать диалог.
        }
    }

    /// <summary>
    /// Записывает диагностическую информацию об ошибках обновления во временный файл
    /// (%TEMP%\ConfigurationManagement\update\update-dialog.log), чтобы причину сбоя можно
    /// было изучить после неудачного скачивания/применения (issue #162).
    /// </summary>
    private static void LogDiagnostic(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ConfigurationManagement", "update");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "update-dialog.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // Логирование не должно ломать диалог.
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmSizing = 0x0214;

    // Ориентиры сторон для WM_SIZING.
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszBottomRight = 8;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Возвращает фиксированную ширину окна, которую нельзя изменить мышью.
    /// До первой реальной отрисовки берём значение из свойства <see cref="Window.Width"/>.
    /// </summary>
    private int FixedWidth
    {
        get
        {
            var w = (int)Math.Round(ActualWidth);
            return w > 0 ? w : (int)Width;
        }
    }

    /// <summary>
    /// Перехватывает WM_GETMINMAXINFO и WM_SIZING, жёстко фиксируя ширину окна. Это
    /// блокирует «схлопывание» окна при перетаскивании правой/левой границы, которое
    /// обходит ResizeMode="NoResize" из-за кастомного Window-Chrome MaterialDesign
    /// (issue #162). Высоту не ограничиваем — её подстраивает SizeToContent="Height".
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmGetMinMaxInfo:
            {
                var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);

                // Ширина фиксирована: MinTrackSize.X == MaxTrackSize.X == фактической ширине,
                // поэтому изменение ширины мышью невозможно.
                int w = FixedWidth;
                mmi.MinTrackSize.X = w;
                mmi.MaxTrackSize.X = w;
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
                break;
            }
            case WmSizing:
            {
                // Дополнительная защита на случай, если кастомный Chrome инициирует изменение
                // размера в обход MINMAXINFO: принудительно возвращаем фиксированную ширину.
                var rc = Marshal.PtrToStructure<RECT>(lParam);
                int w = FixedWidth;
                switch (wParam.ToInt32())
                {
                    case WmszLeft:
                        rc.Left = rc.Right - w;
                        break;
                    default:
                        rc.Right = rc.Left + w;
                        break;
                }
                Marshal.StructureToPtr(rc, lParam, true);
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }
}
#endif