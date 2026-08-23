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
            // Семь вкладок с длинными подписями в одну строку не помещаются
            // ни в какую разумную ширину, поэтому полоса вкладок слева.
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

        /// <summary>Наблюдатель за значением свойства контрола.</summary>
        private sealed class SettingsObserver<T> : IObserver<T>
        {
            private readonly Action<T> _apply;
            public SettingsObserver(Action<T> apply) => _apply = apply;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(T value) => _apply(value);
        }

        /// <summary>Форматы метки времени в имени файла выгрузки, как в версии для Windows.</summary>
        private static readonly string[] TimestampFormats =
        {
            "yyyyMMdd_HHmmss",
            "yyyy-MM-dd_HH-mm-ss",
            "yyyy-MM-dd_HHmmss",
            "dd.MM.yyyy HH-mm-ss",
            "yyyyMMdd",
            "HHmmss"
        };

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var tabs = new TabControl { TabStripPlacement = Dock.Left };

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

            // Действие после запуска базы или конфигуратора.
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.General.AfterLaunchAction"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            });
            var afterLaunchBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            afterLaunchBox.ItemsSource = new[]
            {
                LocalizationManager.T("Settings.General.AfterLaunchAction.None"),
                LocalizationManager.T("Settings.General.AfterLaunchAction.MinimizeToTray"),
                LocalizationManager.T("Settings.General.AfterLaunchAction.Close")
            };
            afterLaunchBox.SelectedIndex = (int)Models.AfterLaunchActionHelper.Parse(_viewModel.AfterLaunchAction);
            settings.Children.Add(afterLaunchBox);

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
            var archBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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

            // ===== Оформление =====
            var appearance = new StackPanel { Spacing = 6 };
            appearance.Children.Add(Hint(LocalizationManager.T("Settings.Theme.Description")));

            // Правки идут по копии сохранённой схемы, а не применённой предпросмотром:
            // закрытие окна крестиком не должно оставлять редактор на непринятых цветах.
            var editedScheme = _viewModel.ActiveColorScheme.Clone();

            var schemeBox = new ComboBox { MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var colorsPanel = new StackPanel { Spacing = 2 };
            var schemeNames = new List<string>();
            var suppressSchemeEvent = false;
            Button? renameButton = null;
            Button? deleteButton = null;

            static bool IsBuiltInScheme(string name)
                => string.Equals(name, ColorScheme.CreateLight().Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ColorScheme.CreateDark().Name, StringComparison.OrdinalIgnoreCase);

            static string SchemeDisplayName(string name)
            {
                if (string.Equals(name, ColorScheme.CreateLight().Name, StringComparison.OrdinalIgnoreCase))
                    return LocalizationManager.T("Theme.Light");
                if (string.Equals(name, ColorScheme.CreateDark().Name, StringComparison.OrdinalIgnoreCase))
                    return LocalizationManager.T("Theme.Dark");
                return name;
            }

            void UpdateSchemeButtons()
            {
                var index = schemeBox.SelectedIndex;
                var builtIn = index >= 0 && index < schemeNames.Count && IsBuiltInScheme(schemeNames[index]);
                if (renameButton is not null)
                    renameButton.IsEnabled = !builtIn;
                if (deleteButton is not null)
                    deleteButton.IsEnabled = !builtIn;
            }

            void ReloadSchemes(string? select = null)
            {
                var target = select ?? editedScheme.Name;
                schemeNames.Clear();
                schemeNames.AddRange(ThemeManager.EnumerateAllSchemes().Select(x => x.Name));
                if (!schemeNames.Any(n => string.Equals(n, target, StringComparison.OrdinalIgnoreCase)))
                    schemeNames.Add(target);

                suppressSchemeEvent = true;
                schemeBox.ItemsSource = schemeNames.Select(SchemeDisplayName).ToList();
                var index = schemeNames.FindIndex(n => string.Equals(n, target, StringComparison.OrdinalIgnoreCase));
                schemeBox.SelectedIndex = index >= 0 ? index : 0;
                suppressSchemeEvent = false;
                UpdateSchemeButtons();
            }

            void RefreshColors()
            {
                colorsPanel.Children.Clear();
                foreach (var (key, label) in Models.ColorScheme.Definitions)
                {
                    var current = editedScheme.Colors.TryGetValue(key, out var value) ? value : "#FFFFFF";
                    colorsPanel.Children.Add(ColorRow(editedScheme, key, label, current));
                }
            }

            bool NameTaken(string name)
                => IsBuiltInScheme(name) || ThemeManager.FindCustomScheme(name) is not null;

            void ReportSchemeFailure(Exception ex)
                => _viewModel.ShowError(string.Format(LocalizationManager.T("Settings.SchemeFailedLinux"), ex.Message));

            string? SelectedSchemeName()
            {
                var index = schemeBox.SelectedIndex;
                return index >= 0 && index < schemeNames.Count ? schemeNames[index] : null;
            }

            schemeBox.SelectionChanged += (_, _) =>
            {
                if (suppressSchemeEvent)
                    return;
                var name = SelectedSchemeName();
                if (name is null)
                    return;
                var scheme = ThemeManager.EnumerateAllSchemes()
                    .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (scheme is null)
                    return;
                editedScheme = scheme.Clone();
                RefreshColors();
                UpdateSchemeButtons();
            };

            ReloadSchemes();
            RefreshColors();
            appearance.Children.Add(schemeBox);

            var schemeButtons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

            Button SchemeButton(string textKey, string tooltipKey, Action action)
            {
                var button = new Button { Content = LocalizationManager.T(textKey), Margin = new Thickness(0, 0, 6, 4) };
                ToolTip.SetTip(button, LocalizationManager.T(tooltipKey));
                button.Click += (_, _) => action();
                schemeButtons.Children.Add(button);
                return button;
            }

            SchemeButton("Common.Apply", "Settings.Theme.ApplyTooltip", () => ThemeManager.ApplyScheme(editedScheme));

            SchemeButton("Settings.CreateTheme", "Settings.CreateThemeTooltip", () =>
            {
                var name = AskName(LocalizationManager.T("Settings.CreateTheme"),
                    string.Format(LocalizationManager.T("Settings.CopyOf"), editedScheme.Name));
                if (string.IsNullOrWhiteSpace(name))
                    return;
                name = name.Trim();
                if (NameTaken(name))
                {
                    _viewModel.ShowWarning(LocalizationManager.T("Settings.ReservedName"));
                    return;
                }

                var copy = editedScheme.Clone();
                copy.Name = name;
                try
                {
                    ThemeManager.SaveCustomScheme(copy);
                }
                catch (Exception ex)
                {
                    ReportSchemeFailure(ex);
                    return;
                }

                editedScheme = copy;
                ReloadSchemes(name);
                RefreshColors();
            });

            renameButton = SchemeButton("Settings.Rename", "Settings.RenameTooltip", () =>
            {
                var current = SelectedSchemeName();
                if (current is null)
                    return;
                if (IsBuiltInScheme(current))
                {
                    _viewModel.ShowInfo(LocalizationManager.T("Settings.CannotRenameBuiltIn"));
                    return;
                }

                var name = AskName(LocalizationManager.T("Settings.Rename"), current);
                if (string.IsNullOrWhiteSpace(name)
                    || string.Equals(name.Trim(), current, StringComparison.OrdinalIgnoreCase))
                    return;
                name = name.Trim();
                if (NameTaken(name))
                {
                    _viewModel.ShowWarning(LocalizationManager.T("Settings.ReservedName"));
                    return;
                }

                var scheme = ThemeManager.FindCustomScheme(current);
                if (scheme is not null)
                {
                    scheme.Name = name;
                    try
                    {
                        ThemeManager.RenameCustomScheme(scheme, current);
                    }
                    catch (Exception ex)
                    {
                        ReportSchemeFailure(ex);
                        return;
                    }
                }

                if (string.Equals(editedScheme.Name, current, StringComparison.OrdinalIgnoreCase))
                    editedScheme.Name = name;
                ReloadSchemes(name);
                RefreshColors();
            });

            deleteButton = SchemeButton("Common.Delete", "Settings.DeleteThemeTooltip", () =>
            {
                var current = SelectedSchemeName();
                if (current is null)
                    return;
                if (IsBuiltInScheme(current))
                {
                    _viewModel.ShowInfo(LocalizationManager.T("Settings.CannotDeleteBuiltIn"));
                    return;
                }
                if (!_viewModel.Confirm(string.Format(LocalizationManager.T("Settings.DeleteThemeConfirm"), current)))
                    return;

                try
                {
                    ThemeManager.DeleteCustomScheme(current);
                }
                catch (Exception ex)
                {
                    ReportSchemeFailure(ex);
                    return;
                }

                if (string.Equals(editedScheme.Name, current, StringComparison.OrdinalIgnoreCase))
                    editedScheme = editedScheme.IsDark ? ColorScheme.CreateDark() : ColorScheme.CreateLight();
                ReloadSchemes(editedScheme.Name);
                RefreshColors();
            });

            SchemeButton("Settings.ResetColors", "Settings.ResetColorsTooltip", () =>
            {
                editedScheme = ColorScheme.Create(editedScheme.Name, editedScheme.IsDark);
                RefreshColors();
            });

            SchemeButton("Settings.ExportTheme", "Settings.ExportThemeTooltip", () =>
            {
                var path = _viewModel.PickSaveFile(LocalizationManager.T("Settings.ExportSchemeTitle"),
                    editedScheme.Name + ".json");
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try
                {
                    ThemeManager.ExportScheme(editedScheme, path);
                    _viewModel.ShowInfo(string.Format(LocalizationManager.T("Settings.ExportedOk"), path));
                }
                catch (Exception ex)
                {
                    _viewModel.ShowError(string.Format(LocalizationManager.T("Settings.ExportFailed"), ex.Message));
                }
            });

            SchemeButton("Settings.ImportTheme", "Settings.ImportThemeTooltip", () =>
            {
                var path = _viewModel.PickFile(LocalizationManager.T("Settings.ImportSchemeTitle"), string.Empty);
                if (string.IsNullOrWhiteSpace(path))
                    return;
                ColorScheme? imported;
                try
                {
                    imported = ThemeManager.ImportScheme(path);
                }
                catch
                {
                    imported = null;
                }

                if (imported is null || imported.Colors is not { Count: > 0 } || string.IsNullOrWhiteSpace(imported.Name))
                {
                    _viewModel.ShowError(LocalizationManager.T("Settings.ImportFailed"));
                    return;
                }

                if (IsBuiltInScheme(imported.Name))
                {
                    _viewModel.ShowWarning(LocalizationManager.T("Settings.ReservedName"));
                    return;
                }

                if (ThemeManager.FindCustomScheme(imported.Name) is not null
                    && !_viewModel.Confirm(string.Format(
                        LocalizationManager.T("Settings.ImportReplaceLinux"), imported.Name)))
                    return;

                try
                {
                    ThemeManager.SaveCustomScheme(imported);
                }
                catch (Exception ex)
                {
                    ReportSchemeFailure(ex);
                    return;
                }

                editedScheme = imported;
                ReloadSchemes(imported.Name);
                RefreshColors();
                _viewModel.ShowInfo(string.Format(LocalizationManager.T("Settings.ImportedOk"), imported.Name));
            });

            // Кнопки создаются после первого ReloadSchemes, поэтому доступность
            // для встроенной темы выставляется здесь, а не только по смене выбора.
            UpdateSchemeButtons();

            appearance.Children.Add(schemeButtons);
            appearance.Children.Add(GroupTitle(LocalizationManager.T("Settings.Colors")));
            appearance.Children.Add(Hint(LocalizationManager.T("Settings.Colors.Description")));
            appearance.Children.Add(colorsPanel);

            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("Settings.TabAppearance"),
                Content = new ScrollViewer { Content = appearance, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });

            // ===== Базы =====
            var bases = new StackPanel { Spacing = 6 };

            // Каталоги шаблонов конфигураций: список путей и правка вручную,
            // как на этой же вкладке в версии для Windows.
            var templatePaths = new ObservableCollection<string>(_viewModel.TemplateCatalogPaths);
            var templateList = new ListBox
            {
                ItemsSource = templatePaths,
                Height = 110,
                SelectionMode = SelectionMode.Single
            };

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.TemplateDirs")));
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Bases.TemplateDirsHintLinux")));
            bases.Children.Add(templateList);

            var templateButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8) };

            var addTemplate = new Button { Content = LocalizationManager.T("Settings.Bases.AddTemplate") };
            ToolTip.SetTip(addTemplate, LocalizationManager.T("Settings.Bases.AddTemplateTooltip"));
            addTemplate.Click += (_, _) =>
            {
                var folder = _viewModel.PickFolder(LocalizationManager.T("Settings.Bases.AddTemplateFolderDesc"));
                if (string.IsNullOrWhiteSpace(folder) || templatePaths.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    return;
                templatePaths.Add(folder);
            };

            var editTemplate = new Button { Content = LocalizationManager.T("Settings.Bases.EditTemplate") };
            ToolTip.SetTip(editTemplate, LocalizationManager.T("Settings.Bases.EditTemplateTooltip"));
            editTemplate.Click += (_, _) =>
            {
                if (templateList.SelectedItem is not string current)
                    return;
                var folder = _viewModel.PickFolder(LocalizationManager.T("Settings.Bases.EditTemplateFolderDesc"));
                if (string.IsNullOrWhiteSpace(folder) || string.Equals(folder, current, StringComparison.OrdinalIgnoreCase))
                    return;
                if (templatePaths.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    return;
                templatePaths[templatePaths.IndexOf(current)] = folder;
                templateList.SelectedItem = folder;
            };

            var removeTemplate = new Button { Content = LocalizationManager.T("Common.Delete") };
            ToolTip.SetTip(removeTemplate, LocalizationManager.T("Settings.Bases.RemoveTemplateTooltip"));
            removeTemplate.Click += (_, _) =>
            {
                if (templateList.SelectedItem is string selected)
                    templatePaths.Remove(selected);
            };

            var loadTemplates = new Button { Content = LocalizationManager.T("Settings.Bases.LoadDefault") };
            ToolTip.SetTip(loadTemplates, LocalizationManager.T("Settings.Bases.LoadDefaultTooltip"));
            loadTemplates.Click += (_, _) =>
            {
                templatePaths.Clear();
                foreach (var path in _viewModel.DiscoverTemplateCatalogPaths())
                    templatePaths.Add(path);
            };

            templateButtons.Children.Add(addTemplate);
            templateButtons.Children.Add(editTemplate);
            templateButtons.Children.Add(removeTemplate);
            templateButtons.Children.Add(loadTemplates);
            bases.Children.Add(templateButtons);

            // Операции со списком баз целиком: выгрузка и загрузка JSON,
            // разовый импорт из ibases.v8i.
            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.IbaseList")));

            var timestampCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.AddTimestamp"),
                IsChecked = _viewModel.AddTimestampToExportFileName,
                Margin = new Thickness(0, 4, 0, 0)
            };
            ToolTip.SetTip(timestampCheck, LocalizationManager.T("Settings.AddTimestampTooltip"));

            var timestampBox = new AutoCompleteBox
            {
                MinWidth = 200,
                ItemsSource = TimestampFormats,
                FilterMode = AutoCompleteFilterMode.Contains,
                Text = string.IsNullOrWhiteSpace(_viewModel.ExportTimestampFormat)
                    ? TimestampFormats[0]
                    : _viewModel.ExportTimestampFormat
            };
            ToolTip.SetTip(timestampBox, LocalizationManager.T("Settings.Bases.TimestampFormatTooltip"));

            var timestampPreview = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

            string TimestampFormat() =>
                string.IsNullOrWhiteSpace(timestampBox.Text) ? TimestampFormats[0] : timestampBox.Text.Trim();

            void ApplyExportFileNameSettings() =>
                _viewModel.ApplyExportFileNameSettings(timestampCheck.IsChecked == true, TimestampFormat());

            // Предпросмотр собирается теми же двумя шаблонами, что и в версии
            // для Windows: один подставляет метку в имя файла, второй обрамляет
            // это словом «Пример».
            void UpdateTimestampPreview()
            {
                var format = timestampBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(format))
                {
                    timestampPreview.Text = LocalizationManager.T("Settings.TimestampSpecifyHint");
                    return;
                }

                try
                {
                    var name = string.Format(LocalizationManager.T("Settings.TimestampBasePrefix"),
                        DateTime.Now.ToString(format));
                    timestampPreview.Text = string.Format(LocalizationManager.T("Settings.TimestampExample"), name);
                }
                catch (FormatException)
                {
                    timestampPreview.Text = LocalizationManager.T("Settings.TimestampInvalid");
                }
            }

            var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var exportList = new Button { Content = LocalizationManager.T("Settings.Bases.ExportList") };
            ToolTip.SetTip(exportList, LocalizationManager.T("Settings.Bases.ExportListTooltip"));
            // Значения передаются выгрузке прямо из полей, а на диск попадают
            // только по ОК: иначе отмена выгрузки или закрытие окна крестиком
            // всё равно меняли бы настройку.
            exportList.Click += (_, _) =>
                _viewModel.ExportInfobases(timestampCheck.IsChecked == true, TimestampFormat());

            var importList = new Button { Content = LocalizationManager.T("Settings.Bases.ImportList") };
            ToolTip.SetTip(importList, LocalizationManager.T("Settings.Bases.ImportListTooltip"));
            importList.Click += (_, _) => _viewModel.ImportInfobases();

            var importV8i = new Button { Content = LocalizationManager.T("Settings.Bases.ImportV8i") };
            ToolTip.SetTip(importV8i, LocalizationManager.T("Settings.Bases.ImportV8iTooltip"));
            importV8i.Click += (_, _) => _viewModel.ImportFromIbasesV8i();

            listButtons.Children.Add(exportList);
            listButtons.Children.Add(importList);
            listButtons.Children.Add(importV8i);
            bases.Children.Add(listButtons);
            bases.Children.Add(timestampCheck);

            var timestampRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            timestampRow.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Bases.TimestampFormat"),
                VerticalAlignment = VerticalAlignment.Center
            });
            timestampRow.Children.Add(timestampBox);
            timestampRow.Children.Add(timestampPreview);
            bases.Children.Add(timestampRow);

            timestampCheck.IsCheckedChanged += (_, _) => UpdateTimestampPreview();
            timestampBox.GetObservable(AutoCompleteBox.TextProperty)
                .Subscribe(new SettingsObserver<string?>(_ => UpdateTimestampPreview()));
            UpdateTimestampPreview();

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Maintenance")));
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Bases.MaintenanceHint")));

            var maintenanceButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };

            var removeMissing = new Button { Content = LocalizationManager.T("Settings.Bases.RemoveMissing") };
            ToolTip.SetTip(removeMissing, LocalizationManager.T("Settings.Bases.RemoveMissingTooltip"));
            removeMissing.Click += (_, _) => _viewModel.RemoveMissingFileBases();

            var killProcesses = new Button { Content = LocalizationManager.T("Settings.Bases.KillProcesses") };
            ToolTip.SetTip(killProcesses, LocalizationManager.T("Settings.Bases.KillProcessesTooltip"));
            killProcesses.Click += (_, _) => _viewModel.KillOneCProcesses();

            maintenanceButtons.Children.Add(removeMissing);
            maintenanceButtons.Children.Add(killProcesses);
            bases.Children.Add(maintenanceButtons);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.DangerousOps")));
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Bases.DangerousHint")));

            var clearAll = new Button
            {
                Content = LocalizationManager.T("Settings.Bases.ClearAll"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ToolTip.SetTip(clearAll, LocalizationManager.T("Settings.Bases.ClearAllTooltip"));
            clearAll.Click += (_, _) => _viewModel.ClearAllInfobases();
            bases.Children.Add(clearAll);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.TabIbases")));
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Ibases.Description")));

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.SyncMode")));
            var syncModes = new[]
            {
                (Mode: IbasesSyncMode.None, Text: LocalizationManager.T("Settings.Ibases.SyncModeDisabled")),
                (Mode: IbasesSyncMode.Import, Text: LocalizationManager.T("Settings.Ibases.SyncModeImport")),
                (Mode: IbasesSyncMode.Export, Text: LocalizationManager.T("Settings.Ibases.SyncModeExport")),
                (Mode: IbasesSyncMode.Both, Text: LocalizationManager.T("Settings.Ibases.SyncModeBoth"))
            };
            var syncModeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            syncModeBox.ItemsSource = syncModes.Select(m => m.Text).ToList();
            syncModeBox.SelectedIndex = Array.FindIndex(syncModes, m => m.Mode == _viewModel.IbasesSyncMode);
            bases.Children.Add(syncModeBox);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.File")));
            // Строка пути на Grid: поле растягивается на доступную ширину, а кнопка
            // обзора закреплена справа — в отличие от горизонтального StackPanel
            // с фиксированной MinWidth это не вызывает обрезания по горизонтали.
            var fileGrid = new Grid();
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var fileBox = new TextBox { Text = _viewModel.IbasesSyncFilePath, HorizontalAlignment = HorizontalAlignment.Stretch };
            var browse = new Button { Content = "...", Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(browse, LocalizationManager.T("Settings.Ibases.BrowseTooltip"));
            browse.Click += (_, _) =>
            {
                var picked = _viewModel.PickFile(
                    LocalizationManager.T("Sync.ChooseIbasesFile"),
                    LocalizationManager.T("Sync.IbasesFilter"));
                if (!string.IsNullOrWhiteSpace(picked))
                    fileBox.Text = picked;
            };
            Grid.SetColumn(fileBox, 0);
            Grid.SetColumn(browse, 1);
            fileGrid.Children.Add(fileBox);
            fileGrid.Children.Add(browse);
            bases.Children.Add(fileGrid);

            bases.Children.Add(GroupTitle(LocalizationManager.T("Settings.Ibases.SyncTrigger")));
            var triggers = new[]
            {
                (Trigger: IbasesSyncTrigger.OnStartup, Text: LocalizationManager.T("Settings.Ibases.TriggerStartup")),
                (Trigger: IbasesSyncTrigger.Interval, Text: LocalizationManager.T("Settings.Ibases.TriggerInterval")),
                (Trigger: IbasesSyncTrigger.Schedule, Text: LocalizationManager.T("Settings.Ibases.TriggerSchedule"))
            };
            var triggerBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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

            // Подписи как в оригинале: флажок называет само действие, а строка
            // про имена копий идёт пояснением под числом хранимых копий.
            var backupCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.BackupBeforeSync"),
                IsChecked = _viewModel.IbasesBackupEnabled
            };
            bases.Children.Add(backupCheck);
            var keepRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keepRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Settings.Ibases.BackupKeepCount"), VerticalAlignment = VerticalAlignment.Center });
            var keepBox = new TextBox { Text = _viewModel.IbasesBackupKeepCount.ToString(), Width = 80 };
            keepRow.Children.Add(keepBox);
            bases.Children.Add(keepRow);
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Ibases.BackupNote")));

            tabs.Items.Add(new TabItem
            {
                Header = LocalizationManager.T("Settings.TabBases"),
                Content = new ScrollViewer
                {
                    Content = bases,
                    // Горизонтальная прокрутка отключена, чтобы элементы растягивались
                    // по ширине окна (Stretch). При изменении размера окна реквизиты
                    // сжимаются вслед за ним; строки уже адаптивны (Grid со Star-колонкой).
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
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

                // Схема запоминается активной, иначе правка цветов держалась бы
                // только до перезапуска.
                _viewModel.ApplyColorScheme(editedScheme);

                _viewModel.ApplyPlatformSettings(paths, archBox.SelectedItem as string ?? "X64");
                _viewModel.ApplyTemplateCatalogPaths(templatePaths);
                ApplyExportFileNameSettings();

                _viewModel.AfterLaunchAction = afterLaunchBox.SelectedIndex switch
                {
                    0 => Models.AfterLaunchAction.None.ToSettingString(),
                    1 => Models.AfterLaunchAction.MinimizeToTray.ToSettingString(),
                    2 => Models.AfterLaunchAction.Close.ToSettingString(),
                    // Ничего не выбрано: значение остаётся прежним, как в WPF-версии.
                    _ => _viewModel.AfterLaunchAction
                };

                _viewModel.ApplyIbasesSyncSettings(
                    syncModeBox.SelectedIndex >= 0 ? syncModes[syncModeBox.SelectedIndex].Mode : IbasesSyncMode.None,
                    fileBox.Text?.Trim() ?? string.Empty,
                    triggerBox.SelectedIndex >= 0 ? triggers[triggerBox.SelectedIndex].Trigger : IbasesSyncTrigger.OnStartup,
                    int.TryParse(intervalBox.Text, out var interval) && interval > 0 ? interval : 30,
                    scheduleBox.Text?.Trim() ?? string.Empty,
                    backupCheck.IsChecked == true,
                    int.TryParse(keepBox.Text, out var keep) && keep > 0 ? keep : 5);

                var assignments = new (string Action, Controls.HotkeyBox Box)[]
                {
                    (LocalizationManager.T("Main.LaunchEnterprise"), hotkeyEnterprise),
                    (LocalizationManager.T("Main.SectionConfigurator"), hotkeyConfigurator),
                    (LocalizationManager.T("Main.EditSettings"), hotkeyEdit),
                    (LocalizationManager.T("Main.AddBaseOrGroup"), hotkeyAdd),
                    (LocalizationManager.T("Main.Favorites"), hotkeyFavorite),
                    (LocalizationManager.T("Main.Pin"), hotkeyPin),
                    (LocalizationManager.T("Common.Delete"), hotkeyDelete),
                    (LocalizationManager.T("Main.ClearCache"), hotkeyClearCache)
                };

                if (!ValidateHotkeys(assignments))
                    return;

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

        /// <summary>
        /// Строка цвета схемы: подпись и образец. Щелчок открывает выбор цвета
        /// и сразу применяет результат, чтобы правку было видно на приложении.
        /// </summary>
        private Control ColorRow(ColorScheme scheme, string key, string label, string value)
        {
            var swatch = new Border
            {
                Width = 44,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Background = ParseBrush(value)
            };

            var button = new Button
            {
                Content = swatch,
                Padding = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            button.Click += (_, _) =>
            {
                var picker = new ColorPickerWindow(value);
                if (!picker.ShowDialogSync(this))
                    return;

                value = picker.Result;
                scheme.Colors[key] = value;
                swatch.Background = ParseBrush(value);
            };

            var grid = new Grid { Margin = new Thickness(0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(60)));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(text);
            grid.Children.Add(button);
            Grid.SetColumn(button, 1);
            return grid;
        }

        private static IBrush ParseBrush(string value)
        {
            try { return new SolidColorBrush(Color.Parse(value)); }
            catch (Exception) { return Brushes.Transparent; }
        }

        /// <summary>Запрашивает имя схемы отдельным окном ввода.</summary>
        private string? AskName(string title, string initial)
        {
            var dialog = new NameInputWindow(title, LocalizationManager.T("NameInput.Prompt"),
                LocalizationManager.T("Common.Ok"), initial);
            if (!dialog.ShowDialogSync(this))
                return null;

            var name = dialog.Result?.Trim();
            return string.IsNullOrEmpty(name) ? null : name;
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

        /// <summary>
        /// Проверяет назначения перед сохранением: понятное ли сочетание,
        /// не отбирает ли оно обычный ввод и не назначено ли двум действиям.
        /// При отказе окно остаётся открытым, чтобы было что исправлять.
        /// </summary>
        private bool ValidateHotkeys((string Action, Controls.HotkeyBox Box)[] assignments)
        {
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (action, box) in assignments)
            {
                var value = box.Value?.Trim() ?? string.Empty;
                if (value.Length == 0)
                    continue;

                if (!Controls.HotkeyBox.TryParse(value, out var gesture) || gesture is null)
                {
                    _viewModel.ShowWarning(string.Format(LocalizationManager.T("Settings.Hotkeys.Unsupported"), value));
                    return false;
                }

                if (Controls.HotkeyBox.IsUnsafeForTextInput(gesture))
                {
                    _viewModel.ShowWarning(string.Format(LocalizationManager.T("Settings.Hotkeys.Unsafe"), value));
                    return false;
                }

                if (used.TryGetValue(value, out var other))
                {
                    _viewModel.ShowWarning(string.Format(
                        LocalizationManager.T("Settings.Hotkeys.DuplicateMsg"),
                        string.Format(LocalizationManager.T("Settings.Hotkeys.AssignedTo"), value, other + ", " + action)));
                    return false;
                }

                used[value] = action;
            }

            return true;
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