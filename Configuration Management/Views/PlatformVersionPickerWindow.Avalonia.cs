#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (как в стартере): фильтр Все / x32 / x64,
    /// сортировка, дерево 8.3 → 8.3.27 → 8.3.27.2214 (x64). Avalonia/Linux-версия
    /// WPF-окна <see cref="PlatformVersionPickerWindow"/>.
    /// </summary>
    public class PlatformVersionPickerWindow : ModalWindowBase
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionInfo> _allInfos = new();
        private readonly string _currentVersion;
        private bool _sortAscending; // по умолчанию — свежие версии сверху
        private string _archFilter = "all";

        // Целевой узел для первичного выделения текущей версии (и его предки).
        private PlatformVersionGroup? _initialLeaf;
        private HashSet<PlatformVersionGroup>? _initialAncestors;

        private readonly TreeView _tree = new();
        private Button _selectButton = null!;
        private readonly RadioButton _filterAll = new() { Content = LocalizationManager.T("Common.All"), IsChecked = true, GroupName = "Arch" };
        private readonly RadioButton _filterX32 = new() { Content = LocalizationManager.T("PlatformVersionPicker.FilterX32"), GroupName = "Arch" };
        private readonly RadioButton _filterX64 = new() { Content = LocalizationManager.T("PlatformVersionPicker.FilterX64"), GroupName = "Arch" };
        private readonly ToggleButton _sortAsc = new();
        private readonly ToggleButton _sortDesc = new() { IsChecked = true };

        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            Title = LocalizationManager.T("PlatformVersionPicker.Title");
            Width = 520;
            Height = 560;
            MinWidth = 400;
            MinHeight = 400;

            _currentVersion = currentVersion ?? "";

            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            _allInfos = PlatformVersionService.FindInstalledVersionInfos(extras);
            if (_allInfos.Count == 0 && installedPlatformVersions != null)
            {
                _allInfos = installedPlatformVersions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new PlatformVersionInfo { Display = s.Trim(), Path = "" })
                    .ToList();
            }
            else if (installedPlatformVersions != null)
            {
                var known = new HashSet<string>(_allInfos.Select(i => i.Display), StringComparer.OrdinalIgnoreCase);
                foreach (var s in installedPlatformVersions)
                {
                    if (string.IsNullOrWhiteSpace(s) || known.Contains(s.Trim())) continue;
                    _allInfos.Add(new PlatformVersionInfo { Display = s.Trim(), Path = "" });
                }
            }

            Content = BuildRoot();
            RefreshTree();
        }

        public string Result => _selectedVersion;

        private Control BuildRoot()
        {
            // Кнопка выбора создаётся до дерева: её доступность меняет обработчик
            // выделения, а выделение дерево умеет выставить само при подготовке
            // контейнеров.
            // Закрывает окно сам обработчик: у него есть проверка на пустой выбор,
            // и терять её нельзя (PlatformVersionPickerWindow.xaml:259).
            _selectButton = BuildConfirmActionButton("Common.Select", "IconCheck", 140,
                OnSelect_Click, closeOnClick: false);
            _selectButton.Classes.Add("dimmed");
            _selectButton.IsEnabled = false;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Фильтр разрядности и сортировка лежат в карточке с отступом 8,6
            // и скруглением 6 (PlatformVersionPickerWindow.xaml:145): фильтры
            // слева, кнопки сортировки прижаты вправо.
            foreach (var radio in new[] { _filterAll, _filterX32, _filterX64 })
                radio.Styled(ControlThemes.ArchRadio);

            _filterAll.IsCheckedChanged += (_, _) =>
            {
                if (_filterAll.IsChecked != true) return;
                _archFilter = "all"; RefreshTree();
            };
            _filterX32.IsCheckedChanged += (_, _) =>
            {
                if (_filterX32.IsChecked != true) return;
                _archFilter = "x32"; RefreshTree();
            };
            _filterX64.IsCheckedChanged += (_, _) =>
            {
                if (_filterX64.IsChecked != true) return;
                _archFilter = "x64"; RefreshTree();
            };

            var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            filterPanel.Children.Add(_filterAll);
            filterPanel.Children.Add(_filterX32);
            filterPanel.Children.Add(_filterX64);

            _sortAsc.Content = IconHelper.MakeIcon("IconSortAscending", 16, out var sortAscIcon);
            _sortDesc.Content = IconHelper.MakeIcon("IconSortDescending", 16, out var sortDescIcon);
            foreach (var (toggle, icon) in new[] { (_sortAsc, sortAscIcon), (_sortDesc, sortDescIcon) })
            {
                toggle.Styled(ControlThemes.VersionSortToggle);
                // Значок красится подписью кнопки, как в разметке: у выбранной
                // она белая, у остальных обычный цвет текста.
                icon.Bind(Avalonia.Controls.Shapes.Path.FillProperty,
                    new Binding(nameof(ToggleButton.Foreground)) { Source = toggle });
            }
            // Щелчок по уже выбранной кнопке оставляет её выбранной: у автора
            // обработчик принудительно возвращает IsChecked = true
            // (PlatformVersionPickerWindow.xaml.cs:126), и состояния «сортировка
            // не выбрана ни одной кнопкой» в его версии не существует.
            _sortAsc.Click += (_, _) =>
            {
                _sortAsc.IsChecked = true;
                _sortDesc.IsChecked = false;
                if (_sortAscending) return;
                _sortAscending = true;
                RefreshTree();
            };
            _sortDesc.Click += (_, _) =>
            {
                _sortDesc.IsChecked = true;
                _sortAsc.IsChecked = false;
                if (!_sortAscending) return;
                _sortAscending = false;
                RefreshTree();
            };
            ToolTip.SetTip(_sortAsc, LocalizationManager.T("PlatformVersionPicker.SortAscTooltip"));
            ToolTip.SetTip(_sortDesc, LocalizationManager.T("PlatformVersionPicker.SortDescTooltip"));

            var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sortPanel.Children.Add(_sortAsc);
            sortPanel.Children.Add(_sortDesc);
            Grid.SetColumn(sortPanel, 1);

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.Children.Add(filterPanel);
            topGrid.Children.Add(sortPanel);

            var top = new Border
            {
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = topGrid
            };
            ThemeBrushes.Bind(top, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(top, Border.BorderBrushProperty, "BorderColorBrush");

            Grid.SetRow(top, 0);
            grid.Children.Add(top);

            // Дерево
            _tree.SelectionMode = SelectionMode.Single;
            _tree.ItemTemplate = new FuncTreeDataTemplate(
                typeof(object),
                (item, _) => BuildTreeRow(item),
                item => item is PlatformVersionGroup g && g.Children.Count > 0 ? g.Children : null);
            _tree.SelectionChanged += (_, _) =>
            {
                if (_tree.SelectedItem is PlatformVersionGroup { IsLeaf: true, Variant: { } variant })
                {
                    _selectedVersion = variant;
                    _selectButton.IsEnabled = true;
                }
                else
                {
                    _selectedVersion = string.Empty;
                    _selectButton.IsEnabled = false;
                }
            };
            _tree.DoubleTapped += (_, _) =>
            {
                if (!string.IsNullOrEmpty(_selectedVersion))
                    OnSelect_Click();
            };
            // Раскрывает предков текущей версии и выбирает её лист по мере создания
            // контейнеров (см. SelectCurrent). Внутренние контейнеры готовит сам
            // TreeViewItem, поэтому событие дерева доходит и до них.
            _tree.ContainerPrepared += OnTreeContainerPrepared;

            // Элемент дерева по общему шаблону автора (ModernTreeViewItem):
            // раскрыватель «+»/«-», подсветка наведения и выбора, отступ вложенных 16.
            if (Application.Current?.TryFindResource(ControlThemes.ModernTreeItem, out var treeItemTheme) == true
                && treeItemTheme is ControlTheme itemTheme)
            {
                _tree.ItemContainerTheme = itemTheme;
            }

            var treeBorder = new Border
            {
                Child = new ScrollViewer
                {
                    Content = _tree,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(4)
                },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            // Фон и рамка карточки дерева из ресурсов темы
            // (PlatformVersionPickerWindow.xaml:183): без привязки рамка была
            // невидимой, а фон совпадал с фоном окна вместо карточного.
            ThemeBrushes.Bind(treeBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(treeBorder, Border.BorderBrushProperty, "BorderColorBrush");

            Grid.SetRow(treeBorder, 1);
            grid.Children.Add(treeBorder);

            // Кнопки
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            // Оформление и порядок по разметке (PlatformVersionPickerWindow.xaml:257):
            // зелёный «Выбрать» слева, красная «Отмена» справа, зазор 10.
            buttons.Children.Add(_selectButton);
            buttons.Children.Add(BuildCancelActionButton(140));
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        private Control BuildTreeRow(object? item)
        {
            if (item is PlatformVersionGroup node)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1) };
                // Значок и его цвет кодируют тип узла, как в разметке
                // (PlatformVersionPickerWindow.xaml:197): линия это жёлтая папка,
                // группа сборок открытая синяя папка, сборка x64 контурный
                // зелёный куб, x32 сплошной фиолетовый, без метки синее окно.
                var (iconKey, iconColor) = node.Kind switch
                {
                    PlatformNodeKind.Line => ("IconFolder", "#F59E0B"),
                    PlatformNodeKind.BuildGroup => ("IconFolderOpen", "#3B82F6"),
                    PlatformNodeKind.LeafX64 => ("IconCubeOutline", "#22C55E"),
                    PlatformNodeKind.LeafX32 => ("IconCube", "#8B5CF6"),
                    _ => ("IconApplication", "#0EA5E9")
                };
                var icon = IconHelper.MakeIcon(iconKey, 16, new SolidColorBrush(Color.Parse(iconColor)));
                icon.Margin = new Thickness(0, 0, 6, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(icon);
                var text = new TextBlock
                {
                    Text = node.Name,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = node.IsCurrent
                        ? FontWeight.Bold
                        : (node.IsLeaf ? FontWeight.Normal : FontWeight.SemiBold)
                };
                panel.Children.Add(text);
                return panel;
            }
            return new TextBlock { Text = item?.ToString() ?? string.Empty };
        }

        private void RefreshTree()
        {

            var filtered = FilterByArchitecture(_allInfos, _archFilter);
            var tree = PlatformVersionService.BuildGroupedTree(filtered);
            if (_sortAscending)
                tree = ReverseTreeOrder(tree);

            _tree.ItemsSource = tree;

            if (!string.IsNullOrWhiteSpace(_currentVersion))
                SelectCurrent(tree);
        }

        private static List<PlatformVersionInfo> FilterByArchitecture(IEnumerable<PlatformVersionInfo> infos, string filter)
        {
            if (filter == "all")
                return infos.ToList();

            return infos.Where(i =>
            {
                PlatformVersionService.ParseVariant(i.Display, out _, out var arch);
                var label = PlatformVersionService.FormatArchitectureLabel(arch);
                if (filter == "x64")
                    return label == "x64" || string.IsNullOrEmpty(label);
                if (filter == "x32")
                    return label == "x32";
                return true;
            }).ToList();
        }

        private static List<PlatformVersionGroup> ReverseTreeOrder(List<PlatformVersionGroup> roots)
        {
            var list = roots.AsEnumerable().Reverse().ToList();
            foreach (var node in list)
                ReverseChildren(node);
            return list;
        }

        private static void ReverseChildren(PlatformVersionGroup node)
        {
            if (node.Children.Count == 0) return;
            node.Children = node.Children.AsEnumerable().Reverse().ToList();
            foreach (var c in node.Children)
                ReverseChildren(c);
        }

        private void SelectCurrent(IEnumerable<PlatformVersionGroup> roots)
        {
            var leaf = FindBestLeaf(roots, _currentVersion);
            if (leaf is null) return;
            leaf.IsCurrent = true;

            // Avalonia выделяет только через TreeView.SelectedItem, а контейнеры вложенных
            // узлов создаются лениво — пока не раскрыты предки. Поэтому запоминаем лист и
            // путь к нему, а раскрываем и выбираем по мере подготовки контейнеров.
            _initialLeaf = leaf;
            _initialAncestors = new HashSet<PlatformVersionGroup>();
            CollectAncestors(roots, leaf, _initialAncestors);
        }

        private static bool CollectAncestors(
            IEnumerable<PlatformVersionGroup> nodes,
            PlatformVersionGroup target,
            HashSet<PlatformVersionGroup> result)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, target))
                    return true;

                var found = CollectAncestors(node.Children, target, result);
                if (found)
                {
                    result.Add(node);
                    return true;
                }
            }
            return false;
        }

        private static PlatformVersionGroup? FindBestLeaf(IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion)) return null;

            ParseVersionAndArch(currentVersion, out var version, out var arch);
            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 4)
                return FindExactLeaf(nodes, currentVersion);

            var linePrefix = string.Join(".", parts.Take(2));
            var line = nodes.FirstOrDefault(n =>
                !n.IsLeaf && string.Equals(n.Name, linePrefix, StringComparison.OrdinalIgnoreCase));
            if (line is null) return null;

            if (parts.Length == 3)
            {
                var buildPrefix = string.Join(".", parts.Take(3));
                var build = line.Children.FirstOrDefault(n =>
                    !n.IsLeaf && string.Equals(n.Name, buildPrefix, StringComparison.OrdinalIgnoreCase));
                return build is null ? null : FirstLeaf(build.Children, arch);
            }

            return FirstLeaf(line.Children, arch);
        }

        private static PlatformVersionGroup? FirstLeaf(IEnumerable<PlatformVersionGroup> nodes, string? arch)
        {
            foreach (var n in nodes)
            {
                if (n.IsLeaf)
                {
                    if (arch is null || MatchesArch(n.Variant, arch))
                        return n;
                    continue;
                }
                var found = FirstLeaf(n.Children, arch);
                if (found is not null) return found;
            }
            return null;
        }

        private static PlatformVersionGroup? FindExactLeaf(IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
        {
            foreach (var n in nodes)
            {
                if (n.IsLeaf && MatchesCurrent(n.Variant ?? n.Name, currentVersion))
                    return n;
                var found = FindExactLeaf(n.Children, currentVersion);
                if (found is not null) return found;
            }
            return null;
        }

        private static bool MatchesCurrent(string variant, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion)) return false;
            PlatformVersionService.ParseVariant(variant, out var version, out _);
            PlatformVersionService.ParseVariant(currentVersion, out var cur, out _);
            if (string.Equals(version.Trim(), cur.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(variant.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesArch(string? variant, string arch)
        {
            PlatformVersionService.ParseVariant(variant ?? string.Empty, out _, out var a);
            return string.Equals(a, arch, StringComparison.OrdinalIgnoreCase);
        }

        private static void ParseVersionAndArch(string variant, out string version, out string? arch)
        {
            version = variant.Trim();
            arch = null;
            var end = variant.LastIndexOf(')');
            var start = variant.LastIndexOf('(');
            if (end >= 0 && start >= 0 && start < end)
            {
                var a = variant.Substring(start + 1, end - start - 1).Trim();
                if (a == "64" || a == "32")
                {
                    arch = a;
                    version = variant.Substring(0, start).Trim();
                }
            }
        }

        /// <summary>
        /// Раскрывает предков текущей версии и выбирает её лист по мере создания
        /// контейнеров. Начинается с корневых групп: раскрытие каждого предка готовит
        /// контейнеры его детей, пока не дойдём до листа, который и выделяем.
        /// </summary>
        private void OnTreeContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (_initialLeaf is null)
                return;
            if (e.Container?.DataContext is not PlatformVersionGroup node)
                return;

            // Предок на пути к текущей версии — раскрываем, чтобы появились дети.
            if (_initialAncestors is not null && _initialAncestors.Contains(node))
            {
                if (e.Container is TreeViewItem tvi && !tvi.IsExpanded)
                    tvi.IsExpanded = true;
                return;
            }

            // Сам лист — выделяем его как единственный источник выбора.
            if (ReferenceEquals(node, _initialLeaf))
            {
                _initialLeaf = null;
                _initialAncestors = null;
                if (!ReferenceEquals(_tree.SelectedItem, node))
                    _tree.SelectedItem = node;
            }
        }

        private void OnSelect_Click()
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;
            DialogResult = true;
            Close();
        }
    }
}
#endif