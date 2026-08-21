#if LINUX
using Avalonia.Controls;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия контейнера элемента дерева (TreeViewItem). Единый тип контейнеров нужен,
    /// чтобы на них действовал стиль в MainWindow, отключающий стандартную подсветку (фон рисует
    /// карточка строки). Уровень вложенности TreeView вычисляет сам, поэтому ручное свойство Level
    /// из WPF-версии не требуется и удалено.
    /// </summary>
    public class LeveledTreeViewItem : TreeViewItem
    {
        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();
    }
}
#endif