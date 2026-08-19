using System.Text.Json;
using System.Text.Json.Serialization;

namespace Configuration_Management.Models;

/// <summary>
/// Цветовая схема (тема оформления) приложения: именованный набор цветов,
/// накладываемый поверх базовой светлой/тёмной темы. Поддерживает выгрузку
/// и загрузку в JSON-файл, а также создание собственных тем.
/// </summary>
public class ColorScheme
{
    /// <summary>Название схемы (темы).</summary>
    public string Name { get; set; } = "Light";

    /// <summary>true — тёмная базовая тема, false — светлая.</summary>
    public bool IsDark { get; set; }

    /// <summary>
    /// Набор цветов: ключ ресурса (имя Color-ресурса из темы, напр. <c>AccentColor</c>,
    /// либо имя кисти для ресурсов без отдельного цвета, напр. <c>ScrollThumbBrush</c>)
    /// → значение в формате #RRGGBB.
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>Ключ базовой темы (LightTheme.xaml / DarkTheme.xaml).</summary>
    [JsonIgnore]
    public string BaseThemeName => IsDark ? "Dark" : "Light";

    /// <summary>
    /// Возвращает упорядоченное описание редактируемых цветов:
    /// ключ ресурса и человекочитаемая подпись для редактора в настройках.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> Definitions { get; } = new (string, string)[]
    {
        ("AccentColor", "Акцентный цвет"),
        ("AccentHoverColor", "Акцент (наведение)"),
        ("AccentPressedColor", "Акцент (нажатие)"),
        ("SidebarColor", "Боковая панель (фон)"),
        ("SidebarHoverColor", "Боковая панель (наведение)"),
        ("SidebarSelectedColor", "Боковая панель (выбранный)"),
        ("ContentBackgroundColor", "Фон рабочей области"),
        ("CardBackgroundColor", "Фон карточек"),
        ("BorderColor", "Цвет границ"),
        ("TextPrimaryColor", "Основной текст"),
        ("TextSecondaryColor", "Вторичный текст"),
        ("TextOnAccentColor", "Текст на акцентном фоне"),
        ("ButtonTextColor", "Текст кнопок"),
        ("FavoriteColor", "Избранное (★)"),
        ("ItemHoverColor", "Строка списка (наведение)"),
        ("ItemSelectedColor", "Строка списка (выбранная)"),
        ("AvatarBackgroundColor", "Аватар (фон)"),
        ("AvatarTextColor", "Аватар (текст)"),
        ("SecondaryButtonBackgroundColor", "Вторичная кнопка (фон)"),
        ("SecondaryButtonHoverColor", "Вторичная кнопка (наведение)"),
        ("SecondaryButtonPressedColor", "Вторичная кнопка (нажатие)"),
        ("TreeHoverColor", "Дерево (наведение)"),
        ("TreeSelectedColor", "Дерево (выбранный)"),
        ("ScrollTrackBrush", "Полоса прокрутки (трек)"),
        ("ScrollThumbBrush", "Полоса прокрутки (бегунок)"),
        ("ScrollThumbHoverBrush", "Полоса прокрутки (наведение)"),
        ("ScrollThumbPressedBrush", "Полоса прокрутки (нажатие)")
    };

    /// <summary>Возвращает подпись для ключа цвета (если ключ неизвестен — сам ключ).</summary>
    public static string GetLabel(string key)
    {
        foreach (var (k, label) in Definitions)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return label;
        }
        return key;
    }

    /// <summary>Создаёт копию схемы (независимый набор цветов).</summary>
    public ColorScheme Clone() => new()
    {
        Name = Name,
        IsDark = IsDark,
        Colors = new Dictionary<string, string>(Colors, StringComparer.Ordinal)
    };

    /// <summary>Создаёт встроенную светлую схему, соответствующую LightTheme.xaml.</summary>
    public static ColorScheme CreateLight() => Create("Светлая", false);

    /// <summary>Создаёт встроенную тёмную схему, соответствующую DarkTheme.xaml.</summary>
    public static ColorScheme CreateDark() => Create("Тёмная", true);

    /// <summary>Создаёт схему с цветами по умолчанию для указанной базовой темы.</summary>
    public static ColorScheme Create(string name, bool isDark)
    {
        var light = new (string Key, string Value)[]
        {
            ("AccentColor", "#FDBF00"),
            ("AccentHoverColor", "#E0A800"),
            ("AccentPressedColor", "#C49400"),
            ("SidebarColor", "#1E293B"),
            ("SidebarHoverColor", "#273549"),
            ("SidebarSelectedColor", "#FDBF00"),
            ("ContentBackgroundColor", "#F1F5F9"),
            ("CardBackgroundColor", "#FFFFFF"),
            ("BorderColor", "#E2E8F0"),
            ("TextPrimaryColor", "#000000"),
            ("TextSecondaryColor", "#64748B"),
            ("TextOnAccentColor", "#FFFFFF"),
            ("ButtonTextColor", "#000000"),
            ("FavoriteColor", "#F59E0B"),
            ("ItemHoverColor", "#FFF3CD"),
            ("ItemSelectedColor", "#FFE69C"),
            ("AvatarBackgroundColor", "#FFF3CD"),
            ("AvatarTextColor", "#8A6D00"),
            ("SecondaryButtonBackgroundColor", "#FFF3CD"),
            ("SecondaryButtonHoverColor", "#FFE69C"),
            ("SecondaryButtonPressedColor", "#FFD54D"),
            ("TreeHoverColor", "#FFF9E6"),
            ("TreeSelectedColor", "#FFD54D"),
            ("ScrollTrackBrush", "#E8EDF3"),
            ("ScrollThumbBrush", "#B6C2D2"),
            ("ScrollThumbHoverBrush", "#94A3B8"),
            ("ScrollThumbPressedBrush", "#7C8BA0")
        };

        var dark = new (string Key, string Value)[]
        {
            ("AccentColor", "#FFB300"),
            ("AccentHoverColor", "#FFCA28"),
            ("AccentPressedColor", "#FF8F00"),
            ("SidebarColor", "#111827"),
            ("SidebarHoverColor", "#1F2937"),
            ("SidebarSelectedColor", "#FFB300"),
            ("ContentBackgroundColor", "#0F172A"),
            ("CardBackgroundColor", "#1E293B"),
            ("BorderColor", "#334155"),
            ("TextPrimaryColor", "#F1F5F9"),
            ("TextSecondaryColor", "#CBD5E1"),
            ("TextOnAccentColor", "#FFFFFF"),
            ("ButtonTextColor", "#000000"),
            ("FavoriteColor", "#FBBF24"),
            ("ItemHoverColor", "#334155"),
            ("ItemSelectedColor", "#1E3A5F"),
            ("AvatarBackgroundColor", "#1E3A5F"),
            ("AvatarTextColor", "#FFB300"),
            ("SecondaryButtonBackgroundColor", "#FFF3CD"),
            ("SecondaryButtonHoverColor", "#FFE69C"),
            ("SecondaryButtonPressedColor", "#FFD54D"),
            ("TreeHoverColor", "#334155"),
            ("TreeSelectedColor", "#B45309"),
            ("ScrollTrackBrush", "#16202E"),
            ("ScrollThumbBrush", "#3B4A5F"),
            ("ScrollThumbHoverBrush", "#52657F"),
            ("ScrollThumbPressedBrush", "#6B80A0")
        };

        var scheme = new ColorScheme
        {
            Name = name,
            IsDark = isDark
        };
        foreach (var (key, value) in isDark ? dark : light)
            scheme.Colors[key] = value;
        return scheme;
    }

    /// <summary>Возвращает значение цвета по ключу (или значение по умолчанию, если ключа нет).</summary>
    public string Get(string key)
    {
        return Colors.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase)
                ? (IsDark ? "#3B4A5F" : "#B6C2D2")
                : (IsDark ? "#FFB300" : "#FDBF00");
    }

    // ---- Сериализация схемы в JSON ----

    /// <summary>Сериализует схему в JSON-строку.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Десериализует схему из JSON-строки. При ошибке возвращает null.</summary>
    public static ColorScheme? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ColorScheme>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}