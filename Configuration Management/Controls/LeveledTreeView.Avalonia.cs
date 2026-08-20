#if LINUX
using Avalonia;
using Avalonia.Controls;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия TreeView, который вычисляет уровень вложенности каждого контейнера
    /// (TreeViewItem) и сохраняет его в присоединённом свойстве <see cref="LevelProperty"/>.
    /// Используется для отображения «группа в группе» со сдвигом только названия базы.
    /// </summary>
    public class LeveledTreeView : TreeView
    {
        /// <summary>Присоединённое свойство уровня вложенности (0 — корневой уровень), наследуемое.</summary>
        public static readonly AttachedProperty<int> LevelProperty =
            AvaloniaProperty.RegisterAttached<LeveledTreeView, int>(
                "Level", typeof(LeveledTreeView), 0, true);

        /// <summary>Получить уровень вложенности контейнера.</summary>
        public static int GetLevel(AvaloniaObject obj) => obj.GetValue(LevelProperty);

        /// <summary>Задать уровень вложенности контейнера.</summary>
        public static void SetLevel(AvaloniaObject obj, int value) => obj.SetValue(LevelProperty, value);

        protected override Control? GetContainerForItemOverride() => new LeveledTreeViewItem();

        protected override void PrepareContainerForItemOverride(Control element, object? item, int index)
        {
            base.PrepareContainerForItemOverride(element, item, index);
            if (element is TreeViewItem tvi)
            {
                var parent = ItemsControl.ItemsControlFromItemContainer(tvi);
                var level = parent is TreeViewItem parentTvi ? GetLevel(parentTvi) + 1 : 0;
                SetLevel(tvi, level);
            }
        }
    }
}
#endif