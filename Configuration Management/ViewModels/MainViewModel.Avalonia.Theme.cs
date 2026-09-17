#if LINUX
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Themes;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): тема и цветовые схемы (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    public string ThemeName
    {
        get => _themeName;
        set => SetProperty(ref _themeName, value);
    }

    /// <summary>
    /// Запоминает выбранную цветовую схему как активную и сохраняет её в слот
    /// соответствующей базовой темы (светлой/тёмной), чтобы переключение тем
    /// не сбрасывало настроенное оформление.
    /// </summary>
    public void ApplyColorScheme(Models.ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _settings.ActiveColorScheme = clone;
        // Устаревшие раздельные слоты больше не ведутся: схема едина и несёт обе палитры.
        _settings.LightColorScheme = null;
        _settings.DarkColorScheme = null;
        // Применяем палитру по текущему варианту темы; сам вариант не меняем.
        ThemeManager.ApplyScheme(clone);
        // Общий цвет папок хранится в схеме и применяется при построении дерева —
        // пересобираем его, чтобы папки сразу перекрасились.
        RebuildTree();
        SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
    }

    /// <summary>true, если имя соответствует встроенной теме («Светлая»/«Тёмная»).</summary>
    private static bool IsBuiltInSchemeName(string? name)
        => string.Equals(name, "Светлая", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "Тёмная", StringComparison.OrdinalIgnoreCase);

    /// <summary>Сохранённая цветовая схема (две палитры): окно настроек открывает редактор
    /// с неё, а не с той, что применена предпросмотром.</summary>
    public Models.ColorScheme ActiveColorScheme
    {
        get
        {
            var saved = _settings.ActiveColorScheme;
            if (saved is not null && (saved.LightColors.Count > 0 || saved.DarkColors.Count > 0))
            {
                saved.Normalize();
                return saved;
            }
            return ThemeManager.GetBuiltInScheme(_settings.Theme) ?? Models.ColorScheme.CreateLight();
        }
    }

    private void ToggleTheme()
    {
        // Схема одна (несёт обе палитры): переключение темы лишь выбирает палитру.
        ApplySchemeForTheme(ThemeManager.CurrentTheme != ThemeManager.DarkThemeName);
    }

    /// <summary>Применяет выбранный вариант темы (светлую/тёмную), не меняя схему.</summary>
    public void ApplyTheme(string theme)
    {
        ApplySchemeForTheme(theme == ThemeManager.DarkThemeName);
    }

    /// <summary>
    /// Возвращает активную схему (с двумя палитрами). Вариант темы определяет,
    /// какая палитра показывается; сама схема от него не зависит.
    /// </summary>
    public Models.ColorScheme GetSchemeForTheme(string theme)
        => ActiveColorScheme.Clone();

    /// <summary>
    /// Задаёт вариант темы (светлую/тёмную) и применяет активную схему с палитрой
    /// этого варианта.
    /// </summary>
    private void ApplySchemeForTheme(bool dark)
    {
        ThemeManager.ApplyTheme(dark);
        // Общий цвет папок берётся из активной палитры схемы при построении дерева.
        RebuildTree();
        _settings.Theme = dark ? ThemeManager.DarkThemeName : ThemeManager.LightThemeName;
        _themeName = _settings.Theme;
        SaveSettingsSilently();
        OnPropertyChanged(nameof(ThemeName));
    }

    private static bool IsDarkTheme(string? theme)
        => string.Equals(theme, ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase);
}
#endif