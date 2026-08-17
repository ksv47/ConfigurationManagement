using System.Windows;
using System.Windows.Controls;

namespace Configuration_Management.Controls;

/// <summary>
/// TreeViewItem, который при создании дочерних контейнеров выставляет им Level = parent.Level + 1.
/// Работает вместе с <see cref="LeveledTreeView"/>: корневые элементы получают Level в
/// LeveledTreeView.PrepareContainerForItemOverride, вложенные — здесь.
/// </summary>
public class LeveledTreeViewItem : TreeViewItem
{
    protected override DependencyObject GetContainerForItemOverride() => new LeveledTreeViewItem();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is TreeViewItem tvi)
        {
            var parentLevel = LeveledTreeView.GetLevel(this);
            LeveledTreeView.SetLevel(tvi, parentLevel + 1);
        }
    }
}
