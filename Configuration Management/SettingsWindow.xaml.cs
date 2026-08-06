using System.Windows;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private List<string> _installedPlatformVersions;

        /// <summary>
        /// Создаёт диалог настроек приложения.
        /// </summary>
        /// <param name="installedPlatformVersions">Текущий список установленных версий платформы.</param>
        public SettingsWindow(IEnumerable<string>? installedPlatformVersions = null)
        {
            InitializeComponent();
            _installedPlatformVersions = new List<string>(installedPlatformVersions ?? new List<string>());
            UpdatePlatformsDisplay();
        }

        /// <summary>
        /// Список установленных версий платформы 1С.
        /// </summary>
        public List<string> Result => _installedPlatformVersions;

        /// <summary>
        /// Обновляет список установленных версий платформы, сканируя каталоги 1С.
        /// </summary>
        private void OnRefreshPlatforms_Click(object sender, RoutedEventArgs e)
        {
            _installedPlatformVersions = PlatformVersionService.FindInstalledVersions();
            UpdatePlatformsDisplay();
        }

        /// <summary>
        /// Обновляет отображение списка установленных версий платформы,
        /// группируя их по мажорной версии (например, «8.3.27»).
        /// </summary>
        private void UpdatePlatformsDisplay()
        {
            PlatformsTree.Items.Clear();

            if (_installedPlatformVersions.Count == 0)
            {
                StatusText.Text = "Версии платформы 1С не найдены. Нажмите «Обновить список».";
                return;
            }

            // Группируем версии по первым трём компонентам (мажорная версия).
            var groups = _installedPlatformVersions
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
                PlatformsTree.Items.Add(group);
            }

            StatusText.Text = $"Найдено версий: {_installedPlatformVersions.Count}";
        }

        /// <summary>
        /// Возвращает мажорную версию (первые три компонента) из полной версии.
        /// Например, для «8.3.27.1234» вернёт «8.3.27».
        /// </summary>
        private static string GetMajorVersion(string version)
        {
            var parts = version.Split('.');
            return parts.Length >= 3
                ? string.Join(".", parts.Take(3))
                : version;
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
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