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
        private bool _sortAscending = true;
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
                // Разворачиваем первый уровень (линии 8.3), чтобы было похоже на стартер
                ExpandTopLevel(PlatformsTree);
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

        private static void ExpandTopLevel(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                    tvi.IsExpanded = true;
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
            var leaf = FindMatchingLeaf(roots, currentVersion);
            if (leaf is null) return;
            SelectNodeInTree(PlatformsTree, leaf);
        }

        private static PlatformVersionGroup? FindMatchingLeaf(IEnumerable<PlatformVersionGroup> nodes, string current)
        {
            foreach (var n in nodes)
            {
                if (n.IsLeaf && MatchesCurrent(n.Variant ?? n.Name, current))
                    return n;
                var found = FindMatchingLeaf(n.Children, current);
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
