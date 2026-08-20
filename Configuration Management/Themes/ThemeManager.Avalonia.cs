#if LINUX
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Themes;
using Configuration_Management.Models;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Avalonia-версия ThemeManager (Linux). Управляет переключением светлой/тёмной темы
    /// и цветовых схем: задаёт RequestedThemeVariant и накладывает цвета схемы как ресурсы
    /// приложения (Application.Resources). Пользовательские схемы хранятся в JSON-файлах.
    /// </summary>
    public static class ThemeManager
    {
        public const string LightThemeName = "Light";
        public const string DarkThemeName = "Dark";

        /// <summary>Название активной темы (Light/Dark) — базовая тема текущей схемы.</summary>
        public static string CurrentTheme { get; private set; } = LightThemeName;

        /// <summary>Активная цветовая схема (тема оформления).</summary>
        public static ColorScheme CurrentScheme { get; private set; } = ColorScheme.CreateLight();

        /// <summary>Каталог пользовательских цветовых схем (JSON-файлы).</summary>
        public static string CustomSchemesDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConfigurationManagement",
            "ColorSchemes");

        /// <summary>
        /// Применяет цветовую схему: задаёт базовый вариант темы (Light/Dark) и накладывает
        /// цвета схемы на ресурсы приложения.
        /// </summary>
        public static void ApplyScheme(ColorScheme? scheme)
        {
            var app = Application.Current;
            if (app is null)
                return;

            scheme ??= ColorScheme.CreateLight();
            CurrentScheme = scheme;
            CurrentTheme = scheme.IsDark ? DarkThemeName : LightThemeName;

            app.RequestedThemeVariant = scheme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            ApplyColors(app, scheme);
        }

        /// <summary>
        /// Применяет встроенную тему по имени («Light» / «Dark»).
        /// </summary>
        public static void ApplyTheme(string themeName)
        {
            var isDark = themeName == DarkThemeName;
            ApplyScheme(isDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight());
        }

        /// <summary>Переключает между светлой и тёмной встроенной темой, возвращает новое имя темы.</summary>
        public static string ToggleTheme()
        {
            var next = CurrentTheme == DarkThemeName ? LightThemeName : DarkThemeName;
            ApplyTheme(next);
            return next;
        }

        /// <summary>Возвращает встроенную схему по имени темы («Light»/«Dark») или null.</summary>
        public static ColorScheme? GetBuiltInScheme(string themeName)
            => themeName == DarkThemeName ? ColorScheme.CreateDark() : ColorScheme.CreateLight();

        /// <summary>Шрифт интерфейса по умолчанию.</summary>
        public const string DefaultFontFamily = "Segoe UI";
        public const double DefaultFontSize = 13;
        public const string DefaultFontWeight = "Normal";
        public const string DefaultFontStyle = "Normal";

        /// <summary>
        /// Применяет настройки шрифта к элементу управления. Свойства шрифта в Avalonia
        /// наследуются, поэтому распространяются на дочерние элементы.
        /// </summary>
        public static void ApplyFont(Control? target,
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (target is null)
                return;
            try
            {
                var family = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
                var size = fontSize > 0 ? fontSize : DefaultFontSize;
                target.FontFamily = new FontFamily(family);
                target.FontSize = size;
                target.FontWeight = string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                    ? FontWeight.Bold : FontWeight.Normal;
                target.FontStyle = string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                    ? FontStyle.Italic : FontStyle.Normal;
            }
            catch
            {
                // Игнорируем некорректные настройки шрифта (например, несуществующее семейство).
            }
        }

        /// <summary>
        /// Применяет настройки шрифта ко всем открытым окнам приложения.
        /// </summary>
        public static void ApplyFontToAllWindows(
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (Application.Current is null)
                return;
            foreach (Window window in Application.Current.Windows)
                ApplyFont(window, fontFamily, fontSize, fontWeight, fontStyle);
        }

        // ---- Настройки шрифта отдельных областей интерфейса ----

        public const string FontDefault = "Default";
        public const string FontList = "List";
        public const string FontListHeader = "ListHeader";
        public const string FontRightPanel = "RightPanel";
        public const string FontStatusBar = "StatusBar";
        public const string FontTabs = "Tabs";
        public const string FontButtons = "Buttons";
        public const string FontInputs = "Inputs";

        /// <summary>Все ключи областей (в порядке наложения).</summary>
        public static readonly string[] AllFontScopes =
        {
            FontDefault, FontButtons, FontInputs, FontTabs, FontListHeader, FontList, FontRightPanel, FontStatusBar
        };

        /// <summary>Читаемое название области для интерфейса настроек.</summary>
        public static string FontScopeDisplayName(string key) => key switch
        {
            FontDefault => "По умолчанию",
            FontList => "Список баз",
            FontListHeader => "Заголовки списка",
            FontRightPanel => "Правая панель",
            FontStatusBar => "Нижняя панель (статус)",
            FontTabs => "Вкладки",
            FontButtons => "Кнопки",
            FontInputs => "Поля ввода",
            _ => key
        };

        /// <summary>
        /// Накладывает цвета схемы на ресурсы приложения: для каждого цвета обновляется
        /// ресурс Color и (если есть) одноимённый SolidColorBrush; для ключей, оканчивающихся
        /// на «Brush», обновляется непосредственно кисть.
        /// </summary>
        private static void ApplyColors(Application app, ColorScheme scheme)
        {
            foreach (var kvp in scheme.Colors)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || !TryParseColor(kvp.Value, out var color))
                    continue;

                if (kvp.Key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    app.Resources[kvp.Key] = new SolidColorBrush(color);
                }
                else
                {
                    app.Resources[kvp.Key] = color;
                    app.Resources[kvp.Key + "Brush"] = new SolidColorBrush(color);
                }
            }
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try { color = Color.Parse(hex); return true; }
            catch { color = Colors.Transparent; return false; }
        }

        // ---- Управление пользовательскими схемами ----

        /// <summary>Возвращает список всех доступных схем: встроенные + пользовательские.</summary>
        public static List<ColorScheme> EnumerateAllSchemes()
        {
            var result = new List<ColorScheme>
            {
                ColorScheme.CreateLight(),
                ColorScheme.CreateDark()
            };
            result.AddRange(LoadCustomSchemes());
            return result;
        }

        /// <summary>Загружает пользовательские схемы из каталога пользователя.</summary>
        public static List<ColorScheme> LoadCustomSchemes()
        {
            var result = new List<ColorScheme>();
            if (!Directory.Exists(CustomSchemesDirectory))
                return result;

            foreach (var file in Directory.GetFiles(CustomSchemesDirectory, "*.json"))
            {
                try
                {
                    var scheme = ColorScheme.FromJson(File.ReadAllText(file));
                    if (scheme is not null && !string.IsNullOrWhiteSpace(scheme.Name))
                    {
                        if (string.IsNullOrWhiteSpace(scheme.Name) || scheme.Name == "Light" || scheme.Name == "Dark")
                            scheme.Name = Path.GetFileNameWithoutExtension(file);
                        result.Add(scheme);
                    }
                }
                catch
                {
                    // Пропускаем повреждённые файлы схем.
                }
            }
            return result;
        }

        /// <summary>Ищет пользовательскую схему по имени (с учётом регистра).</summary>
        public static ColorScheme? FindCustomScheme(string name)
            => LoadCustomSchemes().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Сохраняет пользовательскую схему в каталог пользователя.</summary>
        public static void SaveCustomScheme(ColorScheme scheme)
        {
            if (scheme is null || string.IsNullOrWhiteSpace(scheme.Name))
                return;

            Directory.CreateDirectory(CustomSchemesDirectory);
            var file = Path.Combine(CustomSchemesDirectory, SafeFileName(scheme.Name) + ".json");
            File.WriteAllText(file, scheme.ToJson());
        }

        /// <summary>Удаляет пользовательскую схему по имени. Возвращает true, если файл удалён.</summary>
        public static bool DeleteCustomScheme(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            var file = Path.Combine(CustomSchemesDirectory, SafeFileName(name) + ".json");
            if (File.Exists(file))
            {
                File.Delete(file);
                return true;
            }
            return false;
        }

        /// <summary>Выгружает схему в указанный файл JSON.</summary>
        public static void ExportScheme(ColorScheme scheme, string filePath)
        {
            if (scheme is null)
                throw new ArgumentNullException(nameof(scheme));
            File.WriteAllText(filePath, scheme.ToJson());
        }

        /// <summary>Загружает схему из файла JSON. Возвращает null при ошибке.</summary>
        public static ColorScheme? ImportScheme(string filePath)
        {
            if (!File.Exists(filePath))
                return null;
            return ColorScheme.FromJson(File.ReadAllText(filePath));
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var result = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(result) ? "Scheme" : result;
        }
    }
}
#endif