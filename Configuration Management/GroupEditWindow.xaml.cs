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
        private string _iconColor = "#FFFFFF";
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
            ("IconUsers", "Пользователи"),
            ("IconHistory", "История"),
            ("IconSync", "Синхронизация"),
            ("IconBackup", "Резервная копия"),
            ("IconConfiguration", "Конфигурация"),
            ("IconPublish", "Публикация"),
            ("IconMonitoring", "Мониторинг"),
            ("IconScheduler", "Планировщик"),
            ("IconLogs", "Журнал"),
            ("IconRights", "Права"),
            ("IconExtension", "Расширение"),
            ("IconImport", "Импорт"),
            ("IconExport", "Экспорт"),
            ("IconFilter", "Фильтр"),
            ("IconCopy", "Копия"),
            ("IconEdit", "Редактирование"),
            ("IconSave", "Сохранение"),
            ("IconRefresh", "Обновление"),
            ("IconOpen", "Открыть"),
            ("IconWarning", "Внимание"),
            ("IconOk", "ОК"),
            ("IconError", "Ошибка"),
            ("IconAutostart", "Автозапуск"),
            ("IconTheme", "Тема"),
            ("IconSun", "Солнце"),
            ("IconMoon", "Луна"),
            ("IconCompare", "Сравнение"),
            ("IconMerge", "Объединение"),
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
                _color = string.IsNullOrWhiteSpace(editingGroup.Color) ? "#2D6CDF" : editingGroup.Color;
                _iconColor = string.IsNullOrWhiteSpace(editingGroup.IconColor) ? "#FFFFFF" : editingGroup.IconColor;
                _icon = editingGroup.Icon ?? string.Empty;
            }
            else
            {
                Result.Id = Guid.NewGuid().ToString();
            }

            UpdateParentPathDisplay();
            ApplyPaletteColors();
            BuildIconPicker();
            UpdateHeaderColorPreview();
            UpdateIconColorPreview();
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
            void Fill(System.Windows.Controls.Panel? panel)
            {
                if (panel is null) return;
                foreach (var child in panel.Children)
                {
                    if (child is Button button && button.Tag is string hex)
                        button.Background = new SolidColorBrush(ParseColor(hex));
                }
            }
            Fill(HeaderPaletteGrid);
            Fill(IconPaletteGrid);
        }

        private void BuildIconPicker()
        {
            IconPickerPanel.Children.Clear();
            var iconBrush = new SolidColorBrush(ParseColor(_iconColor));
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
                        Foreground = Brushes.White,
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
                                Fill = iconBrush,
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

            HighlightSelectedIcon();
        }

        /// <summary>Перекрашивает иконки в панели выбора в цвет иконки.</summary>
        private void ApplyIconPickerColors()
        {
            var brush = new SolidColorBrush(ParseColor(_iconColor));
            foreach (var child in IconPickerPanel.Children)
            {
                if (child is Button { Content: Path path })
                    path.Fill = brush;
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

        private static readonly Brush IconPickBaseBackground =
            new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)); // #374151

        private void HighlightSelectedIcon()
        {
            var c = ParseColor(_iconColor);
            // Светлая иконка — подсветка выбора делаем яркой обводкой, фон остаётся тёмным.
            var isLightIcon = (c.R + c.G + c.B) / 3.0 > 200;
            foreach (var child in IconPickerPanel.Children)
            {
                if (child is Button button && button.Tag is string key)
                {
                    var isSelected = string.Equals(key, _icon, StringComparison.Ordinal);
                    button.BorderBrush = isSelected
                        ? (isLightIcon
                            ? new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)) // янтарь
                            : new SolidColorBrush(c))
                        : new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
                    button.BorderThickness = new Thickness(isSelected ? 2.5 : 1);
                    button.Background = IconPickBaseBackground;
                }
            }
        }

        private void OnHeaderPalette_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                _color = hex;
                UpdateHeaderColorPreview();
            }
        }

        private void OnIconPalette_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex)
            {
                _iconColor = hex;
                UpdateIconColorPreview();
            }
        }

        private void OnPickHeaderColor_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ColorPickerWindow(_color) { Owner = this };
            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.Result))
            {
                _color = picker.Result;
                UpdateHeaderColorPreview();
            }
        }

        private void OnPickIconColor_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ColorPickerWindow(_iconColor) { Owner = this };
            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.Result))
            {
                _iconColor = picker.Result;
                UpdateIconColorPreview();
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
            Result.IconColor = _iconColor;
            Result.Icon = _icon;
            Result.ParentId = _parentId ?? string.Empty;
            DialogResult = true;
        }

        private void UpdateHeaderColorPreview()
        {
            ColorPreview.Background = new SolidColorBrush(ParseColor(_color));
            ColorHexText.Text = _color;
        }

        private void UpdateIconColorPreview()
        {
            IconColorPreview.Background = new SolidColorBrush(ParseColor(_iconColor));
            IconColorHexText.Text = _iconColor;
            ApplyIconPickerColors();
            HighlightSelectedIcon();
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
