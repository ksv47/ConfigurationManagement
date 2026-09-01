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
        /// Обработчик клика по заголовку колонки для смены сортировки.
        /// </summary>
        private void OnColumnHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string field)
                return;
            _viewModel.SetSortField(field);
            e.Handled = true;
        }

        /// <summary>
        /// Строит целевую последовательность колонок (логические ключи) по выбранному
        /// пользователем порядку. Первая итерация идёт по пользовательскому порядку
        /// (<see cref="_viewModel.ColumnOrderKeys"/>), отбрасывая незнакомые ключи, — поэтому
        /// фактически применяется порядок, заданный пользователем в настройках, в т.ч. перенос
        /// колонки «Действия». Второй проход лишь дополняет недостающие известные колонки в
        /// порядке по умолчанию, гарантируя их наличие.
        /// </summary>
        private List<string> BuildColumnLayout()
        {
            var known = new[] { "Version", "LaunchMode", "Actions", "ServerBase", "LastLaunch", "Size", "Configuration" };
            var keys = new List<string>();
            // Идём по ПОЛЬЗОВАТЕЛЬСКОМУ порядку, отбрасывая незнакомые ключи,
            // чтобы фактически применять выбранный порядок (в т.ч. перенос «Действий»).
            var source = _viewModel?.ColumnOrderKeys ?? Array.Empty<string>();
            foreach (var k in source)
                if (Array.IndexOf(known, k) >= 0 && !keys.Contains(k))
                    keys.Add(k);
            // Гарантируем, что все известные колонки присутствуют (незнакомые ключи
            // из сохранённого порядка пропускаются).
            foreach (var k in known)
                if (!keys.Contains(k))
                    keys.Add(k);

            return keys;
        }

        /// <summary>
        /// Перестраивает колонки данных сетки <paramref name="grid"/> (заголовка, строки базы
        /// или заголовка группы) под выбранный порядок: передвигает определения колонок и
        /// обновляет позиции размещённых в них элементов. Фиксированные колонки слева и
        /// «Название» не трогаются.
        /// </summary>
        private void ReorderGridColumns(Grid grid, int firstDataCol)
        {
            if (grid is null || _viewModel is null)
                return;

            var layout = BuildColumnLayout();
            var leading = firstDataCol;
            var dataCount = grid.ColumnDefinitions.Count - leading;
            if (dataCount <= 0)
                return;

            // Первый проход: определяем логический ключ для каждого перемещаемого элемента
            // региона данных по статической раскладке. Ключ сохраняется в attached-свойстве
            // ColumnKey (Tag занят — сортировка/двойной клик), поэтому повторные вызовы
            // корректно работают и после перестановок, и для уже перестроенных сеток.
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue; // объединённые ячейки (название/теги) двигаются отдельно
                var c = Grid.GetColumn(fe);
                if (c < leading || c >= leading + dataCount)
                    continue;
                if (string.IsNullOrEmpty(GetColumnKey(fe)))
                    SetColumnKey(fe, StaticDataColumnKeys[c - leading]);
            }

            // Сопоставление «позиция колонки данных -> логический ключ» по ключам детей.
            var defKey = new string[dataCount];
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (string.IsNullOrEmpty(s))
                    continue;
                if (Grid.GetColumnSpan(fe) != 1)
                    continue;
                var c = Grid.GetColumn(fe);
                if (c >= leading && c < leading + dataCount)
                    defKey[c - leading] = s;
            }

            // Для сеток без дочерних элементов в части колонок данных (например, строка группы,
            // где заполнена только колонка «Действия») ключ незаполненных колонок берём по
            // статической раскладке: такие сетки всегда создаются в статическом порядке.
            for (var i = 0; i < dataCount; i++)
                if (string.IsNullOrEmpty(defKey[i]))
                    defKey[i] = StaticDataColumnKeys[i];

            // Новый порядок определений колонок под нужную раскладку.
            var defs = grid.ColumnDefinitions;
            var newOrder = new List<ColumnDefinition>(dataCount);
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in layout)
            {
                for (var i = 0; i < dataCount; i++)
                {
                    if (defKey[i] == token && placed.Add(token))
                    {
                        newOrder.Add(defs[leading + i]);
                        break;
                    }
                }
            }
            for (var i = 0; i < dataCount; i++)
                if (!placed.Contains(defKey[i]))
                {
                    newOrder.Add(defs[leading + i]);
                    placed.Add(defKey[i]);
                }

            // Пересобираем коллекцию определений: фиксированные слева + новый порядок данных.
            var leadingDefs = new List<ColumnDefinition>(leading);
            for (var i = 0; i < leading; i++)
                leadingDefs.Add(defs[i]);
            defs.Clear();
            foreach (var d in leadingDefs)
                defs.Add(d);
            foreach (var d in newOrder)
                defs.Add(d);

            // Обновляем позиции детей и span широких ячеек (до «Действий»).
            // Заголовок группы: имя/счётчик занимают область названия и тянутся до «Действий».
            var actionsColumn = leading + layout.IndexOf("Actions");
            var isGroup = ReferenceEquals(grid.Tag, GroupGridMarker);
            foreach (var obj in grid.Children)
            {
                if (obj is not FrameworkElement fe)
                    continue;
                var s = GetColumnKey(fe);
                if (!string.IsNullOrEmpty(s))
                {
                    var ti = layout.IndexOf(s);
                    if (ti >= 0)
                        Grid.SetColumn(fe, leading + ti);
                }
                else if (isGroup && Grid.GetRow(fe) == 0 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
                else if (Grid.GetRow(fe) == 1 && Grid.GetColumn(fe) == 0 && Grid.GetColumnSpan(fe) > 1)
                {
                    Grid.SetColumnSpan(fe, actionsColumn);
                }
            }
        }

        /// <summary>Рекурсивно собирает уже созданные сетки строк баз/заголовков групп по маркеру.</summary>
        private static List<Grid> FindRowGrids(DependencyObject? root, object marker)
        {
            var result = new List<Grid>();
            FindRowGridsCore(root, marker, result);
            return result;
        }

        private static void FindRowGridsCore(DependencyObject? parent, object marker, List<Grid> acc)
        {
            if (parent is null)
                return;
            if (parent is Grid g && ReferenceEquals(g.Tag, marker))
                acc.Add(g);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                FindRowGridsCore(VisualTreeHelper.GetChild(parent, i), marker, acc);
        }

        /// <summary>
        /// Находит первую сетку с указанным маркером внутри <paramref name="root"/>
        /// (используется для выравнивания заголовка по строке базы).
        /// </summary>
        private static Grid? FindGridByMarker(DependencyObject? root, object marker)
        {
            if (root is Grid g && ReferenceEquals(g.Tag, marker))
                return g;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindGridByMarker(VisualTreeHelper.GetChild(root, i), marker);
                if (found is not null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Применяет выбранный порядок колонок к заголовку и всем созданным строкам баз
        /// и заголовкам групп. Вызывается при старте (после компоновки) и при изменении
        /// порядка в настройках.
        /// </summary>
        private void ApplyColumnOrder()
        {
            if (HeaderGrid is not null)
                ReorderGridColumns(HeaderGrid, HeaderFirstDataColumn);
            foreach (var grid in FindRowGrids(MainTree, RowGridMarker))
                ReorderGridColumns(grid, RowFirstDataColumn);
            foreach (var grid in FindRowGrids(MainTree, GroupGridMarker))
                ReorderGridColumns(grid, RowFirstDataColumn);
        }

        /// <summary>
        /// Обработчик Loaded сетки строки базы в шаблоне: применяет выбранный порядок
        /// колонок к каждой вновь созданной строке (включая строки, появляющиеся при
        /// виртуализации/прокрутке дерева).
        /// </summary>
        private void OnInfobaseRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            ReorderGridColumns(grid, RowFirstDataColumn);
            grid.Tag = RowGridMarker;
        }

        /// <summary>
        /// Обработчик Loaded сетки заголовка группы в шаблоне: применяет выбранный порядок
        /// колонок, чтобы команды группы оставались в колонке «Действия» на уровне строк баз.
        /// </summary>
        private void OnGroupRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
                return;
            ReorderGridColumns(grid, RowFirstDataColumn);
            grid.Tag = GroupGridMarker;
        }

        /// <summary>
        /// Обработчик раскрытия/сворачивания узла дерева (issue #119). Раскрытие/сворачивание
        /// группы меняет глубину первой видимой базы, поэтому компенсатор сдвига заголовка
        /// (HeaderOffsetColumn) устаревает и колонки «уезжают» относительно содержимого.
        /// Пересчитывает выравнивание заголовка с данными после завершения компоновки.
        /// </summary>
        private void OnMainTree_GroupExpansionChanged(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы первая колонка данных заголовка точно совпадала по горизонтали с первой
        /// колонкой данных строки базы. Строка базы и заголовок имеют одинаковый набор
        /// ведущих колонок, поэтому колонки данных всех строк (которые не смещаются
        /// отступами вложенности) оказываются на одной линии с заголовками.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderGrid is null || HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var rowGrid = FindGridByMarker(item, RowGridMarker);
            if (rowGrid is null)
                return;

            // Позиция первой колонки данных строки базы (отсчитывается от левого края сетки).
            double rowStart = 0;
            for (var i = 0; i < RowFirstDataColumn; i++)
                rowStart += rowGrid.ColumnDefinitions[i].ActualWidth;
            var rowOrigin = rowGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            // Позиция первой колонки данных заголовка без учёта текущей ширины компенсатора Offset.
            double headerStart = 0;
            for (var i = 0; i < HeaderFirstDataColumn; i++)
            {
                if (!ReferenceEquals(HeaderGrid.ColumnDefinitions[i], HeaderOffsetColumn))
                    headerStart += HeaderGrid.ColumnDefinitions[i].ActualWidth;
            }
            var headerOrigin = HeaderGrid.TransformToAncestor(this).Transform(new Point(0, 0)).X;

            var offset = Math.Max(0, (rowOrigin + rowStart) - (headerOrigin + headerStart));
            if (Math.Abs(offset - HeaderOffsetColumn.Width.Value) > 0.5)
                HeaderOffsetColumn.Width = new GridLength(offset);

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Выравнивает ширину сетки заголовка с контентом списка, чтобы горизонтальная
        /// прокрутка «до конца» не разъезжала колонки заголовка и строк.
        /// Важно: ширина заголовка не должна включать ширину вертикальной полосы прокрутки
        /// (sbw), иначе гибкая колонка «Название» в заголовке растянется шире, чем в данных,
        /// и фиксированные колонки данных окажутся смещёнными влево относительно заголовков.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            // Жёсткий минимум области списка = сумма ширин всех колонок (как в Linux).
            // Благодаря точной границе горизонтальная полоса появляется только когда
            // колонки реально не помещаются, а не «на волосок» раньше (issue про последнюю
            // пустую колонку). Пересчитывается при каждой синхронизации ширины заголовка.
            UpdateTreeMinWidth();

            var treeScroll = GetTreeScrollViewer();
            double extent = MainTree.ActualWidth;
            double viewport = MainTree.ActualWidth;
            if (treeScroll is not null)
            {
                extent = Math.Max(treeScroll.ExtentWidth, treeScroll.ViewportWidth);
                viewport = treeScroll.ViewportWidth;
            }

            // Не добавляем sbw: заголовок и данные должны иметь одинаковую общую ширину,
            // чтобы колонки данных совпадали с колонками заголовка.
            double target = Math.Max(extent, viewport);
            if (target > 0)
                HeaderGrid.Width = target;
        }

        /// <summary>
        /// Ищет первый реально созданный (видимый) элемент дерева с базой.
        /// </summary>
        private static TreeViewItem? FindFirstInfobaseItem(DependencyObject parent)
        {
            if (parent is TreeViewItem tvi && tvi.DataContext is Infobase)
                return tvi;

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindFirstInfobaseItem(VisualTreeHelper.GetChild(parent, i));
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Находит текстовый элемент названия базы (x:Name=NameText) в строке.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && tb.Name == "NameText")
                    return tb;

                var result = FindNameCell(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Применяет сохранённые ширины колонок списка баз.
        /// Ширины уже загружены в модель (VersionColumnWidth и т.д.), а колонки заголовка
        /// и строки данных привязаны к ним через ColumnVisibilityConverter, поэтому ручная
        /// установка Width не требуется и лишь перебивала бы binding, рассинхронизируя
        /// заголовок с данными.
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            // Колонка «Название» — гибкая (*), фиксированную ширину не задаём.
            // Остальные колонки применяют сохранённые ширины автоматически через binding.
        }

        /// <summary>
        /// Минимальная ширина гибкой колонки «Название» (*): на неё нельзя схлопываться,
        /// чтобы сумма колонок оставалась осмысленной при расчёте минимальной ширины списка.
        /// </summary>
        private const double NameColumnMinWidth = 220;

        /// <summary>
        /// Минимальная ширина колонки «Действия»: совпадает с MinWidth=120, заданной трём
        /// ColumnDefinition этой колонки (заголовок, группа, база) в MainWindow.xaml, чтобы
        /// три кнопки-иконки (Запуск, Конфигуратор, Очистить кеш) оставались доступными.
        /// </summary>
        private const double ActionsColumnMinWidth = 120;

        /// <summary>
        /// Задаёт списку точную минимальную ширину, равную сумме ширин всех колонок
        /// заголовка (гибкое «Название» — по своему минимуму) вместе с ведущими отступами
        /// (колонки кнопок групп, компенсатор сдвига дерева, избранное, закрепление).
        /// Аналог <c>UpdateListMinWidth</c> из Linux/Avalonia: благодаря жёсткой границе
        /// горизонтальная прокрутка появляется ровно тогда, когда колонки реально не
        /// помещаются по ширине, а не «на волосок» раньше из-за округления последней
        /// (пустой) колонки «Конфигурация».
        /// </summary>
        private void UpdateTreeMinWidth()
        {
            if (MainTree is null || HeaderGrid is null)
                return;

            var defs = HeaderGrid.ColumnDefinitions;
            if (defs.Count <= HeaderFirstDataColumn)
                return;

            double total = 0;
            foreach (var d in defs)
            {
                if (ReferenceEquals(d, NameColumn))
                    total += d.Width.IsAbsolute ? d.Width.Value : NameColumnMinWidth;
                else if (d.Width.IsAbsolute)
                    total += d.Width.Value;
                else
                    total += d.MinWidth;
            }

            // Минимум задаём КОНТЕНТУ прокрутки (внутреннему ScrollContentPresenter дерева),
            // а не самому MainTree: у дерева собственный внутренний ScrollViewer, и MinWidth
            // на контроле лишь растянул бы область просмотра, а не заставил бы контент
            // переполняться. Задание минимума контенту даёт жёсткую границу (как в Linux):
            // горизонтальная полоса появляется ровно тогда, когда сумма колонок превышает
            // доступную ширину, а не «на волосок» раньше из-за округления последней
            // (пустой) колонки «Конфигурация».
            var presenter = GetTreeScrollContentPresenter();
            if (presenter is not null)
                presenter.MinWidth = total;
        }

        /// <summary>
        /// Внутренний ScrollContentPresenter шаблона TreeView — контент его ScrollViewer.
        /// </summary>
        private ScrollContentPresenter? GetTreeScrollContentPresenter()
        {
            var treeScroll = GetTreeScrollViewer();
            return treeScroll is null ? null : FindVisualChild<ScrollContentPresenter>(treeScroll);
        }

        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель расположен на правом краю своей колонки (Grid.Column=N),
        /// поэтому он меняет ширину колонки с тем же индексом N.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, NameSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, VersionSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ConfigurationSplitter))
                return ConfigurationColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, ActionsSplitter))
                return ActionsColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return ServerColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return LastLaunchColumn;
            if (ReferenceEquals(sender, SizeSplitter))
                return SizeColumn;
            return null;
        }

        /// <summary>
        /// Начинает перетаскивание разделителя: захватывает мышь и запоминает стартовые значения.
        /// </summary>
        private void OnColumnResize_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var column = GetSplitterTargetColumn(sender);
            if (column is null)
                return;

            _resizeColumn = column;
            _resizeStartWidth = column.ActualWidth;
            _resizeStartMouse = e.GetPosition(this);

            if (sender is UIElement element)
                element.CaptureMouse();

            e.Handled = true;
        }

        /// <summary>
        /// Меняет ширину только целевой колонки при движении мыши.
        /// Ширина записывается в модель (VersionColumnWidth и т.д.), к которой привязаны
        /// и колонка заголовка, и колонки данных — поэтому заголовок и данные синхронно
        /// изменяются. Прямая установка Width перебивала бы binding и рассинхронизировала их.
        /// </summary>
        private void OnColumnResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeColumn is null || sender is not UIElement element || !element.IsMouseCaptured)
                return;

            var current = e.GetPosition(this);
            var delta = current.X - _resizeStartMouse.X;

            var newWidth = _resizeStartWidth + delta;
            if (ReferenceEquals(_resizeColumn, ActionsColumn) && newWidth < ActionsColumnMinWidth)
                newWidth = ActionsColumnMinWidth;
            else if (newWidth < 40)
                newWidth = 40;

            if (ReferenceEquals(_resizeColumn, SizeColumn))
            {
                // SizeColumnWidth имеет публичный сеттер и авто-сохраняется при изменении.
                _viewModel.SizeColumnWidth = newWidth;
                return;
            }

            _viewModel.UpdateColumnWidths(
                ReferenceEquals(_resizeColumn, NameColumn) ? newWidth : NameColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, VersionColumn) ? newWidth : VersionColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ConfigurationColumn) ? newWidth : ConfigurationColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LaunchModeColumn) ? newWidth : LaunchModeColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ServerColumn) ? newWidth : ServerColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, LastLaunchColumn) ? newWidth : LastLaunchColumn?.ActualWidth ?? 0,
                ReferenceEquals(_resizeColumn, ActionsColumn) ? newWidth : ActionsColumn?.ActualWidth ?? 0);
        }

        /// <summary>
        /// Завершает перетаскивание разделителя и сохраняет ширины колонок.
        /// </summary>
        private void OnColumnResize_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.ReleaseMouseCapture();

            if (_resizeColumn is not null)
            {
                _viewModel.SaveColumnWidths(
                    NameColumn?.ActualWidth ?? 0,
                    VersionColumn?.ActualWidth ?? 0,
                    ConfigurationColumn?.ActualWidth ?? 0,
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0,
                    ActionsColumn?.ActualWidth ?? 0);
                SyncHeaderWidthWithList();
            }

            _resizeColumn = null;
            e.Handled = true;
        }

    }
}
#endif
