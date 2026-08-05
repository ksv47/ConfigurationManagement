using System.Windows;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Управляет переключением светлой и тёмной темы приложения.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>Имя ресурса темы по умолчанию (светлая).</summary>
        public const string LightThemeName = "Light";

        /// <summary>Имя ресурса тёмной темы.</summary>
        public const string DarkThemeName = "Dark";

        /// <summary>Ключ словаря ресурсов темы в App.Resources.</summary>
        private const string ThemeDictionaryKey = "ThemeDictionary";

        /// <summary>Текущая активная тема.</summary>
        public static string CurrentTheme { get; private set; } = LightThemeName;

        /// <summary>
        /// Применяет тему по указанному имени.
        /// </summary>
        /// <param name="themeName">Имя темы: "Light" или "Dark".</param>
        public static void ApplyTheme(string themeName)
        {
            var app = Application.Current;
            if (app is null)
                return;

            var uri = themeName == DarkThemeName
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            var dictionary = new ResourceDictionary { Source = uri };

            // Заменяем словарь темы в ресурсах приложения.
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                app.Resources.MergedDictionaries[0] = dictionary;
            }
            else
            {
                app.Resources.MergedDictionaries.Add(dictionary);
            }

            CurrentTheme = themeName == DarkThemeName ? DarkThemeName : LightThemeName;
        }

        /// <summary>
        /// Переключает тему на противоположную и возвращает новое имя темы.
        /// </summary>
        public static string ToggleTheme()
        {
            var next = CurrentTheme == DarkThemeName ? LightThemeName : DarkThemeName;
            ApplyTheme(next);
            return next;
        }
    }
}