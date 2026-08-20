#if LINUX
using System.Collections.ObjectModel;
using Avalonia.Media;
using Configuration_Management.Converters;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Представляет узел дерева групп информационных баз (Avalonia/Linux).
/// Содержит модель группы, коллекцию подгрупп и коллекцию баз, размещённых в этой группе.
/// Также содержит единую коллекцию <see cref="Items"/>, объединяющую подгруппы и базы.
/// </summary>
public class GroupNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private string? _fullPathCache;
    private bool? _containsInfobasesCache;
    private bool _suppressNotifications;

    private readonly string _color;
    private readonly string _iconColor;
    private readonly string _icon;

    public GroupNodeViewModel(
        Group? group,
        GroupNodeViewModel? parent = null,
        string? displayName = null,
        string? defaultColor = null,
        string? defaultIconColor = null,
        string? defaultIcon = null)
    {
        Group = group;
        Parent = parent;
        DisplayName = displayName
            ?? (group is null ? "Без группы" : (string.IsNullOrWhiteSpace(group.Name) ? "Без названия" : group.Name));
        Children = new ObservableCollection<GroupNodeViewModel>();
        Infobases = new ObservableCollection<Infobase>();
        Items = new ObservableCollection<object>();
        _color = group?.Color ?? defaultColor ?? "#2D6CDF";
        _iconColor = group is not null
            ? (!string.IsNullOrWhiteSpace(group.IconColor) ? group.IconColor : "#FFFFFF")
            : (defaultIconColor ?? "#FFFFFF");
        _icon = group?.Icon ?? defaultIcon ?? string.Empty;
        HeaderBrush = GroupColorConverter.GetBrush(_color);
        HeaderTextBrush = GroupTextColorConverter.GetBrush(_color);
        IconBrush = GroupColorConverter.GetBrush(_iconColor);
        Infobases.CollectionChanged += (_, _) =>
        {
            _containsInfobasesCache = null;
            NotifyCountChanged();
        };
        Children.CollectionChanged += (_, _) =>
        {
            _containsInfobasesCache = null;
            NotifyCountChanged();
        };
    }

    /// <summary>Кэшированная кисть фона заголовка группы.</summary>
    public IBrush HeaderBrush { get; }

    /// <summary>Кэшированная кисть текста заголовка (контраст к фону).</summary>
    public IBrush HeaderTextBrush { get; }

    /// <summary>Кэшированная кисть иконки группы.</summary>
    public IBrush IconBrush { get; }

    /// <summary>Модель группы. Null для специальных узлов («Закреплённые», «Без группы»).</summary>
    public Group? Group { get; }

    /// <summary>Родительский узел. Null для корневого узла.</summary>
    public GroupNodeViewModel? Parent { get; internal set; }

    /// <summary>Имя группы для отображения (без пути).</summary>
    public string DisplayName { get; }

    /// <summary>Полный путь группы в иерархии (кэшируется после первого обращения).</summary>
    public string FullPath
    {
        get
        {
            if (_fullPathCache is not null)
                return _fullPathCache;

            if (Group is null)
                return _fullPathCache = string.Empty;

            var parts = new List<string>();
            for (var node = this; node is not null && node.Group is not null; node = node.Parent)
                parts.Add(node.Group.Name);
            parts.Reverse();
            return _fullPathCache = string.Join(GroupHierarchyHelper.PathSeparator, parts);
        }
    }

    /// <summary>Цвет фона заголовка группы.</summary>
    public string Color => Group?.Color ?? _color;

    /// <summary>Цвет иконки (по умолчанию белый, если не задан отдельно).</summary>
    public string IconColor => Group is not null
        ? (!string.IsNullOrWhiteSpace(Group.IconColor) ? Group.IconColor : "#FFFFFF")
        : _iconColor;

    /// <summary>Ключ иконки группы.</summary>
    public string Icon
    {
        get
        {
            if (Group is not null)
                return string.IsNullOrWhiteSpace(Group.Icon) ? "IconFolder" : Group.Icon;

            if (!string.IsNullOrWhiteSpace(_icon))
                return _icon;

            return DisplayName switch
            {
                "Закреплённые" => "IconPin",
                "Все базы" => "IconDatabase",
                _ => "IconFolder"
            };
        }
    }

    /// <summary>Подгруппы текущего узла.</summary>
    public ObservableCollection<GroupNodeViewModel> Children { get; }

    /// <summary>Базы, размещённые непосредственно в этой группе.</summary>
    public ObservableCollection<Infobase> Infobases { get; }

    /// <summary>Единая коллекция для отображения в дереве.</summary>
    public ObservableCollection<object> Items { get; }

    /// <summary>Признак наличия подгрупп.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Признак наличия баз в группе.</summary>
    public bool HasInfobases => Infobases.Count > 0;

    /// <summary>Признак наличия баз в группе или её подгруппах.</summary>
    public bool ContainsInfobases
    {
        get
        {
            if (_containsInfobasesCache.HasValue)
                return _containsInfobasesCache.Value;
            var value = Infobases.Count > 0 || Children.Any(c => c.ContainsInfobases);
            _containsInfobasesCache = value;
            return value;
        }
    }

    /// <summary>Общее количество баз в группе и всех её подгруппах.</summary>
    public int TotalInfobaseCount => Infobases.Count + Children.Sum(c => c.TotalInfobaseCount);

    /// <summary>Сообщает привязкам об изменении счётчика.</summary>
    public void NotifyCountChanged()
    {
        if (_suppressNotifications)
            return;
        OnPropertyChanged(nameof(TotalInfobaseCount));
        OnPropertyChanged(nameof(HasInfobases));
        OnPropertyChanged(nameof(ContainsInfobases));
        Parent?.NotifyCountChanged();
    }

    /// <summary>Включить/выключить уведомления при массовом заполнении.</summary>
    public void SetNotificationsSuppressed(bool suppress)
    {
        _suppressNotifications = suppress;
        foreach (var child in Children)
            child.SetNotificationsSuppressed(suppress);
    }

    /// <summary>Состояние развёрнутости узла в дереве.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Устанавливает развёрнутость без PropertyChanged (массовые expand/collapse).</summary>
    public void SetExpandedSilent(bool expanded) => _isExpanded = expanded;

    /// <summary>Сообщить UI о текущем IsExpanded.</summary>
    public void NotifyIsExpanded() => OnPropertyChanged(nameof(IsExpanded));

    /// <summary>Состояние выделенности узла в дереве.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Заполняет коллекцию <see cref="Items"/>: сначала подгруппы, затем базы текущей группы.
    /// </summary>
    public void PopulateItems(bool includeEmptyGroups = false)
    {
        foreach (var child in Children)
            child.PopulateItems(includeEmptyGroups);

        _containsInfobasesCache = null;
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var child in Children)
            {
                if (includeEmptyGroups || child.ContainsInfobases)
                    Items.Add(child);
            }
            foreach (var infobase in Infobases)
                Items.Add(infobase);
            _containsInfobasesCache = Infobases.Count > 0 || Children.Any(c => c.ContainsInfobases);
        }
        finally
        {
            _suppressNotifications = false;
        }
        OnPropertyChanged(nameof(TotalInfobaseCount));
        OnPropertyChanged(nameof(HasInfobases));
        OnPropertyChanged(nameof(ContainsInfobases));
    }

    /// <summary>Рекурсивно сортирует подгруппы текущего узла по имени.</summary>
    public void SortChildrenRecursive(bool ascending)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        List<GroupNodeViewModel> sorted;
        if (ascending)
            sorted = Children.OrderBy(c => c.DisplayName, comparer).ToList();
        else
            sorted = Children.OrderByDescending(c => c.DisplayName, comparer).ToList();

        Children.Clear();
        foreach (var child in sorted)
            Children.Add(child);

        foreach (var child in Children)
            child.SortChildrenRecursive(ascending);
    }

    public override string ToString() => string.IsNullOrEmpty(FullPath) ? DisplayName : FullPath;

    /// <summary>Строит дерево групп из плоского списка с учётом свойства <see cref="Group.ParentId"/>.</summary>
    public static List<GroupNodeViewModel> BuildTree(IEnumerable<Group> groups)
    {
        var list = groups.ToList();
        var nodes = new Dictionary<string, GroupNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in list)
            nodes[group.Id] = new GroupNodeViewModel(group);

        var inCycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in list)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = group;
            while (current is not null && !string.IsNullOrEmpty(current.ParentId))
            {
                if (!visited.Add(current.Id))
                {
                    inCycle.Add(current.Id);
                    foreach (var id in visited)
                        inCycle.Add(id);
                    break;
                }

                if (!nodes.TryGetValue(current.ParentId, out var parentNode))
                    break;

                current = parentNode.Group;
            }
        }

        var roots = new List<GroupNodeViewModel>();
        foreach (var group in list)
        {
            if (inCycle.Contains(group.Id) ||
                string.IsNullOrEmpty(group.ParentId) ||
                !nodes.ContainsKey(group.ParentId))
            {
                roots.Add(nodes[group.Id]);
                continue;
            }

            var parentNode = nodes[group.ParentId];
            var childNode = nodes[group.Id];
            parentNode.Children.Add(childNode);
            childNode.Parent = parentNode;
        }

        return roots;
    }
}
#endif