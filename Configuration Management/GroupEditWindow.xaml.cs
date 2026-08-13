using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Configuration_Management.Models;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания/редактирования группы.
    /// Поддерживает выбор родительской группы, иконки и цвета.
    /// </summary>
    public partial class GroupEditWindow : Window
    {
        private readonly ObservableCollection<Group> _groups;
        private readonly Group? _editingGroup;
        private string _color = "#2D6CDF";
        private string _icon = string.Empty;
        private string _parentId = string.Empty;

        private static readonly (string Key, string Label)[] AvailableIcons =
        {
            ("", "По умолчанию"),
            ("IconFolder", "Папка"),
            ("IconDatabase", "База"),
            ("IconServices", "Сервер"),
            ("IconStar", "Звезда"),
            ("IconTag", "Тег"),
            ("IconPin", "Закрепить"),
            ("IconInfo", "Инфо"),
            ("IconPlay", "Запуск"),
            ("IconSettings", "Настройки"),
            ("IconSearch", "Поиск"),
            ("IconAdd", "Добавить"),
        };

        public GroupEditWindow(IEnumerable<Group> groups, Group? parent = null)
            : this(groups, parent?.Id ?? string.Empty, editingGroup: null)
        {
        }

        public GroupEditWindow(IEnumerable<Group> groups, string parentId, Group? editingGroup)
        {
            InitializeComponent();
            _groups = new ObservableCollection<Group>(groups);
            _editingGroup = editingGroup;
            _parentId = parentId ?? string.Empty;

            if (editingGroup is not null)
            {
                Result.Id = editingGroup.Id;
                NameBox.Text = editingGroup.Name;
                DescriptionBox.Text = editingGroup.Description;
                _color = editingGroup.Color;
                _icon = editingGroup.Icon ?? string.Empty;
            }
            else
            {
                Result.Id = Guid.NewGuid().ToString();
            }

            UpdateParentPathDisplay();
            ApplyPaletteColors();
            BuildIconPicker();
            UpdateColorPreview();
            HighlightSelectedIcon();
        }

        public Group Result { get; private set; } = new();

        private void UpdateParentPathDisplay()
        {
            if (string.IsNullOrEmpty(_parentId))
            {
                ParentPathBox.Text = "— Корневая группа —";
                return;
            }

            var parent = _groups.FirstOrDefault(g =>
                string.Equals(g.Id, _parentId, StringComparison.OrdinalIgnoreCase));
            ParentPathBox.Text = parent is null
                ? "— Корневая группа —"
                : GroupHierarchyHelper.GetFullPath(parent, _groups);
        }

        private void OnSelectParent_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: _parentId,
                excludeGroupId: _editingGroup?.Id,
                allowNone: true,
                noneLabel: "— Корневая группа —")
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
            {
                _parentId = dialog.ResultGroupId;
                UpdateParentPathDisplay();
            }
        }

        private void ApplyPaletteColors()
        {
            foreach (var child in PaletteGrid.Children)
            {
                if (child is Button button && button.Tag is string hex)
                    button.Background = new SolidColorBrush(ParseColor(hex));
            }
        }

        private void BuildIconPicker()
        {
            IconPickerPanel.Children.Clear();
            foreach (var (key, label) in AvailableIcons)
            {
                var btn = new Button
                {
                    Style = (Style)FindResource("IconPickButton"),
                    Tag = key,
                    ToolTip = label
                };

                if (string.IsNullOrEmpty(key))
                {
                    btn.Content = new TextBlock
                    {
                        Text = "∅",
                        FontSize = 14,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    try
                    {
                        var geom = TryFindResource(key) as Geometry;
                        if (geom != null)
                        {
                            btn.Content = new Path
                            {
                                Data = geom,
                                Fill = (Brush)FindResource("TextPrimaryBrush"),
                                Width = 18,
                                Height = 18,
                                Stretch = Stretch.Uniform
                            };
                        }
                        else
                        {
                            btn.Content = new TextBlock { Text = "?", FontSize = 12 };
                        }
                    }
                    catch
                    {
                        btn.Content = new TextBlock { Text = "?", FontSize = 12 };
                    }
                }

                btn.Click += OnIcon_Click;
                IconPickerPanel.Children.Add(btn);
            }
        }

        private void OnIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string key)
            {
                _icon = key;
                HighlightSelectedIcon();
            }
        }

        private void HighlightSelectedIcon()
        {
            foreach (var child in IconPickerPanel.Children)
            {
                if (child is Button button && button.Tag is string key)
                {
                    var isSelected = string.Equals(key, _icon, StringComparison.Ordinal);
                    button.BorderBrush = isSelected
                        ? (Brush)FindResource("AccentBrush")
                        : (Brush)FindResource("BorderBrushColor");
                    button.BorderThickness = new Thickness(isSelected ? 2 : 1);
                }
            }
        }

        private void OnPaletteColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                _color = hex;
                UpdateColorPreview();
            }
        }

        private void OnPickColor_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ColorPickerWindow(_color) { Owner = this };
            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.Result))
            {
                _color = picker.Result;
                UpdateColorPreview();
            }
        }

        private void OnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Укажите наименование группы.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result.Name = NameBox.Text.Trim();
            Result.Description = DescriptionBox.Text.Trim();
            Result.Color = _color;
            Result.Icon = _icon;
            Result.ParentId = _parentId ?? string.Empty;
            DialogResult = true;
        }

        private void UpdateColorPreview()
        {
            ColorPreview.Background = new SolidColorBrush(ParseColor(_color));
            ColorHexText.Text = _color;
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex ?? "#2D6CDF");
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString("#2D6CDF");
            }
        }
    }
}
