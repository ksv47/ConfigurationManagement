using System.IO;
using System.Windows;
using System.Windows.Media;
using Configuration_Management.Models;

namespace Configuration_Management.Themes
{
    /// <summary>
    /// Управляет переключением светлой и тёмной темы приложения и цветовыми схемами.
    /// Поддерживает встроенные схемы («Светлая», «Тёмная»), пользовательские темы
    /// (сохраняются в каталог пользователя) и выгрузку/загрузку схем в JSON-файл.
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

        private static readonly Uri LightUri = new("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkUri = new("Themes/DarkTheme.xaml", UriKind.Relative);

        /// <summary>
        /// Применяет цветовую схему: загружает базовую тему (Light/Dark) и накладывает
        /// цвета схемы на ресурсы приложения.
        /// </summary>
        public static void ApplyScheme(ColorScheme scheme)
        {
            var app = Application.Current;
            if (app is null)
                return;

            scheme ??= ColorScheme.CreateLight();
            CurrentScheme = scheme;
            CurrentTheme = scheme.IsDark ? DarkThemeName : LightThemeName;

            var uri = scheme.IsDark ? DarkUri : LightUri;
            var dictionary = new ResourceDictionary { Source = uri };
            ApplyColors(dictionary, scheme);

            // Ищем существующий словарь темы (Light/Dark), не трогаем MaterialDesign.
            var merged = app.Resources.MergedDictionaries;
            int index = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                var src = merged[i].Source?.OriginalString ?? "";
                if (src.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    || src.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                merged[index] = dictionary;
            else
                merged.Add(dictionary);
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
        {
            return themeName == DarkThemeName ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
        }

        /// <summary>
        /// Накладывает цвета схемы на загруженный словарь темы: для каждого цвета
        /// обновляется ресурс Color и (если есть) одноимённый SolidColorBrush; для
        /// ключей, оканчивающихся на «Brush», обновляется непосредственно кисть.
        /// </summary>
        private static void ApplyColors(ResourceDictionary dict, ColorScheme scheme)
        {
            foreach (var kvp in scheme.Colors)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    continue;
                if (!TryParseColor(kvp.Value, out var color))
                    continue;

                if (kvp.Key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    if (dict.Contains(kvp.Key))
                        dict[kvp.Key] = new SolidColorBrush(color);
                }
                else
                {
                    if (dict.Contains(kvp.Key))
                        dict[kvp.Key] = color;
                    var brushKey = kvp.Key + "Brush";
                    if (dict.Contains(brushKey))
                        dict[brushKey] = new SolidColorBrush(color);
                }
            }
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
            }
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
                        // Сохраняем название по имени файла, если имя в файле отсутствует.
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
        {
            return LoadCustomSchemes().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

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
