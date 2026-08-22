#if LINUX
using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
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

            // Источником указан сам узел: DataContext контейнера к этому моменту
            // может быть ещё не установлен.
            _expandedBindings.Add(treeItem, treeItem.Bind(TreeViewItem.IsExpandedProperty,
                new Binding("IsExpanded") { Mode = BindingMode.TwoWay, Source = item }));
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