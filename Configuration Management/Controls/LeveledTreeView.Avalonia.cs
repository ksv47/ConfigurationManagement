#if LINUX
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Configuration_Management.ViewModels;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия TreeView для дерева баз. Контейнеры строк — <see cref="LeveledTreeViewItem"/>,
    /// а вложенность и сдвиг уровней обеспечивает штатный механизм TreeView. Ручное вычисление
    /// уровня (присоединённое свойство Level), унаследованное от WPF, здесь не требуется и удалено.
    /// </summary>
    public class LeveledTreeView : TreeView
    {
        /// <summary>
        /// Тема оформления ищется по типу контрола, а для наследника её в Fluent нет:
        /// без этого шаблон не находится и контрол не отрисовывается вовсе.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TreeView);

        public LeveledTreeView()
        {
            AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);
        }

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();

        // Контейнеры переиспользуются, поэтому прежняя привязка освобождается:
        // иначе на одном контейнере копились бы выражения привязки.
        private readonly ConditionalWeakTable<Control, IDisposable> _expandedBindings = new();

        /// <summary>
        /// Связывает раскрытие контейнера с моделью узла. Делается здесь, а не по
        /// событию ContainerPrepared у дерева: вложенные контейнеры готовит
        /// родительский TreeViewItem, и его событие до дерева не доходит, поэтому
        /// подгруппы оставались свёрнутыми независимо от модели. Подготовку своих
        /// детей TreeViewItem перенаправляет сюда же.
        /// </summary>
        protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
        {
            base.PrepareContainerForItemOverride(container, item, index);
            ReleaseExpandedBinding(container);

            if (container is not TreeViewItem treeItem || item is not GroupNodeViewModel)
                return;

            // Источником указан сам узел, а не DataContext контейнера: привязка
            // тогда не зависит от того, когда и чем контекст будет установлен.
            _expandedBindings.Add(treeItem, treeItem.Bind(TreeViewItem.IsExpandedProperty,
                new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = item }));
        }

        /// <summary>
        /// Переход в начало, в конец и на страницу. Обработчик стоит на
        /// туннелировании: внутренняя прокрутка дерева лежит в маршруте ближе
        /// к строке и разбирает PageUp с PageDown сама, помечая их
        /// обработанными, так что до самого дерева они не доходят.
        /// Распределение взято у версии для Windows: голые клавиши переносят
        /// выделение, а с Ctrl прокручивают, не трогая его.
        /// </summary>
        private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key is not (Key.Home or Key.End or Key.PageUp or Key.PageDown))
                return;

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                ScrollBy(e.Key);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers != KeyModifiers.None)
                return;

            // Пустой список: у TreeView в Avalonia эти клавиши считаются
            // направленными, и при пустом выборе он берёт первый элемент
            // представления, которого нет. Гасим событие до него.
            if (ItemsView.Count == 0)
            {
                e.Handled = true;
                return;
            }

            var rows = VisibleRows();
            if (rows.Count == 0)
                return;

            var current = rows.FindIndex(row => ReferenceEquals(row.DataContext, SelectedItem));
            var target = e.Key switch
            {
                Key.Home => 0,
                Key.End => rows.Count - 1,
                Key.PageUp => PageStep(rows, current, back: true),
                Key.PageDown => PageStep(rows, current, back: false),
                _ => -1
            };
            if (target < 0)
                return;

            e.Handled = true;
            if (target == current)
                return;

            // Выделение ведётся через SelectedItem дерева: правая панель и модель
            // слушают именно его, а IsSelected у контейнера их не трогает.
            SelectedItem = rows[target].DataContext;
            rows[target].BringIntoView();
            rows[target].Focus();
        }

        /// <summary>Прокрутка без переноса выделения, вариант с Ctrl.</summary>
        private void ScrollBy(Key key)
        {
            if (TreeScroll is not { } scroll)
                return;

            var hidden = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            var target = key switch
            {
                Key.Home => 0,
                Key.End => hidden,
                Key.PageUp => scroll.Offset.Y - scroll.Viewport.Height,
                _ => scroll.Offset.Y + scroll.Viewport.Height
            };

            var next = scroll.Offset.WithY(Math.Clamp(target, 0, hidden));
            if (next != scroll.Offset)
                scroll.Offset = next;
        }

        private ScrollViewer? TreeScroll =>
            this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        /// <summary>
        /// Строки в порядке показа: обход контейнеров сверху вниз, в развёрнутые
        /// узлы с заходом внутрь. По координатам порядок не строится, потому что
        /// у части контейнеров пересчёт координат к дереву не удаётся, и такие
        /// строки сваливаются в начало.
        /// </summary>
        private List<TreeViewItem> VisibleRows()
        {
            var rows = new List<TreeViewItem>();
            Collect(this);
            return rows;

            void Collect(ItemsControl parent)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    rows.Add(item);
                    if (item.IsExpanded)
                        Collect(item);
                }
            }
        }

        /// <summary>
        /// Строка через экран от текущей. Отсчёт идёт по координатам, а не по
        /// числу строк: высота у групп и баз разная.
        /// </summary>
        private int PageStep(List<TreeViewItem> rows, int current, bool back)
        {
            if (current < 0)
                return back ? 0 : rows.Count - 1;

            // Отсчёт по координатам строки, а не по сумме высот контейнеров:
            // высота контейнера группы включает всех её детей, и шаг ушёл бы
            // во всю группу разом.
            var page = TreeScroll?.Viewport.Height ?? Bounds.Height;
            var from = Top(rows[current]);

            if (back)
            {
                for (var i = current - 1; i >= 0; i--)
                    if (Top(rows[i]) <= from - page)
                        return i;
                return 0;
            }

            for (var i = current + 1; i < rows.Count; i++)
                if (Top(rows[i]) >= from + page)
                    return i;
            return rows.Count - 1;

            double Top(TreeViewItem item) => item.TranslatePoint(default, this)?.Y ?? 0;
        }

        /// <summary>
        /// Возвращает контейнер строки для указанных данных по всему дереву
        /// (включая вложенные уровни групп) или null, если контейнер ещё не создан.
        /// Используется, чтобы вернуть клавиатурный фокус на строку после
        /// пересборки дерева, когда прежний контейнер уничтожен.
        /// </summary>
        public TreeViewItem? ContainerForItem(object data)
        {
            return Find(this, data);

            static TreeViewItem? Find(ItemsControl parent, object data)
            {
                for (var i = 0; i < parent.ItemCount; i++)
                {
                    if (parent.ContainerFromIndex(i) is not TreeViewItem item)
                        continue;
                    if (ReferenceEquals(item.DataContext, data))
                        return item;
                    if (item.IsExpanded)
                    {
                        var found = Find(item, data);
                        if (found is not null)
                            return found;
                    }
                }
                return null;
            }
        }

        protected override void ClearContainerForItemOverride(Control container)
        {
            ReleaseExpandedBinding(container);
            base.ClearContainerForItemOverride(container);
        }

        private void ReleaseExpandedBinding(Control container)
        {
            if (!_expandedBindings.TryGetValue(container, out var previous))
                return;
            previous.Dispose();
            _expandedBindings.Remove(container);
        }
    }
}
#endif