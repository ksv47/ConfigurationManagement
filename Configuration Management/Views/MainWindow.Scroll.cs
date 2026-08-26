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

        /// <summary>
        /// Внутренний ScrollViewer шаблона TreeView (отвечает за вертикальную и горизонтальную прокрутку).
        /// </summary>
        private ScrollViewer? GetTreeScrollViewer()
        {
            if (MainTree is null)
                return null;
            // Шаблон может быть ещё не применён.
            MainTree.ApplyTemplate();
            return FindVisualChild<ScrollViewer>(MainTree);
        }

        /// <summary>
        /// Подписывается на ScrollChanged внутреннего ScrollViewer дерева (синхронизация заголовка).
        /// </summary>
        private void AttachTreeScrollHandler()
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
                return;
            treeScroll.ScrollChanged -= OnTreeScroll_ScrollChanged;
            treeScroll.ScrollChanged += OnTreeScroll_ScrollChanged;
        }

        private void OnTreeScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (DbHeaderScroll is null)
                return;

            if (Math.Abs(DbHeaderScroll.HorizontalOffset - e.HorizontalOffset) > 0.01)
                DbHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

            if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
                SyncHeaderWidthWithList();
        }

        private void OnMainTree_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        private void OnDbHeader_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollListByWheel(e);
        }

        /// <summary>
        /// Прокрутка списка: колесо — вертикаль, Shift+колесо — горизонталь.
        /// Всегда помечает событие обработанным, чтобы вложенные элементы не «съедали» колесо.
        /// </summary>
        private void ScrollListByWheel(MouseWheelEventArgs e)
        {
            var treeScroll = GetTreeScrollViewer();
            if (treeScroll is null)
            {
                // Повторная попытка после загрузки шаблона.
                AttachTreeScrollHandler();
                treeScroll = GetTreeScrollViewer();
            }

            if (treeScroll is null)
                return;

            // e.Delta обычно ±120; делим для плавности.
            var offset = -e.Delta / 3.0;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                treeScroll.ScrollToHorizontalOffset(treeScroll.HorizontalOffset + offset);
            }
            else
            {
                treeScroll.ScrollToVerticalOffset(treeScroll.VerticalOffset + offset);
            }

            e.Handled = true;
        }

    }
}
#endif
