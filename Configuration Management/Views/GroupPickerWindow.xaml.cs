#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>Вид объекта, для которого выбирается группа (определяет формулировки Title/Subtitle/Help).</summary>
    public enum GroupPickerObjectKind
    {
        Group,
        Infobase
    }

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
        private string _filterText = string.Empty;
        private GroupNodeViewModel? _selectedNode;

        /// <param name="groups">Список групп.</param>
        /// <param name="currentGroupId">Текущая выбранная группа (для подсветки).</param>
        /// <param name="excludeGroupId">Группа, которую нельзя выбрать (сама редактируемая + потомки отфильтруются).</param>
        /// <param name="allowNone">Разрешить выбор «Без группы» / корень.</param>
        /// <param name="noneLabel">Подпись корневого пункта.</param>
        /// <param name="kind">Вид выбираемого объекта (группа или база) — определяет формулировки Title/Subtitle/Help.</param>
        public GroupPickerWindow(
            IEnumerable<Group> groups,
            string? currentGroupId = null,
            string? excludeGroupId = null,
            bool allowNone = true,
            string noneLabel = "",
            GroupPickerObjectKind kind = GroupPickerObjectKind.Group)
        {
            InitializeComponent();
            _groups = groups.ToList();
            _currentGroupId = currentGroupId ?? string.Empty;
            _allowNone = allowNone;
            _noneLabel = string.IsNullOrEmpty(noneLabel)
                ? LocalizationManager.T("Connection.NoGroup")
                : noneLabel;

            ApplyObjectKind(kind);

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
        /// Подставляет конкретные формулировки заголовка/подзаголовка/справки в зависимости от вида выбираемого объекта.
        /// </summary>
        private void ApplyObjectKind(GroupPickerObjectKind kind)
        {
            string titleKey, subtitleKey, helpKey;
            if (kind == GroupPickerObjectKind.Infobase)
            {
                titleKey = "GroupPicker.TitleBase";
                subtitleKey = "GroupPicker.SubtitleBase";
                helpKey = "GroupPicker.HelpBase";
            }
            else
            {
                titleKey = "GroupPicker.TitleGroup";
                subtitleKey = "GroupPicker.SubtitleGroup";
                helpKey = "GroupPicker.HelpGroup";
            }
            Title = LocalizationManager.T(titleKey);
            if (TitleText is not null) TitleText.Text = LocalizationManager.T(titleKey);
            if (SubtitleText is not null) SubtitleText.Text = LocalizationManager.T(subtitleKey);
            if (HelpLink is not null) HelpLink.HelpText = LocalizationManager.T(helpKey);
        }

        /// <summary>
        /// Перестраивает дерево с учётом текущего направления сортировки и текста поиска.
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

            // Поиск фильтрует дерево, сохраняя иерархию: остаются узлы, где совпал сам
            // узел или хотя бы один потомок.
            if (!string.IsNullOrWhiteSpace(_filterText))
                items = FilterRoots(items, _filterText.Trim());

            GroupsTree.ItemsSource = items;

            var hasMatch = items.Count > 0;
            EmptyLabel.Visibility = hasMatch ? Visibility.Collapsed : Visibility.Visible;
            GroupsTree.Visibility = hasMatch ? Visibility.Visible : Visibility.Collapsed;

            if (!hasMatch)
            {
                _selectedNode = null;
                UpdateSelection();
                return;
            }

            if (!string.IsNullOrEmpty(_currentGroupId))
                SelectById(items, _currentGroupId);
            else if (_allowNone && items.Count > 0 && _selectedNode is null)
            {
                _selectedNode = items[0];
                UpdateSelection();
            }
            else
            {
                UpdateSelection();
            }
        }

        /// <summary>Фильтрует корневые узлы по подстроке имени, сохраняя ветви иерархии.</summary>
        private static List<GroupNodeViewModel> FilterRoots(IEnumerable<GroupNodeViewModel> roots, string filter)
        {
            var result = new List<GroupNodeViewModel>();
            foreach (var root in roots)
            {
                var copy = FilterNode(root, filter);
                if (copy is not null)
                    result.Add(copy);
            }
            return result;
        }

        private static GroupNodeViewModel? FilterNode(GroupNodeViewModel node, string filter)
        {
            var selfMatches = node.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            var keptChildren = new List<GroupNodeViewModel>();
            foreach (var child in node.Children)
            {
                var childCopy = FilterNode(child, filter);
                if (childCopy is not null)
                    keptChildren.Add(childCopy);
            }

            if (!selfMatches && keptChildren.Count == 0)
                return null;

            var copy = new GroupNodeViewModel(node.Group, displayName: node.DisplayName);
            foreach (var child in keptChildren)
                copy.Children.Add(child);
            return copy;
        }

        /// <summary>Обновляет состояние кнопки «Выбрать» и сводку выбора внизу окна.</summary>
        private void UpdateSelection()
        {
            var enabled = _selectedNode is not null;
            SelectButton.IsEnabled = enabled;

            if (PathLabel is not null)
            {
                PathLabel.Text = _selectedNode?.Group is null
                    ? (_allowNone ? _noneLabel : string.Empty)
                    : GroupHierarchyHelper.GetFullPath(_selectedNode.Group, _groups);
                PathLabel.Visibility = _selectedNode is null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void OnSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _filterText = SearchBox.Text ?? string.Empty;
            ClearSearch.Visibility = _filterText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshTree();
        }

        private void OnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
                UpdateSelection();
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
            UpdateSelection();
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
#endif
