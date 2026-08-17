using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора версии платформы 1С (тот же вид, что в Настройки → Платформы):
    /// линия (8.2 / 8.3 / 8.5) → разрядность (64/32) → сборка с путём.
    /// </summary>
    public partial class PlatformVersionPickerWindow : Window
    {
        private string _selectedVersion = string.Empty;
        private List<PlatformVersionGroup> _tree = new();

        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            InitializeComponent();

            // Учитываем доп. пути поиска платформ (как в настройках).
            var extras = PlatformVersionService.GetAdditionalSearchPaths();
            var infos = PlatformVersionService.FindInstalledVersionInfos(extras);
            if (infos.Count == 0 && installedPlatformVersions != null)
            {
                infos = installedPlatformVersions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new PlatformVersionInfo { Display = s.Trim(), Path = "" })
                    .ToList();
            }
            else if (installedPlatformVersions != null)
            {
                var known = new HashSet<string>(infos.Select(i => i.Display), StringComparer.OrdinalIgnoreCase);
                foreach (var s in installedPlatformVersions)
                {
                    if (string.IsNullOrWhiteSpace(s) || known.Contains(s.Trim())) continue;
                    infos.Add(new PlatformVersionInfo { Display = s.Trim(), Path = "" });
                }
            }

            // Группировка: 8.2 / 8.3 / 8.5 → 64/32 → сборка
            _tree = PlatformVersionService.BuildGroupedTree(infos);
            PlatformsTree.ItemsSource = _tree;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExpandAllGroups(PlatformsTree);
                if (!string.IsNullOrWhiteSpace(currentVersion))
                    SelectCurrent(currentVersion);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public string Result => _selectedVersion;

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

        /// <summary>Разворачивает все нелистовые узлы (линии 8.x и разрядность), чтобы группировка была видна сразу.</summary>
        private static void ExpandAllGroups(ItemsControl parent)
        {
            parent.UpdateLayout();
            foreach (var item in parent.Items)
            {
                if (item is not PlatformVersionGroup node || node.IsLeaf)
                    continue;
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
                    continue;
                container.IsExpanded = true;
                container.UpdateLayout();
                ExpandAllGroups(container);
            }
        }

        private void SelectCurrent(string currentVersion)
        {
            var leaf = FindMatchingLeaf(_tree, currentVersion);
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
