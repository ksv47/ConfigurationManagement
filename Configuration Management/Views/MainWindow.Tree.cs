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
        /// Собирает контейнеры видимых строк дерева в порядке их отображения
        /// (сверху вниз), включая строки развёрнутых подгрупп. Навигация ведётся
        /// по контейнерам, а не по объектам данных: закреплённая база присутствует
        /// в дереве дважды (узел «Закреплённые» и собственная группа), и работа
        /// с данными всякий раз возвращала бы первое (верхнее) вхождение.
        /// </summary>
        private List<TreeViewItem> GetVisibleTreeViewItems()
        {
            if (MainTree is null)
                return new List<TreeViewItem>();
            var rows = new List<TreeViewItem>();
            Collect(MainTree);
            return rows;

            void Collect(ItemsControl parent)
            {
                for (var i = 0; i < parent.Items.Count; i++)
                {
                    if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    rows.Add(item);
                    if (item.IsExpanded)
                        Collect(item);
                }
            }
        }

        /// <summary>
        /// Индекс текущей строки навигации. Определяется по контейнеру под
        /// фокусом либо под выделением, а не по объекту данных: закреплённая
        /// база присутствует в дереве дважды (узел «Закреплённые» и собственная
        /// группа), и поиск по ссылке данных вернул бы первое вхождение,
        /// «перепрыгивая» выделение в начало списка.
        /// </summary>
        private int FindCurrentRowIndex(List<TreeViewItem> rows)
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not TreeViewItem tvi)
                    continue;
                var idx = rows.IndexOf(tvi);
                if (idx >= 0)
                    return idx;
                break;
            }

            for (var i = 0; i < rows.Count; i++)
                if (rows[i].IsSelected)
                    return i;

            return rows.FindIndex(item =>
                (item.DataContext is Infobase ib && ReferenceEquals(ib, _viewModel.SelectedInfobase)) ||
                (item.DataContext is GroupNodeViewModel gn && ReferenceEquals(gn, _viewModel.SelectedGroupNode)));
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
        /// Выделяет конкретную строку дерева и синхронизирует модель, не ища
        /// контейнер заново по данным: у закреплённой базы данные встречаются
        /// дважды (узел «Закреплённые» и собственная группа), и повторный поиск
        /// вернул бы первую (верхнюю) копию, «перепрыгивая» выделение в начало.
        /// </summary>
        private void SelectRowItem(TreeViewItem item)
        {
            switch (item.DataContext)
            {
                case Infobase infobase:
                    ApplySelection(item, infobase);
                    break;
                case GroupNodeViewModel group when group.Group is not null:
                    ApplyGroupSelection(item, group);
                    break;
            }

            // Фокус переносится и на служебные узлы («Закреплённые», «Без группы»):
            // у них модель не выделяется, но навигация должна продолжиться оттуда.
            item.Focus();
            System.Windows.Input.Keyboard.Focus(item);

            // Прокрутка к строке, как у SelectTreeNode.
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
        /// Восстанавливает выделение и клавиатурный фокус выбранной строки дерева после
        /// пересборки списка (например, после сохранения настроек базы). Прежний контейнер
        /// строки уничтожен заменой коллекции <see cref="ViewModels.MainViewModel.GroupNodes"/>,
        /// поэтому подсветка и фокус пропадают вместе с ним. Выделение восстанавливается всегда;
        /// клавиатурный фокус возвращается только если сейчас не идёт ввод в текстовом поле
        /// (поиск, теги) — чтобы не выбивать курсор при наборе.
        /// </summary>
        private void RestoreTreeKeyboardFocus()
        {
            if (MainTree is null || _viewModel is null)
                return;

            // Два прохода: первый — как только дерево перестроено (Loaded); второй — в самом
            // конце очереди (ApplicationIdle), когда виртуализация уже достроила контейнеры и
            // WPF завершил собственное восстановление фокуса после закрытия модального окна.
            Dispatcher.BeginInvoke(new Action(RevealAndSelectAfterRebuild),
                System.Windows.Threading.DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(RevealAndSelectAfterRebuild),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Восстанавливает выделение и клавиатурный фокус выбранной строки после пересборки.
        /// С учётом виртуализации: при виртуализации контейнер дочерней строки существует только
        /// внутри раскрытой группы, поэтому сначала раскрывается цепочка групп-предков цели,
        /// затем контейнер выбирается, доводится до видимой области и (вне текстового поля) получает
        /// фокус. Цель читается в момент выполнения, поэтому порядок установки SelectedInfobase
        /// относительно пересборки не важен.
        /// </summary>
        private void RevealAndSelectAfterRebuild()
        {
            if (MainTree is null || _viewModel is null)
                return;

            try
            {
                var target = (object?)_viewModel.SelectedInfobase ?? _viewModel.SelectedGroupNode;
                if (target is null)
                    return;

                // Цепочка групп от корня к родителю цели (Group == null — спец-узлы «Без группы»/«Закреплённые»).
                // Для цели-базы родитель — её группа; для цели-группы — её родитель. Раскрываем именно
                // ПРЕДКОВ, чтобы отредактированная группа осталась свёрнутой, если была свёрнута.
                GroupNodeViewModel? leaf = target switch
                {
                    Infobase ib => FindGroupNodeByInfobase(ib),
                    GroupNodeViewModel gn => gn.Parent,
                    _ => null
                };
                var stack = new Stack<GroupNodeViewModel>();
                for (var g = leaf; g is not null && g.Group is not null; g = g.Parent)
                    stack.Push(g);
                // Раскрываем цепочку групп от корня к цели: BringIntoView гарантирует, что группа
                // попадает в видимую область и материализуется (иначе при виртуализации её контейнер
                // может отсутствовать), после чего раскрытие реализует дочерние строки.
                // Группы, которые пользователь свернул, принудительно не раскрываем: иначе свёрнутая
                // группа, внутри которой выбрана база, после любой пересборки дерева (таймер
                // синхронизации, редактирование, сортировка) разворачивалась бы обратно, и она
                // «не сворачивалась» бы. Скрытый элемент просто останется не выделенным.
                foreach (var group in stack)
                {
                    // Группы, которые пользователь свернул, принудительно не раскрываем.
                    // Опора только на group.IsExpanded недостаточна: к моменту пересборки
                    // IsExpanded у свёрнутой группы может быть true (авторазворачивание при
                    // поиске/фильтре либо значение ещё не применено), а ключ свёрнутой
                    // группы уже сохранён в _collapsedGroups. Иначе свёрнутый родитель,
                    // внутри которого выбрана база во вложенной группе «домашнее», после
                    // любой пересборки раскрывался бы обратно и «не сворачивался».
                    if (!group.IsExpanded || _viewModel.IsGroupCollapsed(group.NodeKey))
                        continue;
                    if (FindTreeViewItemForData(group) is { } gItem)
                    {
                        gItem.BringIntoView();
                        gItem.IsExpanded = true;
                    }
                }

                var item = FindTreeViewItemForData(target);
                if (item is null)
                    return;

                switch (target)
                {
                    case Infobase infobase:
                        ApplySelection(item, infobase);
                        break;
                    case GroupNodeViewModel group when group.Group is not null:
                        ApplyGroupSelection(item, group);
                        break;
                    default:
                        return;
                }

                item.BringIntoView();

                // Клавиатурный фокус возвращаем строке. Защищаем только поле поиска: после закрытия
                // модального окна настроек WPF может временно держать фокус на каком-либо контроле,
                // и строгая проверка «не TextBox» оставила бы базу без фокуса. Во время набора в поиске
                // курсор из поля не выбиваем.
                if (SearchTextBox is null || !ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, SearchTextBox))
                {
                    item.Focus();
                    System.Windows.Input.Keyboard.Focus(item);
                }
            }
            catch { /* элемент мог отсоединиться во время пересборки */ }
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
                // При прокрутке ВНИЗ ниже края вьюпорта из-за Recycling-виртуализации
                // реализуются НОВЫЕ контейнеры строк, раскладка которых к этому моменту
                // ещё не выполнена: позиция (TransformToAncestor) и ActualHeight окажутся
                // неактуальными, и величина прокрутки получится больше одной строки.
                // Принудительно доводим раскладку целевого контейнера до конца, чтобы
                // замер был точным (симметрично тому, как работает прокрутка вверх,
                // где строки над вьюпортом уже реализованы и разложены).
                item.UpdateLayout();

                // TransformToAncestor(scrollViewer) даёт позицию элемента ОТНОСИТЕЛЬНО
                // вьюпорта (уже с учётом прокрутки): top/bottom лежат в диапазоне видимой
                // области (0..ViewportHeight), отрицательные — выше верха вьюпорта.
                // Их нельзя сравнивать с VerticalOffset — это смещение в координатах
                // контента (растёт при прокрутке вниз). Смешивание систем координат
                // давало «прыжки» списка к началу и «прятало» выбранную базу внизу.
                var point = item.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
                var top = point.Y;                        // относительно вьюпорта
                var bottom = top + item.ActualHeight;     // относительно вьюпорта
                var viewportBottom = scrollViewer.ViewportHeight;

                if (top < 0)
                {
                    // Элемент выше верха вьюпорта: поднимаем на разницу top.
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + top);
                }
                else if (bottom > viewportBottom)
                {
                    // Элемент ниже низа вьюпорта: опускаем на величину выступа за нижний край.
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + (bottom - viewportBottom));
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
