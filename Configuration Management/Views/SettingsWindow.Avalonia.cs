#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux). Восемь вкладок: «Настройки»,
    /// «Платформы», «Отображение», «Цветовое оформление», «Базы», «Резервное
    /// копирование», «Клавиши», «О программе». Блок ibases.v8i вложен во вкладку
    /// «Базы», тогда как в версии для Windows это отдельная вкладка.
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
            // Кнопки окно строит само, поэтому подписку на смену языка
            // базовый класс не включает: включаем явно.
            EnsureLanguageSubscription();
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

        /// <summary>
        /// Пересобирает окно при смене языка: подписи вкладок, флажков и кнопок
        /// создаются в коде через LocalizationManager.T, поэтому сами по себе
        /// они не переключаются. Базовая реализация обновляет только ОК и
        /// Отмену, а язык здесь применяется сразу при выборе в списке, и без
        /// пересборки окно оставалось наполовину на прежнем языке.
        /// Введённые, но не сохранённые значения при этом читаются заново
        /// из настроек: сменить язык посреди правки означает начать её заново.
        /// </summary>
        protected override void OnLanguageChanged(object? sender, EventArgs e)
        {
            base.OnLanguageChanged(sender, e);

            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                Content = BuildRoot();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Content = BuildRoot());
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Вид колонки вкладок задаёт тема из словаря; TabStripPlacement её
            // шаблон не читает, но свойство ставится, чтобы состояние контрола
            // отвечало фактическому расположению полосы, как в разметке.
            var tabs = new TabControl { TabStripPlacement = Dock.Left };
            tabs.Styled(ControlThemes.SettingsTabControl);

            // ===== Настройки =====
            // Расстояния как в разметке (SettingsWindow.xaml:1107): между
            // переключателями 6, перед компактным режимом 12.
            var settings = new StackPanel { Spacing = 6 };

            // Тема оформления. Редактируемая схема и колбэк обновления редактора объявляются
            // здесь, чтобы радиокнопки «Светлая/Тёмная» переключали базовую тему именно той
            // схемы, которую пользователь редактирует (и которая сохраняется по «Применить»).
            var editedScheme = _viewModel.ActiveColorScheme.Clone();
            System.Action? refreshEditedScheme = null;
            var themeLabel = new TextBlock { Text = LocalizationManager.T("Settings.ThemeLabel"), FontWeight = FontWeight.SemiBold };
            settings.Children.Add(themeLabel);
            var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var lightTheme = new RadioButton { Content = LocalizationManager.T("Main.LightTheme"), GroupName = "Theme", IsChecked = !ThemeManager.CurrentScheme.IsDark };
            var darkTheme = new RadioButton { Content = LocalizationManager.T("Main.DarkTheme"), GroupName = "Theme", IsChecked = ThemeManager.CurrentScheme.IsDark };
            ThemeChanged(lightTheme, darkTheme, _viewModel, theme =>
            {
                editedScheme = _viewModel.GetSchemeForTheme(theme);
                refreshEditedScheme?.Invoke();
            });
            themePanel.Children.Add(lightTheme);
            themePanel.Children.Add(darkTheme);
            settings.Children.Add(themePanel);

            // Язык интерфейса. Как в разметке WPF (SettingsWindow.xaml:1088):
            // заголовок раздела и подпись самой строки это разные ключи.
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Language"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 4)
            });
            var langRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            langRow.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.LanguageLabel"),
                VerticalAlignment = VerticalAlignment.Center
            });
            var langBox = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
            langBox.ItemsSource = LocalizationManager.Instance.AvailableLanguages;
            langBox.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
            langBox.SelectedItem = LocalizationManager.Instance.AvailableLanguages
                .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage);
            // Язык применяется сразу при выборе (и сохраняется в настройках), чтобы
            // интерфейс перестраивался на новый язык без нажатия «OK».
            langBox.SelectionChanged += (_, _) =>
            {
                if (langBox.SelectedItem is LanguageInfo li &&
                    !string.Equals(li.Code, LocalizationManager.Instance.CurrentLanguage, StringComparison.Ordinal))
                {
                    _viewModel.ApplyLanguage(li.Code);
                }
            };
            langRow.Children.Add(langBox);
            settings.Children.Add(langRow);
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Language.AppliedHint"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

            // Раздел поведения приложения: в разметке WPF (SettingsWindow.xaml:1104)
            // он начинается своим заголовком, а первым в нём идёт разрешение
            // нескольких экземпляров.
            settings.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.General.Behavior"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            });

            // Несколько экземпляров: настройка лежит в общем с версией для
            // Windows файле и уже учитывается при запуске (App.axaml.cs),
            // но в Linux-сборке её нечем было изменить.
            var multipleInstancesCheck = SettingsSwitch("Settings.General.AllowMultipleInstances", _viewModel.AllowMultipleInstances, "IconApplication", "#3B82F6");
            settings.Children.Add(multipleInstancesCheck);

            // Поведение значка в области уведомлений. До этого три настройки
            // жили только в файле и в версии для Windows: в Linux-сборке ни
            // флажков, ни учёта не было.
            var trayIconCheck = SettingsSwitch("Settings.General.ShowTrayIcon", _viewModel.ShowTrayIcon, "IconDockBottom", "#14B8A6");
            var closeToTrayCheck = SettingsSwitch("Settings.General.CloseToTray", _viewModel.CloseToTray, "IconMinus", "#F59E0B");
            var escapeToTrayCheck = SettingsSwitch("Settings.General.EscapeToTray", _viewModel.EscapeToTray, "IconKeyboard", "#8B5CF6");
            settings.Children.Add(trayIconCheck);
            settings.Children.Add(closeToTrayCheck);
            settings.Children.Add(escapeToTrayCheck);

            // Компактный режим интерфейса.
            var compactToggle = SettingsSwitch("Settings.CompactMode", _viewModel.CompactMode, "IconCompress", "#22C55E");
            compactToggle.Margin = new Thickness(0, 6, 0, 0);
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

            // Запоминание геометрии окна. Значения лежали в общем файле настроек,
            // но Linux-сборка их не читала и не писала вовсе.
            var rememberLayoutCheck = SettingsSwitch("Settings.General.RememberWindowLayout", _viewModel.RememberWindowLayout, "IconMonitor", "#EC4899");
            rememberLayoutCheck.Margin = new Thickness(0, 6, 0, 0);
            settings.Children.Add(rememberLayoutCheck);

            // Управление учётными записями (профилями).
            var manageProfilesButton = new Button
            {
                Content = LocalizationManager.T("Settings.General.ManageProfiles"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0)
            };
            manageProfilesButton.Click += (_, _) =>
            {
                var profiles = AppServices.GetRequiredService<IProfileService>();
                new ProfilesWindow(profiles).ShowDialogSync(this);
            };
            settings.Children.Add(manageProfilesButton);

            var tabGeneral = MainTab("IconApplicationCog", "Settings.TabGeneral",
                new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // ===== Платформы =====
            var platforms = new StackPanel { Spacing = 6 };
            platforms.Children.Add(Hint(LocalizationManager.T("Settings.Platforms.Intro")));

            var versionsList = new ListBox { MinHeight = 120, MaxHeight = 180 };
            ToolTip.SetTip(versionsList, LocalizationManager.T("Settings.Platforms.TreeTooltip"));
            var versionsEmpty = Hint(LocalizationManager.T("Settings.PlatformsNotFound"));
            platforms.Children.Add(versionsList);
            platforms.Children.Add(versionsEmpty);

            var pathsList = new ListBox { MinHeight = 90, MaxHeight = 140 };
            ToolTip.SetTip(pathsList, LocalizationManager.T("Settings.AdditionalPaths.ListTooltip"));
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
            // Кнопка «Изменить» из разметки WPF (SettingsWindow.xaml:434). Поведение
            // повторяет OnEditPlatformPath_Click целиком, включая оба сообщения:
            // без них при пустом выделении кнопка выглядит нерабочей, а на дубле
            // строка молча исчезала бы вместо предупреждения.
            var editPath = new Button { Content = LocalizationManager.T("Common.Edit") };
            ToolTip.SetTip(editPath, LocalizationManager.T("Settings.AdditionalPaths.EditTooltip"));
            editPath.Click += (_, _) =>
            {
                if (pathsList.SelectedItem is not string selected || string.IsNullOrEmpty(selected))
                {
                    _viewModel.ShowInfo(LocalizationManager.T("Settings.SelectPathToEdit"),
                        LocalizationManager.T("Settings.AdditionalPathsTitle"));
                    return;
                }

                // Диалог открывается на редактируемом каталоге, как в версии
                // для Windows: иначе искать приходится с начала.
                var folder = _viewModel.PickFolder(
                    LocalizationManager.T("Settings.ChooseNewPlatformFolder"), selected)?.Trim();
                if (string.IsNullOrWhiteSpace(folder)
                    || string.Equals(folder, selected, StringComparison.OrdinalIgnoreCase))
                    return;

                if (paths.Any(existing => !string.Equals(existing, selected, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    _viewModel.ShowInfo(LocalizationManager.T("Settings.PathAlreadyAdded"),
                        LocalizationManager.T("Settings.AdditionalPathsTitle"));
                    return;
                }

                var index = paths.IndexOf(selected);
                if (index < 0)
                    return;
                paths[index] = folder;
                pathsList.SelectedItem = folder;
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
            pathButtons.Children.Add(editPath);
            pathButtons.Children.Add(removePath);
            platforms.Children.Add(pathButtons);

            platforms.Children.Add(GroupTitle(LocalizationManager.T("Settings.DefaultArch")));
            platforms.Children.Add(Hint(LocalizationManager.T("Settings.DefaultArch.Hint")));
            var archBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            archBox.ItemsSource = new[] { "X64", "X86" };
            archBox.SelectedItem = string.Equals(_viewModel.DefaultArchitecture, "X86", StringComparison.OrdinalIgnoreCase) ? "X86" : "X64";
            platforms.Children.Add(archBox);

            var tabPlatforms = MainTab("IconServer", "Settings.TabPlatforms",
                new ScrollViewer { Content = platforms, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // ===== Отображение =====
            var displayIcons = new StackPanel { Spacing = 6 };
            var displayColumns = new StackPanel { Spacing = 6 };
            var displayPanels = new StackPanel { Spacing = 6 };
            var displayStatus = new StackPanel { Spacing = 6 };
            var displayFont = new StackPanel { Spacing = 6 };

            displayIcons.Children.Add(Hint(LocalizationManager.T("Settings.Icons.Description")));
            var favoritesCheck = DisplayCheck("Settings.Icons.FavoritesButton", _viewModel.ShowFavoritesButton);
            var pinnedCheck = DisplayCheck("Settings.Icons.PinButton", _viewModel.ShowPinnedButton);
            var tagsCheck = DisplayCheck("Settings.Icons.Tags", _viewModel.ShowTags);
            var tagPanelCheck = DisplayCheck("Settings.Icons.TagFilterPanel", _viewModel.ShowTagFilterPanel);
            foreach (var check in new[] { favoritesCheck, pinnedCheck, tagsCheck, tagPanelCheck })
                displayIcons.Children.Add(check);

            // Видимость и порядок колонок редактируются в одном списке: у каждой
            // строки есть флажок видимости, а порядок задаётся кнопками «Вверх»/«Вниз»
            // по выбранной строке. Так не нужно держать две раздельные группы настроек.
            displayColumns.Children.Add(Hint(LocalizationManager.T("Settings.Columns.Description")));
            displayColumns.Children.Add(GroupTitle(LocalizationManager.T("Settings.Columns.OrderTitle")));
            displayColumns.Children.Add(Hint(LocalizationManager.T("Settings.Columns.OrderHint")));

            static string ColumnOrderLabel(string key) => LocalizationManager.T(key switch
            {
                "Version" => "Column.Version",
                "Configuration" => "Column.Configuration",
                "LaunchMode" => "Column.LaunchMode",
                "ServerBase" => "Column.ServerBase",
                "LastLaunch" => "Column.LastLaunch",
                "Size" => "Column.Size",
                "Actions" => "Column.Actions",
                _ => "Column.Name"
            });

            bool ColumnVisible(string key) => key switch
            {
                "Version" => _viewModel.ShowVersionColumn,
                "Configuration" => _viewModel.ShowConfigurationColumn,
                "LaunchMode" => _viewModel.ShowLaunchModeColumn,
                "ServerBase" => _viewModel.ShowServerColumn,
                "LastLaunch" => _viewModel.ShowLastLaunchColumn,
                "Size" => _viewModel.ShowSizeColumn,
                _ => true
            };

            var orderItems = new ObservableCollection<ColumnOrderItem>(
                _viewModel.ColumnOrderKeys.Select(k => new ColumnOrderItem(k, ColumnOrderLabel(k), ColumnVisible(k), IconHelper.ColumnIconKey(k))));
            var orderList = new ListBox
            {
                ItemsSource = orderItems,
                MinHeight = UiMetrics.Scaled(180),
                MaxHeight = UiMetrics.Scaled(240),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                // Каждая строка — флажок видимости с именем колонки; переключение
                // правит только видимость, порядок меняется кнопками ниже. Иконка
                // колонки совпадает с иконкой заголовка списка баз.
                ItemTemplate = new FuncDataTemplate<ColumnOrderItem>((item, _) =>
                {
                    // Переработка контейнеров виртуализацией строит шаблон с null:
                    // ClearContainerForItemOverride сбрасывает Content, и ContentPresenter
                    // зовёт шаблон ещё раз уже без данных.
                    if (item is null)
                        return new Control();

                    var content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    content.Children.Add(IconHelper.MakeIcon(item.IconKey, 14, "TextSecondaryBrush"));
                    var label = new TextBlock
                    {
                        Text = item.Display,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    content.Children.Add(label);
                    ToolTip.SetTip(content, LocalizationManager.T("Settings.Columns.RowSelectHint"));

                    // Пилюля-переключатель стиля ColumnVisibilitySwitch разметки
                    // (SettingsWindow.xaml:31): дорожка без подписи.
                    var check = new ToggleButton();
                    check.Styled(ControlThemes.ColumnVisibilitySwitch);
                    check.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty,
                        new Avalonia.Data.Binding(nameof(ColumnOrderItem.Visible))
                        { Mode = Avalonia.Data.BindingMode.TwoWay });

                    var row = new Grid { Margin = new Thickness(4, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    Grid.SetColumn(content, 0);
                    Grid.SetColumn(check, 1);
                    row.Children.Add(content);
                    row.Children.Add(check);
                    return row;
                })
            };
            // Колонка «Название» закреплена и всегда первая, её тумблер неактивен.
            // В разметке WPF она стоит отдельной строкой над списком, за ней
            // разделитель, и всё вместе лежит в карточке.
            var nameRowContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameRowContent.Children.Add(IconHelper.MakeIcon(IconHelper.ColumnIconKey("Name"), 14, "AccentBrush"));
            var nameRowLabel = new TextBlock
            {
                Text = LocalizationManager.T("Column.Name"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Themes.ThemeBrushes.Bind(nameRowLabel, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            nameRowContent.Children.Add(nameRowLabel);

            var nameRow = new Grid { Margin = new Thickness(4, 3) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            nameRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            // Строка «Название» закреплена: переключатель показан отмеченным
            // и недоступен, стиль тот же (SettingsWindow.xaml:554).
            var nameRowSwitch = new ToggleButton
            {
                IsChecked = true,
                IsEnabled = false
            };
            nameRowSwitch.Styled(ControlThemes.ColumnVisibilitySwitch);
            Grid.SetColumn(nameRowContent, 0);
            Grid.SetColumn(nameRowSwitch, 1);
            nameRow.Children.Add(nameRowContent);
            nameRow.Children.Add(nameRowSwitch);

            var orderCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6)
            };
            Themes.ThemeBrushes.Bind(orderCard, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(orderCard, Border.BorderBrushProperty, "BorderColorBrush");
            var orderCardBody = new StackPanel();
            orderCardBody.Children.Add(nameRow);
            orderCardBody.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 4) });
            orderCardBody.Children.Add(orderList);
            orderCard.Child = orderCardBody;
            displayColumns.Children.Add(orderCard);

            var orderButtons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var moveUp = new Button { Content = LocalizationManager.T("Settings.Columns.OrderUp"), Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
            var moveDown = new Button { Content = LocalizationManager.T("Settings.Columns.OrderDown"), IsEnabled = false };

            void UpdateOrderButtons()
            {
                var idx = orderList.SelectedIndex;
                moveUp.IsEnabled = idx > 0;
                moveDown.IsEnabled = idx >= 0 && idx < orderItems.Count - 1;
            }
            orderList.SelectionChanged += (_, _) => UpdateOrderButtons();

            moveUp.Click += (_, _) =>
            {
                var idx = orderList.SelectedIndex;
                if (idx <= 0) return;
                orderItems.Move(idx, idx - 1);
                orderList.SelectedIndex = idx - 1;
                UpdateOrderButtons();
            };
            moveDown.Click += (_, _) =>
            {
                var idx = orderList.SelectedIndex;
                if (idx < 0 || idx >= orderItems.Count - 1) return;
                orderItems.Move(idx, idx + 1);
                orderList.SelectedIndex = idx + 1;
                UpdateOrderButtons();
            };
            orderButtons.Children.Add(moveUp);
            orderButtons.Children.Add(moveDown);
            displayColumns.Children.Add(orderButtons);


            displayPanels.Children.Add(Hint(LocalizationManager.T("Settings.Panels.Description")));
            var rightPanelCheck = DisplayCheck("Settings.Panels.RightPanelDetails", _viewModel.ShowRightPanelDetails, "IconPageLayoutSidebarRight", "#14B8A6");
            var sessionPanelCheck = DisplayCheck("Settings.Panels.SessionLaunchPanel", _viewModel.ShowSessionLaunchPanel, "IconMonitor", "#8B5CF6");
            var groupByGroupCheck = DisplayCheck("Settings.Panels.GroupByGroups", _viewModel.GroupByGroup, "IconFolder", "#3B82F6");
            // Режим списка «только избранные» тот же, что переключается кнопкой
            // в главном окне: флажок и кнопка меняют одно значение.
            var favoritesOnlyCheck = DisplayCheck("Settings.Panels.ShowFavoritesOnly", _viewModel.IsListModeFavorites, "IconStar", "#FBBF24");
            var emptyGroupsCheck = DisplayCheck("Settings.Panels.ShowEmptyGroups", _viewModel.ShowEmptyGroups, "IconFolderOutline", "#0EA5E9");

            // Пояснения под переключателями стоят там же, где в разметке WPF
            // (SettingsWindow.xaml:628): у правой панели, у блока сессии
            // и у пустых групп. Ключи для них были в локализации, но не
            // использовались нигде.
            displayPanels.Children.Add(rightPanelCheck);
            displayPanels.Children.Add(Hint(LocalizationManager.T("Settings.Panels.RightPanelDetailsHint")));
            displayPanels.Children.Add(sessionPanelCheck);
            displayPanels.Children.Add(Hint(LocalizationManager.T("Settings.Panels.SessionLaunchPanelHint")));
            displayPanels.Children.Add(groupByGroupCheck);
            displayPanels.Children.Add(favoritesOnlyCheck);
            displayPanels.Children.Add(emptyGroupsCheck);
            displayPanels.Children.Add(Hint(LocalizationManager.T("Settings.Panels.ShowEmptyGroupsHint")));

            displayStatus.Children.Add(Hint(LocalizationManager.T("Settings.Status.Description")));
            var statusPathCheck = DisplayCheck("Settings.Status.ConnectionPath", _viewModel.StatusShowConnectionPath, "IconFolderOutline", "#3B82F6");
            var statusPortCheck = DisplayCheck("Settings.Status.Port", _viewModel.StatusShowPort, "IconNetwork", "#6366F1");
            var statusArchCheck = DisplayCheck("Settings.Status.Architecture", _viewModel.StatusShowArchitecture, "IconMonitor", "#8B5CF6");
            var statusVersionCheck = DisplayCheck("Column.Version", _viewModel.StatusShowPlatformVersion, "IconPackage", "#A855F7");
            var statusLaunchModeCheck = DisplayCheck("Column.LaunchMode", _viewModel.StatusShowLaunchMode, "IconPlay", "#22C55E");
            var statusClientTypeCheck = DisplayCheck("Settings.Status.ClientType", _viewModel.StatusShowClientType, "IconMonitor", "#EC4899");
            var statusConnectionTypeCheck = DisplayCheck("Settings.Status.ConnectionType", _viewModel.StatusShowConnectionType, "IconDatabase", "#6366F1");
            var statusUserCheck = DisplayCheck("Settings.Status.User", _viewModel.StatusShowUser, "IconUsers", "#94A3B8");
            var statusIdCheck = DisplayCheck("Settings.Status.Id", _viewModel.StatusShowId, "IconInfo", "#0EA5E9");
            foreach (var check in new[]
            {
                statusPathCheck, statusPortCheck, statusArchCheck, statusVersionCheck, statusLaunchModeCheck,
                statusClientTypeCheck, statusConnectionTypeCheck, statusUserCheck, statusIdCheck
            })
                displayStatus.Children.Add(check);

            // Подвкладка «Шрифт»: область интерфейса, семейство, размер, начертание,
            // образец и кнопка предпросмотра. Состав и порядок из SettingsWindow.xaml:752.
            var editedFonts = new Dictionary<string, ElementFontSettings>();
            foreach (var kv in _viewModel.ElementFonts)
                editedFonts[kv.Key] = kv.Value?.Clone() ?? new ElementFontSettings();
            if (!editedFonts.ContainsKey(ThemeManager.FontDefault))
                editedFonts[ThemeManager.FontDefault] = new ElementFontSettings
                {
                    FontFamily = _viewModel.FontFamily,
                    FontSize = _viewModel.FontSize,
                    FontWeight = _viewModel.FontWeight,
                    FontStyle = _viewModel.FontStyle
                };

            displayFont.Children.Add(Hint(LocalizationManager.T("Settings.Font.Description")));
            displayFont.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Font.Element"),
                Margin = new Thickness(0, 0, 0, 6)
            });

            var fontScopeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var key in ThemeManager.AllFontScopes)
                fontScopeBox.Items.Add(new FontScopeItem(key));
            fontScopeBox.SelectedIndex = 0;
            displayFont.Children.Add(fontScopeBox);

            var fontGrid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(160)));
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            for (var i = 0; i < 3; i++)
                fontGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var fontFamilyBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
            // Первыми идут те же десять имён, что и у автора
            // (Views/SettingsWindow.Fonts.cs:53-60): настройка, сделанная
            // на Windows, должна открываться на Linux своим же значением.
            var authorFamilies = new[]
            {
                "Segoe UI", "Arial", "Calibri", "Tahoma", "Verdana",
                "Trebuchet MS", "Georgia", "Times New Roman", "Courier New", "Consolas"
            };
            foreach (var family in authorFamilies)
                fontFamilyBox.Items.Add(family);
            // Дальше установленные в системе. Без них список на Linux наполовину
            // мёртвый: четырёх имён автора здесь нет, а Skia подставляет вместо
            // них Noto Sans молча, без исключения и без записи в журнал, поэтому
            // выбор такого имени внешне не менял ничего.
            foreach (var family in InstalledFontFamilies(authorFamilies))
                fontFamilyBox.Items.Add(family);

            // Размер можно и выбрать из списка, и набрать руками: в разметке WPF
            // у этого списка стоит IsEditable, и в Avalonia он тоже есть.
            var fontSizeBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
                IsEditable = true
            };
            foreach (var size in new double[]
            {
                8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24,
                26, 28, 32, 36, 40, 48, 56, 64, 72
            })
                fontSizeBox.Items.Add(size);

            var fontFaceBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var face in FontFaces)
                fontFaceBox.Items.Add(face);

            void AddFontRow(int row, string labelKey, Control editor)
            {
                var label = new TextBlock
                {
                    Text = LocalizationManager.T(labelKey),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, row < 2 ? 8 : 0)
                };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 0);
                Grid.SetRow(editor, row);
                Grid.SetColumn(editor, 1);
                fontGrid.Children.Add(label);
                fontGrid.Children.Add(editor);
            }

            AddFontRow(0, "Settings.Font.Family", fontFamilyBox);
            AddFontRow(1, "Settings.Font.Size", fontSizeBox);
            AddFontRow(2, "Settings.Font.Style", fontFaceBox);
            displayFont.Children.Add(fontGrid);

            var fontPreview = new TextBlock
            {
                Text = LocalizationManager.T("Settings.Font.Preview"),
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(fontPreview, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            var fontPreviewCard = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = fontPreview
            };
            Themes.ThemeBrushes.Bind(fontPreviewCard, Border.BackgroundProperty, "CardBackgroundBrush");
            Themes.ThemeBrushes.Bind(fontPreviewCard, Border.BorderBrushProperty, "BorderColorBrush");
            displayFont.Children.Add(fontPreviewCard);

            var fontApplyContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            fontApplyContent.Children.Add(IconHelper.MakeIcon("IconTheme", UiMetrics.Scaled(16), "ButtonTextBrush"));
            var fontApplyLabel = new TextBlock
            {
                Text = LocalizationManager.T("Common.Apply"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Themes.ThemeBrushes.Bind(fontApplyLabel, TextBlock.ForegroundProperty, "ButtonTextBrush");
            fontApplyContent.Children.Add(fontApplyLabel);
            var fontApply = new Button
            {
                Content = fontApplyContent,
                Padding = new Thickness(12, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            // Кнопка акцентная, как ModernButton в разметке WPF. Состояния берутся
            // из темы динамически, чтобы переживать смену цветовой схемы.
            Themes.ThemeBrushes.Bind(fontApply, Button.BackgroundProperty, "AccentBrush");
            PaintButtonStates(fontApply, fontApply.Background ?? Brushes.Transparent,
                new DynamicResourceExtension("AccentHoverBrush"),
                new DynamicResourceExtension("AccentPressedBrush"));
            ToolTip.SetTip(fontApply, LocalizationManager.T("Settings.Font.ApplyTooltip"));
            displayFont.Children.Add(fontApply);

            // Правки живут в editedFonts: переключение области их не теряет,
            // а «Сохранить» пишет весь набор разом.
            var suppressFontLoad = false;

            void StoreFontScope()
            {
                if (suppressFontLoad || fontScopeBox.SelectedItem is not FontScopeItem scope)
                    return;
                // Пока поля не заполнены загрузкой области, писать нечего:
                // иначе в набор уйдут значения по умолчанию вместо сохранённых.
                if (fontFamilyBox.SelectedItem is null && fontFaceBox.SelectedItem is null)
                    return;
                var face = fontFaceBox.SelectedItem as FontFaceItem ?? FontFaces[0];
                editedFonts[scope.Key] = new ElementFontSettings
                {
                    FontFamily = fontFamilyBox.SelectedItem as string ?? ThemeManager.DefaultFontFamily,
                    FontSize = SelectedFontSize(),
                    FontWeight = face.Weight,
                    FontStyle = face.Style
                };
                UpdateFontPreview();
            }

            void UpdateFontPreview()
            {
                var face = fontFaceBox.SelectedItem as FontFaceItem ?? FontFaces[0];
                fontPreview.FontFamily = new FontFamily(fontFamilyBox.SelectedItem as string ?? ThemeManager.DefaultFontFamily);
                fontPreview.FontSize = SelectedFontSize();
                fontPreview.FontWeight = string.Equals(face.Weight, "Bold", StringComparison.OrdinalIgnoreCase)
                    ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
                fontPreview.FontStyle = string.Equals(face.Style, "Italic", StringComparison.OrdinalIgnoreCase)
                    ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal;
            }

            void LoadFontScope()
            {
                if (fontScopeBox.SelectedItem is not FontScopeItem scope)
                    return;
                if (!editedFonts.TryGetValue(scope.Key, out var fs) || fs is null)
                    fs = editedFonts.TryGetValue(ThemeManager.FontDefault, out var def) && def is not null
                        ? def.Clone()
                        : new ElementFontSettings();

                suppressFontLoad = true;
                var known = fontFamilyBox.Items.OfType<string>()
                    .FirstOrDefault(f => string.Equals(f, fs.FontFamily, StringComparison.OrdinalIgnoreCase));
                if (known is null && !string.IsNullOrWhiteSpace(fs.FontFamily))
                {
                    // Сохранённое семейство дописывается в список, а не подменяется
                    // умолчанием: перечислены десять шрифтов Windows, и любой
                    // системный шрифт Linux иначе потерялся бы при первом сохранении.
                    fontFamilyBox.Items.Insert(0, fs.FontFamily);
                    known = fs.FontFamily;
                }
                fontFamilyBox.SelectedItem = known ?? ThemeManager.DefaultFontFamily;
                var listed = fontSizeBox.Items.OfType<double>()
                    .FirstOrDefault(v => Math.Abs(v - fs.FontSize) < 0.01);
                if (listed > 0)
                    fontSizeBox.SelectedItem = listed;
                else
                {
                    // Размер задан вручную и в списке его нет: показываем текстом.
                    fontSizeBox.SelectedItem = null;
                    fontSizeBox.Text = fs.FontSize > 0
                        ? fs.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : ThemeManager.DefaultFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                fontFaceBox.SelectedItem = FontFaces.FirstOrDefault(x =>
                    string.Equals(x.Weight, fs.FontWeight, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Style, fs.FontStyle, StringComparison.OrdinalIgnoreCase)) ?? FontFaces[0];
                suppressFontLoad = false;

                UpdateFontPreview();
            }

            // Набранный руками размер приходит в Text, а не в SelectedItem.
            double SelectedFontSize()
            {
                if (fontSizeBox.SelectedItem is double picked && picked > 0)
                    return picked;
                if (double.TryParse((fontSizeBox.Text ?? string.Empty).Trim().Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var typed)
                    && typed >= 6 && typed <= 96)
                    return typed;
                return ThemeManager.DefaultFontSize;
            }

            fontScopeBox.SelectionChanged += (_, _) => LoadFontScope();
            fontFamilyBox.SelectionChanged += (_, _) => StoreFontScope();
            fontSizeBox.SelectionChanged += (_, _) => StoreFontScope();
            fontFaceBox.SelectionChanged += (_, _) => StoreFontScope();
            fontApply.Click += (_, _) =>
            {
                StoreFontScope();
                _viewModel.PreviewElementFonts(editedFonts);
            };
            // Подписка оформляется до загрузки области, и это важно:
            // GetObservable отдаёт текущее значение прямо при подписке. Пока поля
            // пусты, StoreFontScope выходит сразу, а установки внутри самой
            // загрузки закрыты флагом suppressFontLoad. Если подписаться после
            // загрузки, немедленный вызов запишет в набор показанное значение
            // поверх сохранённого, и «Сохранить» унесёт его в настройки.
            fontSizeBox.GetObservable(ComboBox.TextProperty)
                .Subscribe(new SettingsObserver<string?>(_ => StoreFontScope()));

            LoadFontScope();

            // Раздел «Отображение» разложен по вложенным вкладкам, как в разметке WPF:
            // подвкладки «Значки», «Колонки», «Панели», «Статус» и «Шрифт».
            var displayTabs = new TabControl { Margin = new Thickness(0, 4, 0, 0) };
            displayTabs.Styled(ControlThemes.SettingsSubTabControl);
            displayTabs.Items.Add(SubTab("Settings.Subtab.Icons", "Settings.Subtab.IconsTooltip", "IconStarOutline", displayIcons));
            displayTabs.Items.Add(SubTab("Settings.Subtab.Columns", "Settings.Subtab.ColumnsTooltip", "IconViewColumn", displayColumns));
            displayTabs.Items.Add(SubTab("Settings.Subtab.Panels", "Settings.Subtab.PanelsTooltip", "IconPageLayoutSidebarRight", displayPanels));
            displayTabs.Items.Add(SubTab("Settings.Subtab.Status", "Settings.Subtab.StatusTooltip", "IconDockBottom", displayStatus));
            displayTabs.Items.Add(SubTab("Settings.Subtab.Font", "Settings.Subtab.FontTooltip", "IconFormatFont", displayFont));

            var tabDisplay = MainTab("IconEye", "Settings.TabDisplay", displayTabs);

            // ===== Оформление =====
            var appearance = new StackPanel { Spacing = 6 };
            // Заголовок группы из разметки WPF (SettingsWindow.xaml:824).
            appearance.Children.Add(GroupTitle(LocalizationManager.T("Settings.Theme")));
            appearance.Children.Add(Hint(LocalizationManager.T("Settings.Theme.Description")));

            // Правки идут по копии сохранённой схемы, а не применённой предпросмотром:
            // закрытие окна крестиком не должно оставлять редактор на непринятых цветах.
            editedScheme = _viewModel.ActiveColorScheme.Clone();

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
            refreshEditedScheme = () => { ReloadSchemes(editedScheme.Name); RefreshColors(); };
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

            var tabAppearance = MainTab("IconPalette", "Settings.TabAppearance",
                new ScrollViewer { Content = appearance, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // ===== Базы =====
            var bases = new StackPanel { Spacing = 6 };

            // Вводное описание вкладки, как в разметке WPF (SettingsWindow.xaml:1326).
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Bases.Description")));

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

            // Справка ставится к заголовку блока, а не отдельной строкой:
            // в разметке WPF «ibases.v8i» это имя вкладки, а «Настройки
            // синхронизации» заголовок группы внутри неё, и рядом они
            // не стоят никогда. Здесь блок вложен во вкладку «Базы», поэтому
            // два заголовка подряд означали бы одно и то же.
            var ibasesHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 2)
            };
            ibasesHeader.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.TabIbases"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            ibasesHeader.Children.Add(new Controls.HelpLink
            {
                HelpText = LocalizationManager.T("Settings.Ibases.HelpTextLinux"),
                Margin = new Thickness(6, 0, 0, 0)
            });
            bases.Children.Add(ibasesHeader);
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
            var fileBox = new TextBox { Text = _viewModel.IbasesSyncFilePath, HorizontalAlignment = HorizontalAlignment.Stretch }.Styled(ControlThemes.ModernTextBox);
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
            // Подписи держатся в переменных: версия для Windows гасит их вместе
            // с полями (SettingsWindow.Sync.cs, SyncIntervalLabel и SyncScheduleLabel),
            // иначе рядом с погашенным полем стоит подпись в полную яркость.
            var intervalLabel = new TextBlock { Text = LocalizationManager.T("Settings.Ibases.Interval"), VerticalAlignment = VerticalAlignment.Center };
            intervalRow.Children.Add(intervalLabel);
            var intervalBox = new TextBox { Text = _viewModel.IbasesSyncIntervalMinutes.ToString(), Width = 80 }.Styled(ControlThemes.ModernTextBox);
            intervalRow.Children.Add(intervalBox);
            var scheduleLabel = new TextBlock { Text = LocalizationManager.T("Settings.Ibases.ScheduleTime"), VerticalAlignment = VerticalAlignment.Center };
            intervalRow.Children.Add(scheduleLabel);
            var scheduleBox = new TextBox { Text = _viewModel.IbasesSyncScheduleTime, Width = 80 }.Styled(ControlThemes.ModernTextBox);
            intervalRow.Children.Add(scheduleBox);
            bases.Children.Add(intervalRow);

            // Строка состояния и ручные операции, как в разметке WPF
            // (SettingsWindow.xaml:1258-1281): сначала статус, затем загрузка
            // и выгрузка. Обработчики WPF под #if WINDOWS, но сами действия
            // в Linux-сборке есть, к ним и подключено.
            var syncStatus = Hint(string.Empty);
            syncStatus.Margin = new Thickness(0, 4, 0, 8);
            bases.Children.Add(syncStatus);

            var syncButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var importButton = new Button { Content = LocalizationManager.T("Settings.Ibases.Import") };
            ToolTip.SetTip(importButton, LocalizationManager.T("Settings.Ibases.ImportTooltip"));
            importButton.Click += (_, _) => _viewModel.ImportFromIbasesFile(fileBox.Text);
            var exportButton = new Button { Content = LocalizationManager.T("Settings.Ibases.Export") };
            ToolTip.SetTip(exportButton, LocalizationManager.T("Settings.Ibases.ExportTooltip"));
            exportButton.Click += (_, _) => _viewModel.ExportToIbasesFile(fileBox.Text);
            syncButtons.Children.Add(importButton);
            syncButtons.Children.Add(exportButton);
            bases.Children.Add(syncButtons);

            // Доступность и статус пересчитываются на каждое изменение блока,
            // как это делает UpdateSyncControls в версии для Windows: при
            // отключённой синхронизации поля и кнопки гаснут, а не молча
            // ничего не делают.
            void UpdateSyncControls()
            {
                var mode = syncModeBox.SelectedIndex >= 0
                    ? syncModes[syncModeBox.SelectedIndex].Mode
                    : IbasesSyncMode.None;
                var enabled = mode != IbasesSyncMode.None;
                var trigger = triggerBox.SelectedIndex >= 0
                    ? triggers[triggerBox.SelectedIndex].Trigger
                    : IbasesSyncTrigger.OnStartup;

                fileBox.IsEnabled = enabled;
                browse.IsEnabled = enabled;
                triggerBox.IsEnabled = enabled;
                intervalBox.IsEnabled = enabled && trigger == IbasesSyncTrigger.Interval;
                intervalLabel.IsEnabled = intervalBox.IsEnabled;
                scheduleBox.IsEnabled = enabled && trigger == IbasesSyncTrigger.Schedule;
                scheduleLabel.IsEnabled = scheduleBox.IsEnabled;
                importButton.IsEnabled = enabled && mode is IbasesSyncMode.Import or IbasesSyncMode.Both;
                exportButton.IsEnabled = enabled && mode is IbasesSyncMode.Export or IbasesSyncMode.Both;
                syncStatus.Text = BuildSyncStatus(mode, fileBox.Text, trigger, intervalBox.Text, scheduleBox.Text);
            }

            syncModeBox.SelectionChanged += (_, _) => UpdateSyncControls();
            triggerBox.SelectionChanged += (_, _) => UpdateSyncControls();
            fileBox.TextChanged += (_, _) => UpdateSyncControls();
            intervalBox.TextChanged += (_, _) => UpdateSyncControls();
            scheduleBox.TextChanged += (_, _) => UpdateSyncControls();
            UpdateSyncControls();

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
            var keepBox = new TextBox { Text = _viewModel.IbasesBackupKeepCount.ToString(), Width = 80 }.Styled(ControlThemes.ModernTextBox);
            keepRow.Children.Add(keepBox);
            bases.Children.Add(keepRow);
            bases.Children.Add(Hint(LocalizationManager.T("Settings.Ibases.BackupNote")));

            var restoreButton = new Button
            {
                Content = LocalizationManager.T("Settings.Ibases.Restore"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ToolTip.SetTip(restoreButton, LocalizationManager.T("Settings.Ibases.RestoreTooltip"));
            restoreButton.Click += (_, _) => _viewModel.RestoreIbasesBackup(fileBox.Text);
            bases.Children.Add(restoreButton);

            var tabBases = MainTab("IconDatabase", "Settings.TabBases",
                new ScrollViewer
                {
                    Content = bases,
                    // Горизонтальная прокрутка отключена, чтобы элементы растягивались
                    // по ширине окна (Stretch). При изменении размера окна реквизиты
                    // сжимаются вслед за ним; строки уже адаптивны (Grid со Star-колонкой).
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                });

            // ===== Резервное копирование профиля =====
            var profile = new StackPanel { Spacing = 6 };

            profile.Children.Add(GroupTitle(LocalizationManager.T("Settings.TabProfile")));
            profile.Children.Add(Hint(LocalizationManager.T("Settings.Profile.Description")));
            profile.Children.Add(Hint(LocalizationManager.T("Settings.Profile.Includes")));

            profile.Children.Add(GroupTitle(LocalizationManager.T("Settings.Profile.Directory")));
            var profileDirGrid = new Grid();
            profileDirGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            profileDirGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var profileDirBox = new TextBox
            {
                Text = _viewModel.ProfileBackupDirectory,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            profileDirBox.Styled(ControlThemes.ModernTextBox);
            var profileBrowse = new Button { Content = LocalizationManager.T("Settings.Profile.Browse"), Margin = new Thickness(8, 0, 0, 0) };
            ToolTip.SetTip(profileBrowse, LocalizationManager.T("Settings.Profile.BrowseTooltip"));
            profileBrowse.Click += (_, _) =>
            {
                var picked = _viewModel.PickFolder(LocalizationManager.T("Settings.Profile.Directory"));
                if (!string.IsNullOrWhiteSpace(picked))
                    profileDirBox.Text = picked;
            };
            Grid.SetColumn(profileDirBox, 0);
            Grid.SetColumn(profileBrowse, 1);
            profileDirGrid.Children.Add(profileDirBox);
            profileDirGrid.Children.Add(profileBrowse);
            profile.Children.Add(profileDirGrid);

            var profileRestoreCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.Profile.RestoreOnStartup"),
                IsChecked = _viewModel.ProfileRestoreOnStartup,
                Margin = new Thickness(0, 8, 0, 0)
            };
            profile.Children.Add(profileRestoreCheck);
            profile.Children.Add(Hint(LocalizationManager.T("Settings.Profile.RestoreOnStartupHint")));

            var profileButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var backupNow = new Button { Content = LocalizationManager.T("Settings.Profile.BackupNow") };
            ToolTip.SetTip(backupNow, LocalizationManager.T("Settings.Profile.BackupNowTooltip"));
            backupNow.Click += (_, _) =>
            {
                // Применяем выбранный каталог перед сохранением, чтобы профиль ушёл туда.
                _viewModel.ApplyProfileBackupSettings(profileDirBox.Text, profileRestoreCheck.IsChecked == true);
                _viewModel.BackupProfile();
            };
            var restoreNow = new Button { Content = LocalizationManager.T("Settings.Profile.RestoreNow") };
            ToolTip.SetTip(restoreNow, LocalizationManager.T("Settings.Profile.RestoreNowTooltip"));
            restoreNow.Click += (_, _) =>
            {
                // Применяем выбранный каталог перед восстановлением.
                _viewModel.ApplyProfileBackupSettings(profileDirBox.Text, profileRestoreCheck.IsChecked == true);
                // При успехе данные уже перезагружены; окно закрываем, чтобы не затереть
                // восстановленные настройки старыми значениями из полей формы.
                if (_viewModel.RestoreProfile())
                {
                    DialogResult = true;
                    Close();
                }
            };
            profileButtons.Children.Add(backupNow);
            profileButtons.Children.Add(restoreNow);
            profile.Children.Add(profileButtons);

            // Значок вкладки из словаря автора, как в коде Windows-версии
            // (SettingsWindow.Profile.cs:37 берёт BackupRestore); прежде здесь
            // был самодельный контур.
            var tabProfile = MainTab("IconBackupRestore", "Settings.TabProfile",
                new ScrollViewer { Content = profile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // ===== Клавиши =====
            // Spacing не задаётся: в Avalonia он складывается с полями соседей,
            // а поля строк взяты из разметки WPF и уже держат нужный шаг.
            var hotkeys = new StackPanel();
            var hotkeysTitle = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 2)
            };
            hotkeysTitle.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Hotkeys.Title"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            hotkeysTitle.Children.Add(new Controls.HelpLink
            {
                HelpText = LocalizationManager.T("Settings.Hotkeys.HelpText"),
                Margin = new Thickness(6, 0, 0, 0)
            });
            hotkeys.Children.Add(hotkeysTitle);
            var hotkeysHint = Hint(LocalizationManager.T("Settings.Hotkeys.Description"));
            hotkeysHint.Margin = new Thickness(0, 0, 0, 8);
            hotkeys.Children.Add(hotkeysHint);

            // Поля назначения: HotkeyBox ловит сочетание с клавиатуры, Delete
            // снимает назначение, Escape отменяет ввод. Подписи и порядок строк
            // взяты из разметки WPF (SettingsWindow.xaml, вкладка «Клавиши»).
            var hotkeyEnterprise = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Enterprise"), _viewModel.HotkeyEnterprise);
            var hotkeyConfigurator = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Configurator"), _viewModel.HotkeyConfigurator);
            var hotkeyFavorite = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Favorites"), _viewModel.HotkeyFavorite);
            var hotkeyEdit = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Edit"), _viewModel.HotkeyEdit);
            var hotkeyDelete = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Delete"), _viewModel.HotkeyDelete);
            var hotkeyClearCache = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.ClearCache"), _viewModel.HotkeyClearCache);
            var hotkeyAdd = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.AddBase"), _viewModel.HotkeyAdd);
            var hotkeyPin = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.Pin"), _viewModel.HotkeyPin);
            var hotkeyShowAll = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.ShowAll"), _viewModel.HotkeyShowAll);
            var hotkeyShowFavorites = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.ShowFavorites"), _viewModel.HotkeyShowFavorites);
            var hotkeyShowRecent = HotkeyRow(hotkeys, LocalizationManager.T("Settings.Hotkeys.ShowRecent"), _viewModel.HotkeyShowRecent);

            // Порядок слотов Alt+1…Alt+9, как в разметке WPF (SettingsWindow.xaml:1030):
            // заголовок, пояснение, список слотов и кнопки перестановки справа.
            hotkeys.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Hotkeys.FavoritesOrder"),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 4, 0, 6)
            });
            var favoritesHint = Hint(LocalizationManager.T("Settings.Hotkeys.FavoritesOrderHint"));
            favoritesHint.Margin = new Thickness(0, 0, 0, 8);
            hotkeys.Children.Add(favoritesHint);

            var favoriteSlots = new ObservableCollection<FavoriteSlotItem>(
                _viewModel.FavoriteHotkeyIds
                    .Select(key => new FavoriteSlotItem(key, _viewModel.FindByFavoriteKey(key)?.Name ?? key))
                    .ToList());
            void RenumberSlots()
            {
                for (var i = 0; i < favoriteSlots.Count; i++)
                    favoriteSlots[i].Number = i + 1;
            }
            RenumberSlots();

            var favoritesGrid = new Grid { MinHeight = 140, Margin = new Thickness(0, 0, 0, 12) };
            favoritesGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            favoritesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var favoritesList = new ListBox
            {
                ItemsSource = favoriteSlots,
                SelectionMode = SelectionMode.Single,
                MinHeight = 140,
                MaxHeight = 220
            };
            favoritesList.ItemTemplate = new FuncDataTemplate<FavoriteSlotItem>((item, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                var badge = new TextBlock { FontWeight = FontWeight.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                badge.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(FavoriteSlotItem.Caption)));
                row.Children.Add(badge);
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                name.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(FavoriteSlotItem.Name)));
                row.Children.Add(name);
                return row;
            }, supportsRecycling: true);
            favoritesGrid.Children.Add(favoritesList);

            var favoriteButtons = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            var slotUp = new Button { Content = "\u2191" };
            ToolTip.SetTip(slotUp, LocalizationManager.T("Settings.Hotkeys.MoveUpTooltip"));
            slotUp.Click += (_, _) =>
            {
                var idx = favoritesList.SelectedIndex;
                if (idx <= 0)
                    return;
                favoriteSlots.Move(idx, idx - 1);
                RenumberSlots();
                favoritesList.SelectedIndex = idx - 1;
            };
            var slotDown = new Button { Content = "\u2193" };
            ToolTip.SetTip(slotDown, LocalizationManager.T("Settings.Hotkeys.MoveDownTooltip"));
            slotDown.Click += (_, _) =>
            {
                var idx = favoritesList.SelectedIndex;
                if (idx < 0 || idx >= favoriteSlots.Count - 1)
                    return;
                favoriteSlots.Move(idx, idx + 1);
                RenumberSlots();
                favoritesList.SelectedIndex = idx + 1;
            };
            favoriteButtons.Children.Add(slotUp);
            favoriteButtons.Children.Add(slotDown);
            Grid.SetColumn(favoriteButtons, 1);
            favoritesGrid.Children.Add(favoriteButtons);
            hotkeys.Children.Add(favoritesGrid);

            var tabHotkeys = MainTab("IconKeyboardOutline", "Settings.TabHotkeys",
                new ScrollViewer { Content = hotkeys, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // ===== О программе =====
            var about = BuildAboutTab();
            var tabAbout = MainTab("IconInformationOutline", "Settings.TabAbout",
                new ScrollViewer { Content = about, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // Порядок вкладок по разметке (SettingsWindow.xaml:293 и далее):
            // платформы, отображение, оформление, клавиши, настройки, базы,
            // резервное копирование профиля, о программе. Девятой вкладки
            // «ibases.v8i» (xaml:1164) здесь нет: её содержимое лежит разделом
            // внутри «Баз», это расхождение старше правки и не закрыто.
            tabs.Items.Add(tabPlatforms);
            tabs.Items.Add(tabDisplay);
            tabs.Items.Add(tabAppearance);
            tabs.Items.Add(tabHotkeys);
            tabs.Items.Add(tabGeneral);
            tabs.Items.Add(tabBases);
            tabs.Items.Add(tabProfile);
            tabs.Items.Add(tabAbout);

            Grid.SetRow(tabs, 0);
            grid.Children.Add(tabs);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            // Подвал как в разметке WPF: зелёная «Сохранить» со значком дискеты
            // и красная контурная «Отмена» со значком крестика.
            // Цвета взяты из разметки WPF числами: зелёный фон сохранения и белый
            // текст на нём заданы там напрямую, ключей темы под них нет.
            var saveBrush = new SolidColorBrush(Color.Parse("#16A34A"));
            var onSaveBrush = Brushes.White;
            var okContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            okContent.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Width = UiMetrics.Scaled(16),
                Height = UiMetrics.Scaled(16),
                Data = IconHelper.Geometry("IconSave"),
                Stretch = Stretch.Uniform,
                Fill = onSaveBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            okContent.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Common.Save"),
                FontWeight = FontWeight.SemiBold,
                Foreground = onSaveBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            var ok = new Button
            {
                Content = okContent,
                MinWidth = UiMetrics.Scaled(140),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                BorderThickness = new Thickness(0),
                IsDefault = true
            };
            PaintButtonStates(ok, saveBrush,
                new SolidColorBrush(Color.Parse("#15803D")),
                new SolidColorBrush(Color.Parse("#166534")));
            ok.Click += (_, _) =>
            {
                // Проверка дублей идёт первой: иначе при конфликте окно
                // остаётся открытым, а часть настроек уже на диске.
                // Имена действий в сообщении о дубле берутся не из подписей вкладки:
                // там они с двоеточием на конце и в предложение не встают. Набор
                // ключей тот же, что в массиве assigned проверки WPF
                // (Views/SettingsWindow.xaml.cs): номер строки там сдвинется
                // при первом же обновлении от автора, имя переменной нет.
                var assignments = new (string Action, Controls.HotkeyBox Box)[]
                {
                    (LocalizationManager.T("Main.Enterprise"), hotkeyEnterprise),
                    (LocalizationManager.T("Main.SectionConfigurator"), hotkeyConfigurator),
                    (LocalizationManager.T("Main.Favorites"), hotkeyFavorite),
                    (LocalizationManager.T("Main.EditShort"), hotkeyEdit),
                    (LocalizationManager.T("Common.Delete"), hotkeyDelete),
                    (LocalizationManager.T("Main.ClearCache"), hotkeyClearCache),
                    (LocalizationManager.T("Main.AddBase"), hotkeyAdd),
                    (LocalizationManager.T("Main.Pin"), hotkeyPin),
                    (LocalizationManager.T("Main.AllBasesTooltip"), hotkeyShowAll),
                    (LocalizationManager.T("Main.FavoritesTooltip"), hotkeyShowFavorites),
                    (LocalizationManager.T("Main.RecentTooltip"), hotkeyShowRecent)
                };

                if (!ValidateHotkeys(assignments))
                    return;

                if (langBox.SelectedItem is LanguageInfo li &&
                    !string.Equals(li.Code, LocalizationManager.Instance.CurrentLanguage, StringComparison.Ordinal))
                {
                    _viewModel.ApplyLanguage(li.Code);
                }

                // Схема запоминается активной, иначе правка цветов держалась бы
                // только до перезапуска.
                _viewModel.ApplyColorScheme(editedScheme);

                _viewModel.ApplyPlatformSettings(paths, archBox.SelectedItem as string ?? "X64");
                _viewModel.ApplyBehaviorSettings(
                    multipleInstancesCheck.IsChecked == true,
                    rememberLayoutCheck.IsChecked == true);
                _viewModel.ApplyTraySettings(
                    trayIconCheck.IsChecked == true,
                    closeToTrayCheck.IsChecked == true,
                    escapeToTrayCheck.IsChecked == true);
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

                _viewModel.ApplyProfileBackupSettings(profileDirBox.Text, profileRestoreCheck.IsChecked == true);

                // Список слотов в окне это снимок, снятый при его построении.
                // Пока окно живёт, состав избранного могли изменить импорт,
                // автосинхронизация или само главное окно, если окно настроек
                // открыто немодально из трея. Поэтому снимок не пишется целиком,
                // а накладывается на текущее состояние: сохраняется заданный
                // пользователем порядок тех слотов, что ещё есть, а появившиеся
                // за это время дописываются в конец и не теряются.
                var currentSlots = _viewModel.FavoriteHotkeyIds;
                var orderedSlots = favoriteSlots
                    .Select(slot => slot.Key)
                    .Where(currentSlots.Contains)
                    .Concat(currentSlots.Where(key => favoriteSlots.All(slot => slot.Key != key)))
                    .ToList();
                _viewModel.SetFavoriteHotkeyOrder(orderedSlots);

                _viewModel.ApplyHotkeys(
                    hotkeyEnterprise.Value, hotkeyConfigurator.Value, hotkeyEdit.Value, hotkeyAdd.Value,
                    hotkeyFavorite.Value, hotkeyPin.Value, hotkeyDelete.Value, hotkeyClearCache.Value,
                    hotkeyShowAll.Value, hotkeyShowFavorites.Value, hotkeyShowRecent.Value);

                // Настройки отображения применяются и сохраняются одним вызовом.
                // Видимость колонок читается из тех же элементов списка, где
                // задаётся и порядок: флажок каждой строки и есть её видимость.
                bool VisibleOf(string key) => orderItems.FirstOrDefault(o => o.Key == key)?.Visible ?? true;

                _viewModel.ApplyDisplaySettings(
                    favoritesCheck.IsChecked == true,
                    pinnedCheck.IsChecked == true,
                    tagsCheck.IsChecked == true,
                    tagPanelCheck.IsChecked == true,
                    VisibleOf("Version"),
                    VisibleOf("Configuration"),
                    VisibleOf("LaunchMode"),
                    VisibleOf("ServerBase"),
                    VisibleOf("LastLaunch"),
                    VisibleOf("Size"),
                    rightPanelCheck.IsChecked == true,
                    sessionPanelCheck.IsChecked == true,
                    groupByGroupCheck.IsChecked == true,
                    emptyGroupsCheck.IsChecked == true,
                    orderItems.Select(o => o.Key).ToList());

                // «Только избранные» это режим списка, а не отдельный фильтр:
                // снятие флажка возвращает список к показу всех баз, но режим
                // «недавние» не трогает, иначе флажок отменял бы чужой выбор.
                if (favoritesOnlyCheck.IsChecked == true)
                    _viewModel.IsListModeFavorites = true;
                else if (_viewModel.IsListModeFavorites)
                    _viewModel.IsListModeAll = true;

                _viewModel.ApplyStatusBarSettings(
                    statusPathCheck.IsChecked == true,
                    statusArchCheck.IsChecked == true,
                    statusLaunchModeCheck.IsChecked == true,
                    statusPortCheck.IsChecked == true,
                    statusVersionCheck.IsChecked == true,
                    statusClientTypeCheck.IsChecked == true,
                    statusConnectionTypeCheck.IsChecked == true,
                    statusUserCheck.IsChecked == true,
                    statusIdCheck.IsChecked == true);

                _viewModel.SaveElementFonts(editedFonts);

                DialogResult = true;
                Close();
            };
            // Красный в этом проекте задан числом, а не ключом темы: в разметке WPF
            // отмена нарисована цветом #EF4444 напрямую, кисти под него нет ни там,
            // ни здесь.
            var dangerBrush = new SolidColorBrush(Color.Parse("#EF4444"));
            var cancelContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            cancelContent.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Width = UiMetrics.Scaled(16),
                Height = UiMetrics.Scaled(16),
                Data = IconHelper.Geometry("IconClose"),
                Stretch = Stretch.Uniform,
                Fill = dangerBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            cancelContent.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Common.Cancel"),
                FontWeight = FontWeight.SemiBold,
                Foreground = dangerBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            var cancel = new Button
            {
                Content = cancelContent,
                MinWidth = UiMetrics.Scaled(140),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                BorderThickness = new Thickness(1.5),
                IsCancel = true
            };
            PaintButtonStates(cancel, Brushes.Transparent,
                new SolidColorBrush(Color.Parse("#FEF2F2")),
                new SolidColorBrush(Color.Parse("#FEE2E2")));
            cancel.BorderBrush = dangerBrush;
            // Отмена закрывает окно так же, как крестик: DialogResult остаётся
            // ложным, и вызывающая сторона ничего не применяет.
            cancel.Click += (_, _) => Close();

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
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

            // Подпись значения объявляется ниже, а обновлять её надо отсюда,
            // поэтому обновление передаётся отложенно.
            Action<string>? hexText = null;

            var button = new Button
            {
                Content = swatch,
                Padding = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            void PickColor()
            {
                var picker = new ColorPickerWindow(value);
                if (!picker.ShowDialogSync(this))
                    return;

                value = picker.Result;
                scheme.Colors[key] = value;
                swatch.Background = ParseBrush(value);
                hexText?.Invoke(value);
            }

            button.Click += (_, _) => PickColor();

            // Строка как в разметке WPF (SettingsWindow.xaml:903): подпись, образец,
            // шестнадцатеричное значение и кнопка выбора. Значение показывается
            // потому, что цвет часто переносят копированием, а не глазом.
            var hex = new TextBlock
            {
                Text = value,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            ThemeBrushes.Bind(hex, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            hexText = updated => hex.Text = updated;

            var choose = new Button
            {
                Content = LocalizationManager.T("Settings.ChooseColor"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            choose.Click += (_, _) => PickColor();

            // Ширины колонок как в разметке WPF (SettingsWindow.xaml:906): подпись
            // по содержимому, тянется колонка со значением, а не подпись.
            var grid = new Grid { Margin = new Thickness(0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(text);
            grid.Children.Add(button);
            Grid.SetColumn(button, 1);
            grid.Children.Add(hex);
            Grid.SetColumn(hex, 2);
            grid.Children.Add(choose);
            Grid.SetColumn(choose, 3);
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

        /// <summary>
        /// Красит кнопку с учётом наведения и нажатия. Своим свойством Background
        /// этого не добиться: тема Fluent задаёт состояния вложенным стилем
        /// на части шаблона и перекрывает значение кнопки.
        /// </summary>
        private static void PaintButtonStates(Button button, IBrush normal, object hover, object pressed)
        {
            button.Background = normal;
            foreach (var (state, brush) in new[] { (":pointerover", hover), (":pressed", pressed) })
            {
                var style = new Style(x => x.OfType<Button>().Class(state)
                    .Template().OfType<ContentPresenter>());
                style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, brush));
                button.Styles.Add(style);
            }
        }

        /// <summary>Наблюдатель, который просто зовёт действие на каждое значение.</summary>
        /// <summary>
        /// Установленные в системе семейства, кроме уже перечисленных.
        /// Коллекция заполняется синхронно при первом обращении и после
        /// настройки платформы читается законно; до неё обращение незаконно,
        /// поэтому отказ гасится и список остаётся авторским.
        /// </summary>
        private static IEnumerable<string> InstalledFontFamilies(IReadOnlyCollection<string> already)
        {
            try
            {
                return Avalonia.Media.FontManager.Current.SystemFonts
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n)
                                && !already.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Область интерфейса в списке подвкладки «Шрифт».</summary>
        private sealed class FontScopeItem
        {
            public FontScopeItem(string key) => Key = key;
            public string Key { get; }
            public override string ToString() => ThemeManager.FontScopeDisplayName(Key);
        }

        /// <summary>Начертание шрифта: пара «насыщенность и наклон» с локализованным именем.</summary>
        private sealed class FontFaceItem
        {
            public FontFaceItem(string key, string weight, string style)
            {
                Key = key;
                Weight = weight;
                Style = style;
            }

            public string Key { get; }
            public string Weight { get; }
            public string Style { get; }
            public override string ToString() => LocalizationManager.T(Key);
        }

        private static readonly FontFaceItem[] FontFaces =
        {
            new("Settings.Font.StyleNormal", "Normal", "Normal"),
            new("Settings.Font.StyleBold", "Bold", "Normal"),
            new("Settings.Font.StyleItalic", "Normal", "Italic"),
            new("Settings.Font.StyleBoldItalic", "Bold", "Italic")
        };

        /// <summary>
        /// Вложенная вкладка раздела настроек: значок и подпись в заголовке,
        /// содержимое в своей прокрутке. Повторяет заголовки подвкладок WPF.
        /// </summary>
        private static TabItem SubTab(string titleKey, string tooltipKey, string iconKey, Control content)
        {
            // Кегль и насыщенность подписи задаёт тема SettingsSubTabItem,
            // как стиль разметки (SettingsWindow.xaml:227).
            var tab = new TabItem
            {
                Content = new ScrollViewer
                {
                    Content = content,
                    Padding = new Thickness(8, 4, 4, 4),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            };
            tab.Styled(ControlThemes.SettingsSubTabItem);
            // Кегль подписи масштабируется компактным режимом: с полным кеглем
            // пять вкладок не влезают в ряд, а перенос строки у UniformGrid
            // невозможен. Местное значение старше темы, поэтому ставится здесь.
            tab.FontSize = UiMetrics.ScaledFont(12);

            var icon = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(14), out var path);
            path.Bind(Avalonia.Controls.Shapes.Shape.FillProperty,
                new Avalonia.Data.Binding(nameof(TabItem.Foreground)) { Source = tab });

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    icon,
                    new TextBlock
                    {
                        Text = LocalizationManager.T(titleKey),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            ToolTip.SetTip(header, LocalizationManager.T(tooltipKey));
            tab.Header = header;
            return tab;
        }

        /// <summary>Заголовок группы настроек на вкладке.</summary>
        private static TextBlock GroupTitle(string text) => new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        };

        /// <summary>
        /// Строит строку состояния блока синхронизации по тем значениям, что
        /// сейчас в полях окна, а не по сохранённым. Набор ключей и порядок
        /// частей те же, что у BuildStatusText в ViewModels/SettingsViewModel.cs:
        /// сам метод лежит в файле, который в Linux-сборку не входит.
        /// </summary>
        private static string BuildSyncStatus(IbasesSyncMode mode, string? filePath,
            IbasesSyncTrigger trigger, string? interval, string? scheduleTime)
        {
            if (mode == IbasesSyncMode.None)
                return LocalizationManager.T("Settings.Ibases.StatusDisabled");

            var path = string.IsNullOrWhiteSpace(filePath)
                ? Services.IbasesV8iImporter.FindDefaultPath()
                : filePath.Trim();
            if (string.IsNullOrWhiteSpace(path))
                return LocalizationManager.T("Settings.Ibases.StatusFileNotFound");

            var modeText = mode switch
            {
                IbasesSyncMode.Import => LocalizationManager.T("Settings.Ibases.ModeImportShort"),
                IbasesSyncMode.Export => LocalizationManager.T("Settings.Ibases.ModeExportShort"),
                _ => LocalizationManager.T("Settings.Ibases.ModeBothShort")
            };
            var triggerText = trigger switch
            {
                IbasesSyncTrigger.Interval => string.Format(
                    LocalizationManager.T("Settings.Ibases.TriggerIntervalShort"),
                    int.TryParse(interval, out var minutes) && minutes > 0 ? minutes : 30),
                IbasesSyncTrigger.Schedule => string.Format(
                    LocalizationManager.T("Settings.Ibases.TriggerScheduleShort"), scheduleTime),
                _ => LocalizationManager.T("Settings.Ibases.TriggerStartupShort")
            };
            return string.Format(LocalizationManager.T("Settings.Ibases.StatusFormat"),
                path, modeText, triggerText);
        }

        /// <summary>
        /// Подпись и ссылка под ней. Ссылка открывается системным обработчиком:
        /// в версии для Windows это делает OnAboutLink_Click, которого
        /// в Linux-сборке нет.
        /// </summary>
        private StackPanel LinkBlock(string caption, string url)
        {
            var block = new StackPanel();
            var captionBlock = new TextBlock
            {
                Text = caption,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            ThemeBrushes.Bind(captionBlock, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            block.Children.Add(captionBlock);

            var link = new TextBlock
            {
                Text = url,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextWrapping = TextWrapping.Wrap,
                // По умолчанию TextBlock растягивается на всю ширину строки,
                // и тогда Bounds шире нарисованного текста. Проверка попадания
                // ниже считает по Bounds, поэтому ширина прижимается к тексту.
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ThemeBrushes.Bind(link, TextBlock.ForegroundProperty, "AccentBrush");
            link.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                // Отпускание вне текста щелчком не считается: Avalonia
                // захватывает указатель при нажатии, и без этой проверки
                // ссылка срабатывала после перетаскивания далеко в сторону.
                // В версии для Windows этого нет: там MouseLeftButtonUp
                // без захвата, и отпускание вне элемента до ссылки не доходит.
                var point = e.GetPosition(link);
                if (point.X < 0 || point.Y < 0
                    || point.X > link.Bounds.Width || point.Y > link.Bounds.Height)
                    return;
                if (!Services.OneCLauncher.OpenUrl(url))
                    ShowAboutMessage(LocalizationManager.T("Settings.About.LinkOpenFailed"));
            };
            block.Children.Add(link);
            return block;
        }

        /// <summary>Пояснение под заголовком группы настроек.</summary>
        private static TextBlock Hint(string text) => new()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>
        /// Вкладка окна: значок 18 и подпись, содержимое как есть.
        /// Значок красится подписью, как в разметке (SettingsWindow.xaml:296).
        /// </summary>
        private static TabItem MainTab(string iconKey, string titleKey, Control content)
        {
            var tab = new TabItem { Content = content };
            tab.Styled(ControlThemes.SettingsTabItem);
            // Ширина, кегль и значок вкладки уменьшаются в компактном режиме.
            tab.Width = UiMetrics.Scaled(235);
            tab.FontSize = UiMetrics.ScaledFont(13);

            var icon = IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(18), out var path);
            path.Bind(Avalonia.Controls.Shapes.Shape.FillProperty,
                new Avalonia.Data.Binding(nameof(TabItem.Foreground)) { Source = tab });
            icon.Margin = new Thickness(0, 0, 8, 0);

            tab.Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    icon,
                    new TextBlock { Text = LocalizationManager.T(titleKey), VerticalAlignment = VerticalAlignment.Center }
                }
            };
            return tab;
        }

        /// <summary>
        /// Переключатель настройки: подпись слева, дорожка справа. В разметке
        /// это ToggleButton со стилем SettingsToggle, а не флажок.
        /// </summary>
        private static ToggleButton SettingsSwitch(string textKey, bool value,
            string? iconKey = null, string? iconColor = null)
        {
            var caption = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            // Значок слева от подписи и его цвет заданы в разметке числом
            // у каждого переключателя (SettingsWindow.xaml:500 и далее).
            Control content = iconKey is null
                ? caption
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        IconHelper.MakeIcon(iconKey, UiMetrics.Scaled(16),
                            new SolidColorBrush(Color.Parse(iconColor ?? "#94A3B8"))),
                        caption
                    }
                };
            if (content is StackPanel panel)
                ((Control)panel.Children[0]).Margin = new Thickness(0, 0, 8, 0);

            var toggle = new ToggleButton
            {
                Content = content,
                IsChecked = value
            };
            toggle.Styled(ControlThemes.SettingsToggle);
            return toggle;
        }

        private static ToggleButton DisplayCheck(string textKey, bool value,
            string? iconKey = null, string? iconColor = null)
            => SettingsSwitch(textKey, value, iconKey, iconColor);

        private static void ThemeChanged(
            RadioButton light, RadioButton dark, MainViewModel viewModel,
            Action<string> onTheme)
        {
            light.IsCheckedChanged += (_, _) =>
            {
                if (light.IsChecked != true)
                    return;
                onTheme(ThemeManager.LightThemeName);
            };
            dark.IsCheckedChanged += (_, _) =>
            {
                if (dark.IsChecked != true)
                    return;
                onTheme(ThemeManager.DarkThemeName);
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
            // Раскладка строки из разметки WPF: подпись в колонке 170, поле тянется
            // по остатку ширины, шаг между строками 6.
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(170)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var label = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(label);

            // Поле сочетания в разметке идёт тем же стилем, что и обычное поле
            // ввода (SettingsWindow.xaml:977 и далее).
            var box = new Controls.HotkeyBox { Value = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 34 };
            box.Styled(ControlThemes.ModernTextBox);
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
            // Отступы между элементами заданы поштучно, как в разметке
            // (SettingsWindow.xaml:1487-1519), а не общим зазором панели.
            var panel = new StackPanel();

            var asm = Assembly.GetExecutingAssembly();
            var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                              ?? asm.GetName().Version?.ToString() ?? "";
            var title = asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? LocalizationManager.T("App.Title");

            // Название и справка по приложению в одной строке, как в разметке WPF
            // (SettingsWindow.xaml:1488-1494).
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 8)
            };
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new Controls.HelpLink
            {
                // Свой текст без строки про версию: в общем ключе она вписана
                // строкой и отстала (0.3.5.1 против 0.3.5.10), а живая версия
                // печатается здесь же строкой ниже. Windows-сторона это
                // расхождение подтвердила у себя.
                HelpText = LocalizationManager.T("Settings.About.HelpTextLinux"),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(titleRow);

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationManager.T("Settings.About.Version"), infoVersion),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            });

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.About.Author"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Подписи и ссылки на публикацию и репозиторий, как в разметке WPF
            // (SettingsWindow.xaml:1497-1510). В версии для Windows их открывает
            // обработчик под #if WINDOWS, здесь используется системный xdg-open.
            var infostart = LinkBlock(LocalizationManager.T("Settings.About.Infostart"),
                "https://infostart.ru/1c/tools/2764888/");
            infostart.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(infostart);
            panel.Children.Add(LinkBlock(LocalizationManager.T("Settings.About.GitHub"),
                "https://github.com/sivatorov/ConfigurationManagement"));

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.About.AvaloniaText"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 16, 0, 0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(LocalizationManager.T("Settings.About.RuntimeInfo"), Environment.OSVersion, Environment.Is64BitOperatingSystem) + "\n" +
                       string.Format(LocalizationManager.T("Settings.About.DataDir"), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7,
                Margin = new Thickness(0, 8, 0, 0)
            });

            // Кнопка копирования по разметке (SettingsWindow.xaml:1511-1518):
            // вторичная кнопка, отступ сверху 24, значок копирования 16 синим
            // с зазором 6 до подписи.
            var copyCaption = new TextBlock
            {
                Text = LocalizationManager.T("Settings.About.CopyTechInfo"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var copyIcon = IconHelper.MakeIcon("IconCopy", 16, new SolidColorBrush(Color.Parse("#3B82F6")));
            copyIcon.VerticalAlignment = VerticalAlignment.Center;
            copyIcon.Margin = new Thickness(0, 0, 6, 0);
            var copyButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { copyIcon, copyCaption }
                },
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 24, 0, 0),
                Padding = new Thickness(14, 8)
            };
            copyButton.Styled(ControlThemes.SecondaryButton);
            copyButton.Click += async (_, _) =>
            {
                try
                {
                    var text = TechnicalInfoService.Collect();
                    if (this.Clipboard is { } cb)
                        await cb.SetTextAsync(text);
                    ShowAboutMessage(LocalizationManager.T("Settings.About.TechInfoCopied"));
                }
                catch
                {
                    ShowAboutMessage(LocalizationManager.T("Settings.About.TechInfoCopyFailed"));
                }
            };
            panel.Children.Add(copyButton);

            return panel;
        }

        /// <summary>
        /// Показывает информационное окно поверх текущего окна настроек.
        /// </summary>
        private void ShowAboutMessage(string message)
        {
            var win = new MaterialMessageWindowAvalonia(message, LocalizationManager.T("Common.Information"), MaterialMessageKind.Info)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _ = win.ShowDialog(this);
        }

        /// <summary>Строка списка слотов избранного: ключ базы, её имя и номер слота.</summary>
        private sealed class FavoriteSlotItem : INotifyPropertyChanged
        {
            private int _number;

            public FavoriteSlotItem(string key, string name)
            {
                Key = key;
                Name = name;
            }

            public string Key { get; }

            public string Name { get; }

            public int Number
            {
                get => _number;
                set
                {
                    if (_number == value)
                        return;
                    _number = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
                }
            }

            /// <summary>Подпись слота в списке: «Alt+1» и так далее.</summary>
            public string Caption => $"Alt+{_number}";

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        /// <summary>
        /// Строка списка колонок: ключ колонки, локализованное имя и флаг видимости.
        /// Один элемент объединяет порядок и видимость колонки — оба редактируются
        /// в одном списке на вкладке «Отображение».
        /// </summary>
        private sealed class ColumnOrderItem
        {
            public string Key { get; }
            public string Display { get; }
            public bool Visible { get; set; }
            public string IconKey { get; }

            public ColumnOrderItem(string key, string display, bool visible, string iconKey)
            {
                Key = key;
                Display = display;
                Visible = visible;
                IconKey = iconKey;
            }

            public override string ToString() => Display;
        }
    }
}
#endif