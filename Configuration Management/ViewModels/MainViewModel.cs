using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using Microsoft.Win32;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Главная ViewModel приложения «Управление конфигурациями 1С».
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly IInfobaseRepository _repository;
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _logger;
    private readonly IOneCLauncher _launcher;
    private readonly IIbasesSyncService _ibasesSync;
    private Infobase? _selectedInfobase;
    private string _searchText = string.Empty;
    private bool _showFavoritesOnly;
    private bool _groupByGroup = true;
    private string _savedTheme = string.Empty;
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _installedPlatformVersions = new();
    private List<string> _additionalPlatformSearchPaths = new();
    private double _nameColumnWidth;
    private double _versionColumnWidth;
    private double _launchModeColumnWidth;
    private double _serverColumnWidth;
    private double _lastLaunchColumnWidth;
    private bool _showFavoritesButton = true;
    private bool _showPinnedButton = true;
    private bool _showTagFilterPanel;
    private bool _allowMultipleInstances;
    private readonly ObservableCollection<string> _activeTagFilters = new();
    private ListViewMode _listViewMode = ListViewMode.All;

    private bool _showTags = true;
    private bool _showVersionColumn = true;
    private bool _showRightPanelDetails = true;
    private bool _statusShowConnectionPath = true;
    private bool _statusShowArchitecture = true;
    private bool _statusShowLaunchMode = true;

    /// <summary>Переопределение типа клиента для текущего запуска (не сохраняется в базу).</summary>
    private SessionClientMode _sessionClientMode = SessionClientMode.Auto;
    /// <summary>Переопределение разрядности для текущего запуска (не сохраняется в базу).</summary>
    private SessionArchitectureMode _sessionArchitecture = SessionArchitectureMode.Auto;
    /// <summary>Показывать блок «Текущая сессия» в правой панели.</summary>
    private bool _showSessionLaunchPanel = true;
    private bool _statusShowPort = true;
    private bool _statusShowPlatformVersion = true;
    private bool _statusShowClientType;
    private bool _statusShowConnectionType;
    private bool _statusShowUser;
    private bool _statusShowId;
    private bool _showLaunchModeColumn = true;
    private bool _showServerColumn = true;
    private bool _showLastLaunchColumn = true;
    private bool _showSizeColumn = true;
    private double _sizeColumnWidth;
    private double _windowWidth;
    private double _windowHeight;
    private double _windowLeft;
    private double _windowTop;
    /// <summary>Отмена предыдущего отложенного сохранения (debounce).</summary>
    private CancellationTokenSource? _saveDebounceCts;
    private const int SaveDebounceMs = 400;
    private string _windowState = string.Empty;
    private IbasesSyncMode _ibasesSyncMode = IbasesSyncMode.None;
    private string _ibasesSyncFilePath = string.Empty;
    private IbasesSyncTrigger _ibasesSyncTrigger = IbasesSyncTrigger.OnStartup;
    private int _ibasesSyncIntervalMinutes = 30;
    private string _ibasesSyncScheduleTime = "09:00";
    private bool _ibasesBackupEnabled = true;
    private int _ibasesBackupKeepCount = 5;
    private string _syncMessage = string.Empty;
    private DispatcherTimer? _syncTimer;
    private DateTime? _nextScheduleRun;
    private bool _syncTimerRunning;
    private bool _closeToTray;
    private bool _showTrayIcon = true;
    private bool _escapeToTray = true;
    private List<string> _templateCatalogPaths = new();
    private string _hotkeyEnterprise = "F3";
    private string _hotkeyConfigurator = "F4";
    private string _hotkeyFavorite = "F8";
    private string _hotkeyEdit = "F2";
    private string _hotkeyDelete = "Delete";
    private string _hotkeyClearCache = "";
    private string _hotkeyAdd = "Insert";
    private string _hotkeyPin = "";
    private string _sortField = "Name";
    private bool _sortAscending = true;
    private readonly List<string> _favoriteHotkeyIds = new();
    private CancellationTokenSource? _searchDebounceCts;
    private HashSet<string> _activeTagFilterSet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Событие: изменился список избранных с горячими клавишами (нужно перерегистрировать биндинги).</summary>
    public event EventHandler? FavoriteHotkeysChanged;

    /// <summary>
    /// Внутренний набор узлов дерева групп, перестраиваемый при изменении данных.
    /// Заполняется в методе <see cref="RebuildGroupTree"/>.
    /// </summary>
    private List<GroupNodeViewModel> _groupNodes = new();

    public MainViewModel(
        IInfobaseRepository? repository = null,
        IDialogService? dialogs = null,
        IAppLogger? logger = null,
        IOneCLauncher? launcher = null,
        IIbasesSyncService? ibasesSync = null)
    {
        _repository = repository ?? new InfobaseRepository();
        _dialogs = dialogs ?? new WpfDialogService();
        _logger = logger ?? new FileAppLogger();
        _launcher = launcher ?? new OneCLauncherService();
        _ibasesSync = ibasesSync ?? new IbasesSyncService();
        _logger.Info("MainViewModel инициализирован");

        // Загружаем настройки интерфейса (состояние кнопок «Избранные» и «Группировать»).
        var settings = _repository.LoadSettings();
        _showFavoritesOnly = settings.ShowFavoritesOnly;
        _groupByGroup = settings.GroupByGroup;
        _savedTheme = settings.Theme;
        _additionalPlatformSearchPaths = new List<string>(settings.AdditionalPlatformSearchPaths ?? new List<string>());
        PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformSearchPaths);
        // Актуальный список с диска (Program Files + доп. пути, напр. E:\1cPlatform)
        _installedPlatformVersions = PlatformVersionService.FindInstalledVersions(_additionalPlatformSearchPaths);
        if (_installedPlatformVersions.Count == 0 && settings.InstalledPlatformVersions is { Count: > 0 })
            _installedPlatformVersions = new List<string>(settings.InstalledPlatformVersions);
        _nameColumnWidth = settings.NameColumnWidth;
        _versionColumnWidth = settings.VersionColumnWidth;
        _launchModeColumnWidth = settings.LaunchModeColumnWidth;
        _serverColumnWidth = settings.ServerColumnWidth;
        _lastLaunchColumnWidth = settings.LastLaunchColumnWidth;
        _showFavoritesButton = settings.ShowFavoritesButton;
        _showPinnedButton = settings.ShowPinnedButton;
        _showTags = settings.ShowTags;
        _showTagFilterPanel = settings.ShowTagFilterPanel;
        _allowMultipleInstances = settings.AllowMultipleInstances;
        _showVersionColumn = settings.ShowVersionColumn;
        _showRightPanelDetails = settings.ShowRightPanelDetails;
        _showSessionLaunchPanel = settings.ShowSessionLaunchPanel;
        if (Enum.TryParse<SessionClientMode>(settings.SessionClientMode, true, out var scm))
            _sessionClientMode = scm;
        if (Enum.TryParse<SessionArchitectureMode>(settings.SessionArchitecture, true, out var sam))
            _sessionArchitecture = sam;
        _statusShowConnectionPath = settings.StatusShowConnectionPath;
        _statusShowArchitecture = settings.StatusShowArchitecture;
        _statusShowLaunchMode = settings.StatusShowLaunchMode;
        _statusShowPort = settings.StatusShowPort;
        _statusShowPlatformVersion = settings.StatusShowPlatformVersion;
        _statusShowClientType = settings.StatusShowClientType;
        _statusShowConnectionType = settings.StatusShowConnectionType;
        _statusShowUser = settings.StatusShowUser;
        _statusShowId = settings.StatusShowId;
        _showLaunchModeColumn = settings.ShowLaunchModeColumn;
        _showServerColumn = settings.ShowServerColumn;
        _showLastLaunchColumn = settings.ShowLastLaunchColumn;
        _showSizeColumn = settings.ShowSizeColumn;
        _sizeColumnWidth = settings.SizeColumnWidth;
        _windowWidth = settings.WindowWidth;
        _windowHeight = settings.WindowHeight;
        _windowLeft = settings.WindowLeft;
        _windowTop = settings.WindowTop;
        _windowState = settings.WindowState;
        _ibasesSyncMode = settings.IbasesSyncMode;
        _ibasesSyncFilePath = settings.IbasesSyncFilePath;
        _ibasesSyncTrigger = settings.IbasesSyncTrigger;
        _ibasesSyncIntervalMinutes = settings.IbasesSyncIntervalMinutes;
        _ibasesSyncScheduleTime = settings.IbasesSyncScheduleTime;
        _ibasesBackupEnabled = settings.IbasesBackupEnabled;
        _ibasesBackupKeepCount = settings.IbasesBackupKeepCount > 0 ? settings.IbasesBackupKeepCount : 5;
        _closeToTray = settings.CloseToTray;
        _showTrayIcon = settings.ShowTrayIcon;
        _escapeToTray = settings.EscapeToTray;
        _templateCatalogPaths = settings.TemplateCatalogPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();
        OneCTemplateService.SetUserTemplatePaths(_templateCatalogPaths);
        _hotkeyEnterprise = string.IsNullOrWhiteSpace(settings.HotkeyEnterprise) ? "F3" : settings.HotkeyEnterprise.Trim();
        _hotkeyConfigurator = string.IsNullOrWhiteSpace(settings.HotkeyConfigurator) ? "F4" : settings.HotkeyConfigurator.Trim();
        _hotkeyFavorite = settings.HotkeyFavorite?.Trim() ?? "F8";
        _hotkeyEdit = settings.HotkeyEdit?.Trim() ?? "F2";
        _hotkeyDelete = settings.HotkeyDelete?.Trim() ?? "Delete";
        _hotkeyClearCache = settings.HotkeyClearCache?.Trim() ?? "";
        _hotkeyAdd = settings.HotkeyAdd?.Trim() ?? "Insert";
        _hotkeyPin = settings.HotkeyPin?.Trim() ?? "";
        _sortField = string.IsNullOrWhiteSpace(settings.SortField) ? "Name" : settings.SortField;
        _sortAscending = settings.SortAscending;
        if (settings.FavoriteHotkeyIds != null)
        {
            foreach (var id in settings.FavoriteHotkeyIds.Take(9))
            {
                if (!string.IsNullOrEmpty(id))
                    _favoriteHotkeyIds.Add(id);
            }
        }
        foreach (var groupName in settings.CollapsedGroups)
        {
            _collapsedGroups.Add(groupName);
        }

        // Загружаем базы из файла настроек.
        var saved = _repository.Load();
        Infobases = new ObservableCollection<Infobase>(saved);

        // Загружаем группы из файла настроек. Стандартные группы из демо-данных не создаются.
        var loadedGroups = _repository.LoadGroups();
        Groups = new ObservableCollection<Group>(loadedGroups);

        InfobasesView = CollectionViewSource.GetDefaultView(Infobases);
        InfobasesView.Filter = FilterInfobase;
        ApplySortDescriptions();

        // Назначаем слоты Alt+1…9 уже существующим избранным и проставляем номера в UI.
        SyncFavoriteHotkeys();

        // Размеры и маркеры блокировки файловых баз (фоново, не блокируя UI дольше необходимого).
        RefreshFileMetadata();

        // Дерево групп (отображается в виде «группа в группе»).
        GroupNodes = new ObservableCollection<GroupNodeViewModel>();
        RebuildGroupTree();

        SelectInfobaseCommand = new RelayCommand(SelectInfobase);
        RefreshCommand = new RelayCommand(Refresh);
        AddInfobaseCommand = new RelayCommand(AddInfobase);
        EditInfobaseCommand = new RelayCommand(EditInfobase,
            _ => SelectedInfobase != null || SelectedGroupNode?.Group != null);
        DeleteInfobaseCommand = new RelayCommand(DeleteSelected,
            _ => SelectedInfobase != null || SelectedGroupNode?.Group != null);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite, _ => SelectedInfobase != null);
        ToggleFavoriteForCommand = new RelayCommand(ToggleFavoriteFor);
        LaunchCommand = new RelayCommand(Launch, _ => SelectedInfobase != null);
        // Обратная совместимость с XAML: отдельные команды делегируют в единую LaunchCommand.
        LaunchEnterpriseCommand = new RelayCommand(_ => Launch(LaunchKind.Enterprise), _ => SelectedInfobase != null);
        LaunchConfiguratorCommand = new RelayCommand(_ => Launch(LaunchKind.Configurator), _ => SelectedInfobase != null);
        LaunchEnterpriseThinCommand = new RelayCommand(_ => Launch(LaunchKind.Thin32), _ => SelectedInfobase != null);
        LaunchEnterpriseThickCommand = new RelayCommand(_ => Launch(LaunchKind.Thick32), _ => SelectedInfobase != null);
        LaunchEnterpriseThin64Command = new RelayCommand(_ => Launch(LaunchKind.Thin64), _ => SelectedInfobase != null);
        LaunchEnterpriseThick64Command = new RelayCommand(_ => Launch(LaunchKind.Thick64), _ => SelectedInfobase != null);
        ImportFromIbasesV8iCommand = new RelayCommand(ImportFromIbasesV8i);
        ExportToIbasesV8iCommand = new RelayCommand(_ => ExportToIbases());
        SynchronizeWithIbasesCommand = new RelayCommand(SynchronizeWithIbasesManual);
        ToggleRightPanelDetailsCommand = new RelayCommand(_ => ShowRightPanelDetails = !ShowRightPanelDetails);
        ExportInfobasesCommand = new RelayCommand(ExportInfobases);
        ImportInfobasesCommand = new RelayCommand(ImportInfobases);
        ClearAllInfobasesCommand = new RelayCommand(ClearAllInfobases);
        TogglePinCommand = new RelayCommand(TogglePin, _ => SelectedInfobase != null);
        TogglePinForCommand = new RelayCommand(TogglePinFor);
        CopyConnectionStringCommand = new RelayCommand(CopyConnectionString, _ => SelectedInfobase != null);
        ClearCacheCommand = new RelayCommand(ClearCache, _ => SelectedInfobase != null);
        OpenInfobaseFolderCommand = new RelayCommand(OpenInfobaseFolder,
            _ => SelectedInfobase?.Connection.Type == ConnectionType.File);
        CreateDesktopShortcutCommand = new RelayCommand(CreateDesktopShortcut, _ => SelectedInfobase != null);
        RemoveMissingFileBasesCommand = new RelayCommand(RemoveMissingFileBases);
        KillOneCProcessesCommand = new RelayCommand(KillOneCProcesses);
        DumpInfobaseDtCommand = new RelayCommand(DumpInfobaseDt, _ => SelectedInfobase != null);
        DumpConfigurationCfCommand = new RelayCommand(DumpConfigurationCf, _ => SelectedInfobase != null);
        TestInfobaseCommand = new RelayCommand(TestInfobase, _ => SelectedInfobase != null);
        ShowLaunchHistoryCommand = new RelayCommand(ShowLaunchHistory, _ => SelectedInfobase != null);
        RefreshFileSizesCommand = new RelayCommand(_ => RefreshFileMetadata());
        AddTagCommand = new RelayCommand(AddTag);
        AddTagInlineCommand = new RelayCommand(AddTagInline);
        RemoveTagCommand = new RelayCommand(RemoveTag);
        SearchByTagCommand = new RelayCommand(SearchByTag);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        ClearTagFiltersCommand = new RelayCommand(ClearTagFilters, _ => HasActiveTagFilter);
        CollapseAllGroupsCommand = new RelayCommand(CollapseAllGroups);
        ExpandAllGroupsCommand = new RelayCommand(ExpandAllGroups);
        ToggleGroupExpandedCommand = new RelayCommand(ToggleGroupExpanded);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        OpenInfobaseByLinkCommand = new RelayCommand(OpenInfobaseByLink);

        // Если список баз пуст — предлагаем загрузить базы из файла ibases.v8i.
        if (Infobases.Count == 0)
        {
            PromptImportFromIbasesV8i();
        }
    }

    /// <summary>Список информационных баз.</summary>
    public ObservableCollection<Infobase> Infobases { get; }

    /// <summary>Представление списка баз с группировкой и фильтрацией.</summary>
    public ICollectionView InfobasesView { get; }

    /// <summary>Узлы дерева групп информационных баз для отображения «группа в группе».</summary>
    public ObservableCollection<GroupNodeViewModel> GroupNodes { get; private set; }

    /// <summary>Выбранная информационная база.</summary>
    public Infobase? SelectedInfobase
    {
        get => _selectedInfobase;
        set
        {
            if (SetProperty(ref _selectedInfobase, value))
            {
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(StatusBarInfo));
            }
        }
    }

    private GroupNodeViewModel? _selectedGroupNode;

    /// <summary>Выбранный узел группы в дереве (null, если выбрана база или ничего).</summary>
    public GroupNodeViewModel? SelectedGroupNode
    {
        get => _selectedGroupNode;
        set
        {
            var previous = _selectedGroupNode;
            if (SetProperty(ref _selectedGroupNode, value))
            {
                // Сбрасываем подсветку ранее выбранной группы и подсвечиваем новую,
                // чтобы выделение было видно поверх цвета группы в дереве.
                if (previous is not null)
                    previous.IsSelected = false;
                if (_selectedGroupNode is not null)
                    _selectedGroupNode.IsSelected = true;

                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Текст поиска по информационным базам.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ScheduleRebuildGroupTree();
        }
    }

    /// <summary>Показывать только избранные базы.</summary>
    public bool ShowFavoritesOnly
    {
        get => _showFavoritesOnly;
        set
        {
            if (SetProperty(ref _showFavoritesOnly, value))
            {
                // Без InfobasesView.Refresh — дерево строится по EnumerateFilteredInfobases,
                // лишний Refresh на тысячи элементов сильно тормозит UI.
                RebuildGroupTree();
                ScheduleSaveSettings();
            }
        }
    }

    /// <summary>Группировать базы по группам.</summary>
    public bool GroupByGroup
    {
        get => _groupByGroup;
        set
        {
            if (SetProperty(ref _groupByGroup, value))
            {
                RebuildGroupTree();
                ScheduleSaveSettings();
                OnPropertyChanged(nameof(GroupByGroupText));
                OnPropertyChanged(nameof(ShowExpandCollapseButtons));
            }
        }
    }

    /// <summary>Текст кнопки переключения отображения групп.</summary>
    public string GroupByGroupText => _groupByGroup ? "📁 Скрыть группы" : "📁 Показывать группы";

    /// <summary>Список групп информационных баз.</summary>
    public ObservableCollection<Group> Groups { get; }

    /// <summary>Название сохранённой темы оформления (пусто, если тема не сохранялась).</summary>
    public string SavedTheme => _savedTheme;

    /// <summary>Список установленных версий платформы 1С.</summary>
    public List<string> InstalledPlatformVersions => _installedPlatformVersions;

    /// <summary>
    /// Дополнительные пути к каталогам установки платформы 1С.
    /// </summary>
    public List<string> AdditionalPlatformSearchPaths => _additionalPlatformSearchPaths;

    /// <summary>
    /// Сохраняет список установленных версий платформы 1С.
    /// </summary>
    public void SetInstalledPlatformVersions(IEnumerable<string> versions)
    {
        _installedPlatformVersions = new List<string>(versions);
        SaveSettings();
    }

    /// <summary>
    /// Сохраняет дополнительные пути поиска платформы и применяет их к сервису.
    /// </summary>
    public void SetAdditionalPlatformSearchPaths(IEnumerable<string> paths)
    {
        _additionalPlatformSearchPaths = paths?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        PlatformVersionService.SetAdditionalSearchPaths(_additionalPlatformSearchPaths);
        SaveSettings();
    }

    /// <summary>Режим синхронизации с файлом ibases.v8i.</summary>
    public IbasesSyncMode IbasesSyncMode => _ibasesSyncMode;

    /// <summary>Путь к файлу ibases.v8i для синхронизации.</summary>
    public string IbasesSyncFilePath => _ibasesSyncFilePath;

    /// <summary>Момент запуска автоматической синхронизации с файлом ibases.v8i.</summary>
    public IbasesSyncTrigger IbasesSyncTrigger => _ibasesSyncTrigger;

    /// <summary>Интервал автоматической синхронизации в минутах.</summary>
    public int IbasesSyncIntervalMinutes => _ibasesSyncIntervalMinutes;

    /// <summary>Время автоматической синхронизации по расписанию (HH:mm).</summary>
    public string IbasesSyncScheduleTime => _ibasesSyncScheduleTime;

    /// <summary>Создавать резервные копии ibases.v8i перед записью.</summary>
    public bool IbasesBackupEnabled => _ibasesBackupEnabled;

    /// <summary>Число хранимых резервных копий ibases.v8i.</summary>
    public int IbasesBackupKeepCount => _ibasesBackupKeepCount;

    /// <summary>
    /// Текст сообщения о последней выполненной синхронизации с файлом ibases.v8i
    /// (что было обновлено и в какое время). Выводится в строку состояния главного окна.
    /// </summary>
    public string SyncMessage
    {
        get => _syncMessage;
        private set
        {
            if (!SetProperty(ref _syncMessage, value))
                return;
            ScheduleClearSyncMessage();
        }
    }

    private CancellationTokenSource? _syncMessageCts;

    /// <summary>Сообщение о синхронизации скрывается через 10 секунд.</summary>
    private void ScheduleClearSyncMessage()
    {
        _syncMessageCts?.Cancel();
        _syncMessageCts?.Dispose();
        _syncMessageCts = null;

        if (string.IsNullOrEmpty(_syncMessage))
            return;

        var cts = new CancellationTokenSource();
        _syncMessageCts = cts;
        var token = cts.Token;
        _ = ClearSyncMessageAfterDelayAsync(token);
    }

    private async Task ClearSyncMessageAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                _syncMessage = string.Empty;
                OnPropertyChanged(nameof(SyncMessage));
            }
        }
        catch (TaskCanceledException)
        {
            /* новая синхронизация */
        }
    }

    /// <summary>Признак того, что автоматическая синхронизация запущена по интервалу/расписанию.</summary>
    public bool IsAutoSyncRunning => _syncTimerRunning;

    /// <summary>
    /// Применяет настройки синхронизации с файлом ibases.v8i, заданные в окне настроек.
    /// </summary>
    public void ApplyIbasesSyncSettings(IbasesSyncMode mode, string filePath,
        IbasesSyncTrigger trigger, int intervalMinutes, string scheduleTime,
        bool backupEnabled = true, int backupKeepCount = 5)
    {
        _ibasesSyncMode = mode;
        _ibasesSyncFilePath = filePath ?? string.Empty;
        _ibasesSyncTrigger = trigger;
        _ibasesSyncIntervalMinutes = intervalMinutes;
        _ibasesSyncScheduleTime = scheduleTime ?? string.Empty;
        _ibasesBackupEnabled = backupEnabled;
        _ibasesBackupKeepCount = backupKeepCount > 0 ? backupKeepCount : 5;
        SaveSettings();
        RestartAutoSync();
    }

    /// <summary>
    /// После локального изменения базы: выгрузка в ibases.v8i без импорта,
    /// чтобы не перезатереть только что заданные настройки (режим запуска и т.д.).
    /// </summary>
    private void ExportToIbasesAfterLocalChange()
    {
        if (_ibasesSyncMode is not (IbasesSyncMode.Export or IbasesSyncMode.Both))
            return;

        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
            return;

        try
        {
            if (_ibasesBackupEnabled && File.Exists(filePath))
            {
                try { IbasesBackupService.CreateBackup(filePath, _ibasesBackupKeepCount); }
                catch { /* не блокируем сохранение */ }
            }

            var result = _ibasesSync.Export(filePath, Infobases, Groups);
            var text = BuildSyncMessage("Выгружено в файл", result);
            if (!string.IsNullOrEmpty(text))
                SyncMessage = $"{DateTime.Now:HH:mm:ss} — {text}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Экспорт ibases после правки: {ex}");
            SyncMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }

    /// <summary>
    /// Выполняет синхронизацию с файлом ibases.v8i в соответствии с заданным режимом.
    /// В режимах с импортом загружает новые базы из файла, в режимах с экспортом —
    /// выгружает базы приложения в файл. При наличии изменений формирует сообщение
    /// о том, что было обновлено и в какое время, и выводит его в строку состояния.
    /// </summary>
    /// <returns>True, если была выполнена хотя бы одна операция синхронизации.</returns>
    public bool SynchronizeWithIbases()
    {
        if (_ibasesSyncMode == IbasesSyncMode.None)
            return false;

        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
            return false;

        var importPerformed = _ibasesSyncMode is IbasesSyncMode.Import or IbasesSyncMode.Both;
        var exportPerformed = _ibasesSyncMode is IbasesSyncMode.Export or IbasesSyncMode.Both;

        var message = string.Empty;

        if (importPerformed && File.Exists(filePath))
        {
            try
            {
                var result = _ibasesSync.Import(filePath, Infobases, Groups);
                InfobasesView.Refresh();
                Save();
                SaveGroups();
                RebuildGroupTree();
                message = BuildSyncMessage("Загружено из файла", result);
            }
            catch (Exception ex)
            {
                // Не прерываем работу пользователя при авто-синхронизации, но фиксируем ошибку в статусе.
                System.Diagnostics.Debug.WriteLine($"Авто-импорт ibases.v8i: {ex}");
                SyncMessage = $"Ошибка импорта: {ex.Message}";
            }
        }

        if (exportPerformed)
        {
            try
            {
                // Резервная копия перед записью в ibases.v8i.
                if (_ibasesBackupEnabled && File.Exists(filePath))
                {
                    try
                    {
                        var bak = IbasesBackupService.CreateBackup(filePath, _ibasesBackupKeepCount);
                        if (bak is not null)
                            _logger.Info($"Создана резервная копия ibases.v8i: {bak}");
                    }
                    catch (Exception bakEx)
                    {
                        _logger.Error("Не удалось создать резервную копию ibases.v8i", bakEx);
                    }
                }

                var result = _ibasesSync.Export(filePath, Infobases, Groups);
                var exportText = BuildSyncMessage("Выгружено в файл", result);
                if (!string.IsNullOrEmpty(exportText))
                {
                    message = string.IsNullOrEmpty(message)
                        ? exportText
                        : message + "; " + exportText;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Авто-экспорт ibases.v8i: {ex}");
                var err = $"Ошибка экспорта: {ex.Message}";
                SyncMessage = string.IsNullOrEmpty(SyncMessage) ? err : SyncMessage + "; " + err;
            }
        }

        if (!string.IsNullOrEmpty(message))
        {
            SyncMessage = $"{DateTime.Now:HH:mm:ss} — {message}";
        }

        return importPerformed || exportPerformed;
    }

    /// <summary>
    /// Формирует текстовое описание изменений по результату импорта/экспорта.
    /// Возвращает пустую строку, если изменений не было.
    /// </summary>
    private static string BuildSyncMessage(string prefix, object result)
    {
        var parts = new List<string>();

        if (result is IbasesImportResult import)
        {
            if (import.Added > 0) parts.Add($"добавлено баз: {import.Added}");
            if (import.Updated > 0) parts.Add($"обновлено баз: {import.Updated}");
            if (import.Skipped > 0) parts.Add($"пропущено: {import.Skipped}");
            if (import.GroupsCreated > 0) parts.Add($"создано групп: {import.GroupsCreated}");
        }
        else if (result is IbasesExportResult export)
        {
            if (export.Added > 0) parts.Add($"добавлено баз: {export.Added}");
            if (export.Updated > 0) parts.Add($"обновлено баз: {export.Updated}");
            if (export.GroupsCreated > 0) parts.Add($"создано групп: {export.GroupsCreated}");
        }

        return parts.Count == 0 ? string.Empty : $"{prefix}: {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Запускает автоматическую синхронизацию в соответствии с настройками:
    /// по интервалу или по расписанию. При старте также выполняет синхронизацию
    /// (если выбран режим OnStartup или Interval/Schedule).
    /// </summary>
    public void StartAutoSync()
    {
        // При запуске приложения синхронизируемся всегда, если режим включён,
        // независимо от выбранного триггера (OnStartup — сразу, Interval/Schedule — сразу и далее).
        SynchronizeWithIbases();

        RestartAutoSync();
    }

    /// <summary>
    /// Останавливает автоматическую синхронизацию по таймеру.
    /// </summary>
    public void StopAutoSync()
    {
        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTimerTick;
            _syncTimer = null;
        }
        _nextScheduleRun = null;
        SetAutoSyncRunning(false);
    }

    /// <summary>
    /// Перезапускает таймер автоматической синхронизации в соответствии с
    /// текущими настройками. Для режима Interval таймер тикает раз в минуту и
    /// выполняет синхронизацию через заданный интервал; для режима Schedule —
    /// проверяет наступление заданного времени.
    /// </summary>
    public void RestartAutoSync()
    {
        StopAutoSync();

        if (_ibasesSyncMode == IbasesSyncMode.None ||
            _ibasesSyncTrigger is IbasesSyncTrigger.OnStartup)
        {
            return;
        }

        if (!ComputeNextRunTime(out var nextRun))
            return;

        _nextScheduleRun = nextRun;

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _syncTimer.Tick += OnSyncTimerTick;
        _syncTimer.Start();
        SetAutoSyncRunning(true);
    }

    /// <summary>
    /// Обработчик тика таймера автоматической синхронизации.
    /// </summary>
    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        if (_ibasesSyncMode == IbasesSyncMode.None)
        {
            RestartAutoSync();
            return;
        }

        if (_nextScheduleRun is null)
        {
            if (ComputeNextRunTime(out var nextRun))
                _nextScheduleRun = nextRun;
            return;
        }

        if (DateTime.Now >= _nextScheduleRun.Value)
        {
            SynchronizeWithIbases();
            // Планируем следующий запуск.
            if (ComputeNextRunTime(out var nextRun))
                _nextScheduleRun = nextRun;
        }
    }

    /// <summary>
    /// Вычисляет время следующего запуска синхронизации для выбранного режима.
    /// Для интервала — текущее время плюс интервал; для расписания — ближайшее
    /// заданное время (завтра, если время уже прошло сегодня).
    /// </summary>
    private bool ComputeNextRunTime(out DateTime nextRun)
    {
        nextRun = default;

        if (_ibasesSyncTrigger == IbasesSyncTrigger.Interval)
        {
            var intervalMinutes = Math.Max(1, _ibasesSyncIntervalMinutes);
            nextRun = DateTime.Now.AddMinutes(intervalMinutes);
            return true;
        }

        if (_ibasesSyncTrigger == IbasesSyncTrigger.Schedule)
        {
            if (string.IsNullOrWhiteSpace(_ibasesSyncScheduleTime) ||
                !TimeSpan.TryParse(_ibasesSyncScheduleTime, out var time))
            {
                return false;
            }

            var now = DateTime.Now;
            var today = now.Date + time;
            if (today <= now)
            {
                today = today.AddDays(1);
            }

            nextRun = today;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Устанавливает признак запущенной автоматической синхронизации и уведомляет подписчиков.
    /// </summary>
    private void SetAutoSyncRunning(bool running)
    {
        if (_syncTimerRunning != running)
        {
            _syncTimerRunning = running;
            OnPropertyChanged(nameof(IsAutoSyncRunning));
        }
    }

    /// <summary>
    /// Выполняет экспорт текущего списка баз и групп в файл ibases.v8i
    /// (используется для ручного экспорта из окна настроек).
    /// </summary>
    public bool ExportToIbases()
    {
        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
            return false;

        try
        {
            if (_ibasesBackupEnabled && File.Exists(filePath))
            {
                try { IbasesBackupService.CreateBackup(filePath, _ibasesBackupKeepCount); }
                catch { /* не блокируем экспорт */ }
            }
            _ibasesSync.Export(filePath, Infobases, Groups);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Выполняет импорт баз из файла ibases.v8i в приложение
    /// (используется для ручного импорта из окна настроек).
    /// </summary>
    public bool ImportFromIbases()
    {
        var filePath = ResolveIbasesFilePath();
        if (filePath is null || !File.Exists(filePath))
            return false;

        try
        {
            _ibasesSync.Import(filePath, Infobases, Groups);
            InfobasesView.Refresh();
            Save();
            SaveGroups();
            RebuildGroupTree();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Определяет путь к файлу ibases.v8i: пользовательский путь из настроек,
    /// либо стандартный путь 1С, если пользовательский не задан.
    /// </summary>
    private string? ResolveIbasesFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_ibasesSyncFilePath))
            return _ibasesSyncFilePath;

        return IbasesV8iImporter.FindDefaultPath();
    }

    /// <summary>
    /// Применяет изменения списка групп, внесённые в окне настроек.
    /// </summary>
    public void ApplyGroupChanges(IEnumerable<Group> groups)
    {
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
        SaveGroups();
        InfobasesView.Refresh();
        RebuildGroupTree();
    }

    /// <summary>Ширина колонки «Название» (0 — по умолчанию).</summary>
    public double NameColumnWidth
    {
        get => _nameColumnWidth;
        private set
        {
            if (_nameColumnWidth != value)
            {
                _nameColumnWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Ширина колонки «Версия платформы» (0 — по умолчанию).</summary>
    public double VersionColumnWidth
    {
        get => _versionColumnWidth;
        private set
        {
            if (_versionColumnWidth != value)
            {
                _versionColumnWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Ширина колонки «Режим запуска» (0 — по умолчанию).</summary>
    public double LaunchModeColumnWidth
    {
        get => _launchModeColumnWidth;
        private set
        {
            if (_launchModeColumnWidth != value)
            {
                _launchModeColumnWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Ширина колонки «Сервер/База» (0 — по умолчанию).</summary>
    public double ServerColumnWidth
    {
        get => _serverColumnWidth;
        private set
        {
            if (_serverColumnWidth != value)
            {
                _serverColumnWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Ширина колонки «Последний запуск» (0 — по умолчанию).</summary>
    public double LastLaunchColumnWidth
    {
        get => _lastLaunchColumnWidth;
        private set
        {
            if (_lastLaunchColumnWidth != value)
            {
                _lastLaunchColumnWidth = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Показывать колонку-кнопку «Избранное» (★) в списке баз.</summary>
    public bool ShowFavoritesButton => _showFavoritesButton;

    /// <summary>Показывать колонку-кнопку «Закрепить» (📌) в списке баз.</summary>
    public bool ShowPinnedButton => _showPinnedButton;

    /// <summary>Показывать теги баз в списке (кнопка тегов в заголовке списка баз).</summary>
    public bool ShowTags
    {
        get => _showTags;
        set
        {
            if (SetProperty(ref _showTags, value))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Показывать панель быстрого отбора по тегам.</summary>
    public bool ShowTagFilterPanel
    {
        get => _showTagFilterPanel;
        set
        {
            if (SetProperty(ref _showTagFilterPanel, value))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Разрешить несколько экземпляров приложения.</summary>
    public bool AllowMultipleInstances => _allowMultipleInstances;

    /// <summary>Выбранные теги для фильтра (можно несколько одновременно).</summary>
    public ObservableCollection<string> ActiveTagFilters => _activeTagFilters;

    /// <summary>Есть ли активный фильтр по тегам.</summary>
    public bool HasActiveTagFilter => _activeTagFilters.Count > 0;

    /// <summary>Режим списка: Все / Избранное / Недавние.</summary>
    public ListViewMode ListViewMode
    {
        get => _listViewMode;
        set
        {
            if (SetProperty(ref _listViewMode, value))
            {
                // Совместимость с прежним флагом избранного.
                _showFavoritesOnly = value == ListViewMode.Favorites;
                OnPropertyChanged(nameof(ShowFavoritesOnly));
                OnPropertyChanged(nameof(IsListModeAll));
                OnPropertyChanged(nameof(IsListModeFavorites));
                OnPropertyChanged(nameof(IsListModeRecent));
                RebuildGroupTree();
            }
        }
    }

    public bool IsListModeAll
    {
        get => _listViewMode == ListViewMode.All;
        set { if (value) ListViewMode = ListViewMode.All; }
    }

    public bool IsListModeFavorites
    {
        get => _listViewMode == ListViewMode.Favorites;
        set { if (value) ListViewMode = ListViewMode.Favorites; }
    }

    public bool IsListModeRecent
    {
        get => _listViewMode == ListViewMode.Recent;
        set { if (value) ListViewMode = ListViewMode.Recent; }
    }

    /// <summary>Проверяет, выбран ли тег в фильтре.</summary>
    public bool IsTagSelected(string tag) =>
        !string.IsNullOrEmpty(tag) && _activeTagFilters.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Уникальные теги всех баз для панели быстрого отбора.
    /// </summary>
    public IEnumerable<string> AvailableTags =>
        Infobases
            .SelectMany(i => i.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<TagFilterItem> _tagFilterItems = new();

    /// <summary>Теги с признаком выбора для панели фильтров.</summary>
    public ObservableCollection<TagFilterItem> TagFilterItems => _tagFilterItems;

    /// <summary>Пересобирает облако тегов (панель фильтров).</summary>
    public void RefreshTagFilterItems()
    {
        var selected = new HashSet<string>(_activeTagFilters, StringComparer.OrdinalIgnoreCase);
        var tags = Infobases
            .SelectMany(i => i.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Не трогаем UI, если набор тегов и выделение не изменились.
        if (_tagFilterItems.Count == tags.Count)
        {
            var same = true;
            for (var i = 0; i < tags.Count; i++)
            {
                if (!string.Equals(_tagFilterItems[i].Name, tags[i], StringComparison.OrdinalIgnoreCase)
                    || _tagFilterItems[i].IsSelected != selected.Contains(tags[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return;
        }

        _tagFilterItems.Clear();
        foreach (var t in tags)
            _tagFilterItems.Add(new TagFilterItem(t, selected.Contains(t)));
        OnPropertyChanged(nameof(HasActiveTagFilter));
    }

    private void SyncActiveTagFilterSet()
    {
        _activeTagFilterSet = new HashSet<string>(_activeTagFilters, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Отложенная перестройка дерева (поиск по мере ввода).</summary>
    private void ScheduleRebuildGroupTree()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                // Короче debounce — список реагирует быстрее при наборе.
                await Task.Delay(90, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null) return;
                // Loaded priority: после текущего ввода, до фоновой отрисовки.
                await dispatcher.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        RebuildGroupTree();
                }, System.Windows.Threading.DispatcherPriority.DataBind);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.Error("Ошибка отложенной перестройки дерева", ex);
            }
        });
    }

    /// <summary>Показывать кнопки свернуть/развернуть все (только при группировке).</summary>
    public bool ShowExpandCollapseButtons => _groupByGroup;

    /// <summary>Показывать колонку «Версия платформы» в списке баз.</summary>
    public bool ShowVersionColumn => _showVersionColumn;

    /// <summary>Показывать подробности в правой панели (иначе — только кнопки).</summary>
    public bool ShowRightPanelDetails
    {
        get => _showRightPanelDetails;
        set
        {
            if (SetProperty(ref _showRightPanelDetails, value))
            {
                OnPropertyChanged(nameof(RightPanelToggleTooltip));
                ScheduleSaveSettings();
            }
        }
    }

    /// <summary>Показывать блок «Текущая сессия» в правой панели (полный и компактный режим).</summary>
    public bool ShowSessionLaunchPanel
    {
        get => _showSessionLaunchPanel;
        set
        {
            if (SetProperty(ref _showSessionLaunchPanel, value))
                ScheduleSaveSettings();
        }
    }

    public string RightPanelToggleTooltip =>
        _showRightPanelDetails ? "Скрыть подробности правой панели" : "Показать подробности правой панели";

    /// <summary>Тип клиента для текущего запуска (не пишется в настройки базы).</summary>
    public SessionClientMode SessionClientMode
    {
        get => _sessionClientMode;
        set
        {
            if (SetProperty(ref _sessionClientMode, value))
            {
                OnPropertyChanged(nameof(IsSessionClientAuto));
                OnPropertyChanged(nameof(IsSessionClientOrdinary));
                OnPropertyChanged(nameof(IsSessionClientThick));
                OnPropertyChanged(nameof(IsSessionClientThin));
                ScheduleSaveSettings();
            }
        }
    }

    public bool IsSessionClientAuto
    {
        get => _sessionClientMode == SessionClientMode.Auto;
        set { if (value) SessionClientMode = SessionClientMode.Auto; }
    }

    public bool IsSessionClientOrdinary
    {
        get => _sessionClientMode == SessionClientMode.Ordinary;
        set { if (value) SessionClientMode = SessionClientMode.Ordinary; }
    }

    public bool IsSessionClientThick
    {
        get => _sessionClientMode == SessionClientMode.Thick;
        set { if (value) SessionClientMode = SessionClientMode.Thick; }
    }

    public bool IsSessionClientThin
    {
        get => _sessionClientMode == SessionClientMode.Thin;
        set { if (value) SessionClientMode = SessionClientMode.Thin; }
    }

    /// <summary>Разрядность для текущего запуска (не пишется в настройки базы).</summary>
    public SessionArchitectureMode SessionArchitecture
    {
        get => _sessionArchitecture;
        set
        {
            if (SetProperty(ref _sessionArchitecture, value))
            {
                OnPropertyChanged(nameof(IsSessionArchAuto));
                OnPropertyChanged(nameof(IsSessionArch32));
                OnPropertyChanged(nameof(IsSessionArch64));
                ScheduleSaveSettings();
            }
        }
    }

    public bool IsSessionArchAuto
    {
        get => _sessionArchitecture == SessionArchitectureMode.Auto;
        set { if (value) SessionArchitecture = SessionArchitectureMode.Auto; }
    }

    public bool IsSessionArch32
    {
        get => _sessionArchitecture == SessionArchitectureMode.X86;
        set { if (value) SessionArchitecture = SessionArchitectureMode.X86; }
    }

    public bool IsSessionArch64
    {
        get => _sessionArchitecture == SessionArchitectureMode.X64;
        set { if (value) SessionArchitecture = SessionArchitectureMode.X64; }
    }

    public bool StatusShowConnectionPath => _statusShowConnectionPath;
    public bool StatusShowArchitecture => _statusShowArchitecture;
    public bool StatusShowLaunchMode => _statusShowLaunchMode;
    public bool StatusShowPort => _statusShowPort;
    public bool StatusShowPlatformVersion => _statusShowPlatformVersion;
    public bool StatusShowClientType => _statusShowClientType;
    public bool StatusShowConnectionType => _statusShowConnectionType;
    public bool StatusShowUser => _statusShowUser;
    public bool StatusShowId => _statusShowId;

    /// <summary>Сводка для нижней строки состояния по выбранным в настройках полям.</summary>
    public string StatusBarInfo
    {
        get
        {
            var ib = SelectedInfobase;
            if (ib is null)
                return string.Empty;

            var parts = new List<string>();
            if (_statusShowConnectionType)
                parts.Add(ib.ConnectionTypeDisplay);
            if (_statusShowConnectionPath)
            {
                var path = ib.Connection.Type == ConnectionType.File
                    ? (string.IsNullOrWhiteSpace(ib.Connection.FilePath) ? "—" : ib.Connection.FilePath)
                    : ib.ServerDatabaseDisplay;
                if (!string.IsNullOrWhiteSpace(path))
                    parts.Add(path);
            }
            if (_statusShowPort && ib.Connection.Type == ConnectionType.ClientServer && ib.Connection.Port > 0)
                parts.Add($"порт {ib.Connection.Port}");
            if (_statusShowPlatformVersion && !string.IsNullOrWhiteSpace(ib.PlatformVersion))
                parts.Add($"платформа {ib.PlatformVersion}");
            if (_statusShowArchitecture)
                parts.Add(ib.ArchitectureDisplay);
            if (_statusShowLaunchMode)
                parts.Add(ib.ParsedLaunchMode);
            if (_statusShowClientType && !string.IsNullOrWhiteSpace(ib.ClientType))
                parts.Add(ib.ClientType);
            if (_statusShowUser && !string.IsNullOrWhiteSpace(ib.Connection.User))
                parts.Add($"пользователь {ib.Connection.User}");
            if (_statusShowId && !string.IsNullOrWhiteSpace(ib.Id))
                parts.Add($"ID {ib.Id}");

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Показывать колонку «Режим запуска» в списке баз.</summary>
    public bool ShowLaunchModeColumn => _showLaunchModeColumn;

    /// <summary>Показывать колонку «Сервер/База» в списке баз.</summary>
    public bool ShowServerColumn => _showServerColumn;

    /// <summary>Показывать колонку «Последний запуск» в списке баз.</summary>
    public bool ShowLastLaunchColumn => _showLastLaunchColumn;

    /// <summary>Показывать колонку «Размер» файловой ИБ.</summary>
    public bool ShowSizeColumn => _showSizeColumn;

    public double SizeColumnWidth
    {
        get => _sizeColumnWidth;
        set
        {
            if (SetProperty(ref _sizeColumnWidth, value))
                ScheduleSaveSettings();
        }
    }

    /// <summary>
    /// Применяет настройки содержимого нижней панели (строки состояния).
    /// </summary>
    public void ApplyStatusBarSettings(
        bool connectionPath, bool architecture, bool launchMode, bool port,
        bool platformVersion, bool clientType, bool connectionType, bool user,
        bool showId = false)
    {
        _statusShowConnectionPath = connectionPath;
        _statusShowArchitecture = architecture;
        _statusShowLaunchMode = launchMode;
        _statusShowPort = port;
        _statusShowPlatformVersion = platformVersion;
        _statusShowClientType = clientType;
        _statusShowConnectionType = connectionType;
        _statusShowUser = user;
        _statusShowId = showId;
        OnPropertyChanged(nameof(StatusShowConnectionPath));
        OnPropertyChanged(nameof(StatusShowArchitecture));
        OnPropertyChanged(nameof(StatusShowLaunchMode));
        OnPropertyChanged(nameof(StatusShowPort));
        OnPropertyChanged(nameof(StatusShowPlatformVersion));
        OnPropertyChanged(nameof(StatusShowClientType));
        OnPropertyChanged(nameof(StatusShowConnectionType));
        OnPropertyChanged(nameof(StatusShowUser));
        OnPropertyChanged(nameof(StatusShowId));
        OnPropertyChanged(nameof(StatusBarInfo));
        SaveSettings();
    }

    /// <summary>
    /// Применяет настройки отображения списка баз, заданные в окне настроек.
    /// Обновляет видимость колонок, кнопок и тегов, а также поведение
    /// группировки и фильтра по избранному.
    /// </summary>
    public void ApplyDisplaySettings(bool showFavoritesButton, bool showPinnedButton, bool showTags,
        bool showVersionColumn, bool showLaunchModeColumn, bool showServerColumn, bool showLastLaunchColumn,
        bool groupByGroup, bool showFavoritesOnly, bool showSizeColumn = true)
    {
        _showFavoritesButton = showFavoritesButton;
        _showPinnedButton = showPinnedButton;
        _showTags = showTags;
        _showVersionColumn = showVersionColumn;
        _showLaunchModeColumn = showLaunchModeColumn;
        _showServerColumn = showServerColumn;
        _showLastLaunchColumn = showLastLaunchColumn;
        _showSizeColumn = showSizeColumn;

        OnPropertyChanged(nameof(ShowFavoritesButton));
        OnPropertyChanged(nameof(ShowPinnedButton));
        OnPropertyChanged(nameof(ShowTags));
        OnPropertyChanged(nameof(ShowVersionColumn));
        OnPropertyChanged(nameof(ShowLaunchModeColumn));
        OnPropertyChanged(nameof(ShowServerColumn));
        OnPropertyChanged(nameof(ShowLastLaunchColumn));
        OnPropertyChanged(nameof(ShowSizeColumn));

        // Применяем поведение списка (уже имеющиеся настройки).
        GroupByGroup = groupByGroup;
        ShowFavoritesOnly = showFavoritesOnly;

        SaveSettings();
    }

    /// <summary>Сохранённая ширина окна приложения (0 — по умолчанию).</summary>
    public double SavedWindowWidth => _windowWidth;

    /// <summary>Сохранённая высота окна приложения (0 — по умолчанию).</summary>
    public double SavedWindowHeight => _windowHeight;

    /// <summary>Сохранённая позиция окна по горизонтали (0 — по центру).</summary>
    public double SavedWindowLeft => _windowLeft;

    /// <summary>Сохранённая позиция окна по вертикали (0 — по центру).</summary>
    public double SavedWindowTop => _windowTop;

    /// <summary>Сохранённое состояние окна (пусто — обычное).</summary>
    public string SavedWindowState => _windowState;

    /// <summary>
    /// Сохраняет размер, позицию и состояние окна приложения.
    /// </summary>
    public void SaveWindowLayout(double width, double height, double left, double top, string state)
    {
        _windowWidth = width;
        _windowHeight = height;
        _windowLeft = left;
        _windowTop = top;
        _windowState = state;
        SaveSettings();
    }

    /// <summary>Команда импорта баз из файла ibases.v8i.</summary>
    public ICommand ImportFromIbasesV8iCommand { get; }
    public ICommand ExportToIbasesV8iCommand { get; }
    public ICommand SynchronizeWithIbasesCommand { get; }
    public ICommand ToggleRightPanelDetailsCommand { get; }

    /// <summary>Команда экспорта списка информационных баз в файл.</summary>
    public ICommand ExportInfobasesCommand { get; }

    /// <summary>Команда загрузки списка информационных баз из файла.</summary>
    public ICommand ImportInfobasesCommand { get; }

    /// <summary>Команда очистки всего списка информационных баз.</summary>
    public ICommand ClearAllInfobasesCommand { get; }

    /// <summary>Команда закрепления/открепления выбранной базы.</summary>
    public ICommand TogglePinCommand { get; }

    /// <summary>Команда закрепления/открепления конкретной базы.</summary>
    public ICommand TogglePinForCommand { get; }

    /// <summary>Команда копирования строки подключения выбранной базы в буфер обмена.</summary>
    public ICommand CopyConnectionStringCommand { get; }

    /// <summary>Команда очистки локального кеша 1С выбранной базы.</summary>
    public ICommand ClearCacheCommand { get; }

    /// <summary>Открыть каталог файловой базы в проводнике.</summary>
    public ICommand OpenInfobaseFolderCommand { get; }

    /// <summary>Создать ярлык на рабочем столе для выбранной базы.</summary>
    public ICommand CreateDesktopShortcutCommand { get; }

    /// <summary>Удалить из списка файловые базы без 1Cv8.1CD.</summary>
    public ICommand RemoveMissingFileBasesCommand { get; }

    /// <summary>Завершить зависшие процессы платформы 1С.</summary>
    public ICommand KillOneCProcessesCommand { get; }

    /// <summary>Выгрузка ИБ в .dt (пакетный DESIGNER).</summary>
    public ICommand DumpInfobaseDtCommand { get; }

    /// <summary>Выгрузка конфигурации в .cf.</summary>
    public ICommand DumpConfigurationCfCommand { get; }

    /// <summary>Тестирование ИБ (/IBCheckAndRepair -TestOnly).</summary>
    public ICommand TestInfobaseCommand { get; }

    /// <summary>Показать историю запусков выбранной базы.</summary>
    public ICommand ShowLaunchHistoryCommand { get; }

    /// <summary>Пересчитать размеры файловых баз.</summary>
    public ICommand RefreshFileSizesCommand { get; }

    /// <summary>Команда добавления тега к базе.</summary>
    public ICommand AddTagCommand { get; }

    /// <summary>Команда добавления тега к базе прямо в строке названия (без отдельного окна).</summary>
    public ICommand AddTagInlineCommand { get; }

    /// <summary>Команда удаления тега из базы.</summary>
    public ICommand RemoveTagCommand { get; }

    /// <summary>Команда поиска баз по тегу.</summary>
    public ICommand SearchByTagCommand { get; }

    /// <summary>Команда очистки поля поиска.</summary>
    public ICommand ClearSearchCommand { get; }

    /// <summary>Сбросить только выбранные теги фильтра.</summary>
    public ICommand ClearTagFiltersCommand { get; }

    /// <summary>Команда сворачивания всех групп.</summary>
    public ICommand CollapseAllGroupsCommand { get; }

    /// <summary>Команда разворачивания всех групп.</summary>
    public ICommand ExpandAllGroupsCommand { get; }

    /// <summary>Команда сворачивания/разворачивания отдельной группы с сохранением состояния.</summary>
    public ICommand ToggleGroupExpandedCommand { get; }

    /// <summary>Команда открытия окна настроек приложения.</summary>
    public ICommand OpenSettingsCommand { get; }

    /// <summary>
    /// Команда «Перейти по ссылке» — открывает диалог ввода ссылки на
    /// информационную базу (аналог стандартного загрузчика 1С) и запускает базу.
    /// </summary>
    public ICommand OpenInfobaseByLinkCommand { get; }

    /// <summary>Команда выбора информационной базы.</summary>
    public ICommand SelectInfobaseCommand { get; }

    /// <summary>Команда обновления списка баз.</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Команда добавления новой базы.</summary>
    public ICommand AddInfobaseCommand { get; }

    /// <summary>Команда редактирования выбранной базы.</summary>
    public ICommand EditInfobaseCommand { get; }

    /// <summary>Команда удаления выбранной базы.</summary>
    public ICommand DeleteInfobaseCommand { get; }


    /// <summary>Команда добавления/удаления из избранного.</summary>
    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Команда добавления/удаления из избранного для конкретной базы.</summary>
    public ICommand ToggleFavoriteForCommand { get; }

    /// <summary>Команда запуска 1С:Предприятие.</summary>
    /// <summary>Под-VM запуска баз (композиция MainViewModel).</summary>
    /// <summary>Единая команда запуска (параметр: LaunchKind или строка имени enum).</summary>
    public ICommand LaunchCommand { get; }

    public ICommand LaunchEnterpriseCommand { get; }

    /// <summary>Команда запуска Конфигуратора.</summary>
    public ICommand LaunchConfiguratorCommand { get; }

    /// <summary>Команда запуска 1С:Предприятие тонким клиентом (32 бита).</summary>
    public ICommand LaunchEnterpriseThinCommand { get; }

    /// <summary>Команда запуска 1С:Предприятие толстым клиентом (32 бита).</summary>
    public ICommand LaunchEnterpriseThickCommand { get; }

    /// <summary>Команда запуска 1С:Предприятие тонким клиентом (64 бита).</summary>
    public ICommand LaunchEnterpriseThin64Command { get; }

    /// <summary>Команда запуска 1С:Предприятие толстым клиентом (64 бита).</summary>
    public ICommand LaunchEnterpriseThick64Command { get; }

    private void SelectInfobase(object? parameter)
    {
        if (parameter is Infobase infobase)
        {
            SelectedInfobase = infobase;
        }
    }

    private void Refresh(object? parameter)
    {
        Infobases.Clear();
        foreach (var infobase in _repository.Load())
        {
            Infobases.Add(infobase);
        }
        SelectedInfobase = null;
        Save();
        RebuildGroupTree();
    }

    /// <summary>
    /// Открывает мастер добавления (выбор типа: информационная база или группа).
    /// </summary>
    private void AddInfobase(object? parameter)
    {
        var addDialog = new AddEditWindow
        {
            Owner = Application.Current.MainWindow
        };
        if (addDialog.ShowDialog() != true)
            return;

        var defaultGroupPath = SelectedGroupNode?.Group is not null
            ? SelectedGroupNode.FullPath
            : (SelectedInfobase?.Group ?? string.Empty);

        switch (addDialog.SelectedType)
        {
            case "Group":
                AddGroup();
                break;

            case "CreateEmpty":
            case "CreateFromTemplate":
            {
                var createDlg = new CreateInfobaseWindow(
                    fromTemplate: addDialog.SelectedType == "CreateFromTemplate",
                    platformVersions: _installedPlatformVersions,
                    defaultGroupPath: defaultGroupPath,
                    groups: Groups)
                {
                    Owner = Application.Current.MainWindow
                };
                if (createDlg.ShowDialog() == true && createDlg.Result is not null)
                {
                    Infobases.Add(createDlg.Result);
                    SelectedInfobase = createDlg.Result;
                    Save();
                    RebuildGroupTree();
                    ExportToIbasesAfterLocalChange();
                    _dialogs.ShowInfo(
                        $"База «{createDlg.Result.Name}» создана и добавлена в список.",
                        "Создание ИБ");
                }
                break;
            }

            default:
            {
                // Существующая база — только регистрация в списке.
                var dialog = new ConnectionSettingsWindow(null, Groups, _installedPlatformVersions, defaultGroupPath,
                    availableServers: GetAvailableServers(), availablePorts: GetAvailablePorts())
                {
                    Owner = Application.Current.MainWindow
                };
                if (dialog.ShowDialog() == true)
                {
                    Infobases.Add(dialog.Result);
                    SelectedInfobase = dialog.Result;
                    Save();
                    RebuildGroupTree();
                    ExportToIbasesAfterLocalChange();
                }
                break;
            }
        }
    }

    /// <summary>
    /// Открывает диалог создания новой группы. Если выбрана группа — создаётся подгруппа внутри неё.
    /// Если выбрана база — родитель = группа этой базы.
    /// </summary>
    private void AddGroup()
    {
        Group? parent = SelectedGroupNode?.Group;
        if (parent is null && !string.IsNullOrWhiteSpace(SelectedInfobase?.Group))
            parent = GroupHierarchyHelper.FindByFullPath(SelectedInfobase!.Group, Groups);

        var dialog = new GroupEditWindow(Groups, parent)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            Groups.Add(dialog.Result);
            SaveGroups();
            RebuildGroupTree();
        }
    }

    /// <summary>
    /// Возвращает список серверов 1С из клиент-серверных баз списка (без дублей, по алфавиту).
    /// Используется для выпадающего списка «Сервер» в окне настройки подключения.
    /// </summary>
    private IEnumerable<string> GetAvailableServers()
    {
        return Infobases
            .Where(b => b?.Connection?.Type == ConnectionType.ClientServer)
            .Select(b => b.Connection!.Server?.Trim() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Возвращает список портов серверов 1С из клиент-серверных баз списка
    /// (без дублей, по возрастанию). Используется для выпадающего списка
    /// «Порт сервера» в окне настройки подключения.
    /// </summary>
    private IEnumerable<int> GetAvailablePorts()
    {
        return Infobases
            .Where(b => b?.Connection?.Type == ConnectionType.ClientServer)
            .Select(b => b.Connection!.Port)
            .Where(p => p > 0)
            .Distinct()
            .OrderBy(p => p);
    }

    /// <summary>
    /// Редактирует выбранный элемент: базу — через окно подключения, группу — через окно группы.
    /// Тип элемента при редактировании изменить нельзя (база не станет группой и наоборот).
    /// </summary>
    private void EditInfobase(object? parameter)
    {
        // Если выбран узел группы — редактируем группу.
        if (SelectedGroupNode?.Group is Group group)
        {
            EditGroup(group);
            return;
        }

        if (SelectedInfobase is null)
            return;

        var dialog = new ConnectionSettingsWindow(SelectedInfobase, Groups, _installedPlatformVersions,
            availableServers: GetAvailableServers(), availablePorts: GetAvailablePorts())
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            // Применяем изменения к существующему объекту, а не заменяем его новым.
            // Это важно, потому что на объект могут ссылаться и основной список, и представление.
            var target = SelectedInfobase;
            target.Id = dialog.Result.Id;
            target.Name = dialog.Result.Name;
            target.Group = dialog.Result.Group;
            target.Description = dialog.Result.Description;
            target.PlatformVersion = dialog.Result.PlatformVersion;
            target.Architecture = dialog.Result.Architecture;
            target.LaunchMode = dialog.Result.LaunchMode;
            target.LaunchParameters = dialog.Result.LaunchParameters;
            target.ClientType = dialog.Result.ClientType;
            target.IsFavorite = dialog.Result.IsFavorite;
            target.IsPinned = dialog.Result.IsPinned;
            target.LastLaunchDate = dialog.Result.LastLaunchDate;
            target.Tags = dialog.Result.Tags;
            target.MetadataRoot = dialog.Result.MetadataRoot;
            target.Connection = dialog.Result.Connection;
            if (!string.IsNullOrWhiteSpace(dialog.Result.LaunchMode))
                target.LaunchMode = dialog.Result.LaunchMode;

            InfobasesView.Refresh();
            Save();
            RebuildGroupTree();
            // Только выгрузка: импорт сразу после правки затирал режим запуска из ibases.v8i.
            ExportToIbasesAfterLocalChange();
        }
    }

    /// <summary>
    /// Открывает диалог редактирования выбранной группы.
    /// </summary>
    private void EditGroup(Group group)
    {
        var dialog = new GroupEditWindow(Groups, group.ParentId, group)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            // Обновляем поля существующего объекта группы (сохраняем ссылку),
            // чтобы иерархия по ParentId и все привязки остались валидными.
            group.Name = dialog.Result.Name;
            group.Description = dialog.Result.Description;
            group.Color = dialog.Result.Color;
            group.IconColor = dialog.Result.IconColor ?? string.Empty;
            group.Icon = dialog.Result.Icon ?? string.Empty;
            group.ParentId = dialog.Result.ParentId;

            SaveGroups();
            RebuildGroupTree();
        }
    }

    /// <summary>
    /// Удаляет выбранный элемент: базу или группу (с учётом потомков).
    /// </summary>
    private void DeleteSelected(object? parameter)
    {
        // Удаление группы (если выбран узел группы).
        if (SelectedGroupNode?.Group is Group group)
        {
            DeleteGroup(group);
            return;
        }

        if (SelectedInfobase is null)
            return;

        var ib = SelectedInfobase;
        var dlg = new Configuration_Management.DeleteInfobaseWindow(ib)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || !dlg.Confirmed)
            return;

        if (dlg.DeletePhysically)
        {
            var err = InfobaseMaintenanceService.TryDeleteFileBasePhysically(ib);
            if (err is not null)
            {
                _dialogs.ShowError(err, "Физическое удаление");
                // Даже при ошибке на диске продолжаем удаление из списка по запросу пользователя
                if (!_dialogs.Confirm(
                        "Удалить базу только из списка программы (файлы на диске не тронуты или удалены частично)?",
                        "Удаление из списка"))
                    return;
            }
        }

        Infobases.Remove(ib);
        if (ReferenceEquals(SelectedInfobase, ib))
            SelectedInfobase = null;
        Save();
        RebuildGroupTree();
        ExportToIbasesAfterLocalChange();
    }

    /// <summary>
    /// Удаляет группу. Если внутри группы есть дочерние группы или информационные базы,
    /// удаление запрещается: сначала нужно очистить содержимое группы.
    /// </summary>
    private void DeleteGroup(Group group)
    {
        // Проверяем наличие дочерних групп.
        var subgroupCount = Groups.Count(g =>
            string.Equals(g.ParentId, group.Id, StringComparison.OrdinalIgnoreCase));

        // Проверяем наличие баз в группе и всех её подгруппах (по полному пути группы).
        var groupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectGroupPaths(group.Id, groupPaths);
        var infobaseCount = Infobases.Count(ib =>
            !string.IsNullOrWhiteSpace(ib.Group) &&
            groupPaths.Contains(ib.Group.Trim()));

        // Удаление группы, содержащей подгруппы или базы, запрещено.
        if (subgroupCount > 0 || infobaseCount > 0)
        {
            var reasons = new List<string>();
            if (subgroupCount > 0)
                reasons.Add($"подгрупп: {subgroupCount}");
            if (infobaseCount > 0)
                reasons.Add($"информационных баз: {infobaseCount}");

            _dialogs.ShowWarning(
                $"Невозможно удалить группу «{group.Name}».\n\n" +
                "Внутри группы (включая вложенные подгруппы) находится:\n" +
                string.Join("\n", reasons.Select(r => $"• {r}")) + ".\n\n" +
                "Сначала удалите или переместите содержимое группы, затем удалите её.",
                "Удаление невозможно");
            return;
        }

        if (!_dialogs.Confirm(
            $"Удалить группу «{group.Name}»?\n\nЭто действие нельзя отменить.",
            "Подтверждение удаления"))
            return;

        Groups.Remove(group);

        SelectedGroupNode = null;
        SaveGroups();
        RebuildGroupTree();
    }

    /// <summary>
    /// Собирает полные пути указанной группы и всех её потомков в иерархии.
    /// Используется для проверки наличия баз внутри группы перед её удалением.
    /// </summary>
    private void CollectGroupPaths(string groupId, ISet<string> result)
    {
        var descendants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { groupId };
        CollectGroupDescendants(groupId, descendants);

        foreach (var id in descendants)
        {
            var g = Groups.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (g is not null)
            {
                result.Add(GroupHierarchyHelper.GetFullPath(g, Groups));
            }
        }
    }

    /// <summary>
    /// Собирает идентификаторы всех групп-потомков указанной группы.
    /// </summary>
    private void CollectGroupDescendants(string parentId, ISet<string> result)
    {
        var children = Groups
            .Where(g => string.Equals(g.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var child in children)
        {
            if (result.Add(child.Id))
            {
                CollectGroupDescendants(child.Id, result);
            }
        }
    }

    private void ToggleFavorite(object? parameter)
    {
        if (SelectedInfobase is null)
            return;
        ApplyFavoriteToggle(SelectedInfobase);
    }

    private void ToggleFavoriteFor(object? parameter)
    {
        if (parameter is not Infobase infobase)
            return;
        ApplyFavoriteToggle(infobase);
    }

    /// <summary>
    /// Переключает избранное без полной перестройки дерева, если фильтр не скрывает базу.
    /// Иконка обновляется через INotifyPropertyChanged; сохранение — отложенное.
    /// При добавлении в избранное назначается свободный слот Alt+1…9.
    /// </summary>
    private void ApplyFavoriteToggle(Infobase infobase)
    {
        infobase.IsFavorite = !infobase.IsFavorite;

        var key = FavoriteKey(infobase);
        if (infobase.IsFavorite)
        {
            if (!_favoriteHotkeyIds.Contains(key) && _favoriteHotkeyIds.Count < 9)
                _favoriteHotkeyIds.Add(key);
        }
        else
        {
            _favoriteHotkeyIds.Remove(key);
        }

        SyncFavoriteHotkeys();

        // Перестройка нужна только если список/дерево должны изменить состав
        // (фильтр «Только избранные», поиск, отбор по тегу).
        if (ShowFavoritesOnly
            || !string.IsNullOrWhiteSpace(SearchText)
            || HasActiveTagFilter)
        {
            InfobasesView.Refresh();
            RebuildGroupTree();
        }

        ScheduleSave();
        ScheduleSaveSettings();
    }

    /// <summary>Запускает избранную базу по номеру горячей клавиши (1–9 → Alt+N).</summary>

    /// <summary>Недавние базы по дате последнего запуска (для меню трея).</summary>
    public System.Collections.Generic.IReadOnlyList<Models.Infobase> GetRecentInfobases(int count = 7)
    {
        return Infobases
            .Where(i => i.LastLaunchDate.HasValue)
            .OrderByDescending(i => i.LastLaunchDate)
            .Take(Math.Max(1, count))
            .ToList();
    }

    /// <summary>Запуск базы по Id из меню трея (без активации окна).</summary>
    public void LaunchInfobaseById(string id, bool isConfigurator)
    {
        var ib = Infobases.FirstOrDefault(i => i.Id == id);
        if (ib is null) return;

        bool ok;
        if (isConfigurator)
            ok = _launcher.Launch(ib, Services.OneCLaunchMode.Configurator);
        else
            ok = LaunchEnterpriseWithSessionOverrides(ib);

        if (ok)
        {
            ib.LastLaunchDate = DateTime.Now;
            ib.AddLaunchHistory(isConfigurator ? "Configurator" : "Enterprise", "tray");
            InfobasesView.Refresh();
            Save();
            _logger.Info($"[tray] Запущена «{ib.Name}» ({(isConfigurator ? "Конфигуратор" : "Предприятие")})");
        }
        else
        {
            _logger.Warn($"[tray] Не удалось запустить «{ib.Name}»");
        }
    }

    public void LaunchFavoriteByHotkey(int number)
    {
        if (number < 1 || number > 9 || number > _favoriteHotkeyIds.Count)
            return;
        var key = _favoriteHotkeyIds[number - 1];
        var ib = FindByFavoriteKey(key);
        if (ib is null)
            return;

        SelectedInfobase = ib;
        // Запуск напрямую через лаунчер — не зависит от CanExecute команд UI.
        var ok = _launcher.Launch(ib, OneCLaunchMode.Enterprise);
        if (ok)
        {
            ib.LastLaunchDate = DateTime.Now;
            ScheduleSave();
            _logger.Info($"Запущена избранная база «{ib.Name}» по Alt+{number}");
        }
        else
        {
            _logger.Warn($"Не удалось запустить избранную базу «{ib.Name}» по Alt+{number}");
        }
    }

    /// <summary>Возвращает номер горячей клавиши (1–9) для базы или 0, если не назначен.</summary>
    public int GetFavoriteHotkeyNumber(Infobase infobase)
    {
        var key = FavoriteKey(infobase);
        var idx = _favoriteHotkeyIds.IndexOf(key);
        return idx >= 0 ? idx + 1 : 0;
    }

    /// <summary>
    /// Устанавливает поле сортировки. Повторный клик по тому же полю меняет направление.
    /// </summary>
    public void SetSortField(string field)
    {
        if (string.Equals(_sortField, field, StringComparison.OrdinalIgnoreCase))
            _sortAscending = !_sortAscending;
        else
        {
            _sortField = field;
            _sortAscending = field != "LastLaunchDate"; // дату удобнее сначала по убыванию
        }
        ApplySortDescriptions();
        InfobasesView.Refresh();
        RebuildGroupTree();
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(SortAscending));
        ScheduleSaveSettings();
    }

    private void ApplySortDescriptions()
    {
        InfobasesView.SortDescriptions.Clear();
        // Закреплённые всегда сверху.
        InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.GroupSortOrder), ListSortDirection.Ascending));
        var dir = _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        switch (_sortField)
        {
            case "LastLaunchDate":
                InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.LastLaunchDate), dir));
                InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.Name), ListSortDirection.Ascending));
                break;
            case "SortOrder":
                InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.SortOrder), dir));
                InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.Name), ListSortDirection.Ascending));
                break;
            default:
                InfobasesView.SortDescriptions.Add(new SortDescription(nameof(Infobase.Name), dir));
                break;
        }
    }

    /// <summary>
    /// Сортирует базы согласно текущему полю (_sortField) и направлению.
    /// Закреплённые (GroupSortOrder) всегда идут первыми.
    /// </summary>
    private IEnumerable<Infobase> ApplyCurrentSort(IEnumerable<Infobase> source)
    {
        var query = source.OrderBy(i => i.GroupSortOrder);
        query = _sortField switch
        {
            "LastLaunchDate" when _sortAscending =>
                query.ThenBy(i => i.LastLaunchDate ?? DateTime.MinValue)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "LastLaunchDate" =>
                query.ThenByDescending(i => i.LastLaunchDate ?? DateTime.MinValue)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "SortOrder" when _sortAscending =>
                query.ThenBy(i => i.SortOrder)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "SortOrder" =>
                query.ThenByDescending(i => i.SortOrder)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ when _sortAscending =>
                query.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ =>
                query.ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };
        return query;
    }

    /// <summary>Стабильный ключ базы для слотов горячих клавиш.</summary>
    private static string FavoriteKey(Infobase ib) =>
        !string.IsNullOrEmpty(ib.Id) ? ib.Id : "name:" + ib.Name;

    /// <summary>
    /// Назначает слоты Alt+1…9 существующим избранным и обновляет номера на карточках.
    /// </summary>
    private void SyncFavoriteHotkeys()
    {
        try
        {
            if (Infobases is null)
                return;

            // Удаляем ключи, которых больше нет в списке баз.
            _favoriteHotkeyIds.RemoveAll(key =>
                !Infobases.Any(ib => FavoriteKey(ib) == key));

            // Добавляем избранные без слота (в порядке имени).
            foreach (var ib in Infobases.Where(i => i.IsFavorite).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                var key = FavoriteKey(ib);
                if (!_favoriteHotkeyIds.Contains(key) && _favoriteHotkeyIds.Count < 9)
                    _favoriteHotkeyIds.Add(key);
            }

            // Проставляем номера на объектах Infobase для UI.
            foreach (var ib in Infobases)
            {
                var key = FavoriteKey(ib);
                var idx = _favoriteHotkeyIds.IndexOf(key);
                ib.FavoriteHotkeyNumber = idx >= 0 ? idx + 1 : 0;
            }

            FavoriteHotkeysChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // не роняем приложение из‑за избранного
        }
    }

    /// <summary>Публичный доступ к упорядоченному списку ключей избранного (для настроек).</summary>
    public IReadOnlyList<string> FavoriteHotkeyIds => _favoriteHotkeyIds;

    /// <summary>Возвращает базу по ключу слота избранного.</summary>
    public Infobase? FindByFavoriteKey(string key) =>
        Infobases.FirstOrDefault(ib => FavoriteKey(ib) == key);

    /// <summary>
    /// Заменяет порядок слотов горячих клавиш (из окна настроек).
    /// </summary>
    public void SetFavoriteHotkeyOrder(IEnumerable<string> orderedKeys)
    {
        _favoriteHotkeyIds.Clear();
        foreach (var key in orderedKeys.Where(k => !string.IsNullOrEmpty(k)).Take(9))
            _favoriteHotkeyIds.Add(key);
        SyncFavoriteHotkeys();
        ScheduleSaveSettings();
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (SetProperty(ref _closeToTray, value))
                ScheduleSaveSettings();
        }
    }

    public bool ShowTrayIcon
    {
        get => _showTrayIcon;
        set
        {
            if (SetProperty(ref _showTrayIcon, value))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Esc сворачивает окно в трей (если значок в трее включён).</summary>
    public bool EscapeToTray
    {
        get => _escapeToTray;
        set
        {
            if (SetProperty(ref _escapeToTray, value))
                ScheduleSaveSettings();
        }
    }

    
    /// <summary>Обновляет список каталогов шаблонов из настроек.</summary>
    public void SetTemplateCatalogPaths(System.Collections.Generic.IEnumerable<string> paths)
    {
        _templateCatalogPaths = paths?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new System.Collections.Generic.List<string>();
        Services.OneCTemplateService.SetUserTemplatePaths(_templateCatalogPaths);
        ScheduleSaveSettings();
    }

public string HotkeyEnterprise
    {
        get => _hotkeyEnterprise;
        set
        {
            if (SetProperty(ref _hotkeyEnterprise, NormalizeHotkey(value, "F3")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyConfigurator
    {
        get => _hotkeyConfigurator;
        set
        {
            if (SetProperty(ref _hotkeyConfigurator, NormalizeHotkey(value, "F4")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyFavorite
    {
        get => _hotkeyFavorite;
        set
        {
            if (SetProperty(ref _hotkeyFavorite, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyEdit
    {
        get => _hotkeyEdit;
        set
        {
            if (SetProperty(ref _hotkeyEdit, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyDelete
    {
        get => _hotkeyDelete;
        set
        {
            if (SetProperty(ref _hotkeyDelete, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyClearCache
    {
        get => _hotkeyClearCache;
        set
        {
            if (SetProperty(ref _hotkeyClearCache, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyAdd
    {
        get => _hotkeyAdd;
        set
        {
            if (SetProperty(ref _hotkeyAdd, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    public string HotkeyPin
    {
        get => _hotkeyPin;
        set
        {
            if (SetProperty(ref _hotkeyPin, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    private static string NormalizeHotkey(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public string SortField => _sortField;
    public bool SortAscending => _sortAscending;

    private void TogglePin(object? parameter)
    {
        if (SelectedInfobase is null)
            return;
        ApplyPinToggle(SelectedInfobase);
    }

    private void TogglePinFor(object? parameter)
    {
        if (parameter is not Infobase infobase)
            return;
        ApplyPinToggle(infobase);
    }

    /// <summary>
    /// Переключает закрепление: обновляет блок «Закреплённые» точечно, без полной
    /// перестройки дерева групп. Сохранение на диск — отложенное (не блокирует UI).
    /// </summary>
    private void ApplyPinToggle(Infobase infobase)
    {
        infobase.IsPinned = !infobase.IsPinned;
        UpdatePinnedSection(infobase);
        ScheduleSave();
    }

    /// <summary>
    /// Добавляет или убирает базу в узле «Закреплённые» без RebuildGroupTree.
    /// </summary>
    private void UpdatePinnedSection(Infobase infobase)
    {
        // При отключённой группировке закрепление влияет только на данные.
        if (!_groupByGroup)
        {
            return;
        }

        const string pinnedName = "Закреплённые";
        var pinned = GroupNodes.FirstOrDefault(n => n.Group is null && n.DisplayName == pinnedName);

        if (infobase.IsPinned)
        {
            if (pinned is null)
            {
                pinned = new GroupNodeViewModel(null, displayName: pinnedName) { IsExpanded = true };
                pinned.Infobases.Add(infobase);
                pinned.PopulateItems(); // NotifyCountChanged внутри
                GroupNodes.Insert(0, pinned);
                _groupNodes.Insert(0, pinned);
                return;
            }

            if (!pinned.Infobases.Contains(infobase))
            {
                pinned.Infobases.Add(infobase);
                pinned.PopulateItems();
            }
            else
            {
                pinned.NotifyCountChanged();
            }
        }
        else if (pinned is not null)
        {
            pinned.Infobases.Remove(infobase);
            if (pinned.Infobases.Count == 0)
            {
                GroupNodes.Remove(pinned);
                _groupNodes.Remove(pinned);
            }
            else
            {
                pinned.PopulateItems();
            }
        }
    }

    /// <summary>
    /// Откладывает сохранение списка баз (debounce), чтобы серия быстрых кликов
    /// по звёздочке/закреплению не писала JSON на каждый клик и не блокировала UI.
    /// </summary>
    private void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveDebounceCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounceMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;

                // Снимок коллекции на UI-потоке, запись файла — в фоне.
                List<Infobase> snapshot = Application.Current?.Dispatcher is { } dispatcher
                    ? await dispatcher.InvokeAsync(() => Infobases.ToList())
                    : Infobases.ToList();

                if (token.IsCancellationRequested)
                    return;

                await _repository.SaveAsync(snapshot, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Новый клик отменил предыдущее сохранение — нормально.
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка отложенного сохранения баз", ex);
                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        _dialogs.ShowError($"Не удалось сохранить список баз.\n{ex.Message}", "Ошибка сохранения"));
                }
                catch
                {
                    // ignore secondary UI failures
                }
            }
        }, token);
    }

    /// <summary>
    /// Единая точка запуска 1С. parameter — LaunchKind, строка имени enum или null (Enterprise).
    /// Для Enterprise учитываются переопределения «Текущая сессия» (клиент и разрядность).
    /// </summary>
    private void Launch(object? parameter)
    {
        if (SelectedInfobase is null)
            return;

        var kind = ResolveLaunchKind(parameter);
        bool ok;
        switch (kind)
        {
            case LaunchKind.Configurator:
                ok = _launcher.Launch(SelectedInfobase, OneCLaunchMode.Configurator);
                break;
            case LaunchKind.Thin32:
                ok = _launcher.Launch(SelectedInfobase, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x86);
                break;
            case LaunchKind.Thick32:
                ok = _launcher.Launch(SelectedInfobase, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x86);
                break;
            case LaunchKind.Thin64:
                ok = _launcher.Launch(SelectedInfobase, OneCLaunchMode.Enterprise, OneCClientType.Thin, OneCArchitecture.x64);
                break;
            case LaunchKind.Thick64:
                ok = _launcher.Launch(SelectedInfobase, OneCLaunchMode.Enterprise, OneCClientType.Thick, OneCArchitecture.x64);
                break;
            default:
                ok = LaunchEnterpriseWithSessionOverrides(SelectedInfobase);
                break;
        }

        if (ok)
        {
            SelectedInfobase.AddLaunchHistory(kind.ToString(),
                $"клиент={_sessionClientMode}, арх={_sessionArchitecture}");
            InfobasesView.Refresh();
            Save();
            _logger.Info($"Запущена база «{SelectedInfobase.Name}» ({kind}, клиент={_sessionClientMode}, арх={_sessionArchitecture})");
        }
        else
        {
            _logger.Warn($"Не удалось запустить базу «{SelectedInfobase.Name}» ({kind})");
        }
    }

    /// <summary>
    /// Запуск 1С:Предприятие с учётом переключателей «Текущая сессия».
    /// </summary>
    private bool LaunchEnterpriseWithSessionOverrides(Infobase ib)
    {
        // Полностью «Авто» — стандартная логика по настройкам базы.
        if (_sessionClientMode == SessionClientMode.Auto &&
            _sessionArchitecture == SessionArchitectureMode.Auto)
        {
            return _launcher.Launch(ib, OneCLaunchMode.Enterprise);
        }

        OneCClientType? client = _sessionClientMode switch
        {
            SessionClientMode.Thin => OneCClientType.Thin,
            SessionClientMode.Thick => OneCClientType.Thick,
            SessionClientMode.Ordinary => OneCClientType.Thick,
            _ => ResolveClientFromInfobase(ib)
        };

        var arch = _sessionArchitecture switch
        {
            SessionArchitectureMode.X86 => OneCArchitecture.x86,
            SessionArchitectureMode.X64 => OneCArchitecture.x64,
            _ => OneCLauncher.ResolveArchitecture(ib.Architecture, ib.PlatformVersion)
        };

        return _launcher.Launch(ib, OneCLaunchMode.Enterprise, client, arch);
    }

    /// <summary>Тип клиента из настройки базы (LaunchMode).</summary>
    private static OneCClientType? ResolveClientFromInfobase(Infobase ib)
    {
        if (string.Equals(ib.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(ib.LaunchMode, "Толстый клиент", StringComparison.OrdinalIgnoreCase))
            return OneCClientType.Thick;
        if (string.Equals(ib.LaunchMode, "Тонкий клиент", StringComparison.OrdinalIgnoreCase))
            return OneCClientType.Thin;
        // Веб и прочее — без принудительного /RunMode
        return null;
    }

    private static LaunchKind ResolveLaunchKind(object? parameter) => parameter switch
    {
        LaunchKind k => k,
        string s when Enum.TryParse<LaunchKind>(s, true, out var parsed) => parsed,
        _ => LaunchKind.Enterprise
    };

    private bool FilterInfobase(object item)
    {
        if (item is not Infobase infobase)
            return false;

        if (_listViewMode == ListViewMode.Favorites && !infobase.IsFavorite)
            return false;
        if (_listViewMode == ListViewMode.Recent && !infobase.LastLaunchDate.HasValue)
            return false;

        if (_activeTagFilterSet.Count > 0
            && !infobase.Tags.Any(t => _activeTagFilterSet.Contains(t)))
            return false;

        var filter = SearchText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(filter))
            return true;

        return infobase.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (infobase.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.Group?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.PlatformVersion?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.ServerDatabaseDisplay?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || (infobase.ConnectionStringDisplay?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || infobase.Tags.Any(t => t.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void Save()
    {
        try
        {
            _repository.Save(Infobases.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения баз", ex);
            throw;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await _repository.SaveAsync(Infobases.ToList()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка асинхронного сохранения баз", ex);
            _dialogs.ShowError($"Не удалось сохранить список баз.\n{ex.Message}", "Ошибка сохранения");
        }
    }

    private void SaveGroups()
    {
        try
        {
            _repository.SaveGroups(Groups.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка сохранения групп", ex);
            throw;
        }
    }

    private async Task SaveGroupsAsync()
    {
        try
        {
            await _repository.SaveGroupsAsync(Groups.ToList()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка асинхронного сохранения групп", ex);
        }
    }

    private void SaveSettings()
    {
        _repository.SaveSettings(new AppSettings
        {
            ShowFavoritesOnly = _showFavoritesOnly,
            GroupByGroup = _groupByGroup,
            Theme = _savedTheme,
            CollapsedGroups = _collapsedGroups.ToList(),
            InstalledPlatformVersions = _installedPlatformVersions,
            AdditionalPlatformSearchPaths = _additionalPlatformSearchPaths,
            NameColumnWidth = _nameColumnWidth,
            VersionColumnWidth = _versionColumnWidth,
            LaunchModeColumnWidth = _launchModeColumnWidth,
            ServerColumnWidth = _serverColumnWidth,
            LastLaunchColumnWidth = _lastLaunchColumnWidth,
            ShowFavoritesButton = _showFavoritesButton,
            ShowPinnedButton = _showPinnedButton,
            ShowTags = _showTags,
            ShowTagFilterPanel = _showTagFilterPanel,
            AllowMultipleInstances = _allowMultipleInstances,
            ShowVersionColumn = _showVersionColumn,
            ShowRightPanelDetails = _showRightPanelDetails,
            ShowSessionLaunchPanel = _showSessionLaunchPanel,
            SessionClientMode = _sessionClientMode.ToString(),
            SessionArchitecture = _sessionArchitecture.ToString(),
            StatusShowConnectionPath = _statusShowConnectionPath,
            StatusShowArchitecture = _statusShowArchitecture,
            StatusShowLaunchMode = _statusShowLaunchMode,
            StatusShowPort = _statusShowPort,
            StatusShowPlatformVersion = _statusShowPlatformVersion,
            StatusShowClientType = _statusShowClientType,
            StatusShowConnectionType = _statusShowConnectionType,
            StatusShowUser = _statusShowUser,
            StatusShowId = _statusShowId,
            ShowLaunchModeColumn = _showLaunchModeColumn,
            ShowServerColumn = _showServerColumn,
            ShowLastLaunchColumn = _showLastLaunchColumn,
            ShowSizeColumn = _showSizeColumn,
            SizeColumnWidth = _sizeColumnWidth,
            WindowWidth = _windowWidth,
            WindowHeight = _windowHeight,
            WindowLeft = _windowLeft,
            WindowTop = _windowTop,
            WindowState = _windowState,
            IbasesSyncMode = _ibasesSyncMode,
            IbasesSyncFilePath = _ibasesSyncFilePath,
            IbasesSyncTrigger = _ibasesSyncTrigger,
            IbasesSyncIntervalMinutes = _ibasesSyncIntervalMinutes,
            IbasesSyncScheduleTime = _ibasesSyncScheduleTime,
            IbasesBackupEnabled = _ibasesBackupEnabled,
            IbasesBackupKeepCount = _ibasesBackupKeepCount,
            CloseToTray = _closeToTray,
            ShowTrayIcon = _showTrayIcon,
            EscapeToTray = _escapeToTray,
            TemplateCatalogPaths = _templateCatalogPaths.ToList(),
            HotkeyEnterprise = _hotkeyEnterprise,
            HotkeyConfigurator = _hotkeyConfigurator,
            HotkeyFavorite = _hotkeyFavorite,
            HotkeyEdit = _hotkeyEdit,
            HotkeyDelete = _hotkeyDelete,
            HotkeyClearCache = _hotkeyClearCache,
            HotkeyAdd = _hotkeyAdd,
            HotkeyPin = _hotkeyPin,
            SortField = _sortField,
            SortAscending = _sortAscending,
            FavoriteHotkeyIds = _favoriteHotkeyIds.ToList()
        });
    }

    /// <summary>
    /// Сохраняет ширины колонок списка баз в настройках.
    /// </summary>
    public void SaveColumnWidths(double nameWidth, double versionWidth, double launchModeWidth, double serverWidth, double lastLaunchWidth)
    {
        NameColumnWidth = nameWidth;
        VersionColumnWidth = versionWidth;
        LaunchModeColumnWidth = launchModeWidth;
        ServerColumnWidth = serverWidth;
        LastLaunchColumnWidth = lastLaunchWidth;
        SaveSettings();
    }

    /// <summary>
    /// Обновляет ширины колонок в памяти (без сохранения в файл).
    /// Используется для синхронизации колонок строк во время перетаскивания разделителя.
    /// </summary>
    public void UpdateColumnWidths(double nameWidth, double versionWidth, double launchModeWidth, double serverWidth, double lastLaunchWidth)
    {
        NameColumnWidth = nameWidth;
        VersionColumnWidth = versionWidth;
        LaunchModeColumnWidth = launchModeWidth;
        ServerColumnWidth = serverWidth;
        LastLaunchColumnWidth = lastLaunchWidth;
    }

    /// <summary>
    /// Сохраняет выбранную тему оформления в настройках.
    /// </summary>
    public void SaveTheme(string theme)
    {
        _savedTheme = theme;
        SaveSettings();
    }

    /// <summary>
    /// Возвращает true, если указанная группа свёрнута в списке баз.
    /// </summary>
    public bool IsGroupCollapsed(string groupName)
    {
        return _collapsedGroups.Contains(groupName);
    }

    /// <summary>
    /// Устанавливает состояние свёрнутости группы и сохраняет его в настройках.
    /// </summary>
    public void SetGroupCollapsed(string groupName, bool collapsed)
    {
        if (collapsed)
        {
            _collapsedGroups.Add(groupName);
        }
        else
        {
            _collapsedGroups.Remove(groupName);
        }
        SaveSettings();
    }

    /// <summary>
    /// Сворачивает/разворачивает группу по кнопке «свернуть»/«развернуть» на узле дерева.
    /// Принимает узел <see cref="GroupNodeViewModel"/> в качестве параметра команды.
    /// Явно переключает состояние развёрнутости узла и сохраняет результат в настройках,
    /// чтобы не зависеть от порядка срабатывания двухсторонней привязки.
    /// </summary>
    private void ToggleGroupExpanded(object? parameter)
    {
        if (parameter is not GroupNodeViewModel node)
            return;

        node.IsExpanded = !node.IsExpanded;
        // Для реальных групп ключом служит полный путь, для служебных узлов
        // («Закреплённые», «Без группы») — отображаемое имя, т.к. пути у них нет.
        var key = string.IsNullOrEmpty(node.FullPath) ? node.DisplayName : node.FullPath;
        SetGroupCollapsed(key, !node.IsExpanded);
    }

    /// <summary>
    /// Сворачивает все группы в дереве баз.
    /// </summary>
    private void CollapseAllGroups(object? parameter)
    {
        SetExpandedDeepSilent(_groupNodes, expanded: false);
        _collapsedGroups.Clear();
        CollectGroupPaths(_groupNodes, _collapsedGroups);
        ScheduleSaveSettings();

        // Сворачиваем только существующие контейнеры (быстро, без пересборки ItemsSource).
        if (Application.Current?.MainWindow is global::Configuration_Management.MainWindow window)
            window.ApplyGroupExpandedState(expand: false);
    }

    /// <summary>
    /// Разворачивает все группы в дереве баз.
    /// </summary>
    private void ExpandAllGroups(object? parameter)
    {
        // Модель сразу в expanded — при создании новых TreeViewItem Binding читает true.
        SetExpandedDeepSilent(_groupNodes, expanded: true);
        _collapsedGroups.Clear();
        ScheduleSaveSettings();

        // По уровням через контейнеры TreeView (без PropertyChanged-лавины и без ReplaceGroupNodes).
        if (Application.Current?.MainWindow is global::Configuration_Management.MainWindow window)
            window.ApplyGroupExpandedState(expand: true);
    }

    /// <summary>Рекурсивно задаёт IsExpanded без уведомлений UI.</summary>
    private static void SetExpandedDeepSilent(IEnumerable<GroupNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.SetExpandedSilent(expanded);
            SetExpandedDeepSilent(node.Children, expanded);
        }
    }

    /// <summary>
    /// Рекурсивно устанавливает состояние развёрнутости всех узлов дерева групп.
    /// </summary>
    private static void CollapseAllNodes(IEnumerable<GroupNodeViewModel> nodes, bool collapse)
    {
        foreach (var node in nodes)
        {
            node.SetExpandedSilent(!collapse);
            CollapseAllNodes(node.Children, collapse);
        }
    }

    /// <summary>
    /// Рекурсивно собирает полные пути всех реальных групп в указанный набор.
    /// </summary>
    private static void CollectGroupPaths(IEnumerable<GroupNodeViewModel> nodes, HashSet<string> target)
    {
        foreach (var node in nodes)
        {
            // Для реальных групп ключом служит полный путь, для служебных узлов
            // («Закреплённые», «Без группы») — отображаемое имя (единый формат с ToggleGroupExpanded).
            var key = string.IsNullOrEmpty(node.FullPath) ? node.DisplayName : node.FullPath;
            target.Add(key);
            CollectGroupPaths(node.Children, target);
        }
    }

    /// <summary>
    /// Перебирает базы с учётом фильтров (избранное, поиск, тег) без ICollectionView.Refresh.
    /// </summary>
    private IEnumerable<Infobase> EnumerateFilteredInfobases()
    {
        var search = SearchText?.Trim() ?? string.Empty;
        var hasSearch = search.Length > 0;
        var hasTags = _activeTagFilterSet.Count > 0;
        var mode = _listViewMode;

        IEnumerable<Infobase> source = Infobases;
        if (mode == ListViewMode.Favorites)
            source = source.Where(i => i.IsFavorite);
        else if (mode == ListViewMode.Recent)
            source = source.Where(i => i.LastLaunchDate.HasValue)
                           .OrderByDescending(i => i.LastLaunchDate);

        foreach (var infobase in source)
        {
            // Несколько тегов: база подходит, если есть хотя бы один из выбранных (OR).
            if (hasTags
                && !infobase.Tags.Any(t => _activeTagFilterSet.Contains(t)))
                continue;

            // Поиск по имени/описанию/пути/серверу работает вместе с тегами (AND).
            if (hasSearch)
            {
                if (!(infobase.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                      || (infobase.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.Group?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.PlatformVersion?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.ServerDatabaseDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (infobase.ConnectionStringDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                      || infobase.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))))
                    continue;
            }

            yield return infobase;
        }
    }

    /// <summary>
    /// Перестраивает дерево групп на основе текущего списка групп и баз.
    /// Закреплённые базы попадают в корневой узел «Закреплённые», базы без группы — в «Без группы».
    /// Группы и подгруппы, не содержащие баз (в том числе при активном фильтре «Только избранные»),
    /// в дерево не попадают.
    /// </summary>
    public void RebuildGroupTree()
    {
        // Один проход по базам без CollectionView.Refresh (он дорогой на больших списках).
        // Учитываем выбранное поле сортировки (_sortField / _sortAscending).
        var filtered = EnumerateFilteredInfobases();
        // В режиме «Недавние» порядок уже по LastLaunchDate; иначе — выбранная сортировка.
        var visible = (_listViewMode == ListViewMode.Recent
            ? filtered
            : ApplyCurrentSort(filtered)).ToList();

        // Когда группировка отключена — показываем плоский список всех баз в одном узле.
        if (!_groupByGroup)
        {
            var flatNode = new GroupNodeViewModel(null, displayName: "Все базы");
            flatNode.SetNotificationsSuppressed(true);
            foreach (var infobase in visible)
                flatNode.Infobases.Add(infobase);
            flatNode.PopulateItems();
            flatNode.SetNotificationsSuppressed(false);
            flatNode.IsExpanded = true;
            _groupNodes = new List<GroupNodeViewModel> { flatNode };
            ReplaceGroupNodes(_groupNodes);
            return;
        }

        var roots = GroupNodeViewModel.BuildTree(Groups);

        var pinnedNode = new GroupNodeViewModel(null, displayName: "Закреплённые");
        var noGroupNode = new GroupNodeViewModel(null, displayName: "Без группы");

        // Индексация по каноническому пути (GetFullPath) и по FullPath узла —
        // после DnD группы пути в памяти и в UI должны совпасть сразу, без перезапуска.
        var pathToNode = new Dictionary<string, GroupNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        void IndexNode(GroupNodeViewModel node)
        {
            if (node.Group is not null)
            {
                // FullPath уже кэшируется в узле — не вызываем GetFullPath по всему списку групп.
                var path = node.FullPath;
                if (!string.IsNullOrEmpty(path))
                {
                    pathToNode[path] = node;
                    var normalized = NormalizeGroupPath(path);
                    if (!string.IsNullOrEmpty(normalized) &&
                        !string.Equals(normalized, path, StringComparison.OrdinalIgnoreCase))
                        pathToNode[normalized] = node;
                }
            }
            foreach (var child in node.Children)
                IndexNode(child);
        }
        foreach (var root in roots)
            IndexNode(root);

        // Без NotifyCountChanged на каждое добавление базы (лавина PropertyChanged).
        foreach (var root in roots)
            root.SetNotificationsSuppressed(true);
        pinnedNode.SetNotificationsSuppressed(true);
        noGroupNode.SetNotificationsSuppressed(true);

        foreach (var infobase in visible)
        {
            if (infobase.IsPinned)
                pinnedNode.Infobases.Add(infobase);

            var groupPath = infobase.Group?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(groupPath))
            {
                noGroupNode.Infobases.Add(infobase);
                continue;
            }

            if (pathToNode.TryGetValue(groupPath, out var node)
                || pathToNode.TryGetValue(NormalizeGroupPath(groupPath), out node))
            {
                node.Infobases.Add(infobase);
                continue;
            }

            noGroupNode.Infobases.Add(infobase);
        }

        foreach (var root in roots)
            root.PopulateItems();
        pinnedNode.PopulateItems();
        noGroupNode.PopulateItems();

        foreach (var root in roots)
            root.SetNotificationsSuppressed(false);
        pinnedNode.SetNotificationsSuppressed(false);
        noGroupNode.SetNotificationsSuppressed(false);

        var next = new List<GroupNodeViewModel>();
        if (pinnedNode.ContainsInfobases)
            next.Add(pinnedNode);
        if (noGroupNode.ContainsInfobases)
            next.Add(noGroupNode);
        foreach (var root in roots)
        {
            if (root.ContainsInfobases)
                next.Add(root);
        }

        _groupNodes = next;

        // Expand/collapse до Replace — один проход построения TreeView.
        if (ShouldAutoExpandGroups())
            ExpandAllNodesWithContent(next);
        else
            ApplyExpandedState(next);

        ReplaceGroupNodes(next);

        // Панель тегов обновляем только если набор тегов мог измениться
        // (не на каждый символ поиска — там уже есть ранний выход, но лишний проход лишний).
        RefreshTagFilterItems();
    }

    /// <summary>
    /// Нужно ли автоматически разворачивать группы с видимыми базами:
    /// при поиске, фильтре по тегам, режиме «Избранное» или «Недавние».
    /// </summary>
    private bool ShouldAutoExpandGroups() =>
        !string.IsNullOrWhiteSpace(SearchText)
        || HasActiveTagFilter
        || _listViewMode == ListViewMode.Favorites
        || _listViewMode == ListViewMode.Recent;

    /// <summary>
    /// Разворачивает узлы дерева, в которых есть базы (или вложенные с базами).
    /// Используется при поиске, фильтре по тегу, избранном и недавних.
    /// </summary>
    private static void ExpandAllNodesWithContent(IEnumerable<GroupNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.ContainsInfobases)
                node.SetExpandedSilent(true);
            ExpandAllNodesWithContent(node.Children);
        }
    }

    /// <summary>
    /// Заменяет содержимое GroupNodes с минимумом лишних уведомлений UI.
    /// </summary>
    private void ReplaceGroupNodes(List<GroupNodeViewModel> next)
    {
        // Новая коллекция вместо Clear/Add: один сброс ItemsSource у TreeView,
        // без промежуточных CollectionChanged на каждый корневой узел.
        GroupNodes = new ObservableCollection<GroupNodeViewModel>(next);
        OnPropertyChanged(nameof(GroupNodes));
    }

    /// <summary>
    /// Отложенное сохранение настроек (фильтр избранного, группировка и т.п.) без блокировки UI.
    /// </summary>
    private void ScheduleSaveSettings()
    {
        _ = Task.Run(() =>
        {
            try
            {
                // Небольшой debounce, если пользователь быстро щёлкает фильтры.
                Thread.Sleep(150);
                Application.Current?.Dispatcher.Invoke(SaveSettings);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка отложенного сохранения настроек", ex);
            }
        });
    }

    /// <summary>
    /// Применяет сохранённое состояние развёрнутости к узлам дерева.
    /// </summary>
    private void ApplyExpandedState(IEnumerable<GroupNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            // Для реальных групп ключом служит полный путь, для служебных узлов
            // («Закреплённые», «Без группы») — отображаемое имя (единый формат).
            var key = string.IsNullOrEmpty(node.FullPath) ? node.DisplayName : node.FullPath;
            node.SetExpandedSilent(!IsGroupCollapsed(key));
            ApplyExpandedState(node.Children);
        }
    }

    /// <summary>
    /// Рекурсивно ищет узел дерева групп по идентификатору группы.
    /// </summary>
    private GroupNodeViewModel? FindGroupNode(GroupNodeViewModel node, string groupId)
    {
        if (node.Group is not null && string.Equals(node.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }
        foreach (var child in node.Children)
        {
            var found = FindGroupNode(child, groupId);
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Перемещает группу на позицию другой группы, переупорядочивая элементы в списке баз.
    /// </summary>
    public void MoveGroup(string sourceGroup, string targetGroup)
    {
        if (string.IsNullOrEmpty(sourceGroup) || string.IsNullOrEmpty(targetGroup))
            return;
        if (string.Equals(sourceGroup, targetGroup, StringComparison.OrdinalIgnoreCase))
            return;

        // Собираем элементы перетаскиваемой группы.
        var sourceItems = Infobases
            .Where(i => string.Equals(i.GroupDisplay, sourceGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sourceItems.Count == 0)
            return;

        // Удаляем элементы перетаскиваемой группы из коллекции.
        foreach (var item in sourceItems)
        {
            Infobases.Remove(item);
        }

        // Находим индекс первого элемента целевой группы в обновлённой коллекции.
        var targetIndex = Infobases
            .ToList()
            .FindIndex(i => string.Equals(i.GroupDisplay, targetGroup, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            targetIndex = Infobases.Count;
        }

        // Вставляем элементы перетаскиваемой группы на позицию целевой группы.
        for (var i = 0; i < sourceItems.Count; i++)
        {
            Infobases.Insert(targetIndex + i, sourceItems[i]);
        }

        InfobasesView.Refresh();
        Save();
        RebuildGroupTree();
    }

    /// <summary>
    /// Открывает окно настроек приложения (платформы, группы, дополнительные функции).
    /// </summary>
    private void OpenSettings(object? parameter)
    {
        var dialog = new SettingsWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Открывает диалог ввода ссылки на информационную базу (аналог «Перейти по ссылке»
    /// в стандартном загрузчике 1С) и запускает указанную базу в 1С:Предприятие.
    /// </summary>
    private void OpenInfobaseByLink(object? parameter)
    {
        var dialog = new LinkInputWindow
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Result))
            return;

        var link = dialog.Result;
        _logger.Info($"Запуск 1С по ссылке: {link}");
        OneCLauncher.LaunchByLink(link);
    }

    /// <summary>
    /// Показывает окно с предложением загрузить базы из файла ibases.v8i,
    /// если список информационных баз пуст. При согласии выполняет импорт.
    /// </summary>
    private void PromptImportFromIbasesV8i()
    {
        if (!_dialogs.Confirm("Список информационных баз пуст.\n\n" +
            "Хотите загрузить базы из стандартного файла 1С (ibases.v8i)?",
            "Загрузка баз"))
            return;

        // Сначала пытаемся найти файл ibases.v8i автоматически в стандартном месте.
        var filePath = IbasesV8iImporter.FindDefaultPath();

        // Если файл не найден — предлагаем выбрать его вручную.
        if (filePath is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл списка баз 1С (ibases.v8i)",
                Filter = "Файл списка баз 1С (*.v8i)|*.v8i|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            filePath = dialog.FileName;
        }

        try
        {
            var importResult = _ibasesSync.Import(filePath, Infobases, Groups);

            InfobasesView.Refresh();
            Save();
            SaveGroups();
            RebuildGroupTree();

            _dialogs.ShowInfo($"Импорт завершён.\n\n" +
                $"Добавлено новых баз: {importResult.Added}\n" +
                $"Обновлено баз: {importResult.Updated}\n" +
                $"Пропущено (отключено): {importResult.Skipped}\n" +
                $"Создано новых групп: {importResult.GroupsCreated}",
                "Импорт из ibases.v8i");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось выполнить импорт.\n{ex.Message}",
                "Ошибка импорта");
        }
    }

    /// <summary>
    /// Ручная синхронизация с ibases.v8i по режиму из настроек приложения.
    /// Если синхронизация отключена — сообщает об этом и предлагает открыть настройки.
    /// </summary>
    private void SynchronizeWithIbasesManual(object? parameter)
    {
        if (_ibasesSyncMode == IbasesSyncMode.None)
        {
            if (_dialogs.Confirm(
                    "Синхронизация с файлом ibases.v8i отключена в настройках.\n\n" +
                    "Открыть настройки, чтобы выбрать режим (загрузка / выгрузка / двусторонняя)?",
                    "Синхронизация ibases.v8i"))
            {
                OpenSettings(null);
            }
            return;
        }

        var filePath = ResolveIbasesFilePath();
        if (filePath is null)
        {
            _dialogs.ShowInfo(
                "Не удалось определить путь к файлу ibases.v8i.\n" +
                "Укажите путь на вкладке «ibases.v8i» в настройках.",
                "Синхронизация ibases.v8i");
            return;
        }

        var modeText = _ibasesSyncMode switch
        {
            IbasesSyncMode.Import => "загрузка из файла в приложение",
            IbasesSyncMode.Export => "выгрузка из приложения в файл",
            IbasesSyncMode.Both => "двусторонняя (загрузка и выгрузка)",
            _ => "неизвестный режим"
        };

        try
        {
            // Сбрасываем предыдущее сообщение, чтобы увидеть актуальный результат.
            SyncMessage = string.Empty;
            var ok = SynchronizeWithIbases();

            var status = string.IsNullOrWhiteSpace(SyncMessage)
                ? (ok ? "Синхронизация выполнена (изменений нет или режим не потребовал операций)." : "Синхронизация не выполнена.")
                : SyncMessage;

            _dialogs.ShowInfo(
                $"Режим: {modeText}\nФайл: {filePath}\n\n{status}",
                "Синхронизация ibases.v8i");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось выполнить синхронизацию.\n{ex.Message}",
                "Ошибка синхронизации");
        }
    }

    private void ImportFromIbasesV8i(object? parameter)
    {
        // Сначала пытаемся найти файл ibases.v8i автоматически в стандартном месте.
        var filePath = IbasesV8iImporter.FindDefaultPath();

        // Если файл не найден — предлагаем выбрать его вручную.
        if (filePath is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл списка баз 1С (ibases.v8i)",
                Filter = "Файл списка баз 1С (*.v8i)|*.v8i|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            filePath = dialog.FileName;
        }

        try
        {
            var result = _ibasesSync.Import(filePath, Infobases, Groups);

            InfobasesView.Refresh();
            Save();
            SaveGroups();
            RebuildGroupTree();

            _dialogs.ShowInfo($"Импорт завершён.\n\n" +
                $"Добавлено новых баз: {result.Added}\n" +
                $"Обновлено баз: {result.Updated}\n" +
                $"Пропущено (отключено): {result.Skipped}\n" +
                $"Создано новых групп: {result.GroupsCreated}",
                "Импорт из ibases.v8i");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось выполнить импорт.\n{ex.Message}",
                "Ошибка импорта");
        }
    }

    /// <summary>
    /// Экспортирует список информационных баз в выбранный JSON-файл.
    /// </summary>
    private void ExportInfobases(object? parameter)
    {
        if (Infobases.Count == 0)
        {
            _dialogs.ShowInfo("Список информационных баз пуст. Экспортировать нечего.",
                "Экспорт списка баз");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт списка информационных баз",
            Filter = "JSON-файл (*.json)|*.json|Все файлы (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "infobases_export.json",
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var exportData = new InfobaseExportData
            {
                Infobases = Infobases.ToList(),
                Groups = Groups.ToList()
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            });
            File.WriteAllText(dialog.FileName, json);

            _dialogs.ShowInfo($"Список информационных баз успешно экспортирован.\n\n" +
                $"Количество баз: {Infobases.Count}\n" +
                $"Количество групп: {Groups.Count}\n" +
                $"Файл: {dialog.FileName}",
                "Экспорт списка баз");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось выполнить экспорт.\n{ex.Message}",
                "Ошибка экспорта");
        }
    }

    /// <summary>
    /// Загружает список информационных баз из выбранного JSON-файла,
    /// заменяя текущий список.
    /// </summary>
    private void ImportInfobases(object? parameter)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Загрузка списка информационных баз",
            Filter = "JSON-файл (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);

            // Пытаемся загрузить новый формат (базы + группы).
            InfobaseExportData? exportData = null;
            try
            {
                exportData = JsonSerializer.Deserialize<InfobaseExportData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                // Несовместимый формат — обрабатываем ниже.
            }

            List<Infobase> loaded;
            List<Group> loadedGroups;

            if (exportData != null && exportData.Infobases.Count > 0)
            {
                loaded = exportData.Infobases;
                loadedGroups = exportData.Groups;
            }
            else
            {
                // Старый формат: файл содержит только список баз.
                loaded = JsonSerializer.Deserialize<List<Infobase>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<Infobase>();
                loadedGroups = new List<Group>();
            }

            if (loaded.Count == 0)
            {
                _dialogs.ShowWarning("В выбранном файле не найдено ни одной информационной базы.",
                    "Загрузка списка баз");
                return;
            }

            if (!_dialogs.Confirm($"Загрузить {loaded.Count} информационных баз и {loadedGroups.Count} групп из файла?\n\n" +
                "Текущий список баз и групп будет заменён.",
                "Загрузка списка баз"))
                return;

            Infobases.Clear();
            foreach (var infobase in loaded)
            {
                Infobases.Add(infobase);
            }

            Groups.Clear();
            foreach (var group in loadedGroups)
            {
                Groups.Add(group);
            }

            SelectedInfobase = null;
            InfobasesView.Refresh();
            Save();
            SaveGroups();
            RebuildGroupTree();

            _dialogs.ShowInfo($"Список информационных баз успешно загружен.\n\n" +
                $"Количество баз: {loaded.Count}\n" +
                $"Количество групп: {loadedGroups.Count}",
                "Загрузка списка баз");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось выполнить загрузку.\n{ex.Message}",
                "Ошибка загрузки");
        }
    }

    /// <summary>
    /// Очищает весь список информационных баз и групп.
    /// </summary>
    private void ClearAllInfobases(object? parameter)
    {
        if (Infobases.Count == 0 && Groups.Count == 0)
        {
            _dialogs.ShowInfo("Список информационных баз уже пуст.",
                "Очистка списка баз");
            return;
        }

        if (!_dialogs.Confirm($"Очистить весь список информационных баз?\n\n" +
            $"Будет удалено баз: {Infobases.Count}\n" +
            $"Будет удалено групп: {Groups.Count}\n\n" +
            "Это действие необратимо.",
            "Очистка списка баз"))
            return;

        Infobases.Clear();
        Groups.Clear();
        SelectedInfobase = null;
        InfobasesView.Refresh();
        Save();
        SaveGroups();
        RebuildGroupTree();

        _dialogs.ShowInfo("Список информационных баз очищен.",
            "Очистка списка баз");
    }

    private void CopyConnectionString(object? parameter)
    {
        if (SelectedInfobase is null)
            return;

        try
        {
            // Для файловой базы копируем путь в кавычках без префикса File=,
            // для клиент-серверной — строку подключения.
            Clipboard.SetText(SelectedInfobase.ConnectionPathDisplay);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось скопировать строку подключения.\n{ex.Message}",
                "Ошибка копирования");
        }
    }

    /// <summary>
    /// Очищает локальный кеш 1С выбранной базы.
    /// </summary>
    private void ClearCache(object? parameter)
    {
        if (SelectedInfobase is null)
            return;

        if (!_dialogs.Confirm($"Очистить локальный кеш 1С для базы «{SelectedInfobase.Name}»?\n\n" +
            "Кеш будет удалён из каталогов %LOCALAPPDATA%\\1C\\1cv8 и %APPDATA%\\1C\\1cv8.\n" +
            "Рекомендуется закрыть все сеансы 1С для этой базы перед очисткой.",
            "Очистка кеша 1С"))
            return;

        try
        {
            var removed = OneCCacheCleaner.Clear(SelectedInfobase);

            if (removed > 0)
            {
                _dialogs.ShowInfo($"Кеш базы «{SelectedInfobase.Name}» очищен.\nУдалено каталогов: {removed}.",
                    "Очистка кеша 1С");
            }
            else
            {
                _dialogs.ShowInfo($"Каталоги кеша для базы «{SelectedInfobase.Name}» не найдены.",
                    "Очистка кеша 1С");
            }
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Не удалось очистить кеш.\n{ex.Message}",
                "Ошибка очистки кеша");
        }
    }

    /// <summary>Открывает каталог файловой ИБ в проводнике Windows.</summary>
    private void OpenInfobaseFolder(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        if (ib.Connection.Type != ConnectionType.File)
        {
            _dialogs.ShowInfo("Открытие каталога доступно только для файловых информационных баз.",
                "Открыть каталог");
            return;
        }

        if (!InfobaseMaintenanceService.OpenInfobaseFolder(ib))
        {
            _dialogs.ShowError(
                $"Не удалось открыть каталог.\nПуть: {ib.Connection.FilePath}",
                "Открыть каталог");
        }
    }

    /// <summary>Создаёт ярлык .lnk на рабочем столе для запуска базы.</summary>
    private void CreateDesktopShortcut(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        if (InfobaseMaintenanceService.CreateDesktopShortcut(ib))
        {
            _dialogs.ShowInfo(
                $"Ярлык для «{ib.Name}» создан на рабочем столе.\n" +
                "Запуск через 1cv8.exe (как в стандартном стартере 1С).",
                "Ярлык");
            _logger.Info($"Создан ярлык 1С на рабочем столе для базы «{ib.Name}»");
        }
        else
        {
            _dialogs.ShowError(
                "Не удалось создать ярлык.\n" +
                "Проверьте, что установлена платформа 1С (1cv8.exe) и у базы указана версия платформы.",
                "Ярлык");
        }
    }

    /// <summary>Удаляет из списка файловые базы, у которых нет 1Cv8.1CD / каталога.</summary>
    private void RemoveMissingFileBases(object? parameter)
    {
        var missing = Infobases.Where(ib => !InfobaseMaintenanceService.FileBaseExists(ib)).ToList();
        if (missing.Count == 0)
        {
            _dialogs.ShowInfo("Все файловые базы на месте. Удалять нечего.",
                "Проверка файловых баз");
            return;
        }

        var preview = string.Join("\n", missing.Take(15).Select(ib => "• " + ib.Name));
        if (missing.Count > 15)
            preview += $"\n… и ещё {missing.Count - 15}";

        if (!_dialogs.Confirm(
                $"Найдено файловых баз без каталога/1Cv8.1CD: {missing.Count}\n\n{preview}\n\nУдалить их из списка?",
                "Удаление отсутствующих баз"))
            return;

        foreach (var ib in missing)
            Infobases.Remove(ib);

        RebuildGroupTree();
        InfobasesView.Refresh();
        Save();
        _logger.Info($"Удалено отсутствующих файловых баз: {missing.Count}");
        _dialogs.ShowInfo($"Удалено из списка: {missing.Count}.", "Удаление отсутствующих баз");
    }

    /// <summary>Завершает процессы 1cv8 / 1cv8c и связанные.</summary>
    private void KillOneCProcesses(object? parameter)
    {
        var count = InfobaseMaintenanceService.CountOneCProcesses();
        if (count == 0)
        {
            _dialogs.ShowInfo("Процессы платформы 1С не найдены.", "Процессы 1С");
            return;
        }

        if (!_dialogs.Confirm(
                $"Будет завершено процессов 1С: примерно {count}.\n\n" +
                "Несохранённые данные в открытых сеансах могут быть потеряны.\nПродолжить?",
                "Завершение процессов 1С"))
            return;

        var killed = InfobaseMaintenanceService.KillOneCProcesses();
        _logger.Info($"Завершено процессов 1С: {killed}");
        _dialogs.ShowInfo($"Завершено процессов: {killed}.", "Процессы 1С");
    }

    /// <summary>Пересчёт размеров файловых баз.</summary>
    private void RefreshFileMetadata()
    {
        foreach (var ib in Infobases)
        {
            if (ib.Connection.Type != ConnectionType.File)
            {
                ib.FileSizeBytes = null;
                continue;
            }
            ib.FileSizeBytes = InfobaseMaintenanceService.CalculateFileBaseSize(ib);
        }
        InfobasesView?.Refresh();
    }

    private void DumpInfobaseDt(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Выгрузка информационной базы (.dt)",
            Filter = "Выгрузка 1С (*.dt)|*.dt|Все файлы (*.*)|*.*",
            FileName = SanitizeFileName(ib.Name) + ".dt"
        };
        if (dlg.ShowDialog() != true) return;

        if (OneCLauncher.RunDesignerBatch(ib, OneCLauncher.DesignerBatchOperation.DumpIB, dlg.FileName))
        {
            ib.AddLaunchHistory("DumpDT", dlg.FileName);
            Save();
            _dialogs.ShowInfo(
                "Запущена выгрузка ИБ в .dt.\nДождитесь закрытия окна конфигуратора / завершения процесса 1cv8.",
                "Выгрузка .dt");
        }
    }

    private void DumpConfigurationCf(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Выгрузка конфигурации (.cf)",
            Filter = "Конфигурация 1С (*.cf)|*.cf|Все файлы (*.*)|*.*",
            FileName = SanitizeFileName(ib.Name) + ".cf"
        };
        if (dlg.ShowDialog() != true) return;

        if (OneCLauncher.RunDesignerBatch(ib, OneCLauncher.DesignerBatchOperation.DumpCfg, dlg.FileName))
        {
            ib.AddLaunchHistory("DumpCF", dlg.FileName);
            Save();
            _dialogs.ShowInfo(
                "Запущена выгрузка конфигурации в .cf.\nДождитесь завершения процесса 1cv8.",
                "Выгрузка .cf");
        }
    }

    private void TestInfobase(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        if (!_dialogs.Confirm(
                $"Запустить тестирование ИБ «{ib.Name}»?\n\n" +
                "Будет выполнен /IBCheckAndRepair -TestOnly в пакетном режиме конфигуратора.",
                "Тестирование ИБ"))
            return;

        if (OneCLauncher.RunDesignerBatch(ib, OneCLauncher.DesignerBatchOperation.TestAndRepair))
        {
            ib.AddLaunchHistory("Test", "");
            Save();
            _dialogs.ShowInfo(
                "Запущено тестирование ИБ.\nСледите за окном конфигуратора / логом операции.",
                "Тестирование ИБ");
        }
    }

    private void ShowLaunchHistory(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null) return;

        if (ib.LaunchHistory == null || ib.LaunchHistory.Count == 0)
        {
            _dialogs.ShowInfo($"История запусков для «{ib.Name}» пуста.", "История запусков");
            return;
        }

        var text = string.Join("\n", ib.LaunchHistory.Select(h => h.Display));
        _dialogs.ShowInfo($"История запусков «{ib.Name}»:\n\n{text}", "История запусков");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "base").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "base" : s;
    }

    /// <summary>
    /// Добавляет тег к выбранной базе.
    /// </summary>
    private void AddTag(object? parameter)
    {
        var infobase = parameter as Infobase ?? SelectedInfobase;
        if (infobase is null)
            return;

        var dialog = new TagInputWindow
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        var tag = dialog.Result?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(tag))
            return;

        if (!infobase.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            infobase.Tags.Add(tag);
            infobase.NotifyTagsChanged();
            ScheduleSave();
            RefreshTagFilterItems();
        }
    }

    /// <summary>
    /// Добавляет тег к базе прямо в строке названия (без отдельного окна).
    /// Параметр приходит как object[] от MultiBinding: [0] = Infobase, [1] = текст тега.
    /// </summary>
    private void AddTagInline(object? parameter)
    {
        if (parameter is not object[] values || values.Length < 2)
            return;

        if (values[0] is not Infobase infobase || values[1] is not string rawTag)
            return;

        var tag = rawTag.Trim();
        if (string.IsNullOrEmpty(tag))
            return;

        if (!infobase.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            infobase.Tags.Add(tag);
            infobase.NotifyTagsChanged();
            ScheduleSave();
            RefreshTagFilterItems();
        }
    }

    /// <summary>
    /// Удаляет тег из базы.
    /// </summary>
    private void RemoveTag(object? parameter)
    {
        // Параметр приходит как object[] от MultiBinding: [0] = Infobase, [1] = тег.
        if (parameter is not object[] values || values.Length < 2)
            return;

        if (values[0] is not Infobase infobase || values[1] is not string tag)
            return;

        infobase.Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        infobase.NotifyTagsChanged();
        ScheduleSave();
        RefreshTagFilterItems();
    }

    /// <summary>
    /// Переключает тег в мультифильтре (можно выбрать несколько).
    /// </summary>
    private void SearchByTag(object? parameter)
    {
        if (parameter is not string tag || string.IsNullOrWhiteSpace(tag))
            return;

        var existing = _activeTagFilters.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            _activeTagFilters.Remove(existing);
        else
            _activeTagFilters.Add(tag);

        SyncActiveTagFilterSet();
        OnPropertyChanged(nameof(HasActiveTagFilter));
        // Обновляем подсветку чипов тегов — иначе визуально фильтр «остаётся».
        RefreshTagFilterItems();
        RebuildGroupTree();
    }

    /// <summary>
    /// Очищает поле поиска (теги не трогает).
    /// </summary>
    private void ClearSearch(object? parameter)
    {
        // Отменяем отложенную перестройку от набора текста, чтобы не «вернуть» старый фильтр.
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;

        if (!string.IsNullOrEmpty(_searchText))
            _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        RebuildGroupTree();
    }

    /// <summary>
    /// Сбрасывает выбранные теги фильтра.
    /// </summary>
    private void ClearTagFilters(object? parameter)
    {
        if (_activeTagFilters.Count == 0)
            return;
        _activeTagFilters.Clear();
        SyncActiveTagFilterSet();
        OnPropertyChanged(nameof(HasActiveTagFilter));
        // Важно: пересоздать TagFilterItems с IsSelected=false, иначе чипы остаются «включёнными».
        RefreshTagFilterItems();
        RebuildGroupTree();
    }

    /// <summary>
    /// Нормализует путь группы: единый разделитель « / », обрезка пробелов.
    /// </summary>
    private static string NormalizeGroupPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var parts = path
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);
        return string.Join(GroupHierarchyHelper.PathSeparator, parts);
    }

    /// <summary>
    /// Перемещает базу в указанную группу (полный путь).
    /// <paramref name="insertBefore"/> — база, перед которой вставить (null = в конец группы).
    /// </summary>
    public void MoveInfobaseToGroup(Infobase infobase, string groupFullPath, Infobase? insertBefore = null)
    {
        var targetPath = groupFullPath ?? string.Empty;
        var targetNorm = NormalizeGroupPath(targetPath);
        infobase.Group = string.IsNullOrEmpty(targetNorm) ? targetPath : targetNorm;

        // Соседи в целевой группе (кроме переносимой).
        var siblings = Infobases
            .Where(i => !ReferenceEquals(i, infobase)
                        && string.Equals(NormalizeGroupPath(i.Group), targetNorm,
                            StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (insertBefore is not null
            && siblings.Any(s => ReferenceEquals(s, insertBefore)
                                 || string.Equals(s.Id, insertBefore.Id, StringComparison.OrdinalIgnoreCase)
                                    && !string.IsNullOrEmpty(insertBefore.Id)))
        {
            var index = siblings.FindIndex(s =>
                ReferenceEquals(s, insertBefore)
                || (string.Equals(s.Id, insertBefore.Id, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(insertBefore.Id)));
            siblings.Insert(Math.Max(0, index), infobase);
        }
        else
        {
            siblings.Add(infobase);
        }

        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = (i + 1) * 10;

        Save();
        RebuildGroupTree();
        OnPropertyChanged(nameof(AvailableTags));
    }

    /// <summary>
    /// Перемещает группу под другую группу (или в корень при пустом newParentId)
    /// вместе со всеми вложенными подгруппами и информационными базами.
    /// Обновляет ParentId и полные пути Infobase.Group у всей подветки.
    /// </summary>
    public void MoveGroupUnder(Group group, string newParentId)
    {
        newParentId ??= string.Empty;
        if (string.Equals(group.Id, newParentId, StringComparison.OrdinalIgnoreCase))
            return;

        // Нельзя сделать родителем потомка этой группы (иначе цикл в иерархии).
        if (!string.IsNullOrEmpty(newParentId)
            && GroupHierarchyHelper.IsAncestorOrSelf(newParentId, group.Id, Groups))
            return;

        // Старые полные пути: сама группа + все потомки (до смены ParentId).
        var oldPathsById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var subtreeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { group.Id };
        CollectGroupDescendants(group.Id, subtreeIds);
        foreach (var id in subtreeIds)
        {
            var g = Groups.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (g is not null)
                oldPathsById[id] = GroupHierarchyHelper.GetFullPath(g, Groups);
        }

        var oldRootPath = oldPathsById.TryGetValue(group.Id, out var orp) ? orp : string.Empty;
        var oldRootNorm = NormalizeGroupPath(oldRootPath);

        // Меняем родителя только у перемещаемой группы; вложенные группы
        // остаются её потомками через свои ParentId и переезжают вместе с ней.
        group.ParentId = newParentId;

        // Новый полный путь самой перемещаемой группы (после смены родителя).
        var newRootPath = GroupHierarchyHelper.GetFullPath(group, Groups);
        var newRootNorm = NormalizeGroupPath(newRootPath);

        // pathRemap: старый путь (и нормализованный) → новый канонический.
        var pathRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Гарантированно добавляем маппинг для самой перемещаемой группы, чтобы базы,
        // находящиеся непосредственно в ней, всегда получили новый путь.
        if (!string.IsNullOrEmpty(oldRootPath)
            && !string.IsNullOrEmpty(newRootPath))
        {
            pathRemap[oldRootPath] = newRootPath;
            pathRemap[oldRootNorm] = newRootPath;
        }

        foreach (var id in subtreeIds)
        {
            var g = Groups.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (g is null || !oldPathsById.TryGetValue(id, out var oldPath))
                continue;
            var newPath = GroupHierarchyHelper.GetFullPath(g, Groups);
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                continue;
            pathRemap[oldPath] = newPath;
            pathRemap[NormalizeGroupPath(oldPath)] = newPath;
        }

        // Обновляем Infobase.Group у всех баз подветки.
        if (pathRemap.Count > 0)
        {
            // Длинные пути первыми — чтобы «A / B» не переписывался как префикс «A».
            var remapByLength = pathRemap
                .OrderByDescending(kv => kv.Key.Length)
                .ToList();

            foreach (var ib in Infobases)
            {
                var current = ib.Group?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(current))
                    continue;

                var currentNorm = NormalizeGroupPath(current);
                string? mapped = null;

                if (pathRemap.TryGetValue(current, out mapped)
                    || pathRemap.TryGetValue(currentNorm, out mapped))
                {
                    ib.Group = mapped;
                    continue;
                }

                // Префикс: база во вложенном пути, которого не было в pathRemap.
                // Всегда работаем через нормализованный путь и нормализованный ключ, чтобы
                // суффикс и итоговый путь получались каноническими и совпадали с FullPath узла.
                // Иначе база не найдёт группу при перестройке дерева и «уедет» в «Без группы».
                foreach (var (oldKey, newKey) in remapByLength)
                {
                    var oldKeyNorm = NormalizeGroupPath(oldKey);
                    if (string.IsNullOrEmpty(oldKeyNorm))
                        continue;
                    var prefix = oldKeyNorm + GroupHierarchyHelper.PathSeparator;
                    if (!currentNorm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var suffix = currentNorm.Substring(oldKeyNorm.Length);
                    ib.Group = newKey + suffix;
                    break;
                }

                // Фолбэк: если путь базы относится к подветке (сама группа или вложенная),
                // но почему-то не попал в pathRemap — пересчитываем его по старому корневому пути.
                // Защищает от потери группы (попадания базы в «Без группы») при любых расхождениях
                // в формате/нормализации пути.
                if (!string.IsNullOrEmpty(oldRootNorm)
                    && !string.IsNullOrEmpty(newRootPath)
                    && (string.Equals(currentNorm, oldRootNorm, StringComparison.OrdinalIgnoreCase)
                        || currentNorm.StartsWith(oldRootNorm + GroupHierarchyHelper.PathSeparator,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    var suffix = currentNorm.Length > oldRootNorm.Length
                        ? currentNorm.Substring(oldRootNorm.Length)
                        : string.Empty;
                    ib.Group = newRootPath + suffix;
                }
            }

            if (_collapsedGroups is { Count: > 0 })
            {
                var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in _collapsedGroups)
                {
                    if (pathRemap.TryGetValue(key, out var mapped)
                        || pathRemap.TryGetValue(NormalizeGroupPath(key), out mapped))
                        updated.Add(mapped);
                    else if (!string.IsNullOrEmpty(oldRootPath)
                             && (key.StartsWith(oldRootPath + GroupHierarchyHelper.PathSeparator,
                                     StringComparison.OrdinalIgnoreCase)
                                 || NormalizeGroupPath(key).StartsWith(oldRootNorm + GroupHierarchyHelper.PathSeparator,
                                     StringComparison.OrdinalIgnoreCase))
                             && pathRemap.TryGetValue(oldRootPath, out var newRoot))
                        updated.Add(newRoot + key.Substring(Math.Min(key.Length, oldRootPath.Length)));
                    else
                        updated.Add(key);
                }
                _collapsedGroups.Clear();
                foreach (var k in updated)
                    _collapsedGroups.Add(k);
            }
        }

        // Всегда сохраняем базы и группы, затем UI — как после перезапуска.
        Save();
        SaveGroups();
        RebuildGroupTree();
    }

    /// <summary>
    /// Применяет настройки приложения (экземпляры, панель тегов).
    /// </summary>
    public void ApplyAppBehaviorSettings(
        bool allowMultipleInstances,
        bool showTagFilterPanel,
        bool closeToTray = false,
        bool showTrayIcon = true,
        string? hotkeyEnterprise = null,
        string? hotkeyConfigurator = null,
        string? hotkeyFavorite = null,
        string? hotkeyEdit = null,
        string? hotkeyDelete = null,
        string? hotkeyClearCache = null,
        string? hotkeyAdd = null,
        string? hotkeyPin = null,
        bool escapeToTray = true)
    {
        _allowMultipleInstances = allowMultipleInstances;
        _showTagFilterPanel = showTagFilterPanel;
        _closeToTray = closeToTray;
        _showTrayIcon = showTrayIcon;
        _escapeToTray = escapeToTray;
        if (hotkeyEnterprise != null) _hotkeyEnterprise = hotkeyEnterprise.Trim();
        if (hotkeyConfigurator != null) _hotkeyConfigurator = hotkeyConfigurator.Trim();
        if (hotkeyFavorite != null) _hotkeyFavorite = hotkeyFavorite.Trim();
        if (hotkeyEdit != null) _hotkeyEdit = hotkeyEdit.Trim();
        if (hotkeyDelete != null) _hotkeyDelete = hotkeyDelete.Trim();
        if (hotkeyClearCache != null) _hotkeyClearCache = hotkeyClearCache.Trim();
        if (hotkeyAdd != null) _hotkeyAdd = hotkeyAdd.Trim();
        if (hotkeyPin != null) _hotkeyPin = hotkeyPin.Trim();
        OnPropertyChanged(nameof(AllowMultipleInstances));
        OnPropertyChanged(nameof(ShowTagFilterPanel));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(ShowTrayIcon));
        OnPropertyChanged(nameof(EscapeToTray));
        OnPropertyChanged(nameof(HotkeyEnterprise));
        OnPropertyChanged(nameof(HotkeyConfigurator));
        OnPropertyChanged(nameof(HotkeyFavorite));
        OnPropertyChanged(nameof(HotkeyEdit));
        OnPropertyChanged(nameof(HotkeyDelete));
        OnPropertyChanged(nameof(HotkeyClearCache));
        OnPropertyChanged(nameof(HotkeyAdd));
        OnPropertyChanged(nameof(HotkeyPin));
        SaveSettings();
    }

    /// <summary>
    /// Уведомляет UI об изменении списка доступных тегов.
    /// </summary>
    public void RefreshAvailableTags()
    {
        RefreshTagFilterItems();
    }

}

/// <summary>Элемент панели тегов с признаком выбора.</summary>
public sealed class TagFilterItem
{
    public TagFilterItem(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }

    public string Name { get; }
    public bool IsSelected { get; }
}
