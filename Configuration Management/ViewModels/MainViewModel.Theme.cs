#if WINDOWS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management.ViewModels;

/// <summary>Main ViewModel (partial class split by feature blocks, see MainViewModel.*.cs).</summary>
public partial class MainViewModel : ViewModelBase
{

    /// <summary>
    /// Сохраняет выбранную тему оформления в настройках.
    /// </summary>
    public void SaveTheme(string theme)
    {
        _savedTheme = theme;
        SaveSettings();
    }

    /// <summary>
    /// Применяет встроенную тему оформления по имени («Light»/«Dark»), обновляет
    /// активную схему и сохраняет настройки.
    /// </summary>
    public void ApplyTheme(string theme)
    {
        var scheme = Themes.ThemeManager.GetBuiltInScheme(theme) ?? Models.ColorScheme.CreateLight();
        ApplyColorScheme(scheme);
    }

    /// <summary>
    /// Переключает базовую тему (светлую/тёмную), сохраняя пользовательские схемы каждой темы:
    /// применяется схема целевой темы (сохранённая пользовательская или встроенная по умолчанию),
    /// ни одна из сохранённых схем не затирается.
    /// </summary>
    public void ToggleTheme()
    {
        // Схема одна (несёт обе палитры): переключение темы лишь выбирает палитру.
        var targetDark = Themes.ThemeManager.CurrentTheme != Themes.ThemeManager.DarkThemeName;
        ApplySchemeForTheme(targetDark);
        SaveSettings();
        LogTheme($"ToggleTheme -> dark={targetDark}, scheme='{_activeColorScheme?.Name}' (colors light={_activeColorScheme?.LightColors.Count}, dark={_activeColorScheme?.DarkColors.Count})");
    }

    /// <summary>
    /// Задаёт вариант темы (светлый/тёмный) и применяет активную схему с палитрой этого
    /// варианта. Схема одна и несёт обе палитры, поэтому вариант её не меняет.
    /// </summary>
    private void ApplySchemeForTheme(bool dark)
    {
        _savedTheme = dark ? Themes.ThemeManager.DarkThemeName : Themes.ThemeManager.LightThemeName;
        // Убеждаемся, что активная схема применена, затем задаём вариант темы.
        Themes.ThemeManager.ApplyScheme(_activeColorScheme ?? Models.ColorScheme.CreateLight());
        Themes.ThemeManager.ApplyTheme(dark);
        LogTheme($"ApplySchemeForTheme(dark={dark}) -> active='{_activeColorScheme?.Name}'");
    }

    /// <summary>
    /// Возвращает активную схему (с двумя палитрами). Вариант темы определяет,
    /// какая палитра показывается; сама схема от него не зависит.
    /// </summary>
    public Models.ColorScheme GetSchemeForTheme(string theme)
        => (_activeColorScheme ?? Models.ColorScheme.CreateLight()).Clone();

    private static bool IsDarkTheme(string? theme)
        => string.Equals(theme, Themes.ThemeManager.DarkThemeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Применяет цветовую схему, сохраняет её как активную и записывает настройки.
    /// Схема едина и несёт обе палитры; вариант темы она не меняет.
    /// </summary>
    public void ApplyColorScheme(ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _activeColorScheme = clone;
        // Схема применяется по текущему варианту темы (палитра выбирается им).
        Themes.ThemeManager.ApplyScheme(clone);
        SaveSettings();
        LogTheme($"ApplyColorScheme('{clone.Name}', colors light={clone.LightColors.Count}, dark={clone.DarkColors.Count}) -> active='{_activeColorScheme.Name}'");
    }

    /// <summary>
    /// Сохраняет правки схемы как активную (единая схема с двумя палитрами),
    /// не меняя активную тему и не трогая интерфейс. Используется редактором цветов
    /// во вкладке «Цветовое оформление».
    /// </summary>
    public void SaveColorSchemeSlot(ColorScheme scheme)
    {
        if (scheme is null)
            return;
        var clone = scheme.Clone();
        clone.Normalize();
        _activeColorScheme = clone;
        SaveSettings();
        LogTheme($"SaveColorSchemeSlot('{clone.Name}') -> active='{_activeColorScheme?.Name}'");
    }

    /// <summary>Диагностика переключения/применения темы (пишет в лог и во временный файл).</summary>
    private void LogTheme(string message)
    {
        try { _logger.Info("[theme-debug] " + message); } catch { /* не критично */ }
#if DEBUG
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cm_theme_debug.log"),
                "[theme-debug] " + message + Environment.NewLine);
        }
        catch { /* не критично */ }
#endif
    }

    /// <summary>
    /// Применяет активную цветовую схему к интерфейсу (без сохранения настроек).
    /// Используется при запуске, чтобы применить сохранённую тему.
    /// </summary>
    public void ApplyActiveColorSchemeToUi()
    {
        Themes.ThemeManager.ApplyScheme(_activeColorScheme ?? Models.ColorScheme.CreateLight());
    }

    /// <summary>Список доступных схем: встроенные (Светлая/Тёмная) и пользовательские.</summary>
    public IReadOnlyList<Models.ColorScheme> AvailableColorSchemes()
    {
        return Themes.ThemeManager.EnumerateAllSchemes();
    }

    /// <summary>Сохраняет пользовательскую цветовую схему в каталог пользователя.</summary>
    public void SaveCustomColorScheme(Models.ColorScheme scheme)
    {
        Themes.ThemeManager.SaveCustomScheme(scheme);
    }

    /// <summary>Удаляет пользовательскую цветовую схему по имени.</summary>
    public bool DeleteCustomColorScheme(string name)
    {
        return Themes.ThemeManager.DeleteCustomScheme(name);
    }

    /// <summary>
    /// Применяет цветовую схему к интерфейсу для предпросмотра (без сохранения настроек).
    /// </summary>
    public void PreviewColorScheme(Models.ColorScheme scheme)
    {
        if (scheme is null)
            return;
        Themes.ThemeManager.ApplyScheme(scheme);
    }

    /// <summary>
    /// Применяет настройки шрифта интерфейса (семейство, размер, начертание, стиль),
    /// обновляет главное окно и сохраняет настройки.
    /// </summary>
    public void ApplyFontSettings(string fontFamily, double fontSize, string fontWeight, string fontStyle)
    {
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily)
            ? Themes.ThemeManager.DefaultFontFamily : fontFamily;
        _fontSize = fontSize > 0 ? fontSize : Themes.ThemeManager.DefaultFontSize;
        _fontWeight = string.Equals(fontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
            ? "Bold" : Themes.ThemeManager.DefaultFontWeight;
        _fontStyle = string.Equals(fontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
            ? "Italic" : Themes.ThemeManager.DefaultFontStyle;

        Themes.ThemeManager.ApplyFontToAllWindows(_fontFamily, _fontSize, _fontWeight, _fontStyle);

        SaveSettings();
    }

    /// <summary>
    /// Применяет индивидуальные настройки шрифта областей к главному окну.
    /// Используется при запуске и для предпросмотра при «Применить».
    /// </summary>
    public void ApplyElementFonts()
    {
        var window = Application.Current?.MainWindow;
        if (window is not MainWindow mw)
            return;
        Themes.ThemeManager.ApplyElementFonts(mw, _elementFonts);
    }

    /// <summary>
    /// Сохраняет индивидуальные настройки шрифта областей, применяет их и записывает в настройки.
    /// </summary>
    public void SaveElementFonts(IDictionary<string, Models.ElementFontSettings> fonts)
    {
        _elementFonts = fonts is null
            ? new Dictionary<string, Models.ElementFontSettings>()
            : new Dictionary<string, Models.ElementFontSettings>(fonts);

        // «По умолчанию» также обновляет глобальные настройки шрифта (для всех окон и при запуске).
        if (_elementFonts.TryGetValue(Themes.ThemeManager.FontDefault, out var def) && def is not null && def.FontSize > 0)
        {
            _fontFamily = string.IsNullOrWhiteSpace(def.FontFamily) ? Themes.ThemeManager.DefaultFontFamily : def.FontFamily;
            _fontSize = def.FontSize;
            _fontWeight = string.Equals(def.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                ? "Bold" : Themes.ThemeManager.DefaultFontWeight;
            _fontStyle = string.Equals(def.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                ? "Italic" : Themes.ThemeManager.DefaultFontStyle;
        }

        Themes.ThemeManager.ApplyFontToAllWindows(_fontFamily, _fontSize, _fontWeight, _fontStyle);
        ApplyElementFonts();
        SaveSettings();
    }

    /// <summary>
    /// Применяет индивидуальные настройки шрифта областей для предпросмотра (без сохранения).
    /// </summary>
    public void PreviewElementFonts(IReadOnlyDictionary<string, Models.ElementFontSettings> fonts)
    {
        var window = Application.Current?.MainWindow;
        if (window is not MainWindow mw)
            return;
        Themes.ThemeManager.ApplyElementFonts(mw, fonts);
    }

    /// <summary>Выгружает схему в JSON-файл.</summary>
    public void ExportColorScheme(Models.ColorScheme scheme, string filePath)
    {
        Themes.ThemeManager.ExportScheme(scheme, filePath);
    }

    /// <summary>Загружает схему из JSON-файла.</summary>
    public Models.ColorScheme? ImportColorScheme(string filePath)
    {
        return Themes.ThemeManager.ImportScheme(filePath);
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
        // Для реальных групп ключом служит полный путь, для служебных узлов —
        // внутренний маркер (не зависит от языка), т.к. пути у них нет.
        var key = node.NodeKey;
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

    /// <summary>
    /// Сортирует группы по имени (А→Я или Я→А): корневые группы и рекурсивно все подгруппы.
    /// Направление запоминается и применяется при последующих перестройках дерева.
    /// </summary>
    private void SortGroups(bool ascending)
    {
        _groupSortAscending = ascending;
        RebuildGroupTree();
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
            // Для реальных групп ключом служит полный путь, для служебных узлов —
            // внутренний маркер (не зависит от языка; единый формат с ToggleGroupExpanded).
            var key = node.NodeKey;
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
    /// <summary>
    /// Обработчик смены языка интерфейса (Windows): пересчитывает все привязки к VM
    /// и пересобирает дерево групп, чтобы локализованные свойства и служебные узлы
    /// («Все базы», «Без группы», «Избранное») обновились без перезапуска.
    /// Работает для любого направления (ru ↔ en и внешние языки).
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Событие поднимается на UI-потоке (SettingsWindow), но добавляем страховку:
        // если вызов пришёл с другого потока — маршализуем на диспетчер приложения.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            HandleLanguageChanged();
            return;
        }

        dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(HandleLanguageChanged));
    }

    /// <summary>
    /// Непосредственно применяет смену языка: уведомляет об изменении всех свойств VM
    /// (WPF пересчитает StatusBarInfo, ExportIndicatorTooltip, RightPanelToggleTooltip,
    /// GroupByGroupText, SyncMessage и пр.) и пересобирает дерево групп.
    /// </summary>
    private void HandleLanguageChanged()
    {
        // Пустое имя свойства означает «изменились все свойства» — WPF пересчитает
        // все активные привязки к VM.
        OnPropertyChanged(string.Empty);
        // Дерево перестраиваем целиком: узлы со спецмаркерами возвращают
        // LocalizationManager.T(...) на лету (см. GroupNodeViewModel.DisplayName).
        RebuildGroupTree();
    }

    /// <summary>
    /// Отписывается от события смены языка. Вызывается при полном закрытии главного
    /// окна (MainWindow.OnClosing), чтобы избежать утечек и дублирования подписки.
    /// </summary>
    public void UnsubscribeLanguageChanged()
    {
        if (!_languageChangedSubscribed)
            return;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        _languageChangedSubscribed = false;
    }

    public void RebuildGroupTree()
    {
        // Один проход по базам без CollectionView.Refresh (он дорогой на больших списках).
        // Учитываем выбранное поле сортировки (_sortField / _sortAscending).
        var filtered = EnumerateFilteredInfobases();
        // В режиме «Недавние» порядок уже по LastLaunchDate; иначе — выбранная сортировка.
        var visible = (_listViewMode == ListViewMode.Recent
            ? filtered
            : ApplyCurrentSort(filtered)).ToList();

        // Когда группировка отключена или активен временный фильтр
        // (Избранное / Недавние / отбор по тегу / поиск) — показываем плоский список
        // в одном узле без групп и закреплений. Так избегаем дублей: закреплённая база
        // иначе попадает и в «Закреплённые», и в свою группу, а при фильтрации
        // группировка по группам теряет смысл.
        if (!_groupByGroup || IsFilterModeActive())
        {
            var flatNode = new GroupNodeViewModel(
                null,
                displayName: IsFilterModeActive() ? LocalizationManager.T("Main.FlatFound") : null,
                marker: IsFilterModeActive() ? null : GroupNodeViewModel.AllBasesMarker);
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

        // Сортируем корневые группы и все подгруппы по имени согласно сохранённому направлению.
        var groupComparer = StringComparer.OrdinalIgnoreCase;
        roots.Sort(_groupSortAscending
            ? (a, b) => groupComparer.Compare(a.DisplayName, b.DisplayName)
            : (a, b) => groupComparer.Compare(b.DisplayName, a.DisplayName));
        foreach (var root in roots)
            root.SortChildrenRecursive(_groupSortAscending);

        var pinnedNode = new GroupNodeViewModel(
            null,
            marker: GroupNodeViewModel.PinnedMarker,
            defaultColor: _pinnedColor,
            defaultIconColor: _pinnedIconColor,
            defaultIcon: _pinnedIcon);
        var noGroupNode = new GroupNodeViewModel(
            null,
            marker: GroupNodeViewModel.NoGroupMarker,
            defaultColor: _noGroupColor,
            defaultIconColor: _noGroupIconColor,
            defaultIcon: _noGroupIcon);

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
            root.PopulateItems(_showEmptyGroups);
        pinnedNode.PopulateItems(_showEmptyGroups);
        noGroupNode.PopulateItems(_showEmptyGroups);

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
            if (_showEmptyGroups || root.ContainsInfobases)
                next.Add(root);
        }

        _groupNodes = next;

        // Expand/collapse до Replace — один проход построения TreeView.
        if (ShouldAutoExpandGroups())
            ExpandAllNodesWithContent(next);
        else
            ApplyExpandedState(next);

        // Узлы групп пересоздаются при каждой пересборке: ссылка SelectedGroupNode иначе
        // осталась бы на старый (выброшенный) узел, и после правки настроек группы её
        // выделение не восстановилось бы. Перепривязываем выбранную группу по её идентификатору.
        var selectedGroupId = SelectedGroupNode?.Group?.Id;
        if (!string.IsNullOrEmpty(selectedGroupId)
            && FindGroupNodeById(next, selectedGroupId) is { } remapped)
            SelectedGroupNode = remapped;

        ReplaceGroupNodes(next);

        // Панель тегов обновляем только если набор тегов мог измениться
        // (не на каждый символ поиска — там уже есть ранний выход, но лишний проход лишний).
        RefreshTagFilterItems();
    }

    /// <summary>
    /// Рекурсивно ищет узел группы по идентификатору модели в новом дереве.
    /// Нужен для восстановления выбранной группы после пересборки, когда узлы
    /// GroupNodeViewModel пересоздаются, а модель группы остаётся той же.
    /// Спец-узлы («Без группы», «Закреплённые») имеют Group == null и пропускаются.
    /// </summary>
    private static GroupNodeViewModel? FindGroupNodeById(
        IEnumerable<GroupNodeViewModel> roots, string groupId)
    {
        foreach (var root in roots)
        {
            if (root.Group is not null
                && string.Equals(root.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
                return root;
            if (root.Children.Count > 0 && FindGroupNodeById(root.Children, groupId) is { } found)
                return found;
        }
        return null;
    }
}
#endif
