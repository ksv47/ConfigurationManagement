using System.Windows;
using System.Windows.Controls;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management;

/// <summary>
/// Диалог выбора типа очищаемого кэша 1С и набора информационных баз,
/// для которых нужно выполнить очистку.
/// </summary>
public partial class CacheCleanWindow : Window
{
    private readonly List<Infobase> _infobases;
    private readonly Dictionary<CheckBox, Infobase> _baseChecks = new();

    /// <param name="infobases">Все доступные информационные базы.</param>
    /// <param name="initialKind">Изначально выбранный тип кэша.</param>
    /// <param name="defaultSelected">База, выбранная по умолчанию (например, выделенная в главном окне).</param>
    public CacheCleanWindow(IEnumerable<Infobase> infobases, OneCCacheKind initialKind, Infobase? defaultSelected = null)
    {
        InitializeComponent();
        _infobases = infobases.ToList();

        ProgramCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.Program);
        UserCacheCheck.IsChecked = initialKind.HasFlag(OneCCacheKind.User);

        BuildBasesList(defaultSelected);
        UpdateCount();
        UpdateCleanEnabled();
    }

    /// <summary>Тип кэша, выбранный пользователем.</summary>
    public OneCCacheKind SelectedCacheKind { get; private set; } = OneCCacheKind.None;

    /// <summary>Список баз, выбранных для очистки.</summary>
    public IReadOnlyList<Infobase> SelectedInfobases { get; private set; } = Array.Empty<Infobase>();

    private void BuildBasesList(Infobase? defaultSelected)
    {
        BasesPanel.Children.Clear();
        _baseChecks.Clear();

        var checkStyle = (Style)FindResource("ModernMaterialCheckBox");

        foreach (var ib in _infobases)
        {
            var check = new CheckBox
            {
                Content = ib.Name,
                IsChecked = ReferenceEquals(ib, defaultSelected),
                Margin = new Thickness(0, 4, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = checkStyle,
                ToolTip = string.IsNullOrWhiteSpace(ib.ConnectionPathDisplay)
                    ? ib.Name
                    : ib.ConnectionPathDisplay
            };
            check.Checked += OnBaseChecked;
            check.Unchecked += OnBaseChecked;
            _baseChecks[check] = ib;
            BasesPanel.Children.Add(check);
        }
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var check in _baseChecks.Keys)
            check.IsChecked = true;
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var check in _baseChecks.Keys)
            check.IsChecked = false;
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        foreach (var kv in _baseChecks)
        {
            var visible = query.Length == 0
                || kv.Value.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (kv.Value.ConnectionPathDisplay?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
            kv.Key.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnBaseChecked(object sender, RoutedEventArgs e)
    {
        UpdateCount();
        UpdateCleanEnabled();
    }

    private void OnCacheKindChanged(object sender, RoutedEventArgs e)
    {
        UpdateCleanEnabled();
    }

    private void UpdateCount()
    {
        var selected = _baseChecks.Count(kv => kv.Key.IsChecked == true);
        var total = _baseChecks.Count;
        BasesCountText.Text = $"Выбрано: {selected} из {total}";
    }

    private void UpdateCleanEnabled()
    {
        var hasType = ProgramCacheCheck.IsChecked == true || UserCacheCheck.IsChecked == true;
        var hasBases = _baseChecks.Any(kv => kv.Key.IsChecked == true);
        CleanButton.IsEnabled = hasType && hasBases;
    }

    private void OnClean_Click(object sender, RoutedEventArgs e)
    {
        var kind = OneCCacheKind.None;
        if (ProgramCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.Program;
        if (UserCacheCheck.IsChecked == true)
            kind |= OneCCacheKind.User;

        SelectedCacheKind = kind;
        SelectedInfobases = _baseChecks
            .Where(kv => kv.Key.IsChecked == true)
            .Select(kv => kv.Value)
            .ToList();

        DialogResult = true;
    }
}