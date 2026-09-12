#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог «Определение конфигураций всех баз» (issue #236), Avalonia/Linux-версия
    /// WPF-окна <see cref="DetectConfigurationsWindow"/>. Таблица баз с флажками, кнопками
    /// установки/смены/снятия отметок и кнопкой «Определить», которая последовательно
    /// определяет имя конфигурации и номер релиза выбранных баз через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/>. Окно не закрывается
    /// автоматически, пока остались отмеченные (неудачные) строки.
    /// </summary>
    public sealed class DetectConfigurationsWindow : ModalWindowBase
    {
        private readonly List<DetectConfigRowViewModel> _rows = new();
        private readonly Dictionary<DetectConfigRowViewModel, CheckBox> _checks = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _configTexts = new();
        private readonly Dictionary<DetectConfigRowViewModel, TextBlock> _versionTexts = new();

        private readonly StackPanel _rowsPanel = new() { Margin = new Thickness(4, 2) };
        private readonly TextBlock _checkedHeaderText = new();
        private readonly TextBlock _summaryText = new();
        private Button _detectButton = new();
        private readonly Services.IAppLogger _logger =
            AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private bool _closeConfirmed;

        /// <param name="infobases">Все информационные базы для определения.</param>
        public DetectConfigurationsWindow(IReadOnlyList<Infobase> infobases)
        {
            Title = LocalizationManager.T("DetectConfigs.Title");
            Width = 760;
            Height = 560;
            MinWidth = 600;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            foreach (var ib in infobases)
                _rows.Add(new DetectConfigRowViewModel(ib));

            Content = BuildRoot();
            foreach (var row in _rows)
                AddRow(row);
            UpdateCheckedHeader();
            Closing += (_, _) => OnClosingConfirm();
        }

        /// <summary>Признак того, что хотя бы одна база была изменена (для персиста в настройках).</summary>
        public bool DataChanged { get; private set; }

        private static string T(string key) => LocalizationManager.T(key);

        private static TextBlock MakeHeaderText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        private static void BindSecondary(TextBlock block)
            => Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");

        /// <summary>Обновляет заголовок колонки-флажка счётчиком отмеченных элементов.</summary>
        private void UpdateCheckedHeader()
        {
            _checkedHeaderText.Text = string.Format(
                LocalizationManager.T("DetectConfigs.Column.CheckedHeaderFormat"),
                _rows.Count(r => r.IsChecked));
        }

        private void UpdateSelectionButtonsEnabled()
        {
            var busy = !_detectButton.IsEnabled;
            foreach (var (_, check) in _checks)
                check.IsEnabled = !busy;
            // Флажки, которые прямо сейчас обрабатываются, тоже блокируем.
            foreach (var (row, check) in _checks)
                if (row.IsProcessing)
                    check.IsEnabled = false;
        }

        private void OnCheckAll()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = true;
            UpdateCheckedHeader();
        }

        private void OnUncheckAll()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = false;
            UpdateCheckedHeader();
        }

        private void OnInvert()
        {
            foreach (var row in _rows.Where(r => !r.IsProcessing))
                row.IsChecked = !row.IsChecked;
            UpdateCheckedHeader();
        }

        /// <summary>
        /// Последовательно обрабатывает отмеченные строки. Последовательность обязательна:
        /// <see cref="ComReadHost"/> сериализует запросы статической блокировкой, параллельный
        /// вызов упёрся бы в ту же блокировку без ускорения (план issue #236 §3).
        /// </summary>
        private async void OnDetectClick()
        {
            var targets = _rows.Where(r => r.IsChecked && !r.IsProcessing).ToList();
            if (targets.Count == 0)
            {
                _summaryText.Text = LocalizationManager.T("DetectConfigs.NoneSelected");
                return;
            }

            _detectButton.IsEnabled = false;
            UpdateSelectionButtonsEnabled();
            _summaryText.Text = string.Empty;

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

                var success = info is not null;
                if (success)
                {
                    ok++;
                    DataChanged = true;
                }
                else
                {
                    errors++;
                }

                var r = row;
                var infoLocal = info;
                var errLocal = errorText;
                var okLocal = success;
                Dispatcher.UIThread.Post(() =>
                {
                    r.SyncFromInfobase();
                    r.IsProcessing = false;
                    if (okLocal)
                    {
                        r.IsChecked = false;
                    }
                    else
                    {
                        r.ErrorText = errLocal ?? ConfigurationInfoService.LastComError
                            ?? string.Format(LocalizationManager.T("DetectConfigs.ErrorRowFormat"), r.Name);
                    }
                    UpdateRow(r);
                    UpdateCheckedHeader();
                    UpdateSelectionButtonsEnabled();
                });
            }

            var okTotal = ok;
            var errTotal = errors;
            Dispatcher.UIThread.Post(() =>
            {
                _detectButton.IsEnabled = true;
                UpdateSelectionButtonsEnabled();
                _summaryText.Text = string.Format(
                    LocalizationManager.T("DetectConfigs.DoneFormat"), okTotal, errTotal);
            });
        }

        private void OnClosingConfirm()
        {
            if (_closeConfirmed || !_rows.Any(r => r.IsChecked))
                return;

            // Требование #6/#7: если остались отмеченные (необработанные) строки,
            // спрашиваем подтверждение перед закрытием — окно само не закрывается.
            if (_dialogs.Confirm(
                LocalizationManager.T("DetectConfigs.CloseConfirm"),
                LocalizationManager.T("DetectConfigs.Title")))
            {
                _closeConfirmed = true;
            }
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = LocalizationManager.T("DetectConfigs.TitleHeader"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var subtitle = new TextBlock
            {
                Text = LocalizationManager.T("DetectConfigs.Subtitle"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            BindSecondary(subtitle);
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);

            // Список баз
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            var dock = new DockPanel { LastChildFill = true };

            // Панель кнопок выбора
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var checkAll = new Button { Content = LocalizationManager.T("DetectConfigs.Button.CheckAll") };
            checkAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(checkAll, LocalizationManager.T("DetectConfigs.Button.CheckAllTooltip"));
            checkAll.Click += (_, _) => OnCheckAll();

            var uncheckAll = new Button { Content = LocalizationManager.T("DetectConfigs.Button.UncheckAll") };
            uncheckAll.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(uncheckAll, LocalizationManager.T("DetectConfigs.Button.UncheckAllTooltip"));
            uncheckAll.Click += (_, _) => OnUncheckAll();

            var invert = new Button { Content = LocalizationManager.T("DetectConfigs.Button.Invert") };
            invert.Styled(ControlThemes.SelectAllButton);
            ToolTip.SetTip(invert, LocalizationManager.T("DetectConfigs.Button.InvertTooltip"));
            invert.Click += (_, _) => OnInvert();

            toolbar.Children.Add(checkAll);
            toolbar.Children.Add(uncheckAll);
            toolbar.Children.Add(invert);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            dock.Children.Add(toolbarBorder);

            // Шапка таблицы
            var headerGrid = BuildHeaderGrid();
            DockPanel.SetDock(headerGrid, Dock.Top);
            dock.Children.Add(headerGrid);

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };
            dock.Children.Add(scroll);

            listBorder.Child = dock;
            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            _summaryText.VerticalAlignment = VerticalAlignment.Center;
            _summaryText.TextWrapping = TextWrapping.Wrap;
            _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
            BindSecondary(_summaryText);
            Grid.SetColumn(_summaryText, 0);
            bottom.Children.Add(_summaryText);

            _detectButton = new Button
            {
                Content = LocalizationManager.T("DetectConfigs.Button.Detect"),
                Width = 180,
                Height = 36,
                IsDefault = true
            };
            _detectButton.Styled(ControlThemes.DialogConfirmButton);
            _detectButton.Click += (_, _) => OnDetectClick();

            var cancel = BuildCancelActionButton(140);
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Children = { _detectButton, cancel }
            };
            Grid.SetColumn(rightPanel, 1);
            bottom.Children.Add(rightPanel);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Строит закреплённую шапку таблицы (флажок со счётчиком, имя, конфигурация, релиз).</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            ApplyColumns(grid);

            _checkedHeaderText.FontSize = 12;
            _checkedHeaderText.FontWeight = FontWeight.SemiBold;
            BindSecondary(_checkedHeaderText);
            Grid.SetColumn(_checkedHeaderText, 0);
            grid.Children.Add(_checkedHeaderText);

            var nameHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Name"));
            BindSecondary(nameHeader);
            Grid.SetColumn(nameHeader, 1);
            grid.Children.Add(nameHeader);

            var cfgHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.CurrentConfig"));
            BindSecondary(cfgHeader);
            Grid.SetColumn(cfgHeader, 2);
            grid.Children.Add(cfgHeader);

            var verHeader = MakeHeaderText(LocalizationManager.T("DetectConfigs.Column.Version"));
            BindSecondary(verHeader);
            Grid.SetColumn(verHeader, 3);
            grid.Children.Add(verHeader);

            return grid;
        }

        private void ApplyColumns(Grid grid)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));              // 0 — флажок
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 1 — имя
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star))); // 2 — конфигурация
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star))); // 3 — версия
        }

        /// <summary>Строит строку базы и добавляет её в общий список.</summary>
        private void AddRow(DetectConfigRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ApplyColumns(grid);

            var check = new CheckBox
            {
                IsChecked = row.IsChecked,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            check.Styled(ControlThemes.CacheCleanCheckBox);
            check.IsCheckedChanged += (_, _) =>
            {
                row.IsChecked = check.IsChecked == true;
                UpdateCheckedHeader();
            };
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var config = new TextBlock
            {
                Text = row.ConfigurationName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(config, 2);
            grid.Children.Add(config);

            var version = new TextBlock
            {
                Text = row.ConfigurationVersion,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(version, 3);
            grid.Children.Add(version);

            _checks[row] = check;
            _configTexts[row] = config;
            _versionTexts[row] = version;
            _rowsPanel.Children.Add(grid);
        }

        /// <summary>Обновляет текстовые ячейки строки из её модели.</summary>
        private void UpdateRow(DetectConfigRowViewModel row)
        {
            if (_configTexts.TryGetValue(row, out var config))
                config.Text = row.ConfigurationName;
            if (_versionTexts.TryGetValue(row, out var version))
                version.Text = row.ConfigurationVersion;
            if (_checks.TryGetValue(row, out var check))
                check.IsChecked = row.IsChecked;
        }
    }
}
#endif