using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private Point _dragStartPoint;
        private object? _draggedData;
        private bool _isDragging;
        private Forms.NotifyIcon? _trayIcon;
        private bool _forceClose;

        public MainWindow(ViewModels.MainViewModel? viewModel = null)
        {
            InitializeComponent();
            _viewModel = viewModel ?? new ViewModels.MainViewModel();
            DataContext = _viewModel;

            // Применяем сохранённую тему оформления при запуске.
            if (!string.IsNullOrEmpty(_viewModel.SavedTheme))
            {
                ThemeManager.ApplyTheme(_viewModel.SavedTheme);
            }

            UpdateThemeButton();

            // Применяем сохранённые ширины колонок списка баз.
            ApplySavedColumnWidths();

            // Применяем сохранённые размер, позицию и состояние окна.
            ApplySavedWindowLayout();

            // Трей и хоткеи — после загрузки окна (STA/иконка безопаснее на Loaded).
            Loaded += (_, _) =>
            {
                try
                {
                    InitializeTrayIcon();
                    RegisterLaunchHotkeys();
                    RegisterFavoriteHotkeys();
                }
                catch
                {
                    // не блокируем запуск из‑за трея/хоткеев
                }
            };
            _viewModel.FavoriteHotkeysChanged += (_, _) =>
            {
                try { RegisterFavoriteHotkeys(); }
                catch { /* ignore */ }
            };
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.HotkeyEnterprise)
                    or nameof(MainViewModel.HotkeyConfigurator)
                    or nameof(MainViewModel.ShowTrayIcon))
                {
                    try
                    {
                        if (e.PropertyName == nameof(MainViewModel.ShowTrayIcon))
                            UpdateTrayVisibility();
                        else
                            RegisterLaunchHotkeys();
                    }
                    catch { /* ignore */ }
                }
            };
        }

        /// <summary>
        /// Восстанавливает сохранённые размер, позицию и состояние окна приложения.
        /// </summary>
        private void ApplySavedWindowLayout()
        {
            var width = _viewModel.SavedWindowWidth;
            var height = _viewModel.SavedWindowHeight;

            if (width > 0 && height > 0)
            {
                // Не допускаем выход окна за пределы рабочей области экрана.
                var area = SystemParameters.WorkArea;
                var left = _viewModel.SavedWindowLeft;
                var top = _viewModel.SavedWindowTop;
                if (left <= 0 && top <= 0)
                {
                    // Если позиция не сохранена — центрируем окно.
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                else
                {
                    // Ограничиваем позицию, чтобы окно оставалось видимым.
                    var safeLeft = Math.Max(area.Left, Math.Min(left, area.Right - Math.Min(width, area.Width)));
                    var safeTop = Math.Max(area.Top, Math.Min(top, area.Bottom - Math.Min(height, area.Height)));
                    Left = safeLeft;
                    Top = safeTop;
                }

                Width = width;
                Height = height;
            }

            // Восстанавливаем развёрнутое состояние окна.
            if (Enum.TryParse<WindowState>(_viewModel.SavedWindowState, out var state) &&
                state != WindowState.Minimized)
            {
                WindowState = state;
            }
        }

        /// <summary>
        /// Сохраняет размер, позицию и состояние окна приложения при закрытии.
        /// При включённой опции «Закрывать в трей» скрывает окно вместо выхода.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Сохраняем только в обычном состоянии, чтобы не сохранить развёрнутое окно как размер по умолчанию.
            if (WindowState == WindowState.Normal)
            {
                _viewModel.SaveWindowLayout(Width, Height, Left, Top, WindowState.ToString());
            }
            else if (WindowState == WindowState.Maximized)
            {
                _viewModel.SaveWindowLayout(RestoreBounds.Width, RestoreBounds.Height, RestoreBounds.Left, RestoreBounds.Top, WindowState.ToString());
            }

            if (!_forceClose && _viewModel.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (_trayIcon != null)
                    _trayIcon.Visible = true;
                return;
            }

            // Останавливаем автоматическую синхронизацию при закрытии окна.
            _viewModel.StopAutoSync();
            DisposeTrayIcon();

            base.OnClosing(e);
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new Forms.NotifyIcon
                {
                    Text = "Управление конфигурациями 1С",
                    Visible = _viewModel.ShowTrayIcon
                };

                // Иконка из ресурса приложения или стандартная.
                try
                {
                    var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                    if (System.IO.File.Exists(iconPath))
                        _trayIcon.Icon = new Drawing.Icon(iconPath);
                    else
                        _trayIcon.Icon = Drawing.SystemIcons.Application;
                }
                catch
                {
                    _trayIcon.Icon = Drawing.SystemIcons.Application;
                }

                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("Открыть", null, (_, _) => RestoreFromTray());
                menu.Items.Add("Запустить выбранную (Предприятие)", null, (_, _) =>
                {
                    RestoreFromTray();
                    if (_viewModel.LaunchEnterpriseCommand.CanExecute(null))
                        _viewModel.LaunchEnterpriseCommand.Execute(null);
                });
                menu.Items.Add("Запустить выбранную (Конфигуратор)", null, (_, _) =>
                {
                    RestoreFromTray();
                    if (_viewModel.LaunchConfiguratorCommand.CanExecute(null))
                        _viewModel.LaunchConfiguratorCommand.Execute(null);
                });
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("Загрузить из ibases.v8i", null, (_, _) =>
                {
                    RestoreFromTray();
                    if (_viewModel.ImportFromIbasesV8iCommand.CanExecute(null))
                        _viewModel.ImportFromIbasesV8iCommand.Execute(null);
                });
                menu.Items.Add("Настройки", null, (_, _) =>
                {
                    RestoreFromTray();
                    if (_viewModel.OpenSettingsCommand.CanExecute(null))
                        _viewModel.OpenSettingsCommand.Execute(null);
                });
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("Выход", null, (_, _) =>
                {
                    _forceClose = true;
                    Close();
                });
                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
            }
            catch
            {
                _trayIcon = null;
            }
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null)
                _trayIcon.Visible = false;
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void UpdateTrayVisibility()
        {
            if (_trayIcon != null)
                _trayIcon.Visible = _viewModel.ShowTrayIcon;
        }

        /// <summary>
        /// Регистрирует настраиваемые горячие клавиши запуска Предприятие / Конфигуратор.
        /// </summary>
        private void RegisterLaunchHotkeys()
        {
            // Удаляем старые биндинги F2–F12 без модификаторов
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Modifiers == ModifierKeys.None &&
                             kb.Key >= Key.F2 && kb.Key <= Key.F12)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            if (TryParseFunctionKey(_viewModel.HotkeyEnterprise, out var keyEnt))
            {
                InputBindings.Add(new KeyBinding(_viewModel.LaunchEnterpriseCommand, keyEnt, ModifierKeys.None));
            }
            if (TryParseFunctionKey(_viewModel.HotkeyConfigurator, out var keyCfg))
            {
                InputBindings.Add(new KeyBinding(_viewModel.LaunchConfiguratorCommand, keyCfg, ModifierKeys.None));
            }
        }

        private static bool TryParseFunctionKey(string? text, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (Enum.TryParse<Key>(text.Trim(), true, out var parsed) &&
                parsed >= Key.F2 && parsed <= Key.F12)
            {
                key = parsed;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Регистрирует KeyBinding Alt+1…Alt+9 для быстрого запуска избранных баз.
        /// </summary>
        private void RegisterFavoriteHotkeys()
        {
            // Удаляем предыдущие биндинги Alt+1…9
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Modifiers == ModifierKeys.Alt &&
                             kb.Key >= Key.D1 && kb.Key <= Key.D9)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            for (int i = 1; i <= 9; i++)
            {
                int index = i;
                var binding = new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.LaunchFavoriteByHotkey(index)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Alt);
                InputBindings.Add(binding);
            }
        }

        /// <summary>
        /// Надёжный обработчик Alt+1…9 (KeyBinding с Alt иногда перехватывается системой).
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Alt)
                return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key >= Key.D1 && key <= Key.D9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.D0);
                e.Handled = true;
            }
            else if (key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.NumPad0);
                e.Handled = true;
            }
        }

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

        private void OnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
            UpdateThemeButton();
            _viewModel.SaveTheme(ThemeManager.CurrentTheme);
        }

        /// <summary>
        /// Выравнивает колонки заголовка по фактическому положению колонки «Название»
        /// первой видимой базы в списке. Это необходимо, потому что при группировке
        /// базы смещаются вправо отступами вложенности дерева, и фиксированный сдвиг
        /// заголовка (рассчитанный для баз верхнего уровня) перестаёт совпадать с данными.
        /// </summary>
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            AttachTreeScrollHandler();
            if (MainTree is not null)
            {
                MainTree.Loaded += (_, __) => AttachTreeScrollHandler();
            }

            AlignHeaderToData();
            // Повторное выравнивание после завершения первичной компоновки,
            // когда уже известны реальные размеры контейнеров дерева.
            Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);

            // Запускаем автоматическую синхронизацию с файлом ibases.v8i.
            _viewModel.StartAutoSync();
        }

        /// <summary>
        /// Пересчитывает выравнивание заголовка при переключении режима группировки,
        /// когда дерево перестраивается и меняется глубина вложенности баз.
        /// </summary>
        private void OnGroupByToggle_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(AlignHeaderToData), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Подстраивает ширину колонки-компенсатора заголовка (HeaderOffsetColumn) так,
        /// чтобы колонка «Название» заголовка совпадала с колонкой «Название» первой
        /// видимой строки базы в дереве.
        /// </summary>
        private void AlignHeaderToData()
        {
            if (HeaderOffsetColumn is null || MainTree is null)
                return;

            var item = FindFirstInfobaseItem(MainTree);
            if (item is null)
                return;

            var nameCell = FindNameCell(item);
            if (nameCell is null)
                return;

            // Положение колонки «Название» данных относительно дерева (левого края списка).
            // Заголовок использует тот же левый край, поэтому это значение напрямую
            // задаёт компенсирующую колонку за вычетом двух колонок ★/📌 (26+26).
            var dataX = nameCell.TranslatePoint(new Point(0, 0), MainTree).X;
            var offset = Math.Max(0, dataX - 52);
            HeaderOffsetColumn.Width = new GridLength(offset);

            SyncHeaderWidthWithList();
        }

        /// <summary>
        /// Выравнивает ширину сетки заголовка с контентом списка, чтобы горизонтальная
        /// прокрутка «до конца» не разъезжала колонки заголовка и строк.
        /// </summary>
        private void SyncHeaderWidthWithList()
        {
            if (HeaderGrid is null || MainTree is null)
                return;

            var treeScroll = GetTreeScrollViewer();
            double sbw = 0;
            double extent = MainTree.ActualWidth;
            double viewport = MainTree.ActualWidth;
            if (treeScroll is not null)
            {
                sbw = treeScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                    ? SystemParameters.VerticalScrollBarWidth
                    : 0;
                extent = Math.Max(treeScroll.ExtentWidth, treeScroll.ViewportWidth);
                viewport = treeScroll.ViewportWidth;
            }

            double target = Math.Max(extent + sbw, viewport + sbw);
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
        /// Находит текстовый элемент колонки «Название» (Grid.Column=3) строки базы.
        /// </summary>
        private static TextBlock? FindNameCell(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && Grid.GetColumn(tb) == 3)
                    return tb;

                var result = FindNameCell(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Синхронизирует выделение в дереве с выбранной базой.
        /// При выборе группы снимает выделение базы.
        /// </summary>
        private void OnMainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Выбором базы управляет code-behind через обработчики кликов
            // (OnInfobaseTree_PreviewMouseLeftButtonDown), которые явно устанавливают
            // TreeViewItem.IsSelected и SelectedInfobase. Здесь лишь фиксируем результат
            // изменения выбранного элемента, не трогая свойство Infobase.IsSelected.
            // Ранее двухсторонняя привязка IsSelected к модели порождала каскад событий
            // SelectedItemChanged (база дублируется в «Закреплённых» и в своей группе),
            // что приводило к бесконечной рекурсии и StackOverflowException.
            if (e.NewValue is Infobase infobase)
            {
                _viewModel.SelectedInfobase = infobase;
                // Выбор базы снимает выбор группы.
                _viewModel.SelectedGroupNode = null;
            }
            else if (e.NewValue is GroupNodeViewModel groupNode)
            {
                // Выбор группы снимает выбор базы и фиксирует выбранную группу.
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = groupNode;
            }
            else if (e.NewValue is null)
            {
                _viewModel.SelectedInfobase = null;
                _viewModel.SelectedGroupNode = null;
            }

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // чтобы они активировались при программной установке выделения в дереве.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Открывает выпадающее меню выбора типа клиента при нажатии на стрелку
        /// кнопки запуска 1С:Предприятие.
        /// </summary>
        private void OnLaunchSplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            // Открываем меню отложенно, чтобы клик по кнопке не закрыл его сразу.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                button.ContextMenu.IsOpen = true;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnInfobaseTree_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Двойной клик по группе сворачивает/разворачивает её в зависимости от текущего состояния.
            // Двойной клик по базе по-прежнему запускает 1С.
            var source = e.OriginalSource as DependencyObject;
            var treeViewItem = source is null ? null : FindAncestor<TreeViewItem>(source);
            if (treeViewItem?.DataContext is GroupNodeViewModel groupNode && groupNode.Group is not null)
            {
                _viewModel.ToggleGroupExpandedCommand.Execute(groupNode);
                return;
            }

            if (_viewModel.LaunchEnterpriseCommand.CanExecute(null))
            {
                _viewModel.LaunchEnterpriseCommand.Execute(null);
            }
        }

        /// <summary>
        /// Выделяет базу или группу под курсором при правом клике в дереве,
        /// чтобы команды контекстного меню применялись именно к этому элементу.
        /// </summary>
        private void OnInfobaseTree_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var treeView = sender as TreeView;
            if (treeView is null)
            {
                return;
            }

            // Если клик попал по строке базы или группы, выделяем её.
            var source = e.OriginalSource as DependencyObject;
            var treeViewItem = source is null ? null : FindAncestor<TreeViewItem>(source);
            switch (treeViewItem?.DataContext)
            {
                case Infobase infobase:
                    treeViewItem.IsSelected = true;
                    _viewModel.SelectedInfobase = infobase;
                    break;
                case GroupNodeViewModel groupNode when groupNode.Group is not null:
                    treeViewItem.IsSelected = true;
                    _viewModel.SelectedInfobase = null;
                    _viewModel.SelectedGroupNode = groupNode;
                    break;
            }
        }

        /// <summary>
        /// Выделяет базу или группу под курсором при левом клике в дереве.
        /// Сами устанавливаем выбор и помечаем событие обработанным, чтобы
        /// собственная логика TreeView не сбросила выделение. Клики по
        /// интерактивным элементам строки (кнопки, поле ввода) не
        /// перехватываются, чтобы они продолжали работать.
        /// </summary>
        private void OnInfobaseTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Payload DnD фиксируем здесь (не в MouseMove): иначе при сдвиге курсора
            // на дочернюю базу TreeViewItem под курсором меняется и «уезжает» не группа, а базы.
            CaptureDragStart(e);

            var treeView = sender as TreeView;
            if (treeView is null)
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            if (source is null)
            {
                return;
            }

            // Если клик пришёлся по интерактивному элементу (кнопка, поле ввода),
            // не вмешиваемся и не начинаем drag.
            if (FindAncestor<Button>(source) is not null ||
                FindAncestor<TextBox>(source) is not null)
            {
                _draggedData = null;
                return;
            }

            var treeViewItem = FindAncestor<TreeViewItem>(source);
            if (treeViewItem is null)
            {
                return;
            }

            switch (treeViewItem.DataContext)
            {
                case Infobase infobase:
                    _draggedData = infobase;
                    ApplySelection(treeViewItem, infobase);
                    break;
                case GroupNodeViewModel groupNode when groupNode.Group is not null:
                    _draggedData = groupNode;
                    ApplyGroupSelection(treeViewItem, groupNode);
                    break;
                default:
                    _draggedData = null;
                    return;
            }

            // Помечаем клик обработанным, чтобы TreeView не сбросил выбранный элемент.
            e.Handled = true;
        }

        /// <summary>
        /// Блокирует автоматическую прокрутку TreeView к выделенному элементу
        /// (по умолчанию WPF вызывает BringIntoView при IsSelected/Focus — список «прыгает» вверх).
        /// </summary>
        private void OnMainTree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// Устанавливает выделение указанного узла группы и синхронизирует
        /// выбранную группу в модели представления (снимая выбор базы).
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplyGroupSelection(TreeViewItem item, GroupNodeViewModel groupNode)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = null;
            _viewModel.SelectedGroupNode = groupNode;

            // Принудительно пересчитываем состояние кнопок («Изменить», «Удалить» и др.),
            // т.к. программная установка выделения не всегда гарантирует автоматический
            // пересчёт CanExecute команд через CommandManager.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Устанавливает выделение указанного элемента дерева и синхронизирует
        /// выбранную базу в модели представления.
        /// Без Focus()/BringIntoView — позиция прокрутки списка не меняется.
        /// </summary>
        private void ApplySelection(TreeViewItem item, Infobase infobase)
        {
            item.IsSelected = true;
            _viewModel.SelectedInfobase = infobase;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// Показывает поле ввода тега прямо в строке названия базы.
        /// </summary>
        private void OnAddTagInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // InlineTagBox находится в том же StackPanel, что и кнопка «+ тег»,
            // поэтому ищем его через общий предок TreeViewItem.
            var treeViewItem = FindAncestor<TreeViewItem>(button);
            var tagBox = treeViewItem is null ? null : FindVisualChild<TextBox>(treeViewItem);
            if (tagBox is null)
                return;

            // Скрываем кнопку «+ тег» и показываем поле ввода на её месте.
            button.Visibility = Visibility.Collapsed;
            tagBox.Text = string.Empty;
            tagBox.Visibility = Visibility.Visible;
            tagBox.Focus();
            Keyboard.Focus(tagBox);
        }

        /// <summary>
        /// Удаляет тег из базы при нажатии на кнопку «✕» у тега.
        /// </summary>
        private void OnRemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // База определяется через общий предок TreeViewItem.
            var treeViewItem = FindAncestor<TreeViewItem>(button);
            if (treeViewItem?.DataContext is not Infobase infobase)
                return;

            // Тег — это DataContext кнопки (кнопка находится в ItemsControl.ItemTemplate тегов).
            if (button.DataContext is not string tag)
                return;

            if (_viewModel.RemoveTagCommand.CanExecute(null))
            {
                _viewModel.RemoveTagCommand.Execute(new object[] { infobase, tag });
            }
        }

        /// <summary>
        /// Обрабатывает нажатие Enter в поле ввода тега: добавляет тег и скрывает поле.
        /// </summary>
        private void OnInlineTagBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            CommitInlineTag(sender as TextBox);
            e.Handled = true;
        }

        /// <summary>
        /// При потере фокуса полем ввода тега добавляет тег и скрывает поле.
        /// </summary>
        private void OnInlineTagBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitInlineTag(sender as TextBox);
        }

        /// <summary>
        /// Добавляет введённый тег к базе и скрывает поле ввода.
        /// </summary>
        private void CommitInlineTag(TextBox? tagBox)
        {
            if (tagBox is null)
                return;

            var infobase = tagBox.DataContext;
            var tag = tagBox.Text?.Trim() ?? string.Empty;

            // Скрываем поле ввода и возвращаем кнопку «+ тег».
            tagBox.Visibility = Visibility.Collapsed;

            // Кнопка «+ тег» находится рядом с полем ввода в одном StackPanel,
            // поэтому ищем её через общего предка TreeViewItem.
            var treeViewItem = FindAncestor<TreeViewItem>(tagBox);
            var addButton = treeViewItem is null
                ? null
                : FindVisualChildByName<Button>(treeViewItem, "AddTagButton");
            if (addButton is not null)
            {
                addButton.Visibility = Visibility.Visible;
            }

            if (string.IsNullOrEmpty(tag) || infobase is null)
                return;

            if (_viewModel.AddTagInlineCommand.CanExecute(null))
            {
                _viewModel.AddTagInlineCommand.Execute(new object[] { infobase, tag });
            }
        }

        /// <summary>
        /// Ищет дочерний элемент заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Ищет дочерний элемент заданного типа с указанным именем в визуальном дереве.
        /// </summary>
        private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && typedChild.Name == name)
                    return typedChild;

                var result = FindVisualChildByName<T>(child, name);
                if (result is not null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Применяет сохранённые ширины колонок списка баз.
        /// </summary>
        private void ApplySavedColumnWidths()
        {
            SetColumnWidth(NameColumn, _viewModel.NameColumnWidth);
            SetColumnWidth(VersionColumn, _viewModel.VersionColumnWidth);
            SetColumnWidth(LaunchModeColumn, _viewModel.LaunchModeColumnWidth);
            SetColumnWidth(ServerColumn, _viewModel.ServerColumnWidth);
            SetColumnWidth(LastLaunchColumn, _viewModel.LastLaunchColumnWidth);
        }

        /// <summary>
        /// Устанавливает ширину колонки, если задано значение больше нуля.
        /// </summary>
        private static void SetColumnWidth(ColumnDefinition? column, double width)
        {
            if (column is null || width <= 0)
                return;
            column.Width = new GridLength(width);
        }

        // Поля для ручного перетаскивания разделителя колонок.
        private ColumnDefinition? _resizeColumn;
        private double _resizeStartWidth;
        private Point _resizeStartMouse;

        /// <summary>
        /// Определяет колонку, ширину которой меняет данный разделитель.
        /// Разделитель в Grid.Column=N расположен слева от колонки N, поэтому он меняет колонку N-1.
        /// </summary>
        private ColumnDefinition? GetSplitterTargetColumn(object sender)
        {
            if (ReferenceEquals(sender, VersionSplitter))
                return NameColumn;
            if (ReferenceEquals(sender, LaunchModeSplitter))
                return VersionColumn;
            if (ReferenceEquals(sender, ServerSplitter))
                return LaunchModeColumn;
            if (ReferenceEquals(sender, LastLaunchSplitter))
                return ServerColumn;
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
        /// Соседние колонки не затрагиваются: разность впитывает последняя гибкая колонка (*).
        /// </summary>
        private void OnColumnResize_MouseMove(object sender, MouseEventArgs e)
        {
            if (_resizeColumn is null || sender is not UIElement element || !element.IsMouseCaptured)
                return;

            var current = e.GetPosition(this);
            var delta = current.X - _resizeStartMouse.X;

            var newWidth = _resizeStartWidth + delta;
            if (newWidth < 40)
                newWidth = 40;

            _resizeColumn.Width = new GridLength(newWidth);

            _viewModel.UpdateColumnWidths(
                NameColumn?.ActualWidth ?? 0,
                VersionColumn?.ActualWidth ?? 0,
                LaunchModeColumn?.ActualWidth ?? 0,
                ServerColumn?.ActualWidth ?? 0,
                LastLaunchColumn?.ActualWidth ?? 0);
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
                    LaunchModeColumn?.ActualWidth ?? 0,
                    ServerColumn?.ActualWidth ?? 0,
                    LastLaunchColumn?.ActualWidth ?? 0);
                SyncHeaderWidthWithList();
            }

            _resizeColumn = null;
            e.Handled = true;
        }

        private void UpdateThemeButton()
        {
            if (ThemeToggleButton is null)
                return;

            if (ThemeToggleButton.Content is System.Windows.Controls.StackPanel sp
                && sp.Children.Count > 1
                && sp.Children[1] is System.Windows.Controls.TextBlock tb)
            {
                tb.Text = ThemeManager.CurrentTheme == ThemeManager.DarkThemeName ? "Светлая" : "Тёмная";
            }
            else
            {
                ThemeToggleButton.Content = ThemeManager.CurrentTheme == ThemeManager.DarkThemeName ? "Светлая" : "Тёмная";
            }
        }

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

        // ===================== Drag & Drop баз и групп =====================
        //
        // Модель WPF DnD (кратко):
        // 1) Источник: после порога смещения мыши вызывается DragDrop.DoDragDrop — синхронный
        //    цикл до Drop/Cancel. Пока он идёт, приходят DragOver/Drop на цели.
        // 2) Цель: AllowDrop=True; в DragOver обязательно задать e.Effects и e.Handled=true,
        //    иначе курсор «запрещено» и Drop не придёт.
        // 3) Данные: лучше свой payload с MouseDown (не с MouseMove) — иначе под курсором
        //    уже другой TreeViewItem (дочерняя база вместо группы).
        // 4) DoDragDrop возвращается после Drop → finally очищает _draggedData; во время Drop
        //    поле ещё валидно. Дополнительно кладём объект в DataObject по имени формата.

        private const string DragFormatInfobase = "Configuration_Management.Infobase";
        private const string DragFormatGroup = "Configuration_Management.GroupNode";

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
                if (targetGroup.DisplayName == "Закреплённые")
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