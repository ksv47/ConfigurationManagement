#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог «Очистка истории запусков» (issue #246): таблица информационных баз с флажками,
    /// колонками имени базы и количества записей истории запусков. Кнопка «Очистить историю»
    /// обнуляет <see cref="Infobase.LaunchHistory"/> отмеченных баз. Правки вносятся прямо
    /// в объекты <see cref="Infobase"/>, поэтому сохранение выполняется вызывающим кодом
    /// (окном настроек) через персист-метод, если <see cref="DataChanged"/> == true.
    /// </summary>
    public partial class ClearHistoryWindow : Window
    {
        private readonly List<ClearHistoryRowViewModel> _rows = new();

        /// <param name="infobases">Все информационные базы для очистки истории.</param>
        public ClearHistoryWindow(IReadOnlyList<Infobase> infobases)
        {
            InitializeComponent();

            foreach (var ib in infobases)
                _rows.Add(new ClearHistoryRowViewModel(ib));

            BasesGrid.ItemsSource = _rows;
            SummaryText.Text = string.Empty;

            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        /// <summary>Признак того, что хотя бы у одной базы история была очищена (для персиста).</summary>
        public bool DataChanged { get; private set; }

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            var count = _rows.Count(r => r.IsChecked);
            CheckedHeaderText.Text = string.Format(
                LocalizationManager.T("ClearHistory.Column.CheckedHeaderFormat"), count);
        }

        /// <summary>Доступность кнопки «Очистить историю»: активна, если отмечена хотя бы одна база.</summary>
        private void UpdateClearButtonEnabled()
        {
            ClearButton.IsEnabled = _rows.Any(r => r.IsChecked);
        }

        private void OnCheckAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsChecked = true;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        private void OnUncheckAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsChecked = false;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        private void OnInvertClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows)
                row.IsChecked = !row.IsChecked;
            UpdateCheckedHeader();
            UpdateClearButtonEnabled();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Очищает историю запусков отмеченных баз. Выполняется синхронно на UI-потоке:
        /// операция тривиальная (обнуление списка записей), блокировки не требуется.
        /// </summary>
        private void OnClear_Click(object sender, RoutedEventArgs e)
        {
            var targets = _rows.Where(r => r.IsChecked).ToList();
            if (targets.Count == 0)
            {
                SummaryText.Text = LocalizationManager.T("ClearHistory.NoneSelected");
                return;
            }

            var cleared = 0;
            foreach (var row in targets)
            {
                row.Infobase.LaunchHistory = new List<LaunchHistoryEntry>();
                DataChanged = true;
                cleared++;
                row.SyncFromInfobase();
            }

            SummaryText.Text = string.Format(
                LocalizationManager.T("ClearHistory.DoneFormat"), cleared);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
        }
    }
}
#endif