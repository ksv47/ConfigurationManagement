#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
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
            Title = LocalizationManager.T("Settings.Title");
            // Шесть вкладок в одну строку требуют этой ширины: в 840 последняя
            // переносилась на вторую строку.
            Width = 940;
            Height = 620;
            MinWidth = 860;
            MinHeight = 520;

            _viewModel = viewModel;
            // Без контекста привязки переключателей клиента и разрядности
            // не находили свойств и всегда стояли пустыми.
            DataContext = viewModel;
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
            var themeLabel = new TextBlock { Text = LocalizationManager.T("Settings.ThemeLabel"), FontWeight = FontWeight.SemiBold };
            settings.Children.Add(themeLabel);
            var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var lightTheme = new RadioButton { Content = LocalizationManager.T("Main.LightTheme"), GroupName = "Theme", IsChecked = !ThemeManager.CurrentScheme.IsDark };
            var darkTheme = new RadioButton { Content = LocalizationManager.T("Main.DarkTheme"), GroupName = "Theme", IsChecked = ThemeManager.CurrentScheme.IsDark };
            ThemeChanged(lightTheme, darkTheme);
            themePanel.Children.Add(lightTheme);
            themePanel.Children.Add(darkTheme);
            settings.Children.Add(themePanel);

            // Язык интерфейса
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Language") + ":",
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 4)
            });
            var langBox = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
            langBox.ItemsSource = LocalizationManager.Instance.AvailableLanguages;
            langBox.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
            langBox.SelectedItem = LocalizationManager.Instance.AvailableLanguages
                .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage);
            settings.Children.Add(langBox);
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.LanguageHint"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            // Компактный режим интерфейса.
            var compactToggle = new CheckBox
            {
                Content = LocalizationManager.T("Settings.CompactMode"),
                IsChecked = _viewModel.CompactMode,
                Margin = new Thickness(0, 8, 0, 4)
            };
            compactToggle.IsCheckedChanged += (_, _) =>
            {
                var value = compactToggle.IsChecked == true;
                _viewModel.CompactMode = value;
                _viewModel.ApplyCompactMode(value);
            };
            settings.Children.Add(compactToggle);

            // Параметры текущей сессии
            settings.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.DefaultClientLabel"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            // Пять вариантов клиента в строку не помещаются в окно, поэтому переносятся.
            var clientPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientAuto", LocalizationManager.T("Main.SessionClientAuto")));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThin", LocalizationManager.T("Main.SessionClientThin")));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThick", LocalizationManager.T("Main.SessionClientThickManaged")));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientThickOrdinary", LocalizationManager.T("Main.SessionClientThickOrdinary")));
            clientPanel.Children.Add(Radio("SessionClient", "IsSessionClientOrdinary", LocalizationManager.T("Main.SessionClientOrdinary")));
            settings.Children.Add(clientPanel);

            settings.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.DefaultArch"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            var archPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArchAuto", LocalizationManager.T("Main.SessionClientAuto")));
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArch32", "32"));
            archPanel.Children.Add(Radio("SessionArch", "IsSessionArch64", "64"));
            settings.Children.Add(archPanel);

            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.AvaloniaPendingTabs"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            tabs.Items.Add(new TabItem { Header = LocalizationManager.T("Settings.TabGeneral"), Content = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== Платформы =====
            var platforms = new StackPanel { Spacing = 6 };
            platforms.Children.Add(Hint(LocalizationManager.T("Settings.Platforms.Intro")));

            var versionsList = new ListBox { MinHeight = 120, MaxHeight = 180 };
            var versionsEmpty = Hint(LocalizationManager.T("Settings.PlatformsNotFound"));
            platforms.Children.Add(versionsList);
            platforms.Children.Add(versionsEmpty);

            var pathsList = new ListBox { MinHeight = 90, MaxHeight = 140 };
            // Наблюдаемый список: список сам обновляется и не теряет выделение
            // с прокруткой, как было бы при подмене ItemsSource.
            var paths = new ObservableCollection<string>(_viewModel.AdditionalPlatformSearchPaths);
            pathsList.ItemsSource = paths;

            void RefreshVersions()
            {
                var found = _viewModel.FindPlatformVersions(paths);
                versionsList.ItemsSource = found;
                // Пустой список без пояснения выглядит как поломка, поэтому
                // показываем ту же подсказку, что и WPF-версия.
                versionsEmpty.IsVisible = found.Count == 0;
            }

            RefreshVersions();

            var refreshButton = new Button { Content = LocalizationManager.T("Settings.Platforms.Refresh") };
            ToolTip.SetTip(refreshButton, LocalizationManager.T("Settings.Platforms.RefreshTooltip"));
            refreshButton.Click += (_, _) => RefreshVersions();
            platforms.Children.Add(refreshButton);

            platforms.Children.Add(GroupTitle(LocalizationManager.T("Settings.AdditionalPaths")));
            platforms.Children.Add(Hint(LocalizationManager.T("Settings.AdditionalPaths.HintLinux")));
            platforms.Children.Add(pathsList);

            var pathButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var addPath = new Button { Content = LocalizationManager.T("Settings.AdditionalPaths.Add") };
            ToolTip.SetTip(addPath, LocalizationManager.T("Settings.AdditionalPaths.AddTooltip"));
            addPath.Click += (_, _) =>
            {
                var folder = _viewModel.PickFolder(LocalizationManager.T("Settings.AdditionalPaths.Add"));
                if (string.IsNullOrWhiteSpace(folder) || paths.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    return;
                paths.Add(folder);
                // Список версий пересчитывается сразу, как в WPF-версии.
                RefreshVersions();
            };
            var removePath = new Button { Content = LocalizationManager.T("Common.Delete") };
            ToolTip.SetTip(removePath, LocalizationManager.T("Settings.AdditionalPaths.RemoveTooltip"));
            removePath.Click += (_, _) =>
            {
                if (pathsList.SelectedItem is not string selected)
                    return;
                paths.Remove(selected);
                RefreshVersions();
            };
            pathButtons.Children.Add(addPath);
            pathButtons.Children.Add(removePath);
            platforms.Children.Add(pathButtons);

            platforms.Children.Add(GroupTitle(LocalizationManager.T("Settings.DefaultArch")));
            platforms.Children.Add(Hint(LocalizationManager.T("Settings.DefaultArch.Hint")));
            var archBox = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
            archBox.ItemsSource = new[] { "X64", "X86" };
            archBox.SelectedItem = string.Equals(_viewModel.DefaultArchitecture, "X86", StringComparison.OrdinalIgnoreCase) ? "X86" : "X64";
            platforms.Children.Add(archBox);

            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("Settings.TabPlatforms"),
                Content = new ScrollViewer { Content = platforms, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // ===== Отображение =====
            var display = new StackPanel { Spacing = 6 };

            display.Children.Add(GroupTitle(LocalizationManager.T("Settings.Subtab.Icons")));
            display.Children.Add(Hint(LocalizationManager.T("Settings.Icons.Description")));
            var favoritesCheck = DisplayCheck("Settings.Icons.FavoritesButton", _viewModel.ShowFavoritesButton);
            var pinnedCheck = DisplayCheck("Settings.Icons.PinButton", _viewModel.ShowPinnedButton);
            var tagsCheck = DisplayCheck("Settings.Icons.Tags", _viewModel.ShowTags);
            var tagPanelCheck = DisplayCheck("Settings.Icons.TagFilterPanel", _viewModel.ShowTagFilterPanel);
            foreach (var check in new[] { favoritesCheck, pinnedCheck, tagsCheck, tagPanelCheck })
                display.Children.Add(check);

            display.Children.Add(GroupTitle(LocalizationManager.T("Settings.Subtab.Columns")));
            display.Children.Add(Hint(LocalizationManager.T("Settings.Columns.Description")));
            var versionCheck = DisplayCheck("Column.Version", _viewModel.ShowVersionColumn);
            var configurationCheck = DisplayCheck("Settings.Columns.Configuration", _viewModel.ShowConfigurationColumn);
            var launchModeCheck = DisplayCheck("Column.LaunchMode", _viewModel.ShowLaunchModeColumn);
            var serverCheck = DisplayCheck("Column.ServerBase", _viewModel.ShowServerColumn);
            var lastLaunchCheck = DisplayCheck("Column.LastLaunch", _viewModel.ShowLastLaunchColumn);
            var sizeCheck = DisplayCheck("Settings.Columns.Size", _viewModel.ShowSizeColumn);
            foreach (var check in new[] { versionCheck, configurationCheck, launchModeCheck, serverCheck, lastLaunchCheck, sizeCheck })
                display.Children.Add(check);

            display.Children.Add(GroupTitle(LocalizationManager.T("Settings.Subtab.Panels")));
            display.Children.Add(Hint(LocalizationManager.T("Settings.Panels.Description")));
            var rightPanelCheck = DisplayCheck("Settings.Panels.RightPanelDetails", _viewModel.ShowRightPanelDetails);
            var sessionPanelCheck = DisplayCheck("Settings.Panels.SessionLaunchPanel", _viewModel.ShowSessionLaunchPanel);
            var groupByGroupCheck = DisplayCheck("Settings.Panels.GroupByGroups", _viewModel.GroupByGroup);
            var emptyGroupsCheck = DisplayCheck("Settings.Panels.ShowEmptyGroups", _viewModel.ShowEmptyGroups);
            foreach (var check in new[] { rightPanelCheck, sessionPanelCheck, groupByGroupCheck, emptyGroupsCheck })
                display.Children.Add(check);

            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("Settings.TabDisplay"),
                Content = new ScrollViewer { Content = display, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // ===== Базы (ibases.v8i) =====
            var bases = new StackPanel { Spacing = 6 };
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Ibases.Description")));

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.SyncMode")));
            var syncModes = new[]
            {
                (Mode: IbasesSyncMode.None, Text: LocalizationManager.T("Settings.Ibases.SyncModeDisabled")),
                (Mode: IbasesSyncMode.Import, Text: LocalizationManager.T("Settings.Ibases.SyncModeImport")),
                (Mode: IbasesSyncMode.Export, Text: LocalizationManager.T("Settings.Ibases.SyncModeExport")),
                (Mode: IbasesSyncMode.Both, Text: LocalizationManager.T("Settings.Ibases.SyncModeBoth"))
            };
            var syncModeBox = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
            syncModeBox.ItemsSource = syncModes.Select(m => m.Text).ToList();
            syncModeBox.SelectedIndex = Array.FindIndex(syncModes, m => m.Mode == _viewModel.IbasesSyncMode);
            bases.Children.Add(syncModeBox);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.File")));
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var fileBox = new TextBox { Text = _viewModel.IbasesSyncFilePath, MinWidth = 420 };
            var browse = new Button { Content = "..." };
            ToolTip.SetTip(browse, LocalizationManager.T("Settings.Ibases.BrowseTooltip"));
            browse.Click += (_, _) =>
            {
                var picked = _viewModel.PickFile(
                    LocalizationManager.T("Sync.ChooseIbasesFile"),
                    LocalizationManager.T("Sync.IbasesFilter"));
                if (!string.IsNullOrWhiteSpace(picked))
                    fileBox.Text = picked;
            };
            fileRow.Children.Add(fileBox);
            fileRow.Children.Add(browse);
            bases.Children.Add(fileRow);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.SyncTrigger")));
            var triggers = new[]
            {
                (Trigger: IbasesSyncTrigger.OnStartup, Text: LocalizationManager.T("Settings.Ibases.TriggerStartup")),
                (Trigger: IbasesSyncTrigger.Interval, Text: LocalizationManager.T("Settings.Ibases.TriggerInterval")),
                (Trigger: IbasesSyncTrigger.Schedule, Text: LocalizationManager.T("Settings.Ibases.TriggerSchedule"))
            };
            var triggerBox = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
            triggerBox.ItemsSource = triggers.Select(t => t.Text).ToList();
            triggerBox.SelectedIndex = Array.FindIndex(triggers, t => t.Trigger == _viewModel.IbasesSyncTrigger);
            bases.Children.Add(triggerBox);

            var intervalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            intervalRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.Ibases.Interval"), VerticalAlignment = VerticalAlignment.Center });
            var intervalBox = new TextBox { Text = _viewModel.IbasesSyncIntervalMinutes.ToString(), Width = 80 };
            intervalRow.Children.Add(intervalBox);
            intervalRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.Ibases.ScheduleTime"), VerticalAlignment = VerticalAlignment.Center });
            var scheduleBox = new TextBox { Text = _viewModel.IbasesSyncScheduleTime, Width = 80 };
            intervalRow.Children.Add(scheduleBox);
            bases.Children.Add(intervalRow);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Maintenance")));
            var backupCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.Ibases.BackupNote"),
                IsChecked = _viewModel.IbasesBackupEnabled
            };
            bases.Children.Add(backupCheck);
            var keepRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keepRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.Ibases.BackupKeepCount"), VerticalAlignment = VerticalAlignment.Center });
            var keepBox = new TextBox { Text = _viewModel.IbasesBackupKeepCount.ToString(), Width = 80 };
            keepRow.Children.Add(keepBox);
            bases.Children.Add(keepRow);

            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("Settings.TabBases"),
                Content = new ScrollViewer { Content = bases, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // ===== Клавиши =====
            var hotkeys = new StackPanel { Spacing = 10 };
            hotkeys.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Hotkeys.Title"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Поля назначения: HotkeyBox ловит сочетание с клавиатуры, Delete
            // снимает назначение, Escape отменяет ввод.
            var hotkeyEnterprise = HotkeyRow(hotkeys, LocalizationManager.T("Main.LaunchEnterprise"), _viewModel.HotkeyEnterprise);
            var hotkeyConfigurator = HotkeyRow(hotkeys, LocalizationManager.T("Main.SectionConfigurator"), _viewModel.HotkeyConfigurator);
            var hotkeyEdit = HotkeyRow(hotkeys, LocalizationManager.T("Main.EditSettings"), _viewModel.HotkeyEdit);
            var hotkeyAdd = HotkeyRow(hotkeys, LocalizationManager.T("Main.AddBaseOrGroup"), _viewModel.HotkeyAdd);
            var hotkeyFavorite = HotkeyRow(hotkeys, LocalizationManager.T("Main.Favorites"), _viewModel.HotkeyFavorite);
            var hotkeyPin = HotkeyRow(hotkeys, LocalizationManager.T("Main.Pin"), _viewModel.HotkeyPin);
            var hotkeyDelete = HotkeyRow(hotkeys, LocalizationManager.T("Common.Delete"), _viewModel.HotkeyDelete);
            var hotkeyClearCache = HotkeyRow(hotkeys, LocalizationManager.T("Main.ClearCache"), _viewModel.HotkeyClearCache);

            hotkeys.Children.Add(Hint(LocalizationManager.T("Hotkey.Tooltip")));

            tabs.Items.Add(new TabItem { Header = LocalizationManager.T("Settings.TabHotkeys"), Content = new ScrollViewer { Content = hotkeys, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            // ===== О программе =====
            var about = BuildAboutTab();
            tabs.Items.Add(new TabItem { Header = LocalizationManager.T("Settings.TabAbout"), Content = new ScrollViewer { Content = about, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            Grid.SetRow(tabs, 0);
            grid.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            var ok = new Button { Content = LocalizationManager.T("Common.Ok"), MinWidth = 110, IsDefault = true };
            ok.Click += (_, _) =>
            {
                if (langBox.SelectedItem is LanguageInfo li &&
                    !string.Equals(li.Code, LocalizationManager.Instance.CurrentLanguage, StringComparison.Ordinal))
                {
                    _viewModel.ApplyLanguage(li.Code);
                }

                _viewModel.ApplyPlatformSettings(paths, archBox.SelectedItem as string ?? "X64");

                _viewModel.ApplyIbasesSyncSettings(
                    syncModeBox.SelectedIndex >= 0 ? syncModes[syncModeBox.SelectedIndex].Mode : IbasesSyncMode.None,
                    fileBox.Text?.Trim() ?? string.Empty,
                    triggerBox.SelectedIndex >= 0 ? triggers[triggerBox.SelectedIndex].Trigger : IbasesSyncTrigger.OnStartup,
                    int.TryParse(intervalBox.Text, out var interval) && interval > 0 ? interval : 30,
                    scheduleBox.Text?.Trim() ?? string.Empty,
                    backupCheck.IsChecked == true,
                    int.TryParse(keepBox.Text, out var keep) && keep > 0 ? keep : 5);

                _viewModel.ApplyHotkeys(
                    hotkeyEnterprise.Value, hotkeyConfigurator.Value, hotkeyEdit.Value, hotkeyAdd.Value,
                    hotkeyFavorite.Value, hotkeyPin.Value, hotkeyDelete.Value, hotkeyClearCache.Value);

                // Настройки отображения применяются и сохраняются одним вызовом.
                _viewModel.ApplyDisplaySettings(
                    favoritesCheck.IsChecked == true,
                    pinnedCheck.IsChecked == true,
                    tagsCheck.IsChecked == true,
                    tagPanelCheck.IsChecked == true,
                    versionCheck.IsChecked == true,
                    configurationCheck.IsChecked == true,
                    launchModeCheck.IsChecked == true,
                    serverCheck.IsChecked == true,
                    lastLaunchCheck.IsChecked == true,
                    sizeCheck.IsChecked == true,
                    rightPanelCheck.IsChecked == true,
                    sessionPanelCheck.IsChecked == true,
                    groupByGroupCheck.IsChecked == true,
                    emptyGroupsCheck.IsChecked == true);

                DialogResult = true;
                Close();
            };
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 1);
            grid.Children.Add(buttons);

            return grid;
        }

        /// <summary>Заголовок группы настроек на вкладке.</summary>
        private static TextBlock GroupTitle(string text) => new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        };

        /// <summary>Пояснение под заголовком группы настроек.</summary>
        private static TextBlock Hint(string text) => new()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>Переключатель настройки отображения.</summary>
        private static CheckBox DisplayCheck(string textKey, bool value) => new()
        {
            Content = LocalizationManager.T(textKey),
            IsChecked = value
        };

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
            var r = new RadioButton { Content = content, GroupName = groupName, Margin = new Thickness(0, 0, 12, 0) };
            r.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Avalonia.Data.Binding(path) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return r;
        }

        /// <summary>Строка переназначения: подпись действия и поле ввода сочетания.</summary>
        private static Controls.HotkeyBox HotkeyRow(Panel host, string action, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(190)));

            var label = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(label);

            var box = new Controls.HotkeyBox { Value = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Right, Width = 180 };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);

            host.Children.Add(grid);
            return box;
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
            var title = asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? LocalizationManager.T("App.Title");

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = FontWeight.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationManager.T("Settings.About.Version"), infoVersion),
                FontSize = 14
            });

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.About.AvaloniaText"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationManager.T("Settings.About.RuntimeInfo"), Environment.OSVersion, Environment.Is64BitOperatingSystem) + "\n" +
                       string.Format(LocalizationManager.T("Settings.About.DataDir"), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7
            });

            return panel;
        }
    }
}
#endif