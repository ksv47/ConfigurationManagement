namespace Configuration_Management.Models;

/// <summary>
/// Узел дерева платформ: линия (8.3) → разрядность (64/32) → сборка с путём.
/// </summary>
public class PlatformVersionGroup
{
    /// <summary>Заголовок узла (линия, «64-разрядная» или Display сборки).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Путь к папке версии (только у листьев).</summary>
    public string? Path { get; set; }

    /// <summary>Строка варианта для выбора, например «8.3.27.1688 (64)» (только у листьев).</summary>
    public string? Variant { get; set; }

    /// <summary>Вложенные узлы (для групп).</summary>
    public List<PlatformVersionGroup> Children { get; set; } = new();

    /// <summary>Листья (сборки) — для обратной совместимости с шаблонами, где ItemsSource=Versions.</summary>
    public List<PlatformVersionInfo> Versions { get; set; } = new();

    public bool IsLeaf => !string.IsNullOrEmpty(Variant);

    public override string ToString() => Name;
}
