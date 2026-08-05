namespace Configuration_Management.Models;

/// <summary>
/// Тип узла метаданных конфигурации (аналог объектов метаданных 1С).
/// </summary>
public enum MetadataType
{
    /// <summary>Корень конфигурации.</summary>
    Configuration,

    /// <summary>Группа объектов (папка в дереве).</summary>
    Group,

    /// <summary>Справочник.</summary>
    Catalog,

    /// <summary>Документ.</summary>
    Document,

    /// <summary>Отчёт.</summary>
    Report,

    /// <summary>Обработка.</summary>
    DataProcessor,

    /// <summary>Регистр сведений.</summary>
    InformationRegister,

    /// <summary>Регистр накопления.</summary>
    AccumulationRegister,

    /// <summary>Перечисление.</summary>
    Enum,

    /// <summary>Константа.</summary>
    Constant,

    /// <summary>План обмена.</summary>
    ExchangePlan,

    /// <summary>Общий модуль.</summary>
    CommonModule,

    /// <summary>Общая форма.</summary>
    CommonForm,

    /// <summary>Роль.</summary>
    Role,

    /// <summary>Подсистема.</summary>
    Subsystem
}

/// <summary>
/// Узел дерева метаданных конфигурации.
/// </summary>
public class MetadataNode
{
    /// <summary>Наименование узла.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Синоним (русское наименование).</summary>
    public string Synonym { get; set; } = string.Empty;

    /// <summary>Тип узла.</summary>
    public MetadataType Type { get; set; }

    /// <summary>Комментарий / описание.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Дочерние узлы.</summary>
    public List<MetadataNode> Children { get; set; } = new();

    /// <summary>Признак наличия дочерних узлов (для ленивой загрузки).</summary>
    public bool HasChildren => Children.Count > 0;
}