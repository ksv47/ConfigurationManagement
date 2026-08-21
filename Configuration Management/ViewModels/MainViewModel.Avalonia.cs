#if LINUX
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Главная ViewModel для Avalonia (Linux). Упрощённая, но функциональная версия
/// WPF-<c>MainViewModel</c>: загрузка и сохранение списка баз, группы, поиск и теги,
/// избранное, запуск 1С, переключение темы, синхронизация с ibases.v8i.
/// Коллекции на <see cref="ObservableCollection{T}"/> (без ICollectionView) с ручной
/// фильтрацией/сортировкой.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private readonly IInfobaseRepository _repository;
    private readonly IAppLogger _logger;
    private readonly IDialogService _dialog;
    private readonly IOneCLauncher _launcher;
    private readonly IIbasesSyncService _sync;
    private readonly IPlatformVersionService _platformService;

    private List<Infobase> _allInfobases = new();
    private List<Group> _groups = new();

    private AppSettings _settings = new();

    // ---- Поиск / теги ----
    private string _searchText = string.Empty;
    private bool _showTagFilterPanel = true;

    // ---- Вид списка ----
    private string _listMode = "All"; // All / Favorites / Recent
    private bool _groupByGroup = true;
    private bool _showEmptyGroups;

    // ---- Правая панель ----
    private Infobase? _selectedInfobase;
    private GroupNodeViewModel? _selectedGroupNode;
    private bool _showRightPanelDetails = true;

    // ---- Строка состояния ----
    private string _statusBarInfo = "Готово";
    private string _syncMessage = string.Empty;

    // ---- Тема ----
    private string _themeName = ThemeManager.LightThemeName;

    // ---- Текущая сессия ----
    private string _sessionClient = "Авто";
    private string _sessionArch = "Авто";

    private bool _isExporting;
    private string _exportIndicatorTooltip = string.Empty;

    private readonly LaunchViewModel _launchVm;

    /// <summary>Создаёт главную ViewModel и подключает сервисы.</summary>
    public MainViewModel(
        IInfobaseRepository repository,
        IAppLogger logger,
        IDialogService dialog,
        IOneCLauncher launcher,
        IIbasesSyncService sync,
        IPlatformVersionService platformService)
    {
        _repository = repository;
        _logger = logger;
        _dialog = dialog;
        _launcher = launcher;
        _sync = sync;
        _platformService = platformService;

        GroupNodes = new ObservableCollection<GroupNodeViewModel>();
        AllGroupNodes = new ObservableCollection<GroupNodeViewModel>();
        FlatItems = new ObservableCollection<object>();
        TagFilterItems = new ObservableCollection<TagFilterItem>();

        _launchVm = new LaunchViewModel(
            () => SelectedInfobase,
            launcher,
            logger,
            OnLaunched);

        InitializeCommands();
    }

    // ======================= Коллекции =======================

    /// <summary>Корневые узлы дерева групп для отображения.</summary>
    public ObservableCollection<GroupNodeViewModel> GroupNodes { get; }

    /// <summary>Полный список корневых узлов (до фильтрации по виду/поиску).</summary>
    public ObservableCollection<GroupNodeViewModel> AllGroupNodes { get; }

    /// <summary>Плоский список элементов (для режима «Избранное»/«Недавние»/поиска).</summary>
    public ObservableCollection<object> FlatItems { get; }

    /// <summary>Чипы тегов на панели быстрого отбора.</summary>
    public ObservableCollection<TagFilterItem> TagFilterItems { get; }

    // ======================= Команды =======================

    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand SearchByTagCommand { get; private set; } = null!;
    public ICommand ClearTagFiltersCommand { get; private set; } = null!;
    public ICommand LaunchEnterpriseCommand { get; private set; } = null!;
    public ICommand LaunchConfiguratorCommand { get; private set; } = null!;
    public ICommand EditInfobaseCommand { get; private set; } = null!;
    public ICommand AddInfobaseCommand { get; private set; } = null!;
    public ICommand DeleteInfobaseCommand { get; private set; } = null!;
    public ICommand ToggleFavoriteCommand { get; private set; } = null!;
    public ICommand TogglePinCommand { get; private set; } = null!;
    public ICommand OpenSettingsCommand { get; private set; } = null!;
    public ICommand ExpandAllGroupsCommand { get; private set; } = null!;
    public ICommand CollapseAllGroupsCommand { get; private set; } = null!;
    public ICommand SortGroupsAscendingCommand { get; private set; } = null!;
    public ICommand SortGroupsDescendingCommand { get; private set; } = null!;
    public ICommand SynchronizeWithIbasesCommand { get; private set; } = null!;
    public ICommand ToggleThemeCommand { get; private set; } = null!;
    public ICommand ToggleRightPanelDetailsCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand CopyConnectionStringCommand { get; private set; } = null!;
    public ICommand RefreshAllConfigurationInfoCommand { get; private set; } = null!;
    public ICommand OpenInfobaseFolderCommand { get; private set; } = null!;
    public ICommand CreateDesktopShortcutCommand { get; private set; } = null!;
    public ICommand OpenNativeStarterCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SearchByTagCommand = new RelayCommand(SearchByTag);
        ClearTagFiltersCommand = new RelayCommand(ClearTagFilters);
        LaunchEnterpriseCommand = new RelayCommand(_ => Launch(_launchVm.LaunchCommand, LaunchKind.Enterprise), _ => SelectedInfobase is not null);
        LaunchConfiguratorCommand = new RelayCommand(_ => Launch(_launchVm.LaunchCommand, LaunchKind.Configurator), _ => SelectedInfobase is not null);
        EditInfobaseCommand = new RelayCommand(EditInfobase, _ => SelectedInfobase is not null);
        AddInfobaseCommand = new RelayCommand(AddInfobase);
        DeleteInfobaseCommand = new RelayCommand(DeleteInfobase, _ => SelectedInfobase is not null);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite, _ => SelectedInfobase is not null);
        TogglePinCommand = new RelayCommand(TogglePin, _ => SelectedInfobase is not null);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ExpandAllGroupsCommand = new RelayCommand(ExpandAllGroups);
        CollapseAllGroupsCommand = new RelayCommand(CollapseAllGroups);
        SortGroupsAscendingCommand = new RelayCommand(() => SortGroups(true));
        SortGroupsDescendingCommand = new RelayCommand(() => SortGroups(false));
        SynchronizeWithIbasesCommand = new RelayCommand(SynchronizeWithIbases);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleRightPanelDetailsCommand = new RelayCommand(() => ShowRightPanelDetails = !ShowRightPanelDetails);
        ExitCommand = new RelayCommand(ExitApplication);
        CopyConnectionStringCommand = new RelayCommand(CopyConnectionString, _ => SelectedInfobase is not null);
        RefreshAllConfigurationInfoCommand = new RelayCommand(RefreshAllConfigurationInfo);
        OpenInfobaseFolderCommand = new RelayCommand(OpenInfobaseFolder,
            _ => SelectedInfobase?.Connection.Type == ConnectionType.File);
        CreateDesktopShortcutCommand = new RelayCommand(CreateDesktopShortcut, _ => SelectedInfobase is not null);
        OpenNativeStarterCommand = new RelayCommand(OpenNativeStarter);
    }

    private void Launch(ICommand launchVmCommand, LaunchKind kind) => launchVmCommand.Execute(kind);

    // ======================= Свойства =======================

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    public bool ShowTagFilterPanel
    {
        get => _showTagFilterPanel;
        set => SetProperty(ref _showTagFilterPanel, value);
    }

    public bool GroupByGroup
    {
        get => _groupByGroup;
        set
        {
            if (SetProperty(ref _groupByGroup, value))
                ApplyFilter();
        }
    }

    public bool ShowEmptyGroups
    {
        get => _showEmptyGroups;
        set
        {
            if (SetProperty(ref _showEmptyGroups, value))
                RebuildTree();
        }
    }

    public bool IsListModeAll
    {
        get => _listMode == "All";
        set { if (value) SetListMode("All"); }
    }

    public bool IsListModeFavorites
    {
        get => _listMode == "Favorites";
        set { if (value) SetListMode("Favorites"); }
    }

    public bool IsListModeRecent
    {
        get => _listMode == "Recent";
        set { if (value) SetListMode("Recent"); }
    }

    private void SetListMode(string mode)
    {
        if (_listMode == mode)
            return;
        _listMode = mode;
        OnPropertyChanged(nameof(IsListModeAll));
        OnPropertyChanged(nameof(IsListModeFavorites));
        OnPropertyChanged(nameof(IsListModeRecent));
        ApplyFilter();
    }

    public Infobase? SelectedInfobase
    {
        get => _selectedInfobase;
        set
        {
            if (SetProperty(ref _selectedInfobase, value))
            {
                if (value is not null)
                    SelectedGroupNode = null;
                RaiseCommandCanExecuteChanged();
                UpdateStatus();
            }
        }
    }

    public GroupNodeViewModel? SelectedGroupNode
    {
        get => _selectedGroupNode;
        set
        {
            if (SetProperty(ref _selectedGroupNode, value))
            {
                if (value is not null)
                    SelectedInfobase = null;
                UpdateStatus();
            }
        }
    }

    public bool ShowRightPanelDetails
    {
        get => _showRightPanelDetails;
        set => SetProperty(ref _showRightPanelDetails, value, nameof(RightPanelToggleTooltip));
    }

    public string RightPanelToggleTooltip => _showRightPanelDetails
        ? "Свернуть правую панель в компактный режим"
        : "Развернуть правую панель";

    public string StatusBarInfo
    {
        get => _statusBarInfo;
        set => SetProperty(ref _statusBarInfo, value);
    }

    public string SyncMessage
    {
        get => _syncMessage;
        set => SetProperty(ref _syncMessage, value);
    }

    public string ThemeName
    {
        get => _themeName;
        set => SetProperty(ref _themeName, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        set => SetProperty(ref _isExporting, value);
    }

    public string ExportIndicatorTooltip
    {
        get => _exportIndicatorTooltip;
        set => SetProperty(ref _exportIndicatorTooltip, value);
    }

    // ---- Горячие клавиши (для подсказок) ----
    public string HotkeyEnterprise => "F3";
    public string HotkeyConfigurator => "F4";
    public string HotkeyEdit => "Ctrl+E";
    public string HotkeyAdd => "Ctrl+N";
    public string HotkeyFavorite => "Alt+F";
    public string HotkeyPin => "Ctrl+P";
    public string HotkeyDelete => "Del";
    public string HotkeyClearCache => "Ctrl+Shift+C";

    // ---- Видимость колонок ----
    public bool ShowExpandCollapseButtons => GroupByGroup;
    public bool ShowFavoritesButton => _settings.ShowFavoritesButton;
    public bool ShowPinnedButton => _settings.ShowPinnedButton;
    public bool ShowVersionColumn => _settings.ShowVersionColumn;
    public bool ShowConfigurationColumn => _settings.ShowConfigurationColumn;
    public bool ShowLaunchModeColumn => _settings.ShowLaunchModeColumn;
    public bool ShowServerColumn => _settings.ShowServerColumn;
    public bool ShowLastLaunchColumn => _settings.ShowLastLaunchColumn;
    public bool ShowSizeColumn => _settings.ShowSizeColumn;
    public bool ShowTags => _settings.ShowTags;

    public double NameColumnWidth => _settings.NameColumnWidth;
    public double VersionColumnWidth => _settings.VersionColumnWidth;
    public double ConfigurationColumnWidth => _settings.ConfigurationColumnWidth;
    public double LaunchModeColumnWidth => _settings.LaunchModeColumnWidth;
    public double ServerColumnWidth => _settings.ServerColumnWidth;
    public double LastLaunchColumnWidth => _settings.LastLaunchColumnWidth;
    public double SizeColumnWidth => _settings.SizeColumnWidth;

    // ---- Текущая сессия ----
    public string SessionClient
    {
        get => _sessionClient;
        set => SetProperty(ref _sessionClient, value,
            nameof(IsSessionClientAuto), nameof(IsSessionClientOrdinary), nameof(IsSessionClientThick), nameof(IsSessionClientThickOrdinary), nameof(IsSessionClientThin));
    }
    public bool IsSessionClientAuto { get => SessionClient == "Авто"; set { if (value) SessionClient = "Авто"; } }
    public bool IsSessionClientOrdinary { get => SessionClient == "Обычный"; set { if (value) SessionClient = "Обычный"; } }
    public bool IsSessionClientThick { get => SessionClient == "Толстый"; set { if (value) SessionClient = "Толстый"; } }
    public bool IsSessionClientThickOrdinary { get => SessionClient == "ТолстыйОбычные"; set { if (value) SessionClient = "ТолстыйОбычные"; } }
    public bool IsSessionClientThin { get => SessionClient == "Тонкий"; set { if (value) SessionClient = "Тонкий"; } }

    public string SessionArch
    {
        get => _sessionArch;
        set => SetProperty(ref _sessionArch, value, nameof(IsSessionArchAuto), nameof(IsSessionArch32), nameof(IsSessionArch64));
    }
    public bool IsSessionArchAuto { get => SessionArch == "Авто"; set { if (value) SessionArch = "Авто"; } }
    public bool IsSessionArch32 { get => SessionArch == "32"; set { if (value) SessionArch = "32"; } }
    public bool IsSessionArch64 { get => SessionArch == "64"; set { if (value) SessionArch = "64"; } }

    // ======================= Загрузка данных =======================

    /// <summary>Загружает настройки, список баз и групп, строит дерево.</summary>
    public void Initialize()
    {
        try
        {
            _settings = _repository.LoadSettings();
            _allInfobases = _repository.Load();
            _groups = _repository.LoadGroups();

            _groupByGroup = _settings.GroupByGroup;
            _showEmptyGroups = _settings.ShowEmptyGroups;
            _showTagFilterPanel = _settings.ShowTagFilterPanel;
            _themeName = _settings.Theme;

            OnPropertyChanged(nameof(GroupByGroup));
            OnPropertyChanged(nameof(ShowEmptyGroups));
            OnPropertyChanged(nameof(ShowTagFilterPanel));
            NotifyColumnSettings();
            NotifySessionSettings();

            RebuildTree();
            UpdateStatus($"Загружено баз: {_allInfobases.Count}");

            // Применяем сохранённую тему, если активная схема не задана.
            if (_settings.ActiveColorScheme is { Colors.Count: > 0 })
                ThemeManager.ApplyScheme(_settings.ActiveColorScheme);
            else
                ThemeManager.ApplyTheme(string.IsNullOrWhiteSpace(_themeName) ? ThemeManager.LightThemeName : _themeName);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка загрузки данных главного окна", ex);
            _dialog.ShowError($"Не удалось загрузить список баз: {ex.Message}");
        }
    }

    /// <summary>Перестраивает дерево групп из моделей.</summary>
    public void RebuildTree()
    {
        AllGroupNodes.Clear();
        GroupNodes.Clear();
        FlatItems.Clear();

        var roots = GroupNodeViewModel.BuildTree(_groups);
        foreach (var root in roots)
            AllGroupNodes.Add(root);

        // Определяем, какие корневые группы реально показывать (содержат базы).
        foreach (var root in AllGroupNodes)
        {
            if (_showEmptyGroups || root.ContainsInfobases)
                GroupNodes.Add(root);
        }

        RebuildTagFilters();
        ApplyFilter();
    }

    /// <summary>Применяет фильтр по виду списка и поиску.</summary>
    private void ApplyFilter()
    {
        var hasSearch = !string.IsNullOrWhiteSpace(SearchText);
        var hasActiveTags = TagFilterItems.Any(t => t.IsSelected);

        if (hasSearch || hasActiveTags || _listMode != "All")
        {
            // Плоский список результатов.
            FlatItems.Clear();
            var filtered = _allInfobases.Where(MatchesFilter);
            foreach (var ib in filtered.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                FlatItems.Add(ib);
            GroupNodes.Clear();
        }
        else
        {
            FlatItems.Clear();
            GroupNodes.Clear();
            foreach (var root in AllGroupNodes)
            {
                if (_showEmptyGroups || root.ContainsInfobases)
                    GroupNodes.Add(root);
            }
        }
    }

    private bool MatchesFilter(Infobase ib)
    {
        if (_listMode == "Favorites" && !ib.IsFavorite)
            return false;
        if (_listMode == "Recent" && ib.LastLaunchDate is null)
            return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            if (!ContainsIgnoreCase(ib.Name, q)
                && !ContainsIgnoreCase(ib.ServerDatabaseDisplay, q)
                && !ContainsIgnoreCase(ib.ConfigurationName, q)
                && !ContainsIgnoreCase(ib.PlatformVersion, q))
                return false;
        }

        foreach (var tag in TagFilterItems.Where(t => t.IsSelected))
        {
            if (!ib.Tags.Any(t => string.Equals(t, tag.Name, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static bool ContainsIgnoreCase(string? source, string value) =>
        !string.IsNullOrWhiteSpace(source) && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    // ======================= Теги =======================

    private void RebuildTagFilters()
    {
        TagFilterItems.Clear();
        foreach (var tag in _allInfobases
                     .SelectMany(ib => ib.Tags)
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            TagFilterItems.Add(new TagFilterItem(tag));
        }
    }

    private void SearchByTag(object? parameter)
    {
        var name = parameter as string;
        if (string.IsNullOrWhiteSpace(name))
            return;
        var item = TagFilterItems.FirstOrDefault(t => t.Name == name);
        if (item is null)
            return;
        item.IsSelected = !item.IsSelected;
        ApplyFilter();
    }

    public bool HasActiveTagFilter => TagFilterItems.Any(t => t.IsSelected);

    private void ClearTagFilters()
    {
        foreach (var item in TagFilterItems)
            item.IsSelected = false;
        ApplyFilter();
    }

    // ======================= Группы =======================

    /// <summary>Возвращает true, если группа свёрнута (используется конвертерами).</summary>
    public bool IsGroupCollapsed(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return false;
        foreach (var node in AllGroupNodes)
        {
            var found = FindNode(node, groupName);
            if (found is not null)
                return !found.IsExpanded;
        }
        return false;
    }

    private static GroupNodeViewModel? FindNode(GroupNodeViewModel node, string fullPathOrName)
    {
        if (string.Equals(node.FullPath, fullPathOrName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.DisplayName, fullPathOrName, StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var child in node.Children)
        {
            var found = FindNode(child, fullPathOrName);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void ExpandAllGroups()
    {
        foreach (var root in AllGroupNodes)
            SetExpandedRecursive(root, true);
    }

    private void CollapseAllGroups()
    {
        foreach (var root in AllGroupNodes)
            SetExpandedRecursive(root, false);
    }

    private static void SetExpandedRecursive(GroupNodeViewModel node, bool expanded)
    {
        node.SetExpandedSilent(expanded);
        node.NotifyIsExpanded();
        foreach (var child in node.Children)
            SetExpandedRecursive(child, expanded);
    }

    private void SortGroups(bool ascending)
    {
        foreach (var root in AllGroupNodes)
            root.SortChildrenRecursive(ascending);
        RebuildTree();
    }

    // ======================= Запуск / действия =======================

    private void OnLaunched()
    {
        if (SelectedInfobase is not null)
        {
            SelectedInfobase.AddLaunchHistory("Запуск");
            SaveSilently();
        }
    }

    private void EditInfobase()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        _dialog.ShowInfo($"Редактирование базы «{ib.Name}»\n\n(Окно настроек подключения — в разработке Этапа 3/4.)");
    }

    private void AddInfobase()
    {
        _dialog.ShowInfo("Добавление базы или группы.\n\n(Мастер добавления — в разработке Этапа 3/4.)");
    }

    private void DeleteInfobase()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        if (_dialog.Confirm($"Удалить базу «{ib.Name}» из списка?"))
        {
            _allInfobases.Remove(ib);
            SaveSilently();
            RebuildTree();
            SelectedInfobase = null;
        }
    }

    private void ToggleFavorite()
    {
        if (SelectedInfobase is Infobase ib)
        {
            ib.IsFavorite = !ib.IsFavorite;
            SaveSilently();
            ApplyFilter();
        }
    }

    private void TogglePin()
    {
        if (SelectedInfobase is Infobase ib)
        {
            ib.IsPinned = !ib.IsPinned;
            SaveSilently();
            RebuildTree();
        }
    }

    private void CopyConnectionString()
    {
        if (SelectedInfobase is not Infobase ib)
            return;

        var text = ib.ConnectionStringDisplay;
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard is null)
            {
                _logger.Warn("Буфер обмена недоступен (нет активного окна).");
                return;
            }
            _ = clipboard.SetTextAsync(text);
            _logger.Info($"Скопирована строка подключения базы «{ib.Name}»");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось скопировать строку подключения: {ex.Message}");
        }
    }

    private void OpenSettings()
    {
        _dialog.ShowInfo("Настройки приложения.\n\n(Окно настроек — в разработке Этапа 3/4.)");
    }

    private void RefreshAllConfigurationInfo()
    {
        _dialog.ShowInfo("Запрос информации о конфигурации по всем базам.\n\n(Реализация — Этап 5, сервисы платформы 1С.)");
    }

    // ======================= Этап 6: папки / ярлыки / стартер =======================

    /// <summary>Недавно запускавшиеся базы (для меню трея). До 8 по дате запуска.</summary>
    public List<Infobase> RecentInfobases =>
        _allInfobases
            .Where(ib => ib.LastLaunchDate.HasValue)
            .OrderByDescending(ib => ib.LastLaunchDate)
            .Take(8)
            .ToList();

    /// <summary>Открыть каталог файловой базы в файловом менеджере (xdg-open/nautilus).</summary>
    private void OpenInfobaseFolder()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        if (!InfobaseMaintenanceService.OpenInfobaseFolder(ib))
            _dialog.ShowError("Не удалось открыть каталог файловой базы.");
    }

    /// <summary>Создать ярлык .desktop на рабочем столе для запуска базы.</summary>
    private void CreateDesktopShortcut()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        if (InfobaseMaintenanceService.CreateDesktopShortcut(ib))
            _dialog.ShowInfo($"Ярлык для базы «{ib.Name}» создан на рабочем столе.");
        else
            _dialog.ShowError($"Не удалось создать ярлык для базы «{ib.Name}».\n" +
                              "Проверьте, что установлена платформа 1С и доступен рабочий стол.");
    }

    /// <summary>Запустить родной стартер 1С (1cestart).</summary>
    private void OpenNativeStarter()
    {
        if (!InfobaseMaintenanceService.OpenNativeStarter())
            _dialog.ShowError("Не удалось найти и запустить стартер 1С (1cestart).\n" +
                              "Ожидаемые пути: /opt/1cv8/<вер>/common/1cestart, /usr/bin/1cestart.");
    }

    private void SynchronizeWithIbases()
    {
        var path = _dialog.OpenFileDialog(
            "Выберите файл ibases.v8i для синхронизации",
            "Список баз 1С (ibases.v8i)|*.v8i|Все файлы (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var importResult = _sync.Import(path, _allInfobases, _groups);
            SaveSilently();
            RebuildTree();
            SyncMessage = "Синхронизация завершена";
            StatusBarInfo = $"Импортировано из ibases.v8i: баз {_allInfobases.Count}, групп {_groups.Count}";
            _logger.Info($"Синхронизация с ibases.v8i: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка синхронизации с ibases.v8i", ex);
            _dialog.ShowError($"Не удалось синхронизировать с ibases.v8i: {ex.Message}");
            SyncMessage = "Ошибка синхронизации";
        }
    }

    private void ToggleTheme()
    {
        ThemeName = ThemeManager.ToggleTheme();
        _settings.Theme = ThemeName;
        SaveSettingsSilently();
    }

    /// <summary>
    /// Применяет выбранный язык интерфейса и сохраняет его в настройках.
    /// Локализация применяется сразу (обновляются окна с привязками Loc) и
    /// восстанавливается при следующем запуске.
    /// </summary>
    /// <param name="code">Код языка, например "ru", "en" или загруженного внешнего.</param>
    public void ApplyLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        _settings.Language = code;
        SaveSettingsSilently();

        try
        {
            Configuration_Management.Localization.LocalizationManager.Instance.SetLanguage(code);
        }
        catch (Exception ex)
        {
            _logger.Error("Не удалось применить язык интерфейса", ex);
        }
    }

    private void ExitApplication()
    {
        SaveSettingsSilently();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ======================= Сохранение =======================

    private void SaveSilently()
    {
        try { _repository.Save(_allInfobases); }
        catch (Exception ex) { _logger.Error("Не удалось сохранить список баз", ex); }
    }

    private void SaveSettingsSilently()
    {
        try { _repository.SaveSettings(_settings); }
        catch (Exception ex) { _logger.Error("Не удалось сохранить настройки", ex); }
    }

    private void UpdateStatus(string? message = null)
    {
        if (message is not null)
            StatusBarInfo = message;
        else if (SelectedInfobase is not null)
            StatusBarInfo = $"База: {SelectedInfobase.Name} — {SelectedInfobase.ServerDatabaseDisplay}";
        else if (SelectedGroupNode is not null)
            StatusBarInfo = $"Группа: {SelectedGroupNode.FullPath}";
        else
            StatusBarInfo = "Готово";
    }

    private void RaiseCommandCanExecuteChanged()
    {
        (LaunchEnterpriseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (LaunchConfiguratorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditInfobaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteInfobaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleFavoriteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (TogglePinCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CopyConnectionStringCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenInfobaseFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void NotifyColumnSettings()
    {
        OnPropertyChanged(nameof(ShowExpandCollapseButtons));
        OnPropertyChanged(nameof(ShowFavoritesButton));
        OnPropertyChanged(nameof(ShowPinnedButton));
        OnPropertyChanged(nameof(ShowVersionColumn));
        OnPropertyChanged(nameof(ShowConfigurationColumn));
        OnPropertyChanged(nameof(ShowLaunchModeColumn));
        OnPropertyChanged(nameof(ShowServerColumn));
        OnPropertyChanged(nameof(ShowLastLaunchColumn));
        OnPropertyChanged(nameof(ShowSizeColumn));
        OnPropertyChanged(nameof(ShowTags));
        OnPropertyChanged(nameof(NameColumnWidth));
        OnPropertyChanged(nameof(VersionColumnWidth));
        OnPropertyChanged(nameof(ConfigurationColumnWidth));
        OnPropertyChanged(nameof(LaunchModeColumnWidth));
        OnPropertyChanged(nameof(ServerColumnWidth));
        OnPropertyChanged(nameof(LastLaunchColumnWidth));
        OnPropertyChanged(nameof(SizeColumnWidth));
    }

    private void NotifySessionSettings()
    {
        OnPropertyChanged(nameof(SessionClient));
        OnPropertyChanged(nameof(SessionArch));
    }
}

/// <summary>
/// Чип тега на панели быстрого отбора. Уведомляет контейнер при смене выбора.
/// </summary>
public class TagFilterItem : ViewModelBase
{
    private bool _isSelected;

    public TagFilterItem(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
#endif