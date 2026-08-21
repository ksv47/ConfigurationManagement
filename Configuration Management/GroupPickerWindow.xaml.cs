using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора группы в виде дерева (или «Без группы» / корень).
    /// </summary>
    public partial class GroupPickerWindow : Window
    {
        private readonly IReadOnlyList<Group> _groups;
        private readonly List<Group> _allowed;
        private readonly string _currentGroupId;
        private readonly bool _allowNone;
        private readonly string _noneLabel;
        private bool _sortAscending = true; // по умолчанию — по возрастанию (А→Я)
        private GroupNodeViewModel? _selectedNode;

        /// <param name="groups">Список групп.</param>
        /// <param name="currentGroupId">Текущая выбранная группа (для подсветки).</param>
        /// <param name="excludeGroupId">Группа, которую нельзя выбрать (сама редактируемая + потомки отфильтруются).</param>
        /// <param name="allowNone">Разрешить выбор «Без группы» / корень.</param>
        /// <param name="noneLabel">Подпись корневого пункта.</param>
        public GroupPickerWindow(
            IEnumerable<Group> groups,
            string? currentGroupId = null,
            string? excludeGroupId = null,
            bool allowNone = true,
            string noneLabel = "")
        {
            InitializeComponent();
            _groups = groups.ToList();
            _currentGroupId = currentGroupId ?? string.Empty;
            _allowNone = allowNone;
            _noneLabel = string.IsNullOrEmpty(noneLabel)
                ? LocalizationManager.T("Connection.NoGroup")
                : noneLabel;

            // IsAncestorOrSelf(g.Id, excludeId) = exclude is ancestor of g → g is under exclude.
            // Also exclude the group itself via Id check.
            _allowed = string.IsNullOrEmpty(excludeGroupId)
                ? _groups.ToList()
                : _groups.Where(g =>
                        !string.Equals(g.Id, excludeGroupId, StringComparison.OrdinalIgnoreCase)
                        && !GroupHierarchyHelper.IsAncestorOrSelf(g.Id, excludeGroupId, _groups))
                    .ToList();

            RefreshTree();
        }

        /// <summary>
        /// Перестраивает дерево с учётом текущего направления сортировки (по наименованию).
        /// </summary>
        private void RefreshTree()
        {
            var roots = GroupNodeViewModel.BuildTree(_allowed);

            // Сортировка групп по наименованию, как в основном дереве (по возрастанию/убыванию).
            var groupComparer = StringComparer.OrdinalIgnoreCase;
            roots.Sort(_sortAscending
                ? (a, b) => groupComparer.Compare(a.DisplayName, b.DisplayName)
                : (a, b) => groupComparer.Compare(b.DisplayName, a.DisplayName));
            foreach (var root in roots)
                root.SortChildrenRecursive(_sortAscending);

            var items = new List<GroupNodeViewModel>();
            if (_allowNone)
                items.Add(new GroupNodeViewModel(null, displayName: _noneLabel));

            items.AddRange(roots);
            GroupsTree.ItemsSource = items;

            if (!string.IsNullOrEmpty(_currentGroupId))
                SelectById(items, _currentGroupId);
            else if (_allowNone && items.Count > 0)
            {
                _selectedNode = items[0];
                SelectButton.IsEnabled = true;
            }
            else
            {
                SelectButton.IsEnabled = false;
            }
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

        /// <summary>Выбранная группа; null — без группы / корень.</summary>
        public Group? ResultGroup => _selectedNode?.Group;

        /// <summary>Id выбранной группы; пустая строка — корень / без группы.</summary>
        public string ResultGroupId => _selectedNode?.Group?.Id ?? string.Empty;

        /// <summary>Полный путь выбранной группы; пустая строка — без группы.</summary>
        public string ResultFullPath =>
            _selectedNode?.Group is null
                ? string.Empty
                : GroupHierarchyHelper.GetFullPath(_selectedNode.Group, _groups);

        private void SelectById(IEnumerable<GroupNodeViewModel> roots, string groupId)
        {
            foreach (var root in roots)
            {
                if (TrySelect(root, groupId))
                    return;
            }
        }

        private bool TrySelect(GroupNodeViewModel node, string groupId)
        {
            if (node.Group is not null
                && string.Equals(node.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNode = node;
                SelectButton.IsEnabled = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var tvi = FindTreeViewItem(GroupsTree, node);
                    if (tvi is not null)
                    {
                        tvi.IsSelected = true;
                        tvi.BringIntoView();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return true;
            }

            foreach (var child in node.Children)
            {
                if (TrySelect(child, groupId))
                    return true;
            }
            return false;
        }

        private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object data)
        {
            parent.ApplyTemplate();
            var generator = parent.ItemContainerGenerator;
            if (generator.ContainerFromItem(data) is TreeViewItem direct)
                return direct;

            foreach (var item in parent.Items)
            {
                if (generator.ContainerFromItem(item) is not TreeViewItem tvi)
                    continue;
                var found = FindTreeViewItem(tvi, data);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private void OnGroupsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedNode = GroupsTree.SelectedItem as GroupNodeViewModel;
            SelectButton.IsEnabled = _selectedNode is not null;
        }

        private void OnGroupsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectButton.IsEnabled)
                OnSelect_Click(sender, e);
        }

        private void OnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode is null)
                return;
            DialogResult = true;
        }
    }
}
