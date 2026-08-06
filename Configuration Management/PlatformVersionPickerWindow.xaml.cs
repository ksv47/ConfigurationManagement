using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора версии платформы 1С из установленных версий,
    /// сгруппированных по мажорной версии (например, «8.3.27»).
    /// </summary>
    public partial class PlatformVersionPickerWindow : Window
    {
        private string _selectedVersion = string.Empty;

        /// <summary>
        /// Создаёт диалог выбора версии платформы.
        /// </summary>
        /// <param name="installedPlatformVersions">Список установленных версий платформы.</param>
        /// <param name="currentVersion">Текущая выбранная версия (для предварительного выделения).</param>
        public PlatformVersionPickerWindow(IEnumerable<string> installedPlatformVersions, string currentVersion)
        {
            InitializeComponent();
            PopulateTree(installedPlatformVersions, currentVersion);
        }

        /// <summary>
        /// Выбранная версия платформы.
        /// </summary>
        public string Result => _selectedVersion;

        /// <summary>
        /// Заполняет дерево версий, группируя их по мажорной версии.
        /// </summary>
        private void PopulateTree(IEnumerable<string> versions, string currentVersion)
        {
            PlatformsTree.Items.Clear();

            var groups = versions
                .GroupBy(GetMajorVersion)
                .OrderByDescending(g => g.Key, new VersionComparer())
                .Select(g => new PlatformVersionGroup
                {
                    Name = g.Key,
                    Versions = g.OrderByDescending(v => v, new VersionComparer()).ToList()
                })
                .ToList();

            foreach (var group in groups)
            {
                var groupItem = new TreeViewItem
                {
                    Header = group.Name,
                    IsExpanded = true
                };

                foreach (var version in group.Versions)
                {
                    var versionItem = new TreeViewItem
                    {
                        Header = version,
                        Tag = version
                    };
                    if (string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        versionItem.IsSelected = true;
                    }
                    groupItem.Items.Add(versionItem);
                }

                PlatformsTree.Items.Add(groupItem);
            }
        }

        /// <summary>
        /// Возвращает мажорную версию (первые три компонента) из полной версии.
        /// </summary>
        private static string GetMajorVersion(string version)
        {
            var parts = version.Split('.');
            return parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : version;
        }

        /// <summary>
        /// Обрабатывает изменение выделения в дереве: активирует кнопку «Выбрать»,
        /// если выбран конкретный узел версии (не группа).
        /// </summary>
        private void OnPlatformsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = PlatformsTree.SelectedItem as TreeViewItem;
            var isVersion = selected?.Tag is string version && !string.IsNullOrEmpty(version);
            SelectButton.IsEnabled = isVersion;
            if (isVersion)
            {
                _selectedVersion = (string)selected!.Tag;
            }
        }

        /// <summary>
        /// Выбирает версию при двойном клике по узлу версии.
        /// </summary>
        private void OnPlatformsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectButton.IsEnabled)
            {
                OnSelect_Click(sender, e);
            }
        }

        private void OnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedVersion))
                return;

            DialogResult = true;
        }

        /// <summary>
        /// Компаратор для сортировки версий по убыванию.
        /// </summary>
        private sealed class VersionComparer : IComparer<string>
        {
            public int Compare(string? x, string? y)
            {
                if (x == y) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                var xParts = x.Split('.').Select(int.Parse).ToArray();
                var yParts = y.Split('.').Select(int.Parse).ToArray();

                var length = Math.Max(xParts.Length, yParts.Length);
                for (var i = 0; i < length; i++)
                {
                    var xVal = i < xParts.Length ? xParts[i] : 0;
                    var yVal = i < yParts.Length ? yParts[i] : 0;
                    if (xVal != yVal)
                        return xVal.CompareTo(yVal);
                }

                return 0;
            }
        }
    }
}