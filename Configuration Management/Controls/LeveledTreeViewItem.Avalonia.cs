#if LINUX
using Avalonia.Controls;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия TreeViewItem: при создании дочерних контейнеров выставляет им
    /// Level = parent.Level + 1. Работает вместе с <see cref="LeveledTreeView"/>.
    /// </summary>
    public class LeveledTreeViewItem : TreeViewItem
    {
        protected override Control? GetContainerForItemOverride() => new LeveledTreeViewItem();

        protected override void PrepareContainerForItemOverride(Control element, object? item, int index)
        {
            base.PrepareContainerForItemOverride(element, item, index);
            if (element is TreeViewItem tvi)
                LeveledTreeView.SetLevel(tvi, LeveledTreeView.GetLevel(this) + 1);
        }
    }
}
#endif