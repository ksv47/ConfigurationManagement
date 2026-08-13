namespace Configuration_Management.Models;

/// <summary>
/// Настройки интерфейса приложения, сохраняемые между запусками.
/// </summary>
public class AppSettings
{
    /// <summary>Показывать только избранные базы.</summary>
    public bool ShowFavoritesOnly { get; set; }

    /// <summary>Группировать базы по группам.</summary>
    public bool GroupByGroup { get; set; } = true;

    /// <summary>Название выбранной темы оформления.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Имена групп, свёрнутых в списке баз.</summary>
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Список установленных версий платформы 1С.</summary>
    public List<string> InstalledPlatformVersions { get; set; } = new();

    /// <summary>Режим синхронизации с файлом ibases.v8i.</summary>
    public IbasesSyncMode IbasesSyncMode { get; set; } = IbasesSyncMode.None;

    /// <summary>Путь к файлу ibases.v8i для синхронизации (пусто — стандартный путь).</summary>
    public string IbasesSyncFilePath { get; set; } = string.Empty;

    /// <summary>Момент запуска автоматической синхронизации (по умолчанию — при запуске).</summary>
    public IbasesSyncTrigger IbasesSyncTrigger { get; set; } = IbasesSyncTrigger.OnStartup;

    /// <summary>Интервал автоматической синхронизации в минутах (для режима Interval).</summary>
    public int IbasesSyncIntervalMinutes { get; set; } = 30;

    /// <summary>Время автоматической синхронизации по расписанию в формате "HH:mm" (для режима Schedule).</summary>
    public string IbasesSyncScheduleTime { get; set; } = "09:00";

    /// <summary>
    /// Создавать резервную копию файла ibases.v8i перед синхронизацией (экспортом/записью).
    /// </summary>
    public bool IbasesBackupEnabled { get; set; } = true;

    /// <summary>
    /// Сколько последних резервных копий ibases.v8i хранить (старые удаляются).
    /// </summary>
    public int IbasesBackupKeepCount { get; set; } = 5;

    /// <summary>Ширина колонки «Название» в списке баз (0 — по умолчанию).</summary>
    public double NameColumnWidth { get; set; }

    /// <summary>Ширина колонки «Версия платформы» в списке баз (0 — по умолчанию).</summary>
    public double VersionColumnWidth { get; set; }

    /// <summary>Ширина колонки «Режим запуска» в списке баз (0 — по умолчанию).</summary>
    public double LaunchModeColumnWidth { get; set; }

    /// <summary>Ширина колонки «Сервер/База» в списке баз (0 — по умолчанию).</summary>
    public double ServerColumnWidth { get; set; }

    /// <summary>Ширина колонки «Последний запуск» в списке баз (0 — по умолчанию).</summary>
    public double LastLaunchColumnWidth { get; set; }

    /// <summary>Показывать колонку-кнопку «Избранное» (★) в списке баз.</summary>
    public bool ShowFavoritesButton { get; set; } = true;

    /// <summary>Показывать колонку-кнопку «Закрепить» (📌) в списке баз.</summary>
    public bool ShowPinnedButton { get; set; } = true;

    /// <summary>Показывать теги баз в списке.</summary>
    public bool ShowTags { get; set; } = true;

    /// <summary>Показывать панель быстрого отбора по тегам над списком баз.</summary>
    public bool ShowTagFilterPanel { get; set; } = true;

    /// <summary>
    /// Разрешить запуск нескольких экземпляров приложения.
    /// false — при повторном запуске активируется уже открытое окно.
    /// </summary>
    public bool AllowMultipleInstances { get; set; }

    /// <summary>Показывать колонку «Версия платформы» в списке баз.</summary>
    public bool ShowVersionColumn { get; set; } = true;

    /// <summary>Показывать колонку «Режим запуска» в списке баз.</summary>
    public bool ShowLaunchModeColumn { get; set; } = true;

    /// <summary>Показывать колонку «Сервер/База» в списке баз.</summary>
    public bool ShowServerColumn { get; set; } = true;

    /// <summary>Показывать колонку «Последний запуск» в списке баз.</summary>
    public bool ShowLastLaunchColumn { get; set; } = true;

    /// <summary>Сохранённая ширина окна приложения (0 — по умолчанию).</summary>
    public double WindowWidth { get; set; }

    /// <summary>Сохранённая высота окна приложения (0 — по умолчанию).</summary>
    public double WindowHeight { get; set; }

    /// <summary>Сохранённая позиция окна по горизонтали (0 — по центру экрана).</summary>
    public double WindowLeft { get; set; }

    /// <summary>Сохранённая позиция окна по вертикали (0 — по центру экрана).</summary>
    public double WindowTop { get; set; }

    /// <summary>Состояние окна приложения (Normal, Maximized, Minimized).</summary>
    public string WindowState { get; set; } = string.Empty;

    /// <summary>
    /// При закрытии окна сворачивать приложение в системный трей вместо выхода.
    /// </summary>
    public bool CloseToTray { get; set; }

    /// <summary>Показывать значок приложения в системном трее.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Горячая клавиша запуска «1С:Предприятие» (например F3).</summary>
    public string HotkeyEnterprise { get; set; } = "F3";

    /// <summary>Горячая клавиша запуска «Конфигуратор» (например F4).</summary>
    public string HotkeyConfigurator { get; set; } = "F4";

    /// <summary>
    /// Поле сортировки списка баз: Name (по умолчанию), LastLaunchDate, SortOrder.
    /// </summary>
    public string SortField { get; set; } = "Name";

    /// <summary>Направление сортировки: true — по возрастанию, false — по убыванию.</summary>
    public bool SortAscending { get; set; } = true;

    /// <summary>
    /// Упорядоченный список идентификаторов избранных баз для горячих клавиш Alt+1…Alt+9.
    /// Индекс 0 → Alt+1, индекс 1 → Alt+2 и т.д. (максимум 9).
    /// </summary>
    public List<string> FavoriteHotkeyIds { get; set; } = new();
}