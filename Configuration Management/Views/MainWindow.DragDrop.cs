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

        private void OnMainTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging || _draggedData is null)
                return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Не переопределяем payload по позиции — он зафиксирован в MouseDown.
            var data = new DataObject();
            if (_draggedData is Infobase ib)
                data.SetData(DragFormatInfobase, ib);
            else if (_draggedData is GroupNodeViewModel gn)
                data.SetData(DragFormatGroup, gn);
            else
                return;

            // Дублируем по Type — совместимость с GetData(typeof(...)).
            data.SetData(_draggedData.GetType(), _draggedData);

            _isDragging = true;
            try
            {
                DragDrop.DoDragDrop(MainTree, data, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
                _draggedData = null;
            }
        }

        /// <summary>
        /// Считает количество РОДИТЕЛЬСКИХ (не считая собственного) TreeViewItem
        /// от строки базы до корня TreeView — это и есть число уровней вложенности
        /// групп, чьи ItemsPresenter.Margin реально сдвигают строку вправо.
        /// Собственный TreeViewItem строки базы (лист без детей) в счёт не идёт:
        /// его ItemsPresenter ничего не сдвигает, так как у листа нет дочерних строк.
        /// </summary>
        private static int CountAncestorTreeViewItems(DependencyObject node)
        {
            var depth = 0;
            var skippedOwnContainer = false;
            var parent = VisualTreeHelper.GetParent(node);
            while (parent is not null)
            {
                if (parent is TreeViewItem)
                {
                    if (!skippedOwnContainer)
                        skippedOwnContainer = true;
                    else
                        depth++;
                }
                else if (parent is TreeView)
                {
                    break;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
            return depth;
        }

        private void OnMainTree_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void OnMainTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            var payload = ResolveDragPayload(e);
            ResolveDropTarget(e.OriginalSource as DependencyObject, out var targetGroup, out _);
            if (payload is null || targetGroup is null)
            {
                e.Handled = true;
                return;
            }

            if (payload is Infobase)
            {
                e.Effects = DragDropEffects.Move;
            }
            else if (payload is GroupNodeViewModel sourceNode && sourceNode.Group is not null)
            {
                var targetId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceNode.Group.Id, targetId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(targetId)
                        || !GroupHierarchyHelper.IsAncestorOrSelf(targetId, sourceNode.Group.Id, _viewModel.Groups)))
                {
                    e.Effects = DragDropEffects.Move;
                }
            }

            e.Handled = true;
        }

        private void OnMainTree_Drop(object sender, DragEventArgs e)
        {
            var payload = ResolveDragPayload(e);
            if (payload is null)
            {
                e.Handled = true;
                return;
            }

            ResolveDropTarget(e.OriginalSource as DependencyObject,
                out var targetGroup, out var insertBefore);

            if (payload is GroupNodeViewModel sourceGroupNode
                && sourceGroupNode.Group is not null
                && targetGroup is not null)
            {
                var newParentId = targetGroup.Group?.Id ?? string.Empty;
                if (!string.Equals(sourceGroupNode.Group.Id, newParentId, StringComparison.OrdinalIgnoreCase))
                    _viewModel.MoveGroupUnder(sourceGroupNode.Group, newParentId);

                e.Handled = true;
                return;
            }

            if (payload is Infobase infobase && targetGroup is not null)
            {
                if (string.Equals(targetGroup.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                {
                    _viewModel.MoveInfobaseToGroup(infobase, infobase.Group ?? string.Empty, insertBefore);
                }
                else
                {
                    var path = targetGroup.Group is null
                        ? string.Empty
                        : GroupHierarchyHelper.GetFullPath(targetGroup.Group, _viewModel.Groups);
                    if (insertBefore is not null && ReferenceEquals(insertBefore, infobase))
                        insertBefore = null;
                    _viewModel.MoveInfobaseToGroup(infobase, path, insertBefore);
                }
            }

            e.Handled = true;
        }

        /// <summary>
        /// Полезная нагрузка DnD: сначала поле (валидно во время DoDragDrop), затем DataObject.
        /// </summary>
        private object? ResolveDragPayload(DragEventArgs e)
        {
            if (_draggedData is Infobase or GroupNodeViewModel)
                return _draggedData;

            if (e.Data.GetDataPresent(DragFormatGroup))
                return e.Data.GetData(DragFormatGroup);
            if (e.Data.GetDataPresent(DragFormatInfobase))
                return e.Data.GetData(DragFormatInfobase);
            if (e.Data.GetDataPresent(typeof(GroupNodeViewModel)))
                return e.Data.GetData(typeof(GroupNodeViewModel));
            if (e.Data.GetDataPresent(typeof(Infobase)))
                return e.Data.GetData(typeof(Infobase));
            return null;
        }

        /// <summary>
        /// Запоминает точку начала потенциального перетаскивания (из обработчика LButtonDown).
        /// </summary>
        private void CaptureDragStart(MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        /// <summary>
        /// Цель drop: группа + база, перед которой вставить (если курсор над строкой базы).
        /// </summary>
        private static void ResolveDropTarget(
            DependencyObject? source,
            out GroupNodeViewModel? group,
            out Infobase? insertBefore)
        {
            group = null;
            insertBefore = null;
            if (source is null)
                return;

            var item = FindAncestor<TreeViewItem>(source);
            if (item is null)
                return;

            if (item.DataContext is Infobase ib)
            {
                insertBefore = ib;
                var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(item));
                while (parentItem is not null)
                {
                    if (parentItem.DataContext is GroupNodeViewModel gn)
                    {
                        group = gn;
                        return;
                    }
                    parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(parentItem));
                }
                return;
            }

            if (item.DataContext is GroupNodeViewModel g)
                group = g;
        }

    }
}
#endif
