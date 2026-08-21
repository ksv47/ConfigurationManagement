#if LINUX
using Avalonia.Controls;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия TreeView для дерева баз. Контейнеры строк — <see cref="LeveledTreeViewItem"/>,
    /// а вложенность и сдвиг уровней обеспечивает штатный механизм TreeView. Ручное вычисление
    /// уровня (присоединённое свойство Level), унаследованное от WPF, здесь не требуется и удалено.
    /// </summary>
    public class LeveledTreeView : TreeView
    {
        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();
    }
}
#endif