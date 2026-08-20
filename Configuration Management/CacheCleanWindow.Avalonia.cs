#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора типа очищаемого кэша 1С и набора информационных баз, для которых
    /// нужно выполнить очистку. Avalonia/Linux-версия WPF-окна <see cref="CacheCleanWindow"/>.
    /// </summary>
    public class CacheCleanWindow : ModalWindowBase
    {
        private readonly List<Infobase> _infobases;
        private readonly Dictionary<CheckBox, Infobase> _baseChecks = new();

        private readonly CheckBox _programCacheCheck = new() { Content = "Программный кэш" };
        private readonly CheckBox _userCacheCheck = new() { Content = "Пользовательский кэш" };
        private readonly TextBox _searchBox = new() { Padding = new Thickness(10, 7), Watermark = "Поиск базы…" };
        private readonly StackPanel _basesPanel = new();
        private readonly TextBlock _basesCountText = new();
        private readonly Button _cleanButton = new() { IsDefault = true };

        /// <param name="infobases">Все доступные информационные базы.</param>
        /// <param name="initialKind">Изначально выбранный тип кэша.</param>
        /// <param name="defaultSelected">База, выбранная по умолчанию (например, выделенная в главном окне).</param>
        public CacheCleanWindow(IEnumerable<Infobase> infobases, OneCCacheKind initialKind, Infobase? defaultSelected = null)
        {
            Title = "Очистка кэша 1С";
            Width = 580;
            Height = 540;
            MinWidth = 480;
            MinHeight = 440;
            CanResize = true;

            _infobases = infobases.ToList();

            _programCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.Program);
            _userCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.User);
            _programCacheCheck.Checked += (_, _) => UpdateCleanEnabled();
            _programCacheCheck.Unchecked += (_, _) => UpdateCleanEnabled();
            _userCacheCheck.Checked += (_, _) => UpdateCleanEnabled();
            _userCacheCheck.Unchecked += (_, _) => UpdateCleanEnabled();
            _searchBox.TextChanged += (_, _) => OnSearchTextChanged();

            BuildBasesList(defaultSelected);
            UpdateCount();
            UpdateCleanEnabled();

            Content = BuildRoot();
        }

        /// <summary>Тип кэша, выбранный пользователем.</summary>
        public OneCCacheKind SelectedCacheKind { get; private set; } = OneCCacheKind.None;

        /// <summary>Список баз, выбранных для очистки.</summary>
        public IReadOnlyList<Infobase> SelectedInfobases { get; private set; } = Array.Empty<Infobase>();

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = "Очистка кэша 1С: выбор типа кэша и баз",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var description = new TextBlock
            {
                Text = "Программный и пользовательский кэш — разные данные и по-разному влияют на информационные базы. Выберите, какой кэш очистить и для каких баз.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Grid.SetRow(description, 1);
            grid.Children.Add(description);

            var typeLabel = new TextBlock
            {
                Text = "Тип очищаемого кэша",
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 16, 0, 6)
            };
            Grid.SetRow(typeLabel, 2);
            grid.Children.Add(typeLabel);

            var typePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            _programCacheCheck.ToolTip = new ToolTip { Content = "%LOCALAPPDATA%\\1C\\1cv8…" };
            _userCacheCheck.ToolTip = new ToolTip { Content = "%APPDATA%\\1C\\1cv8…" };
            typePanel.Children.Add(_programCacheCheck);
            typePanel.Children.Add(_userCacheCheck);
            Grid.SetRow(typePanel, 3);
            grid.Children.Add(typePanel);

            // Список баз
            var basesBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var dock = new DockPanel { LastChildFill = true };

            _searchBox.Margin = new Thickness(8, 8, 8, 2);
            DockPanel.SetDock(_searchBox, Dock.Top);
            dock.Children.Add(_searchBox);

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(8, 2, 8, 4)
            };
            var selectAll = new Button { Content = "☑ Выбрать все", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            selectAll.Click += (_, _) => { foreach (var check in _baseChecks.Keys) check.IsChecked = true; UpdateCount(); UpdateCleanEnabled(); };
            var clearAll = new Button { Content = "☐ Снять все", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            clearAll.Click += (_, _) => { foreach (var check in _baseChecks.Keys) check.IsChecked = false; UpdateCount(); UpdateCleanEnabled(); };
            toolbar.Children.Add(selectAll);
            toolbar.Children.Add(clearAll);
            DockPanel.SetDock(toolbar, Dock.Top);
            dock.Children.Add(toolbar);

            var basesScroll = new ScrollViewer
            {
                Content = _basesPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4)
            };
            dock.Children.Add(basesScroll);

            basesBorder.Child = dock;
            Grid.SetRow(basesBorder, 4);
            grid.Children.Add(basesBorder);

            // Нижняя панель: счётчик + кнопки
            var bottom = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            Grid.SetColumn(_basesCountText, 0);
            _basesCountText.VerticalAlignment = VerticalAlignment.Center;
            bottom.Children.Add(_basesCountText);

            var cancel = new Button { Content = "Отмена", MinWidth = 100, IsCancel = true };
            cancel.Click += (_, _) => Close();
            Grid.SetColumn(cancel, 1);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            bottom.Children.Add(cancel);

            _cleanButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "🗑", VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Очистить кэш", VerticalAlignment = VerticalAlignment.Center }
                }
            };
            _cleanButton.MinWidth = 130;
            _cleanButton.Click += (_, _) => OnClean_Click();
            Grid.SetColumn(_cleanButton, 2);
            bottom.Children.Add(_cleanButton);

            Grid.SetRow(bottom, 5);
            grid.Children.Add(bottom);

            return grid;
        }

        private void BuildBasesList(Infobase? defaultSelected)
        {
            _basesPanel.Children.Clear();
            _baseChecks.Clear();

            foreach (var ib in _infobases)
            {
                var check = new CheckBox
                {
                    Content = ib.Name,
                    IsChecked = ReferenceEquals(ib, defaultSelected),
                    Margin = new Thickness(0, 4, 0, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = new ToolTip
                    {
                        Content = string.IsNullOrWhiteSpace(ib.ConnectionPathDisplay) ? ib.Name : ib.ConnectionPathDisplay
                    }
                };
                check.Checked += (_, _) => OnBaseChecked();
                check.Unchecked += (_, _) => OnBaseChecked();
                _baseChecks[check] = ib;
                _basesPanel.Children.Add(check);
            }
        }

        private void OnSearchTextChanged()
        {
            var query = _searchBox.Text?.Trim() ?? string.Empty;
            foreach (var kv in _baseChecks)
            {
                var visible = query.Length == 0
                    || kv.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (kv.Value.ConnectionPathDisplay?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
                kv.Key.IsVisible = visible;
            }
        }

        private void OnBaseChecked()
        {
            UpdateCount();
            UpdateCleanEnabled();
        }

        private void UpdateCount()
        {
            var selected = _baseChecks.Count(kv => kv.Key.IsChecked == true);
            var total = _baseChecks.Count;
            _basesCountText.Text = $"Выбрано: {selected} из {total}";
        }

        private void UpdateCleanEnabled()
        {
            var hasType = _programCacheCheck.IsChecked == true || _userCacheCheck.IsChecked == true;
            var hasBases = _baseChecks.Any(kv => kv.Key.IsChecked == true);
            _cleanButton.IsEnabled = hasType && hasBases;
        }

        private void OnClean_Click()
        {
            var kind = OneCCacheKind.None;
            if (_programCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.Program;
            if (_userCacheCheck.IsChecked == true)
                kind |= OneCCacheKind.User;

            SelectedCacheKind = kind;
            SelectedInfobases = _baseChecks
                .Where(kv => kv.Key.IsChecked == true)
                .Select(kv => kv.Value)
                .ToList();

            DialogResult = true;
            Close();
        }
    }
}
#endif