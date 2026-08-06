using System.Collections.ObjectModel;
using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// Представляет узел дерева групп информационных баз.
/// Содержит модель группы, коллекцию подгрупп и коллекцию баз, размещённых в этой группе.
/// </summary>
public class GroupNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    /// <summary>
    /// Создаёт узел дерева для указанной группы.
    /// </summary>
    /// <param name="group">Модель группы. Может быть null для специального узла «Без группы».</param>
    /// <param name="parent">Родительский узел. Null для корневого узла.</param>
    public GroupNodeViewModel(Group? group, GroupNodeViewModel? parent = null)
    {
        Group = group;
        Parent = parent;
        Children = new ObservableCollection<GroupNodeViewModel>();
        Infobases = new ObservableCollection<Infobase>();
    }

    /// <summary>Модель группы. Null для специального узла «Без группы».</summary>
    public Group? Group { get; }

    /// <summary>Родительский узел. Null для корневого узла.</summary>
    public GroupNodeViewModel? Parent { get; }

    /// <summary>Полный путь группы в иерархии.</summary>
    public string FullPath
    {
        get
        {
            if (Group is null)
                return string.Empty;

            var parts = new List<string>();
            for (var node = this; node is not null && node.Group is not null; node = node.Parent)
            {
                parts.Add(node.Group.Name);
            }
            parts.Reverse();
            return string.Join(" / ", parts);
        }
    }

    /// <summary>Имя группы для отображения (без пути).</summary>
    public string DisplayName => Group?.Name ?? "Без группы";

    /// <summary>Цвет группы.</summary>
    public string Color => Group?.Color ?? "#2D6CDF";

    /// <summary>Подгруппы текущего узла.</summary>
    public ObservableCollection<GroupNodeViewModel> Children { get; }

    /// <summary>Базы, размещённые непосредственно в этой группе.</summary>
    public ObservableCollection<Infobase> Infobases { get; }

    /// <summary>Признак наличия подгрупп.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Признак наличия баз в группе.</summary>
    public bool HasInfobases => Infobases.Count > 0;

    /// <summary>Состояние развёрнутости узла в дереве.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Возвращает строковое представление узла (полный путь группы).
    /// </summary>
    public override string ToString() => string.IsNullOrEmpty(FullPath) ? DisplayName : FullPath;
}