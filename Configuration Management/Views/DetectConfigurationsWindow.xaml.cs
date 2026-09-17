#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог «Определение конфигураций всех баз» (issue #236): таблица информационных баз
    /// с флажками, колонками платформы, индикатором логина/пароля и кнопкой свойств базы.
    /// Последовательно определяет имя конфигурации и номер релиза выбранных баз через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/> (COM-коннектор / эвристика) в фоновом
    /// потоке; UI не блокируется. Показывает текущую обрабатываемую базу и результат предыдущей,
    /// позволяет выбрать действие при ошибке («остановить»/«продолжить») и прекратить обработку.
    /// </summary>
    public partial class DetectConfigurationsWindow : Window
    {
        private readonly List<DetectConfigRowViewModel> _rows = new();
        private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly Action<Infobase>? _editBase;

        private CancellationTokenSource? _cts;

        /// <param name="infobases">Все информационные базы для определения.</param>
        /// <param name="editBase">Обратный вызов открытия окна свойств базы (под курсором). Может быть null.</param>
        public DetectConfigurationsWindow(IReadOnlyList<Infobase> infobases, Action<Infobase>? editBase = null)
        {
            InitializeComponent();
            _editBase = editBase;

            foreach (var ib in infobases)
                _rows.Add(new DetectConfigRowViewModel(ib));

            BasesGrid.ItemsSource = _rows;
            SummaryText.Text = string.Empty;
            ProgressText.Text = string.Empty;

            // Действие при ошибке (issue #236, п.2): по умолчанию — остановить обработку.
            ErrorModeCombo.Items.Add(LocalizationManager.T("DetectConfigs.ErrorMode.Stop"));
            ErrorModeCombo.Items.Add(LocalizationManager.T("DetectConfigs.ErrorMode.Continue"));
            ErrorModeCombo.SelectedIndex = 0;

            UpdateCheckedHeader();
        }

        /// <summary>Список строк, отмеченных флажком.</summary>
        public IReadOnlyList<DetectConfigRowViewModel> CheckedRows
            => _rows.Where(r => r.IsChecked && !r.IsProcessing).ToList();

        /// <summary>Признак того, что хотя бы одна база была изменена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            var count = _rows.Count(r => r.IsChecked);
            CheckedHeaderText.Text = string.Format(
                LocalizationManager.T("DetectConfigs.Column.CheckedHeaderFormat"), count);
        }

        /// <summary>Обновляет доступность кнопок управления отметками (блокируются во время прогона).</summary>
        private void UpdateSelectionButtons()
        {
            var busy = DetectButton.IsEnabled == false;
            CheckAllButton.IsEnabled = !busy;
            UncheckAllButton.IsEnabled = !busy;
            InvertButton.IsEnabled = !busy;
            ErrorModeCombo.IsEnabled = !busy;
        }

        private void OnCheckAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = true;
            UpdateCheckedHeader();
        }

        private void OnUncheckAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = false;
            UpdateCheckedHeader();
        }

        private void OnInvertClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = !row.IsChecked;
            UpdateCheckedHeader();
        }

        private void OnErrorModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Выбор комбобокса обрабатывается прямо в цикле обработки.
        }

        /// <summary>Открывает свойства базы под курсором, не закрывая список (issue #236, п.5).</summary>
        private void OnPropertiesClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not DetectConfigRowViewModel row)
                return;
            if (_editBase is null)
                return;
            _editBase(row.Infobase);
            // После редактирования могли измениться платформа и логин/пароль — обновляем строку.
            row.SyncFromInfobase();
            UpdateCheckedHeader();
        }

        private void OnStop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Последовательно обрабатывает отмеченные строки в фоновом потоке. Последовательность
        /// обязательна: <see cref="ComReadHost"/> сериализует запросы статической блокировкой,
        /// параллельный вызов упёрся бы в ту же блокировку без ускорения (план issue #236 §3).
        /// </summary>
        private async void OnDetect_Click(object sender, RoutedEventArgs e)
        {
            var targets = _rows.Where(r => r.IsChecked && !r.IsProcessing).ToList();
            if (targets.Count == 0)
            {
                SummaryText.Text = LocalizationManager.T("DetectConfigs.NoneSelected");
                return;
            }

            var continueOnError = ErrorModeCombo.SelectedIndex == 1;

            DetectButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            UpdateSelectionButtons();
            SummaryText.Text = string.Empty;
            ProgressText.Text = string.Empty;

            // Снимаем кэш-вердикт недоступности COM и сессионную защёлку агента,
            // как в одиночном «Определить» (Windows-API; на Linux действие тривиально).
#if WINDOWS
            OneCComConnector.ResetComVerdicts();
#endif

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var ok = 0;
            var errors = 0;
            var stopped = false;
            string? lastResult = null;

            foreach (var row in targets)
            {
                if (token.IsCancellationRequested)
                {
                    stopped = true;
                    break;
                }

                row.IsProcessing = true;
                ProgressText.Text = string.Format(
                    LocalizationManager.T("DetectConfigs.ProgressFormat"), row.Name);
                if (lastResult != null)
                    SummaryText.Text = lastResult;

                OneCConfigInfo? info = null;
                string? errorText = null;
                try
                {
                    // Режим чтения сведений — «Конфигуратор» (issue #236): учитывается раздельная
                    // авторизация ConfiguratorAuth при её наличии, иначе авторизация базы.
                    info = await Task.Run(() =>
                        ConfigurationInfoService.ReadAndApply(row.Infobase, overwriteExisting: true,
                            mode: OneCLaunchMode.Configurator), token);
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                    row.IsProcessing = false;
                    break;
                }
                catch (Exception ex)
                {
                    errorText = ex.Message;
                    _logger.Error($"Ошибка определения конфигурации базы «{row.Name}»", ex);
                }

                // Продолжение выполняется на UI-потоке (await захватил контекст синхронизации).
                row.SyncFromInfobase();
                row.IsProcessing = false;

                if (info is not null)
                {
                    row.IsChecked = false;
                    DataChanged = true;
                    ok++;
                    lastResult = string.Format(
                        LocalizationManager.T("DetectConfigs.LastOkFormat"), row.Name);
                }
                else
                {
                    row.ErrorText = errorText ?? ConfigurationInfoService.LastComError
                        ?? string.Format(LocalizationManager.T("DetectConfigs.ErrorRowFormat"), row.Name);
                    errors++;
                    lastResult = string.Format(
                        LocalizationManager.T("DetectConfigs.LastErrorFormat"), row.Name, row.ErrorText);
                    if (!continueOnError)
                    {
                        // Действие при ошибке — «остановить»: фиксируем ошибку и прерываем цикл.
                        // Результат (с текстом ошибки) остаётся видимым в SummaryText.
                        UpdateCheckedHeader();
                        break;
                    }
                }
                UpdateCheckedHeader();
            }

            _cts.Dispose();
            _cts = null;

            DetectButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            UpdateSelectionButtons();
            ProgressText.Text = string.Empty;

            if (stopped)
            {
                SummaryText.Text = LocalizationManager.T("DetectConfigs.Stopped");
            }
            else
            {
                var summary = string.Format(
                    LocalizationManager.T("DetectConfigs.DoneFormat"), ok, errors);
                if (errors > 0 && !continueOnError && lastResult != null)
                    summary += "  " + lastResult;
                SummaryText.Text = summary;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (e.Cancel)
                return;
            if (_rows.Any(r => r.IsChecked))
            {
                if (!_dialogs.Confirm(
                        LocalizationManager.T("DetectConfigs.CloseConfirm"),
                        LocalizationManager.T("DetectConfigs.Title")))
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnClosing(e);
        }
    }
}
#endif