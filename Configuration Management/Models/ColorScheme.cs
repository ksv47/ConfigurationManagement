using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration_Management.Localization;

namespace Configuration_Management.Models;

/// <summary>
/// Цветовая схема (тема оформления) приложения: именованный набор из двух палитр —
/// для светлого (<see cref="LightColors"/>) и тёмного (<see cref="DarkColors"/>) режима.
/// Накладывается поверх базовой темы. Поддерживает выгрузку/загрузку в JSON-файл,
/// а также создание собственных тем.
/// </summary>
public class ColorScheme
{
    /// <summary>Название схемы (темы).</summary>
    public string Name { get; set; } = "Light";

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDark { get; set; }

    /// <summary>
    /// Устаревшее поле для чтения старых JSON-файлов (миграция в <see cref="Normalize"/>).
    /// В новые файлы не записывается.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>
    /// Палитра для светлого режима: ключ ресурса (имя Color-ресурса из темы, напр.
    /// <c>AccentColor</c>, либо имя кисти для ресурсов без отдельного цвета, напр.
    /// <c>ScrollThumbBrush</c>) → значение в формате #RRGGBB.
    /// </summary>
    public Dictionary<string, string> LightColors { get; set; } = new();

    /// <summary>Палитра для тёмного режима (тот же формат ключей, что у <see cref="LightColors"/>).</summary>
    public Dictionary<string, string> DarkColors { get; set; } = new();

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

    /// <summary>Возвращает активную палитру (светлую или тёмную) по варианту темы.</summary>
    public Dictionary<string, string> Palette(bool dark)
        => dark ? DarkColors : LightColors;

    /// <summary>
    /// Возвращает значение цвета указанной палитры по ключу. Если ключа нет — значение
    /// по умолчанию для этого варианта (для неузнаваемых ключей — нейтральный запасной).
    /// </summary>
    public string PaletteValue(bool dark, string key)
    {
        var dict = Palette(dark);
        if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        var defaults = dark ? DefaultDarkPalette() : DefaultLightPalette();
        if (defaults.TryGetValue(key, out var def) && !string.IsNullOrWhiteSpace(def))
            return def;

        return key.EndsWith("Brush", StringComparison.OrdinalIgnoreCase)
            ? (dark ? "#3B4A5F" : "#B6C2D2")
            : (dark ? "#FFB300" : "#FDBF00");
    }

    /// <summary>
    /// Приводит схему к актуальному виду: переносит устаревшую одиночную палитру
    /// (<see cref="Colors"/>/<see cref="IsDark"/>) в соответствующее поле и заполняет
    /// отсутствующую палитру значениями по умолчанию. Вызывается при загрузке из JSON
    /// и перед применением схемы.
    /// </summary>
    public void Normalize()
    {
        if (Colors is { Count: > 0 })
        {
            var target = Palette(IsDark);
            var other = Palette(!IsDark);
            if (target.Count == 0)
            {
                foreach (var kvp in Colors)
                    target[kvp.Key] = kvp.Value;
            }
            if (other.Count == 0)
            {
                foreach (var kvp in (IsDark ? DefaultLightPalette() : DefaultDarkPalette()))
                    other[kvp.Key] = kvp.Value;
            }
            // Устаревшие поля больше не нужны.
            Colors = null;
        }

        FillMissing(LightColors, DefaultLightPalette());
        FillMissing(DarkColors, DefaultDarkPalette());
    }

    private static void FillMissing(Dictionary<string, string> target, Dictionary<string, string> defaults)
    {
        foreach (var kvp in defaults)
        {
            if (!target.ContainsKey(kvp.Key))
                target[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>Создаёт копию схемы (независимые наборы цветов обеих палитр).</summary>
    public ColorScheme Clone() => new()
    {
        Name = Name,
        IsDark = IsDark,
        Colors = Colors is null ? null : new Dictionary<string, string>(Colors, StringComparer.Ordinal),
        LightColors = new Dictionary<string, string>(LightColors, StringComparer.Ordinal),
        DarkColors = new Dictionary<string, string>(DarkColors, StringComparer.Ordinal)
    };

    /// <summary>
    /// Собирает единую схему с двумя палитрами из устаревших полей настроек:
    /// старого одиночного <paramref name="active"/> и/или раздельных слотов
    /// <paramref name="light"/>/<paramref name="dark"/>. Возвращает нормализованную
    /// схему, готовую к применению и сохранению в новом формате.
    /// </summary>
    public static ColorScheme FromLegacy(ColorScheme? active, ColorScheme? light, ColorScheme? dark)
    {
        var hasSlots = light is not null || dark is not null;
        var source = hasSlots ? null : active;
        var merged = source?.Clone() ?? CreateLight();
        merged.Name = source?.Name ?? light?.Name ?? dark?.Name ?? "Светлая";

        if (hasSlots)
        {
            if (light is not null)
            {
                light.Normalize();
                merged.LightColors = new Dictionary<string, string>(light.Palette(false), StringComparer.Ordinal);
                if (light.DarkColors.Count > 0)
                    merged.DarkColors = new Dictionary<string, string>(light.DarkColors, StringComparer.Ordinal);
            }
            if (dark is not null)
            {
                dark.Normalize();
                merged.DarkColors = new Dictionary<string, string>(dark.Palette(true), StringComparer.Ordinal);
                if (dark.LightColors.Count > 0)
                    merged.LightColors = new Dictionary<string, string>(dark.LightColors, StringComparer.Ordinal);
            }
        }

        merged.Normalize();
        return merged;
    }

    /// <summary>Создаёт встроенную схему «Светлая» (несёт обе палитры).</summary>
    public static ColorScheme CreateLight() => Create("Светлая", false);

    /// <summary>Создаёт встроенную схему «Тёмная» (несёт обе палитры).</summary>
    public static ColorScheme CreateDark() => Create("Тёмная", true);

    /// <summary>
    /// Создаёт схему с палитрами по умолчанию для обоих режимов. Параметр <paramref name="isDark"/>
    /// сохраняется только в устаревшем поле <see cref="IsDark"/> (для совместимости); сама схема
    /// всегда содержит и светлую, и тёмную палитру.
    /// </summary>
    public static ColorScheme Create(string name, bool isDark)
    {
        var scheme = new ColorScheme { Name = name, IsDark = isDark };
        foreach (var (key, value) in DefaultLightPalette())
            scheme.LightColors[key] = value;
        foreach (var (key, value) in DefaultDarkPalette())
            scheme.DarkColors[key] = value;
        return scheme;
    }

    /// <summary>Палитра по умолчанию для светлого режима (соответствует LightTheme).</summary>
    private static Dictionary<string, string> DefaultLightPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FDBF00",
        ["AccentHoverColor"] = "#E0A800",
        ["AccentPressedColor"] = "#C49400",
        ["SidebarColor"] = "#1E293B",
        ["SidebarHoverColor"] = "#273549",
        ["SidebarSelectedColor"] = "#FDBF00",
        ["ContentBackgroundColor"] = "#F1F5F9",
        ["CardBackgroundColor"] = "#FFFFFF",
        ["BorderColor"] = "#E2E8F0",
        ["TextPrimaryColor"] = "#000000",
        ["TextSecondaryColor"] = "#64748B",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        ["FavoriteColor"] = "#F59E0B",
        ["ItemHoverColor"] = "#FFF3CD",
        ["ItemSelectedColor"] = "#FFE69C",
        ["AvatarBackgroundColor"] = "#FFF3CD",
        ["AvatarTextColor"] = "#8A6D00",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#FFF9E6",
        ["TreeSelectedColor"] = "#FFD54D",
        ["ScrollTrackBrush"] = "#E8EDF3",
        ["ScrollThumbBrush"] = "#B6C2D2",
        ["ScrollThumbHoverBrush"] = "#94A3B8",
        ["ScrollThumbPressedBrush"] = "#7C8BA0"
    };

    /// <summary>Палитра по умолчанию для тёмного режима (соответствует DarkTheme).</summary>
    private static Dictionary<string, string> DefaultDarkPalette() => new(StringComparer.Ordinal)
    {
        ["AccentColor"] = "#FFB300",
        ["AccentHoverColor"] = "#FFCA28",
        ["AccentPressedColor"] = "#FF8F00",
        ["SidebarColor"] = "#111827",
        ["SidebarHoverColor"] = "#1F2937",
        ["SidebarSelectedColor"] = "#FFB300",
        ["ContentBackgroundColor"] = "#0F172A",
        ["CardBackgroundColor"] = "#1E293B",
        ["BorderColor"] = "#334155",
        ["TextPrimaryColor"] = "#F1F5F9",
        ["TextSecondaryColor"] = "#CBD5E1",
        ["TextOnAccentColor"] = "#FFFFFF",
        ["ButtonTextColor"] = "#000000",
        ["FavoriteColor"] = "#FBBF24",
        ["ItemHoverColor"] = "#334155",
        ["ItemSelectedColor"] = "#1E3A5F",
        ["AvatarBackgroundColor"] = "#1E3A5F",
        ["AvatarTextColor"] = "#FFB300",
        ["SecondaryButtonBackgroundColor"] = "#FFF3CD",
        ["SecondaryButtonHoverColor"] = "#FFE69C",
        ["SecondaryButtonPressedColor"] = "#FFD54D",
        ["TreeHoverColor"] = "#334155",
        ["TreeSelectedColor"] = "#B45309",
        ["ScrollTrackBrush"] = "#16202E",
        ["ScrollThumbBrush"] = "#3B4A5F",
        ["ScrollThumbHoverBrush"] = "#52657F",
        ["ScrollThumbPressedBrush"] = "#6B80A0"
    };

    // ---- Сериализация схемы в JSON ----

    /// <summary>Сериализует схему в JSON-строку (пишутся только <see cref="Name"/> и обе палитры).</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Десериализует схему из JSON-строки. При ошибке возвращает null.</summary>
    public static ColorScheme? FromJson(string json)
    {
        try
        {
            var scheme = JsonSerializer.Deserialize<ColorScheme>(json, JsonOptions);
            scheme?.Normalize();
            return scheme;
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