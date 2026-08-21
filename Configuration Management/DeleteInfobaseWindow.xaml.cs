using System.IO;
using System.Windows;
using Configuration_Management.Localization;
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
            GroupText.Text = string.IsNullOrWhiteSpace(infobase.Group) ? LocalizationManager.T("Connection.NoGroup") : infobase.Group;
            PlatformText.Text = string.IsNullOrWhiteSpace(infobase.PlatformVersion) ? "—" : infobase.PlatformVersion;

            var isFile = infobase.Connection.Type == ConnectionType.File;
            PhysicalPanel.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;

            if (isFile)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(infobase);
                var exists = InfobaseMaintenanceService.FileBaseExists(infobase);
                if (exists && !string.IsNullOrEmpty(dir))
                {
                    ExistsText.Text = string.Format(LocalizationManager.T("DeleteInfobase.ExistsYes"), dir);
                    ExistsText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                    PhysicalDeleteCheck.IsEnabled = true;
                    PhysicalHint.Text =
                        string.Format(LocalizationManager.T("DeleteInfobase.PhysicalHintDynamic"), dir);
                }
                else
                {
                    ExistsText.Text = string.IsNullOrEmpty(dir)
                        ? LocalizationManager.T("DeleteInfobase.DirNotSpecified")
                        : string.Format(LocalizationManager.T("DeleteInfobase.DirNotFound"), dir);
                    ExistsText.Foreground = System.Windows.Media.Brushes.Gray;
                    PhysicalDeleteCheck.IsEnabled = false;
                    PhysicalDeleteCheck.IsChecked = false;
                    PhysicalHint.Text = LocalizationManager.T("DeleteInfobase.PhysicalUnavailable");
                }
            }
            else
            {
                ExistsText.Text = LocalizationManager.T("DeleteInfobase.NonFileOnlyFromList");
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
                    string.Format(LocalizationManager.T("DeleteInfobase.PhysicalConfirm"), dir),
                    LocalizationManager.T("DeleteInfobase.PhysicalDeleteTitle"),
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
