#if LINUX
using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Models;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux). Портированы ключевые вкладки:
    /// «Настройки», «Клавиши», «О программе». Полноценные вкладки «Отображение»,
    /// «Платформы», «ibases.v8i», «Базы» и редактор цветовых схем требуют
    /// публичного API сохранения настроек в Avalonia-версии <see cref="MainViewModel"/>
    /// (отложено — см. комментарии и итоговый отчёт).
    /// </summary>
    public class SettingsWindow : ModalWindowBase
    {
        private readonly MainViewModel _viewModel;

        /// <summary>
        /// Создаёт диалог настроек приложения.
        /// </summary>
        /// <param name="viewModel">Главная модель представления приложения.</param>
        public SettingsWindow(MainViewModel viewModel)
        {
            Title = "Настройки приложения";
            Width = 720;
            Height = 580;
            MinWidth = 640;
            MinHeight = 520;

            _viewModel = viewModel;
            Content = BuildRoot();
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var tabs = new TabControl();

            // ===== Настройки =====
            var settings = new StackPanel { Spacing = 14 };

            // Тема оформления
            var themeLabel = new TextBlock { Text = "Тема оформления:", FontWeight = FontWeight.SemiBold };
            settings.Children.Add(themeLabel);
            var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var lightTheme = new RadioButton { Content = "Светлая", GroupName = "Theme", IsChecked = !ThemeManager.CurrentScheme.IsDark };
            var darkTheme = new RadioButton { Content = "Тёмная", GroupName = "Theme", IsChecked = ThemeManager.CurrentScheme.IsDark };
            ThemeChanged(lightTheme, darkTheme);
            themePanel.Children.Add(lightTheme);
            themePanel.Children.Add(darkTheme);
            settings.Children.Add(themePanel);

            // Параметры текущей сессии
            settings.Children.Add(new TextBlock { Text = "Клиент по умолчанию:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            var clientPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientAuto", "Авто"));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThin", "Тонкий"));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThick", "Толстый"));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThickOrdinary", "Толстый (обычные)"));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientOrdinary", "Обычный"));
            settings.Children.Add(clientPanel);

            settings.Children.Add(new TextBlock { Text = "Разрядность по умолчанию:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            var archPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArchAuto", "Авто"));
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArch32", "32"));
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArch64", "64"));
            settings.Children.Add(archPanel);

            settings.Children.Add(new TextBlock
            {
                Text = "Вкладки «Отображение», «Платформы», «ibases.v8i», «Базы» и редактор цветовых схем будут доступны после добавления публичного API сохранения настроек в Avalonia-версию MainViewModel.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            tabs.Items.Add(new TabItem { Header = "Настройки", Content = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== Клавиши =====
            var hotkeys = new StackPanel { Spacing = 10 };
            hotkeys.Children.Add(new TextBlock
            {
                Text = "Горячие клавиши приложения:",
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var rows = new (string Action, string Key)[]
            {
                ("Запуск 1С:Предприятие", _viewModel.HotkeyEnterprise),
                ("Конфигуратор", _viewModel.HotkeyConfigurator),
                ("Изменить настройки базы", _viewModel.HotkeyEdit),
                ("Добавить базу / группу", _viewModel.HotkeyAdd),
                ("Избранное", _viewModel.HotkeyFavorite),
                ("Закрепить", _viewModel.HotkeyPin),
                ("Удалить", _viewModel.HotkeyDelete),
                ("Очистить кэш", _viewModel.HotkeyClearCache)
            };
            foreach (var (action, key) in rows)
                hotkeys.Children.Add(BuildHotkeyRow(action, key));

            hotkeys.Children.Add(new TextBlock
            {
                Text = "Изменение назначения клавиш (переназначение) отложено — доступен справочный список текущих сочетаний.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            tabs.Items.Add(new TabItem { Header = "Клавиши", Content = new ScrollViewer { Content = hotkeys, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== О программе =====
            var about = BuildAboutTab();
            tabs.Items.Add(new TabItem { Header = "О программе", Content = new ScrollViewer { Content = about, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            Grid.SetRow(tabs, 0);
            grid.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            var ok = new Button { Content = "ОК", MinWidth = 110, IsDefault = true };
            ok.Click += (_, _) => { DialogResult = true; Close(); };
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);

            return grid;
        }

        private static void ThemeChanged(RadioButton light, RadioButton dark)
        {
            light.IsCheckedChanged += (_, _) =>
            {
                if (light.IsChecked == true)
                    ThemeManager.ApplyTheme(ThemeManager.LightThemeName);
            };
            dark.IsCheckedChanged += (_, _) =>
            {
                if (dark.IsChecked == true)
                    ThemeManager.ApplyTheme(ThemeManager.DarkThemeName);
            };
        }

        /// <summary>Радиокнопка с TwoWay-привязкой к свойству ViewModel (режим сессии).</summary>
        private RadioButton Radio(string groupName, string path, string content)
        {
            var r = new RadioButton { Content = content, GroupName = groupName };
            r.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Avalonia.Data.Binding(path) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return r;
        }

        private static Grid BuildHotkeyRow(string action, string key)
        {
            var grid = new Grid { Margin = new Thickness(0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(140)));

            var actionBlock = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(actionBlock, 0);
            grid.Children.Add(actionBlock);

            var keyBorder = new Border
            {
                Child = new TextBlock { Text = key, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Padding = new Thickness(10, 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(keyBorder, 1);
            grid.Children.Add(keyBorder);
            return grid;
        }

        private Control BuildAboutTab()
        {
            var panel = new StackPanel { Spacing = 12 };

            var asm = Assembly.GetExecutingAssembly();
            var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? asm.GetName().Version?.ToString() ?? "";
            var title = asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "Управление конфигурациями 1С";

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = FontWeight.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Версия: v{infoVersion}",
                FontSize = 14
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Приложение для управления списком информационных баз 1С:Предприятие.\n" +
                       "Портируемая версия для Linux (Avalonia) — Этап 3: диалоговые окна.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Платформа: {Environment.OSVersion} | {Environment.Is64BitOperatingSystem}\n" +
                       $"Каталог данных: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}/ConfigurationManagement",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7
            });

            return panel;
        }
    }
}
#endif