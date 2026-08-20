#if LINUX
using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог выбора типа добавляемого элемента (информационная база или группа),
    /// аналогичный стартовому окну «1С:Предприятие». Avalonia/Linux-версия WPF-окна
    /// <see cref="AddEditWindow"/>.
    /// </summary>
    public class AddEditWindow : ModalWindowBase
    {
        /// <summary>Выбранный тип элемента: "Infobase", "CreateEmpty", "CreateFromTemplate" или "Group".</summary>
        public string SelectedType { get; private set; } = "Infobase";

        public AddEditWindow()
        {
            Title = "Добавление в список";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            SystemDecorations = SystemDecorations.Full;

            Content = BuildRoot();
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var header = new TextBlock
            {
                Text = "Что добавить в список?",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var options = new StackPanel();

            options.Children.Add(BuildOption("IconList", "Существующая база",
                "Добавить в список уже созданную информационную базу 1С.", "Infobase", true));
            options.Children.Add(BuildOption("IconSave", "Создать пустую базу",
                "Создать новую пустую ИБ (файловую или на сервере) через CREATEINFOBASE.", "CreateEmpty"));
            options.Children.Add(BuildOption("IconPackage", "Создать из шаблона",
                "Создать базу из шаблона (.cf / .dt) — как в стандартном стартере 1С.", "CreateFromTemplate"));
            options.Children.Add(BuildOption("IconFolder", "Группа",
                "Создать группу для объединения информационных баз по категориям.", "Group"));

            Grid.SetRow(options, 1);
            grid.Children.Add(options);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var cancel = new Button { Content = "Отмена", MinWidth = 100, IsCancel = true };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);

            var next = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "Далее", VerticalAlignment = VerticalAlignment.Center },
                        IconHelper.MakeIcon("IconArrowRight", 16, "TextOnAccentColorBrush")
                    }
                },
                MinWidth = 110,
                Background = new SolidColorBrush(Color.Parse("#16A34A")),
                Foreground = Brushes.White,
                IsDefault = true
            };
            next.Click += (_, _) => { DialogResult = true; Close(); };
            buttons.Children.Add(next);

            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        private RadioButton BuildOption(string iconKey, string title, string description, string tag, bool isChecked = false)
        {
            var radio = new RadioButton
            {
                Tag = tag,
                GroupName = "AddType",
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var content = new Grid { Margin = new Thickness(0) };
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var iconBlock = IconHelper.MakeIcon(iconKey, 26);
            Grid.SetColumn(iconBlock, 0);
            content.Children.Add(iconBlock);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
            textPanel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });
            Grid.SetColumn(textPanel, 1);
            content.Children.Add(textPanel);

            radio.Content = content;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && radio.Tag is string key)
                    SelectedType = key;
            };

            return radio;
        }
    }
}
#endif