#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class MainWindow
    {

        /// <summary>
        /// Собирает список узлов дерева в порядке их отображения (сверху вниз),
        /// включая развёрнутые подгруппы. Возвращает элементы GroupNodeViewModel и Infobase.
        /// </summary>
        private List<object> GetVisibleTreeNodes()
        {
            var result = new List<object>();
            foreach (var root in _viewModel.GroupNodes)
                AppendVisible(root, result);
            return result;
        }

        private static void AppendVisible(object node, List<object> result)
        {
            result.Add(node);

            if (node is not GroupNodeViewModel group || !group.IsExpanded)
                return;

            foreach (var item in group.Items)
                AppendVisible(item, result);
        }

        /// <summary>
        /// Определяет текущий выбранный узел дерева по модели представления.
        /// </summary>
        private object? FindCurrentTreeNode(List<object> visible)
        {
            if (_viewModel.SelectedInfobase is not null)
            {
                foreach (var node in visible)
                {
                    if (ReferenceEquals(node, _viewModel.SelectedInfobase))
                        return node;
                }
                return _viewModel.SelectedInfobase;
            }

            if (_viewModel.SelectedGroupNode is not null)
            {
                foreach (var node in visible)
                {
                    if (ReferenceEquals(node, _viewModel.SelectedGroupNode))
                        return node;
                }
                return _viewModel.SelectedGroupNode;
            }

            return null;
        }

        /// <summary>
        /// Выделяет указанный узел дерева (группу или базу), синхронизирует модель
        /// и переводит фокус на соответствующий TreeViewItem, чтобы дальнейшая
        /// навигация стрелками была стабильной и не «прыгала» на кнопки.
        /// </summary>
        private void SelectTreeNode(object node)
        {
            var item = FindTreeViewItemForData(node);
            switch (node)
            {
                case Infobase infobase:
                    if (item is not null)
                        ApplySelection(item, infobase);
                    else
                        _viewModel.SelectedInfobase = infobase;
                    break;
                case GroupNodeViewModel group when group.Group is not null:
                    if (item is not null)
                        ApplyGroupSelection(item, group);
                    else
                        _viewModel.SelectedGroupNode = group;
                    break;
            }

            if (item is not null)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
            else
            {
                Keyboard.Focus(MainTree);
            }

            // Прокручиваем список к выбранной строке (отложенно, чтобы контейнер
            // виртуализированного узла успел создаться после установки выделения).
            var scrollTarget = item;
            Dispatcher.BeginInvoke(new Action(() => ScrollSelectedIntoView(scrollTarget)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Восстанавливает последнюю выбранную строку списка (базу или группу) после запуска.
        /// Строка определяется по сохранённым значениям
        /// <see cref="ViewModels.MainViewModel.LastSelectedInfobaseId"/> и
        /// <see cref="ViewModels.MainViewModel.LastSelectedGroupPath"/>.
        /// </summary>
        private void RestoreLastSelection()
        {
            if (MainTree is null || _viewModel is null)
                return;

            object? target = null;

            var infobaseId = _viewModel.LastSelectedInfobaseId;
            if (!string.IsNullOrEmpty(infobaseId))
            {
                var ib = _viewModel.Infobases.FirstOrDefault(
                    i => string.Equals(i.Id, infobaseId, StringComparison.Ordinal));
                if (ib is not null)
                    target = ib;
            }
            else
            {
                var groupPath = _viewModel.LastSelectedGroupPath;
                if (!string.IsNullOrEmpty(groupPath))
                {
                    var groupNode = _viewModel.FindGroupNodeByPath(groupPath);
                    if (groupNode is not null)
                        target = groupNode;
                }
            }

            if (target is null)
                return;

            // Отложенно, чтобы виртуализированное дерево успело сгенерировать
            // контейнер строки после первой отрисовки.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (target is Infobase infobase)
                    SelectTreeNode(infobase);
                else if (target is GroupNodeViewModel group)
                    SelectTreeNode(group);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Прокручивает список так, чтобы указанный элемент дерева был в зоне видимости.
        /// Использует внутренний ScrollViewer дерева (вертикальная прокрутка списка).
        /// </summary>
        private void ScrollSelectedIntoView(TreeViewItem? item)
        {
            if (item is null)
                return;

            var scrollViewer = GetTreeScrollViewer();
            if (scrollViewer is null)
                return;

            try
            {
                var point = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                var top = point.Y;
                var bottom = top + item.ActualHeight;

                if (top < scrollViewer.VerticalOffset)
                {
                    scrollViewer.ScrollToVerticalOffset(top);
                }
                else if (bottom > scrollViewer.VerticalOffset + scrollViewer.ViewportHeight)
                {
                    scrollViewer.ScrollToVerticalOffset(bottom - scrollViewer.ViewportHeight);
                }
            }
            catch
            {
                // Элемент мог отсоединиться от визуального дерева — игнорируем.
            }
        }

        /// <summary>
        /// Возвращает контейнер TreeViewItem для указанного DataContext
        /// (поиск по всем раскрытым уровням дерева).
        /// </summary>
        private TreeViewItem? FindTreeViewItemForData(object data)
        {
            if (MainTree is null)
                return null;
            return FindTreeViewItemIn(MainTree, data);
        }

        private static TreeViewItem? FindTreeViewItemIn(ItemsControl parent, object data)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;

                if (ReferenceEquals(tvi.DataContext, data))
                    return tvi;

                if (tvi.Items.Count > 0)
                {
                    var found = FindTreeViewItemIn(tvi, data);
                    if (found is not null)
                        return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Находит узел группы, в котором размещена указанная база.
        /// </summary>
        private GroupNodeViewModel? FindGroupNodeByInfobase(Infobase infobase)
        {
            foreach (var root in _viewModel.GroupNodes)
            {
                var found = FindInNode(root, infobase);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private static GroupNodeViewModel? FindInNode(GroupNodeViewModel node, Infobase infobase)
        {
            foreach (var child in node.Children)
            {
                var found = FindInNode(child, infobase);
                if (found is not null)
                    return found;
            }
            if (node.Infobases.Any(ib => ReferenceEquals(ib, infobase)))
                return node;
            return null;
        }

        /// <summary>
        /// Синхронизирует выделение в дереве с выбранной базой.
        /// При выборе группы снимает выделение базы.
        /// </summary>
        private void OnMainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Выбором базы управляет code-behind через обработчики кликов
            // (OnInfobaseTree_PreviewMouseLeftButtonDown), которые явно устанавливают
            // TreeViewItem.IsSelected и SelectedInfobase. Здесь лишь фиксируем результат
            // изменения выбранного элемента, не трогая свойство Infobase.IsSelected.
            // Ранее двухсторонняя привязка IsSelected к модели порождала каскад событий
            // SelectedItemChanged (база дублируется в «Закреплённых» и в своей группе),
            // что приводило к бесконечной рекурсии и StackOverflowException.
            if (e.NewValue is Infobase infobase)
            {
                _viewModel.SelectedInfobase = infobase;
                // Выбор базы снимает выбор группы.
                _viewModel.SelectedGroupNode = null;
            }
            else if (e.NewValue is GroupNodeViewModel groupNode)
            {
                // Выбор группы снимает выбор базы и фиксирует выбранную группу.
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = groupNode;
            }
            else if (e.NewValue is null)
            {
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = null;
            }

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // чтобы они активировались при программной установке выделения в дереве.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Синхронизирует IsExpanded контейнеров TreeView с моделью.
        /// Только узлы GroupNodeViewModel; без многократных layout-проходов.
        /// </summary>
        internal void ApplyGroupExpandedState(bool expand)
        {
            if (MainTree is null)
                return;

            if (expand)
                ExpandGroupContainers(MainTree);
            else
                CollapseGroupContainers(MainTree);
        }

        private static void CollapseGroupContainers(ItemsControl parent)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is not ViewModels.GroupNodeViewModel node)
                    continue;
                node.NotifyIsExpanded();
            }
        }

        /// <summary>
        /// Раскрывает только контейнеры групп. Вложенность подтянется из Binding IsExpanded
        /// (в модели уже выставлено silent), без ручного обхода каждого уровня.
        /// </summary>
        private static void ExpandGroupContainers(ItemsControl parent)
        {
            for (var i = 0; i < parent.Items.Count; i++)
            {
                if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem tvi)
                    continue;
                if (tvi.DataContext is not ViewModels.GroupNodeViewModel node)
                    continue;
                node.NotifyIsExpanded();
                // Уже раскрытый узел: обновить вложенные контейнеры, если они есть.
                if (tvi.IsExpanded)
                    ExpandGroupContainers(tvi);
            }
        }

        private void OnMainTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// Устанавливает выделение указанного узла группы и синхронизирует
        /// выбранную группу в модели представления (снимая выбор базы).
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplyGroupSelection(TreeViewItem item, GroupNodeViewModel groupNode)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = null;
            _viewModel.SelectedGroupNode = groupNode;

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // т.к. программная установка выделения не всегда гарантирует автоматический
            // пересчёт CanExecute команд через CommandManager.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Устанавливает выделение указанного элемента дерева и синхронизирует
        /// выбранную базу в модели представления.
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplySelection(TreeViewItem item, Infobase infobase)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = infobase;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

    }
}
#endif
