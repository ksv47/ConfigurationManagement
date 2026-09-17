#if LINUX
using System.Windows.Input;
using Avalonia.Threading;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): запуск 1С, избранное и горячие клавиши запуска (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Слоты Alt+1…Alt+9 в порядке назначения: ключи баз.</summary>
    private readonly List<string> _favoriteHotkeyIds = new();

    // ---- Действие после запуска базы или конфигуратора ----
    private string _afterLaunchAction = "None";

    /// <summary>
    /// Что делать с окном после успешного запуска: "None", "MinimizeToTray" или "Close".
    /// Хранится строкой, как в WPF-версии и в файле настроек.
    /// </summary>
    public string AfterLaunchAction
    {
        get => _afterLaunchAction;
        set
        {
            if (SetProperty(ref _afterLaunchAction, value))
            {
                _settings.AfterLaunchAction = value;
                SaveSettingsSilently();
            }
        }
    }

    /// <summary>
    /// Глубина истории запусков одной базы (issue #246): максимальное количество записей
    /// истории запусков, которое запоминается для информационной базы. Минимум 1.
    /// </summary>
    public int MaxLaunchHistoryPerBase
    {
        get => Math.Max(1, _settings.MaxLaunchHistoryPerBase);
        set
        {
            var v = Math.Max(1, value);
            if (_settings.MaxLaunchHistoryPerBase == v)
                return;
            _settings.MaxLaunchHistoryPerBase = v;
            SaveSettingsSilently();
        }
    }

    /// <summary>Запрос к главному окну выполнить действие после успешного запуска.</summary>
    public event Action<Models.AfterLaunchAction>? AfterLaunchRequested;

    /// <summary>Оповещает главное окно, если настройка требует действия.</summary>
    public void NotifyAfterLaunch()
    {
        var action = Models.AfterLaunchActionHelper.Parse(_afterLaunchAction);
        if (action != Models.AfterLaunchAction.None)
            AfterLaunchRequested?.Invoke(action);
    }

    private void Launch(ICommand launchVmCommand, LaunchKind kind) => launchVmCommand.Execute(kind);

    /// <summary>
    /// Запуск с разовыми параметрами: диалог правит параметры только на один
    /// запуск, сохранённое значение базы возвращается в любом случае.
    /// </summary>
    private void LaunchWithParams(LaunchKind kind)
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;

        var dialog = new Configuration_Management.LaunchParametersWindow(infobase.LaunchParameters ?? string.Empty);
        if (!dialog.ShowDialogSync(OwnerWindow()))
            return;

        var saved = infobase.LaunchParameters ?? string.Empty;
        try
        {
            infobase.LaunchParameters = dialog.Result;
            Launch(_launchVm.LaunchCommand, kind);
        }
        finally
        {
            infobase.LaunchParameters = saved;
            // Успешный запуск сохраняет список баз изнутри, то есть подменённое
            // значение уже успело уйти на диск. Возвращаем файл к прежнему виду,
            // иначе разовые параметры остались бы у базы навсегда.
            SaveSilently();
        }
    }

    /// <summary>
    /// Запуск с авторизацией: сохранённые имя и пароль на один раз убираются,
    /// чтобы платформа спросила их сама. Прежние значения возвращаются всегда.
    /// </summary>
    private void LaunchWithAuth()
    {
        var infobase = SelectedInfobase;
        if (infobase?.Connection is not { } connection)
            return;

        var savedUser = connection.User;
        var savedPassword = connection.Password;
        var savedMode = connection.AuthenticationMode;

        // У базы может быть отдельная авторизация Предприятия, и лаунчер
        // предпочитает именно её: без этого пункт молча запускал бы клиент
        // с сохранёнными учётными данными.
        var enterpriseAuth = infobase.EnterpriseAuth;
        var savedAuthUser = enterpriseAuth?.User;
        var savedAuthPassword = enterpriseAuth?.Password;
        var savedAuthMode = enterpriseAuth?.AuthenticationMode;

        try
        {
            connection.User = string.Empty;
            connection.Password = string.Empty;
            connection.AuthenticationMode = AuthenticationMode.Prompt;

            if (enterpriseAuth is not null)
            {
                enterpriseAuth.User = string.Empty;
                enterpriseAuth.Password = string.Empty;
                enterpriseAuth.AuthenticationMode = AuthenticationMode.Prompt;
            }

            Launch(_launchVm.LaunchCommand, LaunchKind.Enterprise);
        }
        finally
        {
            connection.User = savedUser;
            connection.Password = savedPassword;
            connection.AuthenticationMode = savedMode;

            if (enterpriseAuth is not null)
            {
                enterpriseAuth.User = savedAuthUser ?? string.Empty;
                enterpriseAuth.Password = savedAuthPassword ?? string.Empty;
                enterpriseAuth.AuthenticationMode = savedAuthMode ?? AuthenticationMode.Prompt;
            }

            // Причина та же, что и у запуска с параметрами: успешный запуск
            // сохраняет базы изнутри, и пустые учётные данные уже на диске.
            SaveSilently();
        }
    }

    /// <summary>
    /// Запуск базы из меню трея: прямо по ссылке, не трогая выделение в списке.
    /// В Windows-версии это делает LaunchInfobaseById (MainViewModel.Commands.cs:544),
    /// и она тоже не меняет SelectedInfobase: иначе запуск из трея переставлял бы
    /// выделение, правую панель и строку состояния. Источник записи в истории
    /// тот же, что у автора.
    /// </summary>
    public void LaunchFromTray(Infobase ib, bool configurator)
    {
        if (!_allInfobases.Contains(ib))
            return;

        var ok = configurator
            ? _launcher.Launch(ib, Services.OneCLaunchMode.Configurator)
            : _launcher.Launch(ib, Services.OneCLaunchMode.Enterprise);

        if (ok)
        {
            ib.AddLaunchHistory(configurator ? "Configurator" : "Enterprise", "tray");
            SaveSilently();
            OnPropertyChanged(nameof(RecentInfobases));
            _logger.Info($"[tray] Запущена «{ib.Name}» ({(configurator ? "Конфигуратор" : "Предприятие")})");
            NotifyAfterLaunch();
        }
        else
        {
            _logger.Warn($"[tray] Не удалось запустить «{ib.Name}»");
        }
    }

    private void OnLaunched()
    {
        if (SelectedInfobase is not null)
        {
            SelectedInfobase.AddLaunchHistory(LocalizationManager.T("Main.LaunchAction"));
            SaveSilently();
        }

        // Список недавних изменился, и его показывает меню трея.
        OnPropertyChanged(nameof(RecentInfobases));

        // Одна точка на все пути запуска: команды окна, контекстное меню и трей
        // приходят сюда же, в отличие от WPF, где уведомление расставлено трижды.
        NotifyAfterLaunch();
    }

    /// <summary>
    /// Открывает диалог ввода ссылки на информационную базу и запускает её,
    /// как «Перейти по ссылке» в стандартном загрузчике 1С.
    /// </summary>
    private void OpenInfobaseByLink()
    {
        var dialog = new Configuration_Management.LinkInputWindow();
        if (!dialog.ShowDialogSync(OwnerWindow()) || string.IsNullOrWhiteSpace(dialog.Result))
            return;

        var link = dialog.Result;
        _logger.Info($"Запуск 1С по ссылке: {link}");
        if (!OneCLauncher.LaunchByLink(link))
        {
            _dialog.ShowError(string.Format(LocalizationManager.T("Main.ErrOpenLink"), link));
            return;
        }

        NotifyAfterLaunch();
    }

    private void ToggleFavorite() => ToggleFavoriteFor(SelectedInfobase);

    private void TogglePin() => TogglePinFor(SelectedInfobase);

    private void DumpInfobaseDt(object? parameter) =>
        DumpViaDesigner(parameter, OneCLauncher.DesignerBatchOperation.DumpIB, ".dt",
            "Main.DumpDtDialogTitle", "Main.DtFileFilter", "Main.DumpDtStarted", "Main.DumpDtTitle");

    private void DumpConfigurationCf(object? parameter) =>
        DumpViaDesigner(parameter, OneCLauncher.DesignerBatchOperation.DumpCfg, ".cf",
            "Main.DumpCfDialogTitle", "Main.CfFileFilter", "Main.DumpCfStarted", "Main.DumpCfTitle");

    /// <summary>
    /// Выгрузка базы или конфигурации пакетным запуском конфигуратора.
    /// Общая часть обеих команд: в версии для Windows это два почти одинаковых метода.
    /// </summary>
    private void DumpViaDesigner(
        object? parameter,
        OneCLauncher.DesignerBatchOperation operation,
        string extension,
        string dialogTitleKey,
        string filterKey,
        string startedKey,
        string titleKey)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null)
            return;

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T(dialogTitleKey),
            BuildExportFileName(SanitizeFileName(ib.Name), extension,
                _settings.AddTimestampToExportFileName, _settings.ExportTimestampFormat),
            LocalizationManager.T(filterKey));
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!OneCLauncher.RunDesignerBatch(ib, operation, path))
            return;

        ib.AddLaunchHistory(operation == OneCLauncher.DesignerBatchOperation.DumpIB ? "DumpDT" : "DumpCF", path);
        SaveSilently();
        _dialog.ShowInfo(LocalizationManager.T(startedKey), LocalizationManager.T(titleKey));
    }

    private void RefreshConfigurationInfo(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null)
            return;

        // Сброса вердиктов о недоступности COM здесь нет: он живёт
        // в OneCComConnector.cs, а тот в Linux-сборку не входит.
        // Временная индикация процесса (issue #244): надпись «(обновление информации)»
        // в колонке «Конфигурация», если она видима; иначе в «№ релиза»; иначе в «Название».
        var indicatorColumn = _settings.ShowConfigurationColumn ? "Configuration"
            : _settings.ShowConfigurationVersionColumn ? "ConfigurationVersion"
            : "Name";
        ib.SetConfigInfoIndicator(true, indicatorColumn);
        // Строки Avalonia не следят за PropertyChanged модели — пересобираем дерево,
        // чтобы надпись появилась в ячейке выбранной колонки.
        RebuildTree();

        var baseName = ib.Name;
        _ = Task.Run(() =>
        {
            OneCConfigInfo? info = null;
            try
            {
                // Режим чтения сведений — «Конфигуратор» (issue #236): учётные данные берутся
                // из ConfiguratorAuth при её наличии, иначе из авторизации базы.
                info = ConfigurationInfoService.ReadAndApply(ib, overwriteExisting: true,
                    mode: OneCLaunchMode.Configurator);
            }
            catch
            {
                // причина уходит в LastComError и показывается ниже
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Надпись очищается независимо от результата (успех или ошибка).
                ib.SetConfigInfoIndicator(false, indicatorColumn);
                RebuildTree();

                if (info is null)
                {
                    var comError = ConfigurationInfoService.LastComError;
                    var detail = string.IsNullOrWhiteSpace(comError)
                        ? LocalizationManager.T("Main.ConfigInfoCheckHint")
                        : string.Format(LocalizationManager.T("Main.ConfigInfoReason"), comError);
                    _logger.Warn($"Не удалось получить информацию о конфигурации базы «{baseName}». {detail}");
                    _dialog.ShowWarning(
                        string.Format(LocalizationManager.T("Main.ErrConfigInfo"), baseName, detail),
                        LocalizationManager.T("Main.ConfigInfoTitle"));
                    return;
                }

                SaveSilently();

                var name = info.Value.Name.Trim();
                var version = info.Value.Version.Trim();
                _logger.Info($"Обновлена информация о конфигурации базы «{baseName}»: {name} ({version})");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoBase"), baseName));
                if (name.Length > 0)
                    sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoName"), name));
                if (version.Length > 0)
                    sb.AppendLine(string.Format(LocalizationManager.T("Main.ConfigInfoVersion"), version));
                _dialog.ShowInfo(sb.ToString().TrimEnd(), LocalizationManager.T("Main.ConfigInfoTitle"));
            });
        });
    }

    private static string SanitizeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string((name ?? "base").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "base" : s;
    }

    private void ToggleFavoriteFor(Infobase? infobase)
    {
        if (infobase is null)
            return;

        infobase.IsFavorite = !infobase.IsFavorite;

        // Снятая звезда освобождает слот явно: пересчёт сам этого не сделает,
        // он выбрасывает только ключи баз, которых больше нет в списке.
        // Тот же порядок в версии для Windows (MainViewModel.Commands.cs:460).
        if (!infobase.IsFavorite)
            _favoriteHotkeyIds.Remove(FavoriteKey(infobase));

        SyncFavoriteHotkeys();
        SaveSilently();
        // Слоты живут в настройках, а не в списке баз, поэтому сохраняются
        // отдельно: SaveSilently пишет только список.
        SaveSettingsSilently();

        // Состав списка меняется только при активном временном фильтре: там база
        // может выпасть из выборки. Без фильтра строка перекрашивается сама
        // по уведомлению модели, и пересобирать дерево незачем.
        if (IsFilterModeActive())
            ApplyFilter();
    }

    // ======================= Горячие клавиши избранного =======================

    /// <summary>
    /// Ключ базы для слота. Идентификатор, а при его отсутствии имя: тот же
    /// ключ, что и в версии для Windows, поэтому список слотов переносится
    /// между платформами через общий файл настроек.
    /// </summary>
    private static string FavoriteKey(Infobase ib) =>
        !string.IsNullOrEmpty(ib.Id) ? ib.Id : "name:" + ib.Name;

    /// <summary>Упорядоченный список ключей слотов, для окна настроек.</summary>
    public IReadOnlyList<string> FavoriteHotkeyIds => _favoriteHotkeyIds;

    /// <summary>Возвращает базу по ключу слота.</summary>
    public Infobase? FindByFavoriteKey(string key) =>
        _allInfobases.FirstOrDefault(ib => FavoriteKey(ib) == key);

    /// <summary>
    /// Раздаёт слоты избранным базам и проставляет номера на самих базах.
    /// Список хранится в настройках, поэтому здесь же переписывается в них:
    /// любое последующее сохранение унесёт его на диск.
    /// </summary>
    private void SyncFavoriteHotkeys()
    {
        try
        {
            // Слот должен соответствовать только текущим избранным базам, иначе
            // вкладка, счётчик и список горячих клавиш разъезжаются (issue #194).
            _favoriteHotkeyIds.RemoveAll(key =>
                !_allInfobases.Any(ib => ib.IsFavorite && FavoriteKey(ib) == key));

            // Избранные без слота получают его в порядке имени, как в версии
            // для Windows. Слотов девять, лишние остаются без номера.
            foreach (var ib in _allInfobases
                         .Where(i => i.IsFavorite)
                         .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                var key = FavoriteKey(ib);
                if (!_favoriteHotkeyIds.Contains(key) && _favoriteHotkeyIds.Count < 9)
                    _favoriteHotkeyIds.Add(key);
            }

            foreach (var ib in _allInfobases)
            {
                var idx = _favoriteHotkeyIds.IndexOf(FavoriteKey(ib));
                ib.FavoriteHotkeyNumber = idx >= 0 ? idx + 1 : 0;
            }

            _settings.FavoriteHotkeyIds = _favoriteHotkeyIds.ToList();
        }
        catch (Exception ex)
        {
            // Избранное не повод ронять окно: без слотов список просто
            // остаётся без номеров.
            _logger.Error("Не удалось разложить слоты избранного", ex);
        }
    }

    /// <summary>Заменяет порядок слотов, заданный в окне настроек.</summary>
    public void SetFavoriteHotkeyOrder(IEnumerable<string> orderedKeys)
    {
        _favoriteHotkeyIds.Clear();
        foreach (var key in orderedKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Take(9))
            _favoriteHotkeyIds.Add(key);
        SyncFavoriteHotkeys();
        SaveSettingsSilently();
    }

    /// <summary>
    /// Запускает Предприятие для базы из слота. Запуск идёт обычным путём окна,
    /// а не прямым вызовом лаунчера, как в версии для Windows: так работают
    /// и разовые параметры, и действие после запуска.
    /// </summary>
    public void LaunchFavoriteByHotkey(int number)
    {
        if (number < 1 || number > _favoriteHotkeyIds.Count)
            return;

        var ib = FindByFavoriteKey(_favoriteHotkeyIds[number - 1]);
        if (ib is null)
            return;

        SelectedInfobase = ib;
        _logger.Info($"Запуск избранной базы «{ib.Name}» по Alt+{number}");
        Launch(_launchVm.LaunchCommand, LaunchKind.Enterprise);
    }

    private void TogglePinFor(Infobase? infobase)
    {
        if (infobase is null)
            return;

        infobase.IsPinned = !infobase.IsPinned;
        SaveSilently();
        UpdatePinnedSection(infobase);
    }

    /// <summary>
    /// Точечно обновляет узел «Закреплённые», как это делает WPF-версия
    /// (ApplyPinToggle / UpdatePinnedSection): полная пересборка дерева здесь
    /// стоила бы пересоздания всех строк и потери выделения. Узел правится
    /// всегда, а в видимый список попадает только когда он там уместен:
    /// AllGroupNodes переживает и фильтр, и отключение группировки, и после
    /// возврата к группам список берётся именно оттуда.
    /// </summary>
    private void UpdatePinnedSection(Infobase infobase)
    {
        var pinnedVisible = _groupByGroup && !IsFilterModeActive();
        var pinned = AllGroupNodes.FirstOrDefault(node => node.Group is null
            && string.Equals(node.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal));

        // Закрепление меняет и порядок внутри своей группы: закреплённые идут
        // первыми (GroupSortOrder). Без этого строка осталась бы на прежнем
        // месте до следующей полной пересборки.
        SortOwningNode(infobase);

        if (infobase.IsPinned)
        {
            if (pinned is null)
            {
                pinned = new GroupNodeViewModel(null, marker: GroupNodeViewModel.PinnedMarker);
                pinned.Infobases.Add(infobase);
                pinned.PopulateItems(_showEmptyGroups);
                ApplyExpandedState(pinned);
                SubscribeExpandedTracking(pinned);
                AllGroupNodes.Insert(0, pinned);
                if (pinnedVisible)
                    GroupNodes.Insert(0, pinned);
                return;
            }

            if (!pinned.Infobases.Contains(infobase))
            {
                pinned.Infobases.Add(infobase);
                SortNodeInfobases(pinned);
                pinned.PopulateItems(_showEmptyGroups);
            }
            else
            {
                pinned.NotifyCountChanged();
            }

            return;
        }

        if (pinned is null)
            return;

        pinned.Infobases.Remove(infobase);
        if (pinned.Infobases.Count > 0)
        {
            pinned.PopulateItems(_showEmptyGroups);
            return;
        }

        AllGroupNodes.Remove(pinned);
        GroupNodes.Remove(pinned);
    }

    /// <summary>
    /// Переупорядочивает узел, в котором лежит база, кроме служебного узла
    /// «Закреплённые»: его состав меняется отдельно.
    /// </summary>
    private void SortOwningNode(Infobase infobase)
    {
        foreach (var root in AllGroupNodes)
        {
            var owner = FindNodeWith(root, infobase);
            if (owner is null
                || string.Equals(owner.Marker, GroupNodeViewModel.PinnedMarker, StringComparison.Ordinal))
                continue;

            SortNodeInfobases(owner);
            owner.PopulateItems(_showEmptyGroups);
            return;
        }
    }

    /// <summary>Ищет узел дерева, в списке баз которого лежит указанная база.</summary>
    private static GroupNodeViewModel? FindNodeWith(GroupNodeViewModel node, Infobase infobase)
    {
        if (node.Infobases.Contains(infobase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeWith(child, infobase);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>Переупорядочивает базы узла по текущему полю сортировки.</summary>
    private void SortNodeInfobases(GroupNodeViewModel node)
    {
        var sorted = ApplyCurrentSort(node.Infobases).ToList();
        node.SetNotificationsSuppressed(true);
        try
        {
            node.Infobases.Clear();
            foreach (var infobase in sorted)
                node.Infobases.Add(infobase);
        }
        finally
        {
            node.SetNotificationsSuppressed(false);
        }
    }
}
#endif