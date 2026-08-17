using System.Windows;
using System.Windows.Controls;

namespace Configuration_Management.Controls;

/// <summary>
/// TreeView, который автоматически вычисляет уровень вложенности для каждого контейнера
/// (TreeViewItem) и сохраняет его в присоединённом свойстве <see cref="LevelProperty"/>.
/// Уровень используется, чтобы при отображении «группа в группе» сдвигать вправо только
/// название базы (и заголовки групп), оставляя остальные колонки данных строго под заголовками.
/// </summary>
public class LeveledTreeView : TreeView
{
    /// <summary>Присоединённое свойство уровня вложенности (0 — корневой уровень).</summary>
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.RegisterAttached(
            "Level",
            typeof(int),
            typeof(LeveledTreeView),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Получить уровень вложенности контейнера.</summary>
    public static int GetLevel(DependencyObject obj) => (int)obj.GetValue(LevelProperty);

    /// <summary>Задать уровень вложенности контейнера.</summary>
    public static void SetLevel(DependencyObject obj, int value) => obj.SetValue(LevelProperty, value);

    protected override DependencyObject GetContainerForItemOverride() => new TreeViewItem();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is TreeViewItem tvi)
        {
            var parent = ItemsControl.ItemsControlFromItemContainer(tvi);
            var level = parent is TreeViewItem parentTvi ? GetLevel(parentTvi) + 1 : 0;
            SetLevel(tvi, level);
        }
    }
}