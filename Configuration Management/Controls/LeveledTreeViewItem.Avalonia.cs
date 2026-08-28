#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

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
        /// <summary>
        /// Тема оформления ищется по типу контрола, а для наследника её в Fluent нет:
        /// без этого шаблон не находится и контрол не отрисовывается вовсе.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();

        private Control? _chevron;

        /// <summary>
        /// Подсказка стрелки раскрытия. Сама стрелка приходит из шаблона Fluent,
        /// поэтому ищется по имени части после его применения. В разметке WPF
        /// подсказка тоже своя на каждое состояние (MainWindow.xaml:1436 и 1439).
        /// </summary>
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _chevron = e.NameScope.Find<Control>("PART_ExpandCollapseChevron");
            UpdateChevronTooltip();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsExpandedProperty)
                UpdateChevronTooltip();
        }

        private void UpdateChevronTooltip()
        {
            if (_chevron is null)
                return;
            // Подсказка одна и статичная, как в разметке WPF. Раньше здесь были
            // две меняющиеся по состоянию, это расхождение с версией для Windows.
            ToolTip.SetTip(_chevron, Localization.LocalizationManager.T("Main.ExpandCollapseGroup"));
        }
    }
}
#endif