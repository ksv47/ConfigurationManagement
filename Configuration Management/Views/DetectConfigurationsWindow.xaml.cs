#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// с флажками, кнопками установки/смены/снятия отметок и кнопкой «Определить», которая
    /// последовательно определяет имя конфигурации и номер релиза выбранных баз через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/> (COM-коннектор / эвристика).
    /// Окно не закрывается автоматически, пока остались отмеченные (неудачные) строки.
    /// </summary>
    public partial class DetectConfigurationsWindow : Window
    {
        private readonly List<DetectConfigRowViewModel> _rows = new();
        private readonly IAppLogger _logger = AppServices.GetRequiredService<IAppLogger>();

        /// <param name="infobases">Все информационные базы для определения.</param>
        public DetectConfigurationsWindow(IReadOnlyList<Infobase> infobases)
        {
            InitializeComponent();

            foreach (var ib in infobases)
                _rows.Add(new DetectConfigRowViewModel(ib));

            BasesGrid.ItemsSource = _rows;
            SummaryText.Text = string.Empty;
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

            DetectButton.IsEnabled = false;
            UpdateSelectionButtons();
            SummaryText.Text = string.Empty;

            // Снимаем кэш-вердикт недоступности COM и сессионную защёлку агента,
            // как в одиночном «Определить» (Windows-API; на Linux действие тривиально).
#if WINDOWS
            OneCComConnector.ResetComVerdicts();
#endif

            var ok = 0;
            var errors = 0;
            foreach (var row in targets)
            {
                row.IsProcessing = true;
                OneCConfigInfo? info = null;
                string? errorText = null;
                try
                {
                    info = await Task.Run(() =>
                        ConfigurationInfoService.ReadAndApply(row.Infobase, overwriteExisting: true));
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
                }
                else
                {
                    row.ErrorText = errorText ?? ConfigurationInfoService.LastComError
                        ?? string.Format(LocalizationManager.T("DetectConfigs.ErrorRowFormat"), row.Name);
                    errors++;
                }
                UpdateCheckedHeader();
            }

            DetectButton.IsEnabled = true;
            UpdateSelectionButtons();
            SummaryText.Text = string.Format(
                LocalizationManager.T("DetectConfigs.DoneFormat"), ok, errors);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (e.Cancel)
                return;
            if (_rows.Any(r => r.IsChecked))
            {
                var answer = MessageBox.Show(
                    LocalizationManager.T("DetectConfigs.CloseConfirm"),
                    LocalizationManager.T("DetectConfigs.Title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
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