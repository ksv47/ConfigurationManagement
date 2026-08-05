using Configuration_Management.Models;

namespace Configuration_Management.ViewModels;

/// <summary>
/// ViewModel для узла дерева метаданных конфигурации.
/// </summary>
public class MetadataNodeViewModel : ViewModelBase
{
    private readonly MetadataNode _node;
    private bool _isExpanded;
    private bool _isSelected;

    public MetadataNodeViewModel(MetadataNode node)
    {
        _node = node;
    }

    /// <summary>Наименование узла.</summary>
    public string Name => _node.Name;

    /// <summary>Синоним (русское наименование).</summary>
    public string Synonym => _node.Synonym;

    /// <summary>Отображаемое наименование (синоним, если задан, иначе имя).</summary>
    public string DisplayName => string.IsNullOrEmpty(Synonym) ? Name : $"{Synonym} ({Name})";

    /// <summary>Тип узла.</summary>
    public MetadataType Type => _node.Type;

    /// <summary>Комментарий / описание.</summary>
    public string Comment => _node.Comment;

    /// <summary>Дочерние узлы.</summary>
    public List<MetadataNodeViewModel> Children { get; } = new();

    /// <summary>Признак наличия дочерних узлов.</summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>Развёрнут ли узел в дереве.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Выбран ли узел в дереве.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Рекурсивно строит дерево ViewModel из модели метаданных.
    /// </summary>
    public static MetadataNodeViewModel FromModel(MetadataNode node)
    {
        var vm = new MetadataNodeViewModel(node);
        foreach (var child in node.Children)
        {
            vm.Children.Add(FromModel(child));
        }
        return vm;
    }
}