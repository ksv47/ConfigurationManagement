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

    /// <summary>Показывать пустые группы (без информационных баз) в дереве.</summary>
    public bool ShowEmptyGroups { get; set; } = false;

    /// <summary>Цвет фона заголовка узла «Без группы» (в формате #RRGGBB).</summary>
    public string NoGroupColor { get; set; } = "#2D6CDF";

    /// <summary>Цвет иконки узла «Без группы» (в формате #RRGGBB).</summary>
    public string NoGroupIconColor { get; set; } = "#FFFFFF";

    /// <summary>Ключ иконки узла «Без группы» (имя Geometry из Icons.xaml, пусто — по умолчанию).</summary>
    public string NoGroupIcon { get; set; } = string.Empty;

    /// <summary>Название выбранной темы оформления.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>Имена групп, свёрнутых в списке баз.</summary>
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>Список установленных версий платформы 1С.</summary>
    public List<string> InstalledPlatformVersions { get; set; } = new();

    /// <summary>
    /// Дополнительные пути к каталогам установки платформы 1С
    /// (помимо стандартных Program Files и Program Files (x86)).
    /// Пользователь может указать нестандартные/портативные установки.
    /// </summary>
    public List<string> AdditionalPlatformSearchPaths { get; set; } = new();

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

    /// <summary>Показывать колонку «Конфигурация» (название и версия) в списке баз.</summary>
    public bool ShowConfigurationColumn { get; set; } = true;

    /// <summary>Ширина колонки «Конфигурация» (0 — по умолчанию).</summary>
    public double ConfigurationColumnWidth { get; set; }

    /// <summary>Показывать колонку «Режим запуска» в списке баз.</summary>
    public bool ShowLaunchModeColumn { get; set; } = true;

    /// <summary>Показывать колонку «Сервер/База» в списке баз.</summary>
    public bool ShowServerColumn { get; set; } = true;

    /// <summary>Показывать колонку «Последний запуск» в списке баз.</summary>
    public bool ShowLastLaunchColumn { get; set; } = true;

    /// <summary>Показывать колонку «Размер» (файловые ИБ) в списке баз.</summary>
    public bool ShowSizeColumn { get; set; } = true;

    /// <summary>Ширина колонки «Размер» (0 — по умолчанию).</summary>
    public double SizeColumnWidth { get; set; }

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
    /// Запоминать размер, позицию, состояние окна и монитор, на котором оно было
    /// закрыто, и восстанавливать их при следующем запуске.
    /// </summary>
    public bool RememberWindowLayout { get; set; } = true;

    /// <summary>
    /// При закрытии окна сворачивать приложение в системный трей вместо выхода.
    /// </summary>
    public bool CloseToTray { get; set; }

    /// <summary>Показывать значок приложения в системном трее.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Горячая клавиша запуска «1С:Предприятие» (например F3). Пусто — не назначена.</summary>
    public string HotkeyEnterprise { get; set; } = "F3";

    /// <summary>Горячая клавиша запуска «Конфигуратор» (например F4).</summary>
    public string HotkeyConfigurator { get; set; } = "F4";

    /// <summary>Горячая клавиша «Избранное» (например F8).</summary>
    public string HotkeyFavorite { get; set; } = "F8";

    /// <summary>Горячая клавиша «Изменить» (например F2).</summary>
    public string HotkeyEdit { get; set; } = "F2";

    /// <summary>Горячая клавиша «Удалить» (например Delete).</summary>
    public string HotkeyDelete { get; set; } = "Delete";

    /// <summary>Горячая клавиша «Очистить кэш».</summary>
    public string HotkeyClearCache { get; set; } = "";

    /// <summary>Горячая клавиша «Добавить базу» (например Insert).</summary>
    public string HotkeyAdd { get; set; } = "Insert";

    /// <summary>Горячая клавиша «Закрепить».</summary>
    public string HotkeyPin { get; set; } = "";

    /// <summary>Горячая клавиша показа вкладки «Все базы». Пусто — не назначена.</summary>
    public string HotkeyShowAll { get; set; } = "";

    /// <summary>Горячая клавиша показа вкладки «Избранное». Пусто — не назначена.</summary>
    public string HotkeyShowFavorites { get; set; } = "";

    /// <summary>Горячая клавиша показа вкладки «Недавние». Пусто — не назначена.</summary>
    public string HotkeyShowRecent { get; set; } = "";

    /// <summary>
    /// Поле сортировки списка баз: Name (по умолчанию), LastLaunchDate, SortOrder.
    /// </summary>
    public string SortField { get; set; } = "Name";

    /// <summary>Направление сортировки: true — по возрастанию, false — по убыванию.</summary>
    public bool SortAscending { get; set; } = true;

    /// <summary>
    /// Идентификатор последней выбранной информационной базы (восстанавливается при запуске).
    /// Пусто — база не была выбрана.
    /// </summary>
    public string LastSelectedInfobaseId { get; set; } = string.Empty;

    /// <summary>
    /// Полный путь последней выбранной группы (восстанавливается при запуске).
    /// Пусто — группа не была выбрана.
    /// </summary>
    public string LastSelectedGroupPath { get; set; } = string.Empty;

    /// <summary>
    /// Упорядоченный список идентификаторов избранных баз для горячих клавиш Alt+1…Alt+9.
    /// Индекс 0 → Alt+1, индекс 1 → Alt+2 и т.д. (максимум 9).
    /// </summary>
    public List<string> FavoriteHotkeyIds { get; set; } = new();

    /// <summary>
    /// Показывать подробности в правой панели (имя, подключение, теги).
    /// false — компактный режим: только кнопки действий.
    /// </summary>
    public bool ShowRightPanelDetails { get; set; } = true;

    /// <summary>
    /// Показывать блок «Текущая сессия» (режим клиента и разрядность) в правой панели.
    /// Работает и в полном, и в компактном режиме панели.
    /// </summary>
    public bool ShowSessionLaunchPanel { get; set; } = true;

    /// <summary>Сохранённый режим клиента «текущей сессии» (Auto / Ordinary / Thick / Thin).</summary>
    public string SessionClientMode { get; set; } = "Auto";

    /// <summary>Сохранённая разрядность «текущей сессии» (Auto / X86 / X64).</summary>
    public string SessionArchitecture { get; set; } = "Auto";

    /// <summary>
    /// Разрядность по умолчанию (X86 / X64), используемая при запуске, когда
    /// у информационной базы не указана собственная разрядность.
    /// </summary>
    public string DefaultArchitecture { get; set; } = "X64";

    /// <summary>
    /// Каталоги шаблонов конфигураций (как в стартере 1С).
    /// Пустой список — использовать пути, настроенные в 1С / по умолчанию.
    /// </summary>
    public List<string> TemplateCatalogPaths { get; set; } = new();

    /// <summary>
    /// При Esc сворачивать главное окно в трей (нужен включённый значок в трее).
    /// </summary>
    public bool EscapeToTray { get; set; } = true;

    /// <summary>В нижней панели показывать путь / строку подключения.</summary>
    public bool StatusShowConnectionPath { get; set; } = true;

    /// <summary>В нижней панели показывать разрядность (32/64).</summary>
    public bool StatusShowArchitecture { get; set; } = true;

    /// <summary>В нижней панели показывать режим запуска.</summary>
    public bool StatusShowLaunchMode { get; set; } = true;

    /// <summary>В нижней панели показывать порт сервера.</summary>
    public bool StatusShowPort { get; set; } = true;

    /// <summary>В нижней панели показывать версию платформы.</summary>
    public bool StatusShowPlatformVersion { get; set; } = true;

    /// <summary>В нижней панели показывать тип клиента.</summary>
    public bool StatusShowClientType { get; set; }

    /// <summary>В нижней панели показывать тип подключения.</summary>
    public bool StatusShowConnectionType { get; set; }

    /// <summary>В нижней панели показывать имя пользователя подключения.</summary>
    public bool StatusShowUser { get; set; }

    /// <summary>В нижней панели показывать ID информационной базы.</summary>
    public bool StatusShowId { get; set; }

    /// <summary>
    /// Добавлять дату-время к имени файла при выгрузке (экспорт списка баз в JSON,
    /// выгрузка ИБ в .dt, выгрузка конфигурации в .cf). По умолчанию — включено.
    /// </summary>
    public bool AddTimestampToExportFileName { get; set; } = true;

    /// <summary>
    /// Шаблон (формат) отметки даты и времени для имени файла при выгрузке.
    /// По умолчанию — «yyyyMMdd_HHmmss» (например «20260819_074312»).
    /// Применяется только когда <see cref="AddTimestampToExportFileName"/> включён.
    /// </summary>
    public string ExportTimestampFormat { get; set; } = "yyyyMMdd_HHmmss";

}
