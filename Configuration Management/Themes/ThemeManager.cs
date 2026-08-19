using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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

        /// <summary>Шрифт интерфейса по умолчанию (если настройки не заданы).</summary>
        public const string DefaultFontFamily = "Segoe UI";
        public const double DefaultFontSize = 13;
        public const string DefaultFontWeight = "Normal";
        public const string DefaultFontStyle = "Normal";

        /// <summary>
        /// Применяет настройки шрифта интерфейса к указанному элементу (окну).
        /// Семейство, размер, начертание и стиль шрифта задаются через наследуемые
        /// свойства <see cref="TextElement"/>, поэтому распространяются на все дочерние
        /// текстовые элементы, не переопределяющие их явно.
        /// </summary>
        public static void ApplyFont(FrameworkElement target,
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (target is null)
                return;

            try
            {
                var family = string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily;
                var size = fontSize > 0 ? fontSize : DefaultFontSize;

                TextElement.SetFontFamily(target, new FontFamily(family));
                TextElement.SetFontSize(target, size);
                TextElement.SetFontWeight(target,
                    string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                        ? FontWeights.Bold : FontWeights.Normal);
                TextElement.SetFontStyle(target,
                    string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                        ? FontStyles.Italic : FontStyles.Normal);
            }
            catch
            {
                // Игнорируем некорректные настройки шрифта (например, несуществующее семейство).
            }
        }

        /// <summary>
        /// Применяет настройки шрифта интерфейса ко всем открытым окнам приложения.
        /// Используется для мгновенного обновления интерфейса после применения/сохранения.
        /// </summary>
        public static void ApplyFontToAllWindows(
            string fontFamily, double fontSize, string fontWeight, string fontStyle)
        {
            if (Application.Current is null)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                ApplyFont(window, fontFamily, fontSize, fontWeight, fontStyle);
            }
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
        /// Применяет настройки шрифта ко всем текстовым элементам поддерева принудительно
        /// (устанавливает локальные значения, перекрывающие фиксированные размеры в XAML).
        /// </summary>
        public static void ApplyFontToTree(DependencyObject root, ElementFontSettings? fs)
        {
            if (root is null)
                return;
            ApplyFontProps(root as FrameworkElement, fs);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                ApplyFontToTree(VisualTreeHelper.GetChild(root, i), fs);
        }

        private static void ApplyFontProps(FrameworkElement? fe, ElementFontSettings? fs)
        {
            if (fe is null)
                return;
            var family = string.IsNullOrWhiteSpace(fs?.FontFamily) ? DefaultFontFamily : fs.FontFamily;
            var size = fs is { FontSize: > 0 } ? fs.FontSize : DefaultFontSize;
            var weight = string.Equals(fs?.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                ? FontWeights.Bold : FontWeights.Normal;
            var style = string.Equals(fs?.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                ? FontStyles.Italic : FontStyles.Normal;

            fe.SetValue(TextElement.FontFamilyProperty, new FontFamily(family));
            fe.SetValue(TextElement.FontSizeProperty, size);
            fe.SetValue(TextElement.FontWeightProperty, weight);
            fe.SetValue(TextElement.FontStyleProperty, style);
        }

        /// <summary>
        /// Применяет индивидуальные настройки шрифта областей к главному окну.
        /// Сначала применяется «По умолчанию» ко всему окну, затем более конкретные
        /// области (кнопки, поля, вкладки, список, заголовки, панели) накладываются поверх.
        /// </summary>
        public static void ApplyElementFonts(MainWindow window, IReadOnlyDictionary<string, ElementFontSettings>? fonts)
        {
            if (window is null)
                return;
            fonts ??= new Dictionary<string, ElementFontSettings>();

            ElementFontSettings? Scope(string key)
            {
                if (fonts.TryGetValue(key, out var fs) && fs is not null && fs.FontSize > 0)
                    return fs;
                return null;
            }

            if (window.Content is FrameworkElement content)
                ApplyFontToTree(content, Scope(FontDefault) ?? new ElementFontSettings());

            ApplyFontToType(window, typeof(Button), Scope(FontButtons));
            ApplyFontToType(window, typeof(TextBoxBase), Scope(FontInputs));
            ApplyFontToType(window, typeof(PasswordBox), Scope(FontInputs));
            ApplyFontToType(window, typeof(ComboBox), Scope(FontInputs));

            ApplyFontToNamed(window, "TabsPanel", Scope(FontTabs));
            ApplyFontToNamed(window, "HeaderGrid", Scope(FontListHeader));
            ApplyFontToNamed(window, "MainTree", Scope(FontList));
            ApplyFontToNamed(window, "RightPanelBorder", Scope(FontRightPanel));
            ApplyFontToNamed(window, "StatusBarBorder", Scope(FontStatusBar));
        }

        private static void ApplyFontToNamed(MainWindow window, string name, ElementFontSettings? fs)
        {
            if (fs is null)
                return;
            var el = window.FindName(name) as FrameworkElement;
            if (el is not null)
                ApplyFontToTree(el, fs);
        }

        private static void ApplyFontToType(DependencyObject root, Type type, ElementFontSettings? fs)
        {
            if (root is null || fs is null)
                return;
            if (type.IsInstanceOfType(root) && root is FrameworkElement fe)
                ApplyFontProps(fe, fs);
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                ApplyFontToType(VisualTreeHelper.GetChild(root, i), type, fs);
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
