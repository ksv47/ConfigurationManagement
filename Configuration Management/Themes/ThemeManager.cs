using System.Windows;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Управляет переключением светлой и тёмной темы приложения.
    /// </summary>
    public static class ThemeManager
    {
        public const string LightThemeName = "Light";
        public const string DarkThemeName = "Dark";

        public static string CurrentTheme { get; private set; } = LightThemeName;

        public static void ApplyTheme(string themeName)
        {
            var app = Application.Current;
            if (app is null)
                return;

            var isDark = themeName == DarkThemeName;
            var uri = isDark
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            var dictionary = new ResourceDictionary { Source = uri };

            // Ищем существующий словарь темы (Light/Dark), не трогаем MaterialDesign
            var merged = app.Resources.MergedDictionaries;
            int index = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                var src = merged[i].Source?.OriginalString ?? "";
                if (src.Contains("LightTheme.xaml", System.StringComparison.OrdinalIgnoreCase)
                    || src.Contains("DarkTheme.xaml", System.StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                merged[index] = dictionary;
            else
                merged.Add(dictionary);

            CurrentTheme = isDark ? DarkThemeName : LightThemeName;
        }

        public static string ToggleTheme()
        {
            var next = CurrentTheme == DarkThemeName ? LightThemeName : DarkThemeName;
            ApplyTheme(next);
            return next;
        }
    }
}
