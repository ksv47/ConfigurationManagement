using System.Collections.ObjectModel;
using System.Windows;
using Configuration_Management.Models;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог управления группами информационных баз.
    /// </summary>
    public partial class GroupSettingsWindow : Window
    {
        private readonly ObservableCollection<Group> _groups;

        /// <summary>
        /// Создаёт диалог управления группами.
        /// </summary>
        /// <param name="groups">Текущий список групп.</param>
        public GroupSettingsWindow(IEnumerable<Group> groups)
        {
            InitializeComponent();
            _groups = new ObservableCollection<Group>(groups);
            GroupsListBox.ItemsSource = _groups;
        }

        /// <summary>
        /// Возвращает итоговый список групп.
        /// </summary>
        public List<Group> Result => _groups.ToList();

        private void OnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupEditWindow
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _groups.Add(dialog.Result);
                GroupsListBox.SelectedItem = dialog.Result;
            }
        }

        private void OnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (GroupsListBox.SelectedItem is not Group group)
                return;

            var dialog = new GroupEditWindow(group)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                // Обновляем отображение списка.
                var index = _groups.IndexOf(group);
                _groups[index] = dialog.Result;
                GroupsListBox.SelectedItem = dialog.Result;
            }
        }

        private void OnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (GroupsListBox.SelectedItem is not Group group)
                return;

            var result = MessageBox.Show(
                $"Удалить группу «{group.Name}»?",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _groups.Remove(group);
            }
        }

        private void OnDone_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}