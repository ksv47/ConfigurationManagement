#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Models;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог создания/редактирования группы. Поддерживает выбор родительской группы,
    /// иконки и цвета. Avalonia/Linux-версия WPF-окна <see cref="GroupEditWindow"/>.
    /// </summary>
    public class GroupEditWindow : ModalWindowBase
    {
        private readonly IReadOnlyList<Group> _groups;
        private readonly Group? _editingGroup;
        private readonly bool _noGroupMode;
        private string _color = "#2D6CDF";
        private string _iconColor = "#FFFFFF";
        private string _icon = string.Empty;
        private string _parentId = string.Empty;

        private readonly TextBox _nameBox = new() { Padding = new Thickness(8, 6) };
        private readonly TextBox _descriptionBox = new() { Padding = new Thickness(8, 6), AcceptsReturn = true, MinHeight = 70 };
        private readonly TextBlock _parentPathBox = new();
        private readonly Border _headerColorPreview = new() { Height = 40, CornerRadius = new CornerRadius(6) };
        private readonly TextBlock _colorHexText = new();
        private readonly Border _iconColorPreview = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(6) };
        private readonly TextBlock _iconColorHexText = new();
        private readonly WrapPanel _iconPickerPanel = new();

        private static readonly (string Key, string Label)[] AvailableIcons =
        {
            ("", "По умолчанию"), ("IconFolder", "Папка"), ("IconDatabase", "База"),
            ("IconServices", "Сервер"), ("IconStar", "Звезда"), ("IconTag", "Тег"),
            ("IconPin", "Закрепить"), ("IconInfo", "Инфо"), ("IconPlay", "Запуск"),
            ("IconSettings", "Настройки"), ("IconSearch", "Поиск"), ("IconAdd", "Добавить"),
            ("IconUsers", "Пользователи"), ("IconHistory", "История"), ("IconSync", "Синхронизация"),
            ("IconBackup", "Резервная копия"), ("IconConfiguration", "Конфигурация"),
            ("IconEdit", "Редактирование"), ("IconSave", "Сохранение"), ("IconRefresh", "Обновление"),
            ("IconWarning", "Внимание"), ("IconError", "Ошибка"), ("IconTheme", "Тема"),
            ("IconCompare", "Сравнение"), ("IconMerge", "Объединение")
        };

        private static readonly string[] HeaderPalette =
        {
            "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16", "#22C55E", "#10B981", "#14B8A6",
            "#06B6D4", "#0EA5E9", "#3B82F6", "#2D6CDF", "#6366F1", "#8B5CF6", "#A855F7", "#D946EF",
            "#EC4899", "#F43F5E", "#78716C", "#374151"
        };

        private static readonly string[] IconPalette =
        {
            "#FFFFFF", "#F8FAFC", "#E2E8F0", "#CBD5E1", "#FBBF24", "#10B981", "#3B82F6", "#A855F7", "#F472B6"
        };

        public GroupEditWindow(IEnumerable<Group> groups, Group? parent = null)
            : this(groups, parent?.Id ?? string.Empty, editingGroup: null)
        {
        }

        public GroupEditWindow(IEnumerable<Group> groups, string parentId, Group? editingGroup)
            : this(groups, parentId, editingGroup, noGroupMode: false,
                  noGroupColor: null, noGroupIconColor: null, noGroupIcon: null)
        {
        }

        /// <summary>Редактирование служебного узла «Без группы» (только цвет и иконка).</summary>
        public GroupEditWindow(IEnumerable<Group> groups, string noGroupColor, string noGroupIconColor, string noGroupIcon)
            : this(groups, parentId: string.Empty, editingGroup: null, noGroupMode: true,
                  noGroupColor, noGroupIconColor, noGroupIcon)
        {
        }

        private GroupEditWindow(
            IEnumerable<Group> groups,
            string parentId,
            Group? editingGroup,
            bool noGroupMode,
            string? noGroupColor,
            string? noGroupIconColor,
            string? noGroupIcon)
        {
            Title = "Настройка группы";
            Width = 540;
            MinWidth = 460;
            MinHeight = 520;
            MaxHeight = 880;
            SizeToContent = SizeToContent.Height;
            CanResize = true;

            _groups = groups.ToList();
            _editingGroup = editingGroup;
            _parentId = parentId ?? string.Empty;
            _noGroupMode = noGroupMode;

            if (noGroupMode)
            {
                _nameBox.Text = "Без группы";
                _nameBox.IsEnabled = false;
                _descriptionBox.IsEnabled = false;
                _parentPathBox.Text = "— Корневая группа —";
                _color = !string.IsNullOrWhiteSpace(noGroupColor) ? noGroupColor : "#2D6CDF";
                _iconColor = !string.IsNullOrWhiteSpace(noGroupIconColor) ? noGroupIconColor : "#FFFFFF";
                _icon = noGroupIcon ?? string.Empty;
            }
            else if (editingGroup is not null)
            {
                Result.Id = editingGroup.Id;
                _nameBox.Text = editingGroup.Name;
                _descriptionBox.Text = editingGroup.Description;
                _color = string.IsNullOrWhiteSpace(editingGroup.Color) ? "#2D6CDF" : editingGroup.Color;
                _iconColor = string.IsNullOrWhiteSpace(editingGroup.IconColor) ? "#FFFFFF" : editingGroup.IconColor;
                _icon = editingGroup.Icon ?? string.Empty;
            }
            else
            {
                Result.Id = Guid.NewGuid().ToString();
            }

            UpdateParentPathDisplay();
            Content = BuildRoot();
        }

        public Group Result { get; private set; } = new();

        private void UpdateParentPathDisplay()
        {
            if (string.IsNullOrEmpty(_parentId))
            {
                _parentPathBox.Text = "— Корневая группа —";
                return;
            }

            var parent = _groups.FirstOrDefault(g =>
                string.Equals(g.Id, _parentId, StringComparison.OrdinalIgnoreCase));
            _parentPathBox.Text = parent is null
                ? "— Корневая группа —"
                : GroupHierarchyHelper.GetFullPath(parent, _groups);
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var tabs = new TabControl();

            // ===== Вкладка «Общие» =====
            var general = new StackPanel { Spacing = 10 };

            var nameLabel = new TextBlock { Text = "Наименование:" };
            general.Children.Add(nameLabel);
            general.Children.Add(_nameBox);

            var parentLabel = new TextBlock { Text = "Родительская группа:" };
            general.Children.Add(parentLabel);
            var parentRow = new Grid();
            parentRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            parentRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            _parentPathBox.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_parentPathBox, 0);
            parentRow.Children.Add(_parentPathBox);
            var selectParent = new Button { Content = "Выбрать…", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            selectParent.IsEnabled = !_noGroupMode;
            selectParent.Click += (_, _) => OnSelectParent_Click();
            Grid.SetColumn(selectParent, 1);
            parentRow.Children.Add(selectParent);
            general.Children.Add(parentRow);

            var descLabel = new TextBlock { Text = "Описание:" };
            general.Children.Add(descLabel);
            general.Children.Add(_descriptionBox);

            tabs.Items.Add(new TabItem { Header = "Общие", Content = new ScrollViewer { Content = general, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== Вкладка «Цвет» =====
            var colorTab = new StackPanel { Spacing = 10 };
            var headerColorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerColorRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            headerColorRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_headerColorPreview, 0);
            headerColorRow.Children.Add(_headerColorPreview);
            var pickHeader = new Button { Content = "Выбрать…", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickHeader.Click += (_, _) => OnPickHeaderColor_Click();
            Grid.SetColumn(pickHeader, 1);
            headerColorRow.Children.Add(pickHeader);
            colorTab.Children.Add(new TextBlock { Text = "Цвет заголовка группы:" });
            colorTab.Children.Add(headerColorRow);
            colorTab.Children.Add(_colorHexText);
            colorTab.Children.Add(new TextBlock { Text = "Палитра:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            colorTab.Children.Add(BuildPalette(HeaderPalette, hex => { _color = hex; UpdateHeaderColorPreview(); }));
            tabs.Items.Add(new TabItem { Header = "Цвет", Content = new ScrollViewer { Content = colorTab, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== Вкладка «Иконка» =====
            var iconTab = new StackPanel { Spacing = 10 };
            var iconColorRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            iconColorRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            iconColorRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(_iconColorPreview, 0);
            _iconColorPreview.HorizontalAlignment = HorizontalAlignment.Left;
            iconColorRow.Children.Add(_iconColorPreview);
            var pickIcon = new Button { Content = "Выбрать…", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            pickIcon.Click += (_, _) => OnPickIconColor_Click();
            Grid.SetColumn(pickIcon, 1);
            iconColorRow.Children.Add(pickIcon);
            iconTab.Children.Add(new TextBlock { Text = "Цвет иконки:" });
            iconTab.Children.Add(iconColorRow);
            iconTab.Children.Add(_iconColorHexText);
            iconTab.Children.Add(new TextBlock { Text = "Палитра иконки:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            iconTab.Children.Add(BuildPalette(IconPalette, hex => { _iconColor = hex; UpdateIconColorPreview(); }));
            iconTab.Children.Add(new TextBlock { Text = "Иконка:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            BuildIconPicker();
            var iconScroll = new ScrollViewer
            {
                Content = _iconPickerPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 300
            };
            iconTab.Children.Add(iconScroll);
            tabs.Items.Add(new TabItem { Header = "Иконка", Content = iconTab });

            Grid.SetRow(tabs, 0);
            grid.Children.Add(tabs);

            var buttons = BuildButtons("Сохранить", 140, OnSave_Click);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);

            // Инициализация предпросмотров
            UpdateHeaderColorPreview();
            UpdateIconColorPreview();

            return grid;
        }

        private static WrapPanel BuildPalette(IEnumerable<string> colors, Action<string> onClick)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var hex in colors)
            {
                var button = new Button
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(ParseColor(hex))
                };
                button.Click += (_, _) => onClick(hex);
                panel.Children.Add(button);
            }
            return panel;
        }

        private void BuildIconPicker()
        {
            _iconPickerPanel.Children.Clear();
            var iconBrush = new SolidColorBrush(ParseColor(_iconColor));
            foreach (var (key, label) in AvailableIcons)
            {
                var btn = new Button
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(3),
                    ToolTip = new ToolTip { Content = label },
                    Tag = key
                };

                if (string.IsNullOrEmpty(key))
                {
                    btn.Content = IconHelper.MakeIcon("IconClose", 16, "TextOnAccentColorBrush");
                }
                else
                {
                    var geom = ResolveIcon(key);
                    btn.Content = new Path
                    {
                        Data = geom,
                        Fill = iconBrush,
                        Width = 18,
                        Height = 18,
                        Stretch = Stretch.Uniform
                    };
                }

                btn.Click += (_, _) => { _icon = key; HighlightSelectedIcon(); };
                _iconPickerPanel.Children.Add(btn);
            }

            HighlightSelectedIcon();
        }

        private static Geometry ResolveIcon(string key)
        {
            if (Application.Current is { } app &&
                app.TryGetResource(key, null, out var res) && res is Geometry g)
                return g;
            return StreamGeometry.Parse("M0,0H1V1H0Z");
        }

        private void ApplyIconPickerColors()
        {
            var brush = new SolidColorBrush(ParseColor(_iconColor));
            foreach (var child in _iconPickerPanel.Children)
            {
                if (child is Button { Content: Path path })
                    path.Fill = brush;
            }
        }

        private void HighlightSelectedIcon()
        {
            var c = ParseColor(_iconColor);
            var isLightIcon = (c.R + c.G + c.B) / 3.0 > 200;
            foreach (var child in _iconPickerPanel.Children)
            {
                if (child is Button { Tag: string key } button)
                {
                    var isSelected = string.Equals(key, _icon, StringComparison.Ordinal);
                    button.BorderBrush = isSelected
                        ? (isLightIcon
                            ? new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))
                            : new SolidColorBrush(c))
                        : new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
                    button.BorderThickness = new Thickness(isSelected ? 2.5 : 1);
                    button.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
                }
            }
        }

        private void OnSelectParent_Click()
        {
            var dialog = new GroupPickerWindow(
                _groups,
                currentGroupId: _parentId,
                excludeGroupId: _editingGroup?.Id,
                allowNone: true,
                noneLabel: "— Корневая группа —");
            if (dialog.ShowDialogSync(this))
            {
                _parentId = dialog.ResultGroupId;
                UpdateParentPathDisplay();
            }
        }

        private void OnPickHeaderColor_Click()
        {
            var picker = new ColorPickerWindow(_color);
            if (picker.ShowDialogSync(this) && !string.IsNullOrWhiteSpace(picker.Result))
            {
                _color = picker.Result;
                UpdateHeaderColorPreview();
            }
        }

        private void OnPickIconColor_Click()
        {
            var picker = new ColorPickerWindow(_iconColor);
            if (picker.ShowDialogSync(this) && !string.IsNullOrWhiteSpace(picker.Result))
            {
                _iconColor = picker.Result;
                UpdateIconColorPreview();
            }
        }

        private void OnSave_Click()
        {
            if (!_noGroupMode)
            {
                Result.Name = _nameBox.Text?.Trim() ?? string.Empty;
                Result.Description = _descriptionBox.Text?.Trim() ?? string.Empty;
            }
            else
            {
                Result.Name = "Без группы";
            }

            Result.Color = _color;
            Result.IconColor = _iconColor;
            Result.Icon = _icon;
            Result.ParentId = _parentId ?? string.Empty;
        }

        private void UpdateHeaderColorPreview()
        {
            _headerColorPreview.Background = new SolidColorBrush(ParseColor(_color));
            _colorHexText.Text = _color;
        }

        private void UpdateIconColorPreview()
        {
            _iconColorPreview.Background = new SolidColorBrush(ParseColor(_iconColor));
            _iconColorHexText.Text = _iconColor;
            ApplyIconPickerColors();
            HighlightSelectedIcon();
        }

        private static Color ParseColor(string? hex)
        {
            try
            {
                return Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#2D6CDF" : hex);
            }
            catch
            {
                return Color.Parse("#2D6CDF");
            }
        }
    }
}
#endif