using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration_Management.Localization;

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
    /// Ключи ресурсов цветов и соответствующие им ключи локализации подписей
    /// для редактора в настройках. Технический ключ ресурса (первый элемент)
    /// хранится и сравнивается — его НЕ переводим; переводится только подпись.
    /// </summary>
    private static readonly (string Key, string LabelKey)[] _definitionKeys = new (string, string)[]
    {
        ("AccentColor", "Color.Accent"),
        ("AccentHoverColor", "Color.AccentHover"),
        ("AccentPressedColor", "Color.AccentPressed"),
        ("SidebarColor", "Color.Sidebar"),
        ("SidebarHoverColor", "Color.SidebarHover"),
        ("SidebarSelectedColor", "Color.SidebarSelected"),
        ("ContentBackgroundColor", "Color.ContentBackground"),
        ("CardBackgroundColor", "Color.CardBackground"),
        ("BorderColor", "Color.Border"),
        ("TextPrimaryColor", "Color.TextPrimary"),
        ("TextSecondaryColor", "Color.TextSecondary"),
        ("TextOnAccentColor", "Color.TextOnAccent"),
        ("ButtonTextColor", "Color.ButtonText"),
        ("FavoriteColor", "Color.Favorite"),
        ("ItemHoverColor", "Color.ItemHover"),
        ("ItemSelectedColor", "Color.ItemSelected"),
        ("AvatarBackgroundColor", "Color.AvatarBackground"),
        ("AvatarTextColor", "Color.AvatarText"),
        ("SecondaryButtonBackgroundColor", "Color.SecondaryButtonBackground"),
        ("SecondaryButtonHoverColor", "Color.SecondaryButtonHover"),
        ("SecondaryButtonPressedColor", "Color.SecondaryButtonPressed"),
        ("TreeHoverColor", "Color.TreeHover"),
        ("TreeSelectedColor", "Color.TreeSelected"),
        ("ScrollTrackBrush", "Color.ScrollTrack"),
        ("ScrollThumbBrush", "Color.ScrollThumb"),
        ("ScrollThumbHoverBrush", "Color.ScrollThumbHover"),
        ("ScrollThumbPressedBrush", "Color.ScrollThumbPressed")
    };

    /// <summary>
    /// Возвращает упорядоченное описание редактируемых цветов:
    /// ключ ресурса и локализованная человекочитаемая подпись для редактора в настройках.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> Definitions =>
        _definitionKeys.Select(d => (d.Key, LocalizationManager.T(d.LabelKey))).ToArray();

    /// <summary>Возвращает локализованную подпись для ключа цвета (если ключ неизвестен — сам ключ).</summary>
    public static string GetLabel(string key)
    {
        foreach (var (k, labelKey) in _definitionKeys)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return LocalizationManager.T(labelKey);
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