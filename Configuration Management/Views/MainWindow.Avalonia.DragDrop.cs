#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Перетаскивание баз и групп в дереве главного окна (Avalonia/Linux).
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Формат содержимого перетаскивания: сама нагрузка живёт в поле окна.</summary>
        private const string DragPayloadMarker = "ConfigurationManagement.Row";

        /// <summary>
        /// Фиксация того, что поедет: как в WPF, нагрузка берётся в нажатии,
        /// а не в движении. Иначе при сдвиге курсора на дочернюю строку под ним
        /// оказывается другой узел и вместо группы уезжает база.
        /// </summary>
        private void OnTreeDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _dragPayload = null;
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _dragStartPoint = point.Position;

            // Клик по кнопке или полю ввода внутри строки перетаскивание не начинает:
            // звезда, булавка, чип тега и раскрытие группы должны работать как обычно.
            if (e.Source is not Visual source
                || source.FindAncestorOfType<Button>(includeSelf: true) is not null
                || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
                return;

            var item = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            _dragPayload = item?.DataContext switch
            {
                Infobase infobase => infobase,
                GroupNodeViewModel node when node.Group is not null => node,
                _ => null
            };
        }

        private async void OnTreeDragPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging || _dragPayload is null || _vm is null)
                return;

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _dragPayload = null;
                return;
            }

            // Порог сдвига обязателен: без него обычный клик по строке начинал бы
            // перетаскивание. Аналога SystemParameters в Avalonia нет.
            if (Math.Abs(point.Position.X - _dragStartPoint.X) < DragThreshold
                && Math.Abs(point.Position.Y - _dragStartPoint.Y) < DragThreshold)
                return;

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(DragPayloadMarker));

            _isDragging = true;
            try
            {
                await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                _vm.LogWarning($"Перетаскивание прервано: {ex.Message}");
            }
            finally
            {
                _isDragging = false;
                _dragPayload = null;
            }
        }

        /// <summary>Порог сдвига в пикселях, после которого нажатие считается перетаскиванием.</summary>
        private const double DragThreshold = 4;

        private void OnTreeDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.None;
            ResolveDropTarget(e.Source as Visual, out var targetNode, out _);
            if (targetNode is not null && IsDropAllowed(_dragPayload, targetNode))
                e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnTreeDrop(object? sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_vm is null)
                return;

            // Отпускание поднимается из насоса сырых событий, а не со стека
            // DoDragDropAsync: исключение отсюда не дало бы завершиться самой
            // операции, и перетаскивание осталось бы включённым навсегда,
            // вместе с курсором и перехватом движений по всему приложению.
            try
            {
                ApplyDrop(e);
            }
            catch (Exception ex)
            {
                _vm.LogWarning($"Перенос не выполнен: {ex.Message}");
            }
        }

        private void ApplyDrop(DragEventArgs e)
        {
            if (_vm is null)
                return;
            var payload = _dragPayload;
            ResolveDropTarget(e.Source as Visual, out var targetNode, out var insertBefore);

            // Проверки повторяются здесь намеренно: в Avalonia отпускание приходит
            // на последнюю цель независимо от того, что вернул DragOver, поэтому
            // на отказ DragOver полагаться нельзя.
            if (targetNode is null || !IsDropAllowed(payload, targetNode))
                return;

            if (payload is GroupNodeViewModel sourceNode && sourceNode.Group is not null)
            {
                _vm.MoveGroupUnder(sourceNode.Group, targetNode.Group?.Id ?? string.Empty);
                return;
            }

            if (payload is not Infobase infobase)
                return;

            if (ReferenceEquals(insertBefore, infobase))
                insertBefore = null;

            // Сброс на «Закреплённые» группу не меняет: там лежат базы из разных групп,
            // и перенос туда означал бы потерю группы.
            if (string.Equals(targetNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
            {
                _vm.MoveInfobaseToGroup(infobase, infobase.Group ?? string.Empty, insertBefore);
                return;
            }

            var path = targetNode.Group is null
                ? string.Empty
                : GroupHierarchyHelper.GetFullPath(targetNode.Group, _vm.Groups);
            _vm.MoveInfobaseToGroup(infobase, path, insertBefore);
        }

        /// <summary>
        /// Допустимость сброса. Узлы без группы разрешены не все: «Все базы» и узел
        /// результата поиска группой не являются, и сброс на них молча обнулил бы
        /// группу базы. В WPF эта ловушка есть, здесь она закрыта.
        /// </summary>
        private bool IsDropAllowed(object? payload, GroupNodeViewModel targetNode)
        {
            if (_vm is null)
                return false;
            if (payload is Infobase)
            {
                return targetNode.Group is not null
                    || string.Equals(targetNode.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal)
                    || string.Equals(targetNode.Marker, GroupNodeViewModel.NoGroupMarker, StringComparison.Ordinal);
            }

            if (payload is not GroupNodeViewModel sourceNode || sourceNode.Group is null)
                return false;

            // Узел без группы означает для группы корень, но не любой: «Все базы»,
            // результат поиска и «Закреплённые» группой не являются, и сброс на них
            // молча вынес бы подгруппу наверх. Вернуть её на место в интерфейсе
            // нечем: смена родителя есть только у перетаскивания.
            if (targetNode.Group is null
                && !string.Equals(targetNode.Marker, GroupNodeViewModel.NoGroupMarker, StringComparison.Ordinal))
                return false;

            var targetId = targetNode.Group?.Id ?? string.Empty;
            if (string.Equals(sourceNode.Group.Id, targetId, StringComparison.OrdinalIgnoreCase))
                return false;

            // Перенос под собственного потомка создал бы цикл в иерархии.
            return string.IsNullOrEmpty(targetId)
                   || !GroupHierarchyHelper.IsAncestorOrSelf(targetId, sourceNode.Group.Id, _vm.Groups);
        }

        /// <summary>
        /// Цель сброса: группа и база, перед которой вставить. Курсор над строкой
        /// базы означает вставку перед ней в её же группу.
        /// </summary>
        private static void ResolveDropTarget(Visual? source, out GroupNodeViewModel? targetNode, out Infobase? insertBefore)
        {
            targetNode = null;
            insertBefore = null;
            if (source is null)
                return;

            var item = source.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            while (item is not null)
            {
                switch (item.DataContext)
                {
                    case GroupNodeViewModel node:
                        targetNode = node;
                        return;
                    case Infobase infobase when insertBefore is null:
                        insertBefore = infobase;
                        break;
                }
                item = item.FindAncestorOfType<TreeViewItem>();
            }
        }
    }
}
#endif