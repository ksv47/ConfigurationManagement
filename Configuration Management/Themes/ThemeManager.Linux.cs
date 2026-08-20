#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Минимальный ThemeManager для Linux (Этап 2).
    /// Применение цветовых схем на Avalonia (Styles/Resources) — Этап 3.
    /// Здесь — заглушка, чтобы инфраструктура приложения компилировалась.
    /// </summary>
    public static class ThemeManager
    {
        public const string LightThemeName = "Light";
        public const string DarkThemeName = "Dark";

        public static void ApplyScheme(ColorScheme scheme) { }
        public static void ApplyTheme(string themeName) { }
    }
}
#endif