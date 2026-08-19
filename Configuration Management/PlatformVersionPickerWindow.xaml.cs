using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (как в стартере):
    /// фильтр Все / x32 / x64, сортировка A→Z / Z→A,
    /// дерево 8.3 → 8.3.27 → 8.3.27.2214 (x64).
    /// </summary>
    public partial class PlatformVersionPickerWindow : Window
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionInfo> _allInfos = new();
        private string _currentVersion = string.Empty;
        private bool _sortAscending = false; // по умолчанию — свежие версии сверху
        private string _archFilter = "all"; // all | x32 | x64

        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            InitializeComponent();
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

            RefreshTree();
        }

        public string Result => _selectedVersion;

        private void RefreshTree()
        {
            var filtered = FilterByArchitecture(_allInfos, _archFilter);
            var tree = PlatformVersionService.BuildGroupedTree(filtered);
            if (_sortAscending)
                tree = ReverseTreeOrder(tree);

            PlatformsTree.ItemsSource = tree;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Полностью разворачиваем дерево (линии → группы сборок → сборки), как в стартере
                ExpandAll(PlatformsTree);
                if (!string.IsNullOrWhiteSpace(_currentVersion))
                    SelectCurrent(_currentVersion);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static List<PlatformVersionInfo> FilterByArchitecture(
            IEnumerable<PlatformVersionInfo> infos, string filter)
        {
            if (filter == "all")
                return infos.ToList();

            return infos.Where(i =>
            {
                PlatformVersionService.ParseVariant(i.Display, out _, out var arch);
                var label = PlatformVersionService.FormatArchitectureLabel(arch);
                if (filter == "x64")
                    return label == "x64" || string.IsNullOrEmpty(label); // без метки часто 64
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

        private static void ExpandAll(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                {
                    tvi.IsExpanded = true;
                    tvi.UpdateLayout();
                    ExpandAll(tvi);
                }
            }
        }

        private void OnArchFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (FilterX32.IsChecked == true) _archFilter = "x32";
            else if (FilterX64.IsChecked == true) _archFilter = "x64";
            else _archFilter = "all";
            RefreshTree();
        }

        private void OnSortAsc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = true;
            SortAsc.IsChecked = true;
            SortDesc.IsChecked = false;
            RefreshTree();
        }

        private void OnSortDesc_Click(object sender, RoutedEventArgs e)
        {
            _sortAscending = false;
            SortDesc.IsChecked = true;
            SortAsc.IsChecked = false;
            RefreshTree();
        }

        private void OnPlatformsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is PlatformVersionGroup { IsLeaf: true, Variant: { } variant })
            {
                _selectedVersion = variant;
                SelectButton.IsEnabled = true;
            }
            else
            {
                SelectButton.IsEnabled = false;
            }
        }

        private void OnPlatformsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedVersion))
                OnSelect_Click(sender, e);
        }

        private void OnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;
            DialogResult = true;
        }

        private void SelectCurrent(string currentVersion)
        {
            if (PlatformsTree.ItemsSource is not IEnumerable<PlatformVersionGroup> roots)
                return;
            var leaf = FindBestLeaf(roots, currentVersion);
            if (leaf is null) return;
            leaf.IsCurrent = true; // подсветка жирным
            SelectNodeInTree(PlatformsTree, leaf);
        }

        /// <summary>
        /// Ищет лист, соответствующий текущей версии. Повторяет «трюк 1С»:
        /// если указана частичная версия (8.3 или 8.3.19, в т.ч. с разрядностью «8.3 (64)»),
        /// выбирается максимальная доступная сборка в пределах этой линии/группы.
        /// </summary>
        private static PlatformVersionGroup? FindBestLeaf(
            IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion)) return null;

            ParseVersionAndArch(currentVersion, out var version, out var arch);
            var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 4)
                return FindExactLeaf(nodes, currentVersion); // полная версия — точное совпадение

            var linePrefix = string.Join(".", parts.Take(2));
            var line = nodes.FirstOrDefault(n =>
                !n.IsLeaf && string.Equals(n.Name, linePrefix, StringComparison.OrdinalIgnoreCase));
            if (line is null) return null;

            if (parts.Length == 3)
            {
                // группа сборок, например «8.3.19» → максимальная сборка в 8.3.19
                var buildPrefix = string.Join(".", parts.Take(3));
                var build = line.Children.FirstOrDefault(n =>
                    !n.IsLeaf && string.Equals(n.Name, buildPrefix, StringComparison.OrdinalIgnoreCase));
                return build is null ? null : FirstLeaf(build.Children, arch);
            }

            // только линия, например «8.3» → 1С сама выбирает максимальную доступную версию
            return FirstLeaf(line.Children, arch);
        }

        /// <summary>
        /// Возвращает первую (максимальную, т.к. дерево отсортировано по убыванию)
        /// сборку в поддереве, при необходимости ограниченную разрядностью.
        /// </summary>
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

        private static PlatformVersionGroup? FindExactLeaf(
            IEnumerable<PlatformVersionGroup> nodes, string currentVersion)
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

        /// <summary>
        /// Разбирает строку версии, отделяя необязательную разрядность «8.3 (64)».
        /// Возвращает версию без суффикса и разрядность (null, если не указана).
        /// </summary>
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

        private static bool SelectNodeInTree(ItemsControl parent, PlatformVersionGroup target)
        {
            foreach (var item in parent.Items)
            {
                if (item is not PlatformVersionGroup node) continue;
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (ReferenceEquals(node, target))
                {
                    if (container is not null)
                    {
                        node.IsSelected = true;
                        container.IsSelected = true;
                        container.BringIntoView();
                    }
                    return true;
                }
                if (container is not null)
                {
                    container.IsExpanded = true;
                    container.UpdateLayout();
                    if (SelectNodeInTree(container, target))
                        return true;
                }
            }
            return false;
        }
    }
}
