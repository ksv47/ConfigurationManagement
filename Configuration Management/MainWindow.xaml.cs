using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        // Флаг для предотвращения рекурсии при синхронизации выделения между двумя списками.
        private bool _isSyncingSelection;

        // Имя группы, над которой находится курсор во время перетаскивания.
        // Определяется в DragOver (где Mouse.DirectlyOver надёжен) и используется в Drop.
        private string? _dragTargetGroup;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Применяем сохранённую тему оформления при запуске.
            if (!string.IsNullOrEmpty(_viewModel.SavedTheme))
            {
                ThemeManager.ApplyTheme(_viewModel.SavedTheme);
            }

            UpdateThemeButton();

        }

        private void OnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ToggleTheme();
            UpdateThemeButton();
            _viewModel.SaveTheme(ThemeManager.CurrentTheme);
        }

        /// <summary>
        /// Синхронизирует выделение между списком закреплённых баз и основным списком,
        /// чтобы одновременно выделялась только одна база.
        /// </summary>
        private void OnPinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection)
                return;

            if (PinnedListBox.SelectedItem is Infobase selected)
            {
                _isSyncingSelection = true;
                _viewModel.SelectedInfobase = selected;
                MainListBox.SelectedItem = null;
                _isSyncingSelection = false;
            }
        }

        /// <summary>
        /// Синхронизирует выделение между основным списком и списком закреплённых баз,
        /// чтобы одновременно выделялась только одна база.
        /// </summary>
        private void OnMainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSelection)
                return;

            if (MainListBox.SelectedItem is Infobase selected)
            {
                _isSyncingSelection = true;
                _viewModel.SelectedInfobase = selected;
                PinnedListBox.SelectedItem = null;
                _isSyncingSelection = false;
            }
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

        /// <summary>
        /// Открывает выпадающее меню дополнительных функций
        /// (экспорт и загрузка списка баз).
        /// </summary>
        private void OnExtraFunctions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                button.ContextMenu.IsOpen = true;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnInfobaseList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.LaunchEnterpriseCommand.CanExecute(null))
            {
                _viewModel.LaunchEnterpriseCommand.Execute(null);
            }
        }

        private void OnGroupExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
            {
                _viewModel.SetGroupCollapsed(group.Name?.ToString() ?? string.Empty, false);
            }
        }

        private void OnGroupCollapsed(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
            {
                _viewModel.SetGroupCollapsed(group.Name?.ToString() ?? string.Empty, true);
            }
        }

        /// <summary>
        /// Начинает перетаскивание группы при нажатии на ручку перетаскивания.
        /// </summary>
        private void OnGroupDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button)
                return;

            // Помечаем событие как обработанное ДО запуска перетаскивания,
            // чтобы кнопка и Expander не реагировали на клик.
            e.Handled = true;

            var expander = FindAncestor<Expander>(button);
            if (expander?.DataContext is CollectionViewGroup group)
            {
                var groupName = group.Name?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(groupName))
                {
                    _dragTargetGroup = null;
                    DragDrop.DoDragDrop(button, groupName, DragDropEffects.Move);
                }
            }
        }

        /// <summary>
        /// Обрабатывает перетаскивание над списком: разрешает перемещение групп
        /// и запоминает целевую группу под курсором.
        /// </summary>
        private void OnListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(string)))
            {
                e.Effects = DragDropEffects.Move;
                _dragTargetGroup = GetGroupNameAtPosition(sender as ListBox, e.GetPosition(sender as IInputElement));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Завершает перетаскивание группы: перемещает её на позицию целевой группы.
        /// </summary>
        private void OnListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(string)) is not string sourceGroup)
                return;

            var targetGroup = _dragTargetGroup;
            if (string.IsNullOrEmpty(targetGroup))
            {
                // Если цель не была определена в DragOver, пробуем определить её по позиции.
                targetGroup = GetGroupNameAtPosition(sender as ListBox, e.GetPosition(sender as IInputElement));
            }

            _dragTargetGroup = null;
            if (!string.IsNullOrEmpty(targetGroup))
            {
                _viewModel.MoveGroup(sourceGroup, targetGroup);
            }
        }

        /// <summary>
        /// Определяет имя группы, над которой находится курсор мыши,
        /// по позиции относительно списка через hit-test.
        /// </summary>
        private static string? GetGroupNameAtPosition(ListBox? listBox, Point position)
        {
            if (listBox is null)
                return null;

            var hit = VisualTreeHelper.HitTest(listBox, position);
            if (hit?.VisualHit is not DependencyObject element)
                return null;

            var expander = FindAncestor<Expander>(element);
            if (expander?.DataContext is CollectionViewGroup group)
            {
                return group.Name?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Ищет предка заданного типа в визуальном дереве.
        /// </summary>
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
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
            // поэтому ищем его через общий предок ListBoxItem.
            var listBoxItem = FindAncestor<ListBoxItem>(button);
            var tagBox = listBoxItem is null ? null : FindVisualChild<TextBox>(listBoxItem);
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

            // База определяется через общий предок ListBoxItem.
            var listBoxItem = FindAncestor<ListBoxItem>(button);
            if (listBoxItem?.DataContext is not Infobase infobase)
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
            // поэтому ищем её через общего предка ListBoxItem.
            var listBoxItem = FindAncestor<ListBoxItem>(tagBox);
            var addButton = listBoxItem is null
                ? null
                : FindVisualChildByName<Button>(listBoxItem, "AddTagButton");
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

        private void UpdateThemeButton()
        {
            if (ThemeToggleButton is null)
                return;

            ThemeToggleButton.Content = ThemeManager.CurrentTheme == ThemeManager.DarkThemeName
                ? "☀️ Светлая"
                : "🌙 Тёмная";
        }
    }
}