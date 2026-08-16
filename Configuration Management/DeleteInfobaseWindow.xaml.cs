using System.IO;
using System.Windows;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог удаления ИБ: сведения о базе и опция физического удаления каталога (файловые базы).
    /// </summary>
    public partial class DeleteInfobaseWindow : Window
    {
        private readonly Infobase _infobase;

        /// <summary>Пользователь подтвердил удаление.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>Нужно физически удалить каталог файловой базы.</summary>
        public bool DeletePhysically { get; private set; }

        public DeleteInfobaseWindow(Infobase infobase)
        {
            InitializeComponent();
            _infobase = infobase;

            NameText.Text = string.IsNullOrWhiteSpace(infobase.Name) ? "—" : infobase.Name;
            TypeText.Text = infobase.ConnectionTypeDisplay;
            PathText.Text = string.IsNullOrWhiteSpace(infobase.ServerDatabaseDisplay)
                ? (infobase.ConnectionStringDisplay ?? "—")
                : infobase.ServerDatabaseDisplay;
            GroupText.Text = string.IsNullOrWhiteSpace(infobase.Group) ? "— Без группы —" : infobase.Group;
            PlatformText.Text = string.IsNullOrWhiteSpace(infobase.PlatformVersion) ? "—" : infobase.PlatformVersion;

            var isFile = infobase.Connection.Type == ConnectionType.File;
            PhysicalPanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;

            if (isFile)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(infobase);
                var exists = InfobaseMaintenanceService.FileBaseExists(infobase);
                if (exists && !string.IsNullOrEmpty(dir))
                {
                    ExistsText.Text = $"да — {dir}";
                    ExistsText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                    PhysicalDeleteCheck.IsEnabled = true;
                    PhysicalHint.Text =
                        $"Будет удалён каталог:\n{dir}\nвместе с 1Cv8.1CD и всеми файлами. Действие необратимо.";
                }
                else
                {
                    ExistsText.Text = string.IsNullOrEmpty(dir)
                        ? "каталог не указан или не найден"
                        : $"каталог не найден: {dir}";
                    ExistsText.Foreground = System.Windows.Media.Brushes.Gray;
                    PhysicalDeleteCheck.IsEnabled = false;
                    PhysicalDeleteCheck.IsChecked = false;
                    PhysicalHint.Text = "Физическое удаление недоступно: каталог базы на диске не найден.";
                }
            }
            else
            {
                ExistsText.Text = "клиент-серверная / веб — только из списка";
                ExistsText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void OnDelete_Click(object sender, RoutedEventArgs e)
        {
            DeletePhysically = PhysicalDeleteCheck.IsChecked == true
                               && PhysicalPanel.Visibility == Visibility.Visible
                               && PhysicalDeleteCheck.IsEnabled;

            if (DeletePhysically)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(_infobase) ?? "";
                var confirm = MessageBox.Show(
                    this,
                    $"Подтвердите физическое удаление каталога базы:\n\n{dir}\n\nВсе файлы будут уничтожены безвозвратно.",
                    "Физическое удаление",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void OnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
