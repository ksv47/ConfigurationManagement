# История изменений

Все заметные изменения проекта «Управление конфигурациями 1С» фиксируются в этом файле.

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версионирование — на [Semantic Versioning](https://semver.org/lang/ru/).

## [0.3.3.44] — 2026-08-23

### Локализация (Windows/WPF)

- **Исправлена регрессия: вместо переводов отображались сырые ключи локализации (например «Main.AllBases»)** — [`LocExtension.cs`](Configuration Management/Localization/LocExtension.cs) возвращён к КОНВЕРТЕРНОЙ привязке (`Source = LocalizationManager.Instance.Source`, `Mode = BindingMode.OneWay`, `Converter = LocalizationValueConverter.Instance`, `ConverterParameter = Key`). Ранее (в 0.3.3.43) привязка была заменена на индексаторную (`Path = new PropertyPath($"[{EscapeIndexerKey(Key)}]")`), которая некорректно резолвится в индексатор `LocalizationSource[string]` и возвращала сырой ключ вместо перевода. Убраны ставшие ненужными `using System.Text;`, `using System.Windows;` и метод `EscapeIndexerKey`.
- **Надёжное динамическое обновление через регистрацию выражений привязки** — в [`LocalizationSource.cs`](Configuration Management/Localization/LocalizationSource.cs) добавлен платформонезависимый механизм: `RegisterForUpdate(object target, Action<object> update)` хранит СЛАБУЮ ссылку на объект и делегат обновления (без захвата target), а `NotifyAll()` после поднятия `PropertyChanged("Item[]")`/`PropertyChanged(string.Empty)` вызывает `UpdateTarget()` у всех зарегистрированных выражений (обёртка в try/catch, защита от выброшенных/отвязанных ссылок, периодическая очистка «мёртвых» ссылок по достижении порога). `LocExtension.ProvideValue` регистрирует полученный `BindingExpression`. `LocalizationSource` не зависит от WPF-типа `BindingExpression` напрямую, поэтому сборка Avalonia (Linux) не ломается.
- **Сохранён проход по визуальному дереву** — в [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs) `RefreshAllBindingsOnVisualTree()` в `RebuildAfterLanguageChange()` остался без изменений: он по-прежнему закрывает MultiBinding-подсказки кнопок запуска (`Path="Source"` + конвертер), которые регистрация из п.2 не покрывает.
- Переводы снова корректно отображаются при загрузке и обновляются динамически при смене языка без перезапуска приложения.

### Документация

- **Версия обновлена до 0.3.3.44** (`InformationalVersion` = 0.3.3.44 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены; версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [0.3.3.43] — 2026-08-23

### Локализация (Windows/WPF)

- **Исправлено: статичные подписи главного окна не переводились сразу при смене языка** — корневая причина устранена в [`LocExtension.cs`](Configuration Management/Localization/LocExtension.cs). Раньше `ProvideValue` создавал привязку с пустым `Path` (`Source` + конвертер по `ConverterParameter=Key`), а привязка WPF без пути НЕ подписывается на `INotifyPropertyChanged` источника, поэтому `LocalizationSource.NotifyAll()` (`PropertyChanged(string.Empty)`) не обновляла такие элементы — они получали значение один раз при создании и оставались на старом языке до перезапуска.
  Теперь `ProvideValue` строит ИНДЕКСАТОРНУЮ привязку: `Path = new PropertyPath($"[{EscapeIndexerKey(Key)}]")` (с экранированием `\` и `"` в ключе), `Source = LocalizationManager.Instance.Source`, `Mode = BindingMode.OneWay`. Конвертер больше не используется — источник возвращает перевод через `this[string key]`. Индексаторные привязки WPF корректно реагируют на `PropertyChanged("Item[]")`, которое поднимает `NotifyAll()`, поэтому все `{loc:Loc}` в любом окне (вкладки «Все базы/Избранное/Недавние», кнопки панели, заголовки колонок, пункты меню и т.п.) переводятся мгновенно, без перезапуска.
- **Принудительное обновление привязок по визуальному дереву окна** — в [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs) в `RebuildAfterLanguageChange()` после обновления заголовка/темы/трея теперь вызывается `RefreshAllBindingsOnVisualTree()`, который через `VisualTreeHelper` (с переходом в логическое дерево, где необходимо) обходит все `DependencyObject` окна и для каждой локальной привязки (`GetLocalValueEnumerator()` + `BindingOperations.GetBindingExpressionBase(dp).UpdateTarget()`) принудительно обновляет целевое значение. Это закрывает и те элементы, которые не реагируют на `PropertyChanged("Item[]")` (например MultiBinding-подсказки кнопок запуска с `Path="Source"` + конвертер). Всё обёрнуто в try/catch — сбой одной привязки не прерывает пересборку интерфейса. Существующая подписка/обработчик смены языка сохранены.
- Смена языка работает в обе стороны (ru ↔ en) и на внешние языки; главное окно полностью переводится без перезапуска.

### Документация

- **Версия обновлена до 0.3.3.43** (`InformationalVersion` = 0.3.3.43 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [0.3.3.42] — 2026-08-23

### Локализация (Windows/WPF)

- **Полная динамическая смена языка интерфейса без перезапуска для локализованных свойств VM и дерева групп** — [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs) (Windows/#if WINDOWS) теперь подписывается на `LocalizationManager.Instance.LanguageChanged` в конструкторе. Новый обработчик `OnLanguageChanged` (с маршалингом на UI-поток через `Dispatcher`) вызывает `HandleLanguageChanged()`, который:
  - вызывает `OnPropertyChanged(string.Empty)` — WPF пересчитывает все привязки к VM, обновляя локализованные свойства (`StatusBarInfo`, `ExportIndicatorTooltip`, `RightPanelToggleTooltip`, `GroupByGroupText`, `SyncMessage` и др.), которые возвращают `LocalizationManager.T(...)` на лету;
  - вызывает `RebuildGroupTree()` — дерево пересобирается целиком, поэтому служебные узлы («Все базы», «Без группы», «Избранное») через `GroupNodeViewModel.DisplayName` сразу получают тексты на новом языке.
  Работает для любого направления (ru ↔ en и внешние языки). Ранее эти элементы обновлялись только после перезапуска, хотя XAML-привязки `{loc:Loc}` уже обновлялись через `Source.NotifyAll()`.
- **Корректная отписка от события** — добавлен публичный метод `UnsubscribeLanguageChanged()` с флагом `_languageChangedSubscribed` (защищает от дублирования подписки); вызывается из `MainWindow.OnClosing` при полном закрытии окна, чтобы не было утечек.

### Документация

- **Версия обновлена до 0.3.3.42** (`InformationalVersion` = 0.3.3.42 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [0.3.3.41] — 2026-08-23

### Локализация (Windows/WPF)

- **Динамическая смена языка интерфейса без перезапуска (Windows/WPF)** — главное окно [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs) теперь подписывается на `LocalizationManager.Instance.LanguageChanged` (по аналогии с Avalonia-версией [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). При смене языка в настройках новый обработчик `OnLanguageChanged`/`RebuildAfterLanguageChange` вручную обновляет элементы, заданные в code-behind: заголовок окна (`Title` с версией — локальное значение перекрывает XAML-привязку `{loc:Loc App.Title}`), подсказку кнопки смены темы ([`UpdateThemeButton()`](Configuration Management/MainWindow.xaml.cs)), а также текст и меню трея ([`RebuildTrayMenu()`](Configuration Management/MainWindow.xaml.cs)). Событие обрабатывается на UI-потоке через диспетчер (`Dispatcher.CheckAccess`/`BeginInvoke`) — работает для любого направления (ru ↔ en и внешние языки).
- **Корректная отписка от события** — в [`OnClosing`](Configuration Management/MainWindow.xaml.cs) (ветка реального закрытия, не сворачивания в трей) выполняется `LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged`, что исключает утечку памяти (окно не удерживается менеджером локализации после закрытия).
- Остальные тексты главного окна (кнопки, колонки, тултипы, empty-state и т.п.) объявлены в XAML через `{loc:Loc ...}` и обновляются автоматически через `LocalizationManager.Source.NotifyAll()`, поэтому дополнительной перестройки не требуют.

### Документация

- **Версия обновлена до 0.3.3.41** (`InformationalVersion` = 0.3.3.41 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в `README.md` обновлены; версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [0.3.3.40] — 2026-08-23

### Локализация / сохранение настроек

- **Исправлено: язык интерфейса терялся при закрытии окна (Windows/WPF)** — метод `SaveSettings()` в [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs:3282) при построении нового объекта `AppSettings` не задавал свойство `Language`, из-за чего оно сохранялось пустым (`""`). При закрытии главного окна (`MainWindow.OnClosing` → `_viewModel.SaveSettings()`) файл настроек перезаписывался с пустым языком, и при следующем запуске `LocalizationManager.Initialize(settings.Language)` выбирал язык системы вместо выбранного пользователем. Теперь `SaveSettings()` всегда записывает актуальный код языка через `LocalizationManager.Instance.CurrentLanguage` в создаваемый объект `AppSettings.Language`.

### Документация

- **Версия обновлена до 0.3.3.40** (`InformationalVersion` = 0.3.3.40 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в `README.md` обновлены; версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [0.3.3.39] — 2026-08-23

### Локализация / сохранение настроек

- **Сохранение языка при закрытии окна в трей** — при уходе главного окна в трей (`OnClosing`) вызывается `MainViewModel.PersistSettings()`, который записывает текущий код языка в `AppSettings.Language` и сохраняет настройки на диск. Ранее язык сохранялся только при полном выходе через команду «Выход», поэтому при закрытии окна крестиком и последующем завершении из трея выбранный язык мог не сохраниться.
- **Динамическое обновление меню трея при смене языка** — меню и подсказка (`ToolTipText`) значка в трее пересобираются в `RebuildAfterLanguageChange()` через новый метод `BuildTrayMenu()`. Подписи пунктов («Показать окно», «Синхронизация», «Настройки», «Выход» и др.) и подсказка трея сразу переключаются на выбранный язык без перезапуска приложения.
- **Публичный метод `PersistSettings()`** в `MainViewModel` — единая точка сохранения настроек (включая язык), используется и при закрытии в трей, и при полном выходе.

### Документация

- **Версия обновлена до 0.3.3.39** (`InformationalVersion` = 0.3.3.39 в `Configuration Management.csproj`). Бейдж и заголовок в `README.md` обновлены; версия в `Settings.About.HelpText` обновлена в `ru.json` и `en.json`.

## [Не выпущено]

### Инструменты / валидация локализации

- **Добавлен автоматический аудит локализации** [`tools/l10n-audit.ps1`](tools/l10n-audit.ps1) (и отчёт [`tools/l10n-audit-report.txt`](tools/l10n-audit-report.txt)). Скрипт (а) сравнивает наборы ключей `ru.json ↔ en.json` и (б) проверяет, что все ключи, используемые в коде/XAML (`LocalizationManager.T("...")`, `{loc:Loc ...}`, `Loc["..."]`, `{Binding Loc[...]}`), присутствуют в `ru.json`.
- **Результат повторной валидации:** расхождений ключей между [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json) нет (1186 = 1186); все 1117 уникальных ключей, найденных в коде/XAML, присутствуют в `ru.json` (и, следовательно, в `en.json`). Захардкоженных пользовательских строк вне локализации в контекстных меню дерева, трей-меню и статус-баре не обнаружено. Дефектов для исправления не выявлено, поэтому номер версии не изменён.

## [0.3.3.38] — 2026-08-23

### Локализация

- **Динамическая смена языка при выборе в настройках (Avalonia/Linux)** — в [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs) добавлен обработчик `SelectionChanged` у выпадающего списка языка: язык теперь применяется (и сохраняется в настройках) сразу при выборе пункта, без нажатия «OK». Ранее изменение языка фиксировалось только по кнопке «OK», поэтому интерфейс выглядел «непереведённым» при закрытии диалога иначе. Также добавлено переопределение `OnLanguageChanged`, которое пересобирает содержимое окна настроек целиком, чтобы все подписи вкладок и элементов обновились на выбранный язык немедленно (базовая реализация [`ModalWindowBase`](Configuration Management/ModalWindowBase.cs) обновляла только кнопки OK/Отмена).
- **Сохранение языка при закрытии программы** — в [`ExitApplication()`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs:2676) текущий код языка (`LocalizationManager.Instance.CurrentLanguage`) записывается в `AppSettings.Language` перед сохранением настроек. Это гарантирует, что язык не теряется между запусками даже если он определялся автоматически (по языку системы) и не был явно выбран через `ApplyLanguage`.

### Документация

- **Версия обновлена до 0.3.3.38** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.38 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.38; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.37] — 2026-08-23

### Локализация

- **Аудит прочих непереведённых строк интерфейса по всему проекту** — проверены все диалоговые окна ([`AddEditWindow`](Configuration Management/AddEditWindow.Avalonia.cs), [`CacheCleanWindow`](Configuration Management/CacheCleanWindow.Avalonia.cs), [`ConnectionSettingsWindow`](Configuration Management/ConnectionSettingsWindow.Avalonia.cs), [`CreateInfobaseWindow`](Configuration Management/CreateInfobaseWindow.Avalonia.cs), [`DeleteInfobaseWindow`](Configuration Management/DeleteInfobaseWindow.Avalonia.cs), [`GroupEditWindow`](Configuration Management/GroupEditWindow.Avalonia.cs), [`GroupPickerWindow`](Configuration Management/GroupPickerWindow.Avalonia.cs), [`GroupSettingsWindow`](Configuration Management/GroupSettingsWindow.Avalonia.cs), [`LaunchParametersWindow`](Configuration Management/LaunchParametersWindow.Avalonia.cs), [`LinkInputWindow`](Configuration Management/LinkInputWindow.Avalonia.cs), [`NameInputWindow`](Configuration Management/NameInputWindow.Avalonia.cs), [`PlatformVersionPickerWindow`](Configuration Management/PlatformVersionPickerWindow.Avalonia.cs), [`SettingsWindow`](Configuration Management/SettingsWindow.Avalonia.cs), [`TagInputWindow`](Configuration Management/TagInputWindow.Avalonia.cs) и др.), контролы (`Controls/*.cs`), сервисы (`Services/*.cs`) и ViewModel (`ViewModels/*.cs`) обеих реализаций (WPF `.xaml`/`.xaml.cs` и Avalonia `.Avalonia.cs`). Способ поиска — regex по кириллице в строковых литералах C# и по содержимому атрибутов XAML/AXAML вне обёртки `{loc:Loc …}`/`Content=`; комментарии не учитывались.
- **Результат: жёстко зашитых пользовательских строк не обнаружено** — весь пользовательский текст (подписи кнопок, заголовки окон, тексты меток, сообщения диалогов/MessageBox, тултипы, пункты контекстных меню, значения выпадающих списков) уже вынесен в локализацию: WPF — `{loc:Loc Key}`, Avalonia — `LocalizationManager.T("Key")`. Ключи [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json) согласованы, недостающих ключей и английских переводов нет.
- **Оставлены без изменений как технические (не показываются пользователю в UI):** значения режимов запуска/клиента («Автоматический», «Тонкий клиент», «Толстый клиент», «Веб-клиент», «Авто», «Обычный», «Тонкий» и т.п.), используемые как внутренние ключи хранения и сопоставляемые с локализованными подписями через маппинги ([`Infobase.cs`](Configuration Management/Models/Infobase.cs), [`IbasesV8iImporter.cs`](Configuration Management/Services/IbasesV8iImporter.cs), [`OneCLauncher*.cs`](Configuration Management/Services/OneCLauncher.cs)); строки журналов (`_logger.*`, `Debug.WriteLine`); диагностические префиксы в логах фатальных ошибок ([`App.xaml.cs`](Configuration Management/App.xaml.cs)); описание окружения Linux для журнала (`LinuxDesktopEnvironment.Describe()`). Их замена на `LocalizationManager.T(...)` сломала бы логику хранения/сопоставления и не дала бы пользы, т.к. они не отображаются в интерфейсе.

### Документация

- **Версия обновлена до 0.3.3.37** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.37 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.37; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.36] — 2026-08-23

### Локализация

- **Аудит подсказок (ToolTips) кнопок и переключателей главного окна** — проверены обе реализации ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml): верхняя панель, заголовки колонок, правая панель; [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs): [`BuildTopBar()`](Configuration Management/MainWindow.Avalonia.cs:183), [`RefreshColumnHeader()`](Configuration Management/MainWindow.Avalonia.cs:1931), [`BuildRightPanel()`](Configuration Management/MainWindow.Avalonia.cs:1144), вспомогательные `TopBarPrimaryButton`/`TopBarSecondaryButton`/`TopBarIconButton`/`PrimaryActionButton`/`SecondaryActionButton`/`HeaderIconButton`). Все подсказки уже вынесены в локализацию: WPF — `{loc:Loc Main.*}` (в т.ч. через `MultiBinding` с ключами `Main.LaunchEnterpriseShort`/`Main.LaunchConfiguratorShort` и хоткеем), Avalonia — `LocalizationManager.T("Main.*Tooltip")` и `ToolTip.SetTip(...)`. Жёстко зашитых русских строк в тултипах не обнаружено.
- **Добавлены недостающие тултипы сегментам «Все / Избранное / Недавние» в Avalonia-версии** — в [`BuildListModeSegments()`](Configuration Management/MainWindow.Avalonia.cs:273) переключатели списка баз не имели подсказок, тогда как в WPF они есть. Теперь им задаются существующие ключи `Main.AllBasesTooltip`/`Main.FavoritesTooltip`/`Main.RecentTooltip`. Ключи `Main.*Tooltip` согласованы между [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json); недостающих ключей и английских переводов нет.

### Документация

- **Версия обновлена до 0.3.3.36** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.36 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.36; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.35] — 2026-08-23

### Локализация

- **Аудит текстов кнопок правой панели главного окна** — проверены обе реализации ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml) и [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs), метод [`BuildRightPanel()`](Configuration Management/MainWindow.Avalonia.cs:1144)). Все подписи кнопок, заголовки секций, тексты split-кнопок и пункты их контекстных меню уже вынесены в локализацию: WPF использует `{loc:Loc Main.*}` (и `{loc:Loc LinkInput.Title}`, `{loc:Loc Common.Delete}`), Avalonia — `LocalizationManager.T("Main.*")` (вспомогательные `PrimaryActionButton`/`SecondaryActionButton`/`SectionCard` получают текст ключом). Жёстко зашитых русских строк в текстах кнопок правой панели не обнаружено.
- **Исправлен английский перевод** текста переключателя `Main.SessionClientOrdinary` в блоке «Текущая сессия» в [`en.json`](Configuration Management/Localization/Languages/en.json): было «Thin (managed)», стало «Ordinary» (согласовано с `ru.json` — «Обычный»). Ключи `Main.*` правой панели согласованы между `ru.json` и `en.json`; недостающих ключей и переводов нет.

### Документация

- **Версия обновлена до 0.3.3.35** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.35 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.35; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.34] — 2026-08-23

### Локализация

- **Аудит названий колонок главного окна** — проверены обе реализации ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml) и [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). Все заголовки колонок уже вынесены в локализацию: WPF использует `{loc:Loc Column.*}`, Avalonia — `LocalizationManager.T("Column.*")` (в методе [`ListColumns()`](Configuration Management/MainWindow.Avalonia.cs)); жёстко зашитых русских строк в названиях колонок не обнаружено. Переводы в [`en.json`](Configuration Management/Localization/Languages/en.json) согласованы с `ru.json`: `Column.Name` = Name, `Column.Version` = Platform version, `Column.Configuration` = Configuration, `Column.LaunchMode` = Launch mode, `Column.ServerBase` = Server/Base, `Column.LastLaunch` = Last launch, `Column.Size` = Size.

### Документация

- **Версия обновлена до 0.3.3.34** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.34 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.34; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.33] — 2026-08-23

### Исправлено

- **Смена языка интерфейса в Linux/Avalonia-версии теперь мгновенно обновляет главное окно** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). Ранее при выборе другого языка в настройках названия колонок, кнопки правой панели и подсказки кнопок (которые создаются в коде через `LocalizationManager.T(...)`) не перерисовывались — переведённый текст появлялся только после перезапуска приложения, что выглядело как непереведённые элементы интерфейса. Теперь главное окно подписывается на событие [`LocalizationManager.Instance.LanguageChanged`](Configuration Management/Localization/LocalizationManager.cs) и пересобирает своё содержимое: заголовок окна, названия колонок, кнопки правой панели и все подсказки обновляются сразу; компактный режим (`UiMetrics.Compact`) сохраняется, а выделение и прокрутка списка баз восстанавливаются после пересборки. На Windows (WPF) такой проблемы не было — там тексты связаны через `{loc:Loc ...}` и обновляются автоматически через `NotifyAll()`.

- **Версия обновлена до 0.3.3.33** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.33 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.33; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.32] — 2026-08-23

### Добавлено (сборка / упаковка)

- **Собран единый исполняемый файл для Windows (WPF, self-contained single-file)** через [`build.ps1`](Configuration Management/build.ps1) (`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`). Выходной артефакт — один файл `ConfigurationManagement.exe` (~75,5 МБ) в каталоге `Configuration Management/publish/win-x64/`, не требующий установленного .NET Runtime. Нативные библиотеки встроены (`IncludeNativeLibrariesForSelfExtract`), применено сжатие (`EnableCompressionInSingleFile`). Рядом с exe находится только папка `Localization/` для подгрузки внешних `.json`-языков (встроенные `ru`/`en` уже внутри исполняемого файла).

### Документация

- **Версия обновлена до 0.3.3.32** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.32 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.32; жёстко прописанная версия в `Settings.About.HelpText` обновлена в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

## [0.3.3.31] — 2026-08-23

### Слияние исправлений от ksv47

- **Влит PR #55 «Linux/Avalonia: перетаскивание баз и групп мышью»** (ветка `ksv47/linux-fixes`). Все изменения этого PR **выполнены автором ksv47**. По функциональным направлениям:
  - **Перетаскивание баз и групп мышью в Linux/Avalonia-версии** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs), [`LeveledTreeView.Avalonia.cs`](Configuration Management/Controls/LeveledTreeView.Avalonia.cs), [`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)): нажатие ловится туннельным событием `PointerPressed`, узел для переноса фиксируется в нажатии, а не в движении (курсор над дочерней строкой больше не подменяет цель), задан порог сдвига, после которого клик становится перетаскиванием. Сброс допустим только на настоящую группу или корректные служебные узлы (без потери группы базы и без создания циклов в иерархии). Перенос базы выполняется в указанную группу и перед строкой другой базы ([`MoveInfobaseToGroup`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)); перенос подгруппы со всей вложенной иерархией пересчитывает полные пути `Infobase.Group` и свёрнутость узлов без «уезжания» баз в «Без группы» ([`MoveGroupUnder`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)).
  - **Действие после запуска базы или конфигуратора в Linux/Avalonia-версии** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs), [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs), [`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)): как в WPF, окно можно свернуть в трей или увести в трей после успешного запуска; при отсутствии значка трея (например, GNOME Shell без AppIndicator) окно безопасно сворачивается, чтобы не оставаться недоступным. Добавлены ключи локализации `Settings.General.AfterLaunchAction.*` и `Main.AfterLaunchTrayUnavailable` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
  - **Сохранение выбора и подсветки при пересборке дерева**: раскрытие узла теперь связывается с моделью самим `LeveledTreeView` на любом уровне вложенности, а выделение строки восстанавливается после перестройки дерева — по идентификатору группы или ключу узла ([`RestoreTreeSelection`](Configuration Management/MainWindow.Avalonia.cs), событие `TreeRebuilt` в [`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)).
  - **Надёжность сохранения**: `SaveSilently()`/`SaveGroupsSilently()` теперь возвращают признак успеха, и экспорт в `ibases.v8i` выполняется только когда удалось записать оба файла — иначе пути баз и дерево групп разъедутся.
- **Авторство изменений:** все правки, вошедшие в этот merge, выполнены **ksv47** ([`PR #55`](https://github.com/ksv47/linux-fixes)) — см. также раздел «Благодарности» в [`README.md`](README.md).
- **Версия обновлена до 0.3.3.31** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.31 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.31.

## [0.3.3.30] — 2026-08-23

### Удалено (мёртвый код, неиспользуемые файлы и функции)

Проведена чистка репозитория: удалены файлы, которые не компилировались ни на одной платформе или не использовались нигде в коде/XAML, а также неиспользуемая функция. После удалений проект успешно собирается (`dotnet build`, конфигурация Windows/WPF).

- **Удалены устаревшие Linux-заглушки, исключённые из сборки и не дававшие кода на Windows (`#if LINUX` + `<Compile Remove>` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)):**
  - [`Themes/ThemeManager.Linux.cs`](Configuration Management/Themes/ThemeManager.Linux.cs) — старая заглушка Этапа 2; вместо неё используется полный `Themes/ThemeManager.Avalonia.cs`.
  - [`Services/LinuxStubs.cs`](Configuration Management/Services/LinuxStubs.cs) — временные DI-заглушки; вместо них — полноценные реализации `OneCLauncher.Linux.cs`, `OneCComConnector.Linux.cs`, `PlatformVersionService.Linux.cs`.
  - [`Services/LinuxOneCServiceShims.cs`](Configuration Management/Services/LinuxOneCServiceShims.cs) — старый шим статических сервисов 1С; функциональность перенесена в `*Linux.cs` (напр., `Launcher.CreateDirCreateFailedFormat` в [`OneCLauncher.Linux.cs`](Configuration Management/Services/OneCLauncher.Linux.cs)).
- **Удалён временный артефакт WPF-компилятора** [`Configuration Management_ujfv2o2k_wpftmp.csproj`](Configuration Management/Configuration Management_ujfv2o2k_wpftmp.csproj) — авто-генерируемый `*_wpftmp.csproj` (создаётся заново при каждой сборке).
- **Удалена неподключаемая тема** [`Themes/ModernTheme.xaml`](Configuration Management/Themes/ModernTheme.xaml) — не упоминалась ни в `App.xaml`, ни в [`ThemeManager.cs`](Configuration Management/Themes/ThemeManager.cs) (используются только `LightTheme.xaml`/`DarkTheme.xaml`).
- **Удалён неиспользуемый конвертер `GroupFullPathConverter` (WPF + Avalonia)** — без привязок в XAML и без использований в коде.
- **Удалена неиспользуемая ViewModel** [`ViewModels/MetadataNodeViewModel.cs`](Configuration Management/ViewModels/MetadataNodeViewModel.cs) — UI метаданных отсутствует (`MetadataNode`-модель сохраняется, используется через `Infobase.MetadataRoot`).
- **Удалена неиспользуемая локальная функция `FlushSection`** в [`OneCTemplateService.cs`](Configuration Management/Services/OneCTemplateService.cs) — пустая no-op заглушка, вызывала предупреждение компилятора CS8321.
- **Устранены предупреждения компилятора CS8601** (nullable) в [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs): в `LaunchEnterpriseWithParams` / `LaunchConfiguratorWithParams` промежуточная переменная `saved` для временного сохранения параметров запуска инициализируется через `?? ""`, исключая назначение nullable-значения в не-nullable свойство.
- **Почищен [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)**: убраны «висячие» `<Compile Remove>` для удалённых файлов и устаревшие комментарии про заглушки; исправлены устаревшие комментарии в [`Services/IOneCLauncher.cs`](Configuration Management/Services/IOneCLauncher.cs) и [`Services/IPlatformVersionService.cs`](Configuration Management/Services/IPlatformVersionService.cs).

### Документация

- Обновлён [`README.md`](README.md): версия в бейдже и заголовке, удалены ссылки на удалённые файлы (`ModernTheme.xaml`, `LinuxOneCServiceShims.cs`, `MetadataNodeViewModel.cs`), уточнено количество конвертеров Avalonia (18 → 10).

### Прочее

- **Версия обновлена до 0.3.3.30** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.30 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.30.

## [0.3.3.29] — 2026-08-23

### Изменено

- **Настройка «После запуска базы или конфигуратора»: вариант «Закрыть» теперь полностью завершает работу приложения, а не уводит его в трей** ([`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs)). Ранее выбор «Закрыть» фактически делал то же, что и «Свернуть в трей» — вызывал `HideToTray()`, оставляя приложение работать в фоне. Теперь после успешного запуска информационной базы или конфигуратора 1С при выбранной опции «Закрыть» главное окно закрывается через `_forceClose = true; Close()` (тот же механизм, что у команды «Выход»): настройки сохраняются, автоматическая синхронизация останавливается, значок трея освобождается, приложение полностью завершает работу. Текст опции обновлён в локализации: [`ru.json`](Configuration Management/Localization/Languages/ru.json) — «Закрыть (увести в трей)» → «Закрыть программу», [`en.json`](Configuration Management/Localization/Languages/en.json) — «Close (hide to tray)» → «Close the program».
- **Версия обновлена до 0.3.3.29** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.29 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.29.

## [0.3.3.28] — 2026-08-23

### Исправлено

- **Добавлены недостающие ключи локализации настройки «После запуска базы или конфигуратора»** в разделе «Поведение приложения» окна настроек ([`SettingsWindow.xaml`](Configuration Management/SettingsWindow.xaml), [`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs)). Ключи `Settings.General.AfterLaunchAction`, `Settings.General.AfterLaunchAction.None`, `Settings.General.AfterLaunchAction.MinimizeToTray` и `Settings.General.AfterLaunchAction.Close` были объявлены ещё в версии 0.3.3.26, но фактически отсутствовали в файлах локализации. Из-за механизма отката [`LocalizationManager.Translate()`](Configuration Management/Localization/LocalizationManager.cs) (текущий язык → английский → русский → сам ключ) в комбобоксе отображался сырой технический ключ (`Settings.General.AfterLaunchAction.MinimizeToTray` и т.п.) вместо понятной подписи «что делать с окном после запуска», что выглядело как нелокализованный англоязычный текст. Ключи добавлены в [`ru.json`](Configuration Management/Localization/Languages/ru.json) («Ничего», «Свернуть в трей», «Закрыть программу») и [`en.json`](Configuration Management/Localization/Languages/en.json) («Do nothing», «Minimize to tray», «Close the program»).
- **Версия обновлена до 0.3.3.28** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.28 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.28.

## [0.3.3.27] — 2026-08-23

### Слияние исправлений от ksv47

- **Влит PR #54 «Linux/Avalonia: доведение до сборки, запуска и работы с платформой 1С»** (ветка `ksv47/linux-fixes`). Все изменения этого PR **выполнены автором ksv47**. Это крупный объём работ по доведению Linux/Avalonia-версии приложения до полноценной сборки, запуска и работы с платформой 1С:Предприятие. По функциональным направлениям:
  - **Запуск и работа с платформой 1С**: поиск платформы на штатной раскладке установки 1С, корректные пути к данным 1С и каталогам (`binDir`, корень кеша), читаемость тёмной темы, определение окружения рабочего стола (Linux), корректная работа файловых менеджеров.
  - **Список баз как таблица с колонками**: сортировка кликом по заголовкам, изменение ширины колонок перетаскиванием разделителя, панель инструментов над списком.
  - **Теги и дерево**: панель отбора по тегам, теги в строке базы и переключатель их показа, состояние дерева, сортировка групп и порядок узлов, сохранение свёрнутости только для групп и служебных узлов.
  - **Сессия, контекстное меню и действия**: блок текущей сессии и полные сведения о подключении, контекстное меню строки, горячие клавиши и их переназначение, удаление базы и группы, переход по ссылке.
  - **Настройки**: вкладки «Отображение», «Платформы», «Базы» и автосинхронизация с `ibases.v8i`, редактор цветовых схем.
  - **Качество**: устранены утечки подписок содержимого, двойная пересборка, синхронные диалоги больше не вешают интерфейс, корректный выход на старте.
  - Детальные описания конкретных правок Linux/Avalonia-версии уже зафиксированы в записях **0.3.3.12–0.3.3.24** ниже; настоящая запись оформляет само слияние PR #54 и фиксирует авторство.
- **Авторство изменений:** все правки, вошедшие в этот merge, выполнены **ksv47** ([`PR #54`](https://github.com/ksv47)) — см. также раздел «Благодарности» в [`README.md`](README.md).
- **Версия обновлена до 0.3.3.27** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.27 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.27.

## [0.3.3.26] — 2026-08-23

### Добавлено

- **Глобальная настройка «После запуска базы или конфигуратора»** (обе платформы) — определяет, что делать с окном приложения сразу после успешного запуска информационной базы или конфигуратора 1С: **«Ничего»** (по умолчанию, окно остаётся как было), **«Свернуть в трей»** или **«Закрыть (уйти в трей)»**. Настройка расположена в **Настройки → Настройки** и хранится в [`AppSettings`](Configuration Management/Models/AppSettings.cs) (`AfterLaunchAction`, новые ключи локализации `Settings.General.AfterLaunchAction*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json)). Реализовано для Windows (WPF) через событие `AfterLaunchRequested` ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs), [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs), [`SettingsWindow.xaml`](Configuration Management/SettingsWindow.xaml)) и для Linux (Avalonia) ([`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs), [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs), [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs)). Уведомление выполняется после успешного запуска из главного окна, по горячей клавише избранного и из меню трея; «Свернуть в трей» сворачивает окно, «Закрыть» уводит его в трей, не завершая приложение.
- **Версия обновлена до 0.3.3.26** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.26 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.26.

## [0.3.3.25] — 2026-08-22

### Изменено

- **Исправлено сброс цветового оформления на дефолтные при переключении светлой/тёмной темы** — выбранная пользовательская цветовая схема больше не затирается встроенной ([`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs), [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs), [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs), [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs)). Ранее переключение темы (кнопка на верхней панели и радиокнопки Светлая/Тёмная в настройках) применяло встроенную схему по умолчанию, полностью теряя кастомизированные цвета; в WPF-версии это дополнительно безвозвратно заменяло сохранённую активную схему. Теперь переключается только базовый вариант (светлый/тёмный): если для целевой темы есть сохранённая схема — применяется она, иначе применяются встроенные цвета, а сохранённая схема остаётся нетронутой и восстанавливается при возврате к её базовой теме.
- **Версия обновлена до 0.3.3.25** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.25 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.25.

## [0.3.3.24] — 2026-08-22

### Изменено

- **Гарантировано выровнена высота кнопки «Очистить кеш» по кнопкам панели действий в WPF-версии** ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml), [`DarkTheme.xaml`](Configuration Management/Themes/DarkTheme.xaml), [`LightTheme.xaml`](Configuration Management/Themes/LightTheme.xaml)). Высота split-блока очистки кеша и соседних кнопок («Закрепить», «Удалить», «Перейти по ссылке» и др.) задаётся явным общим `MinHeight`: 40px в развёрнутом режиме правой панели и 32px в компактном (`ShowRightPanelDetails = False`). `MinHeight` добавлен в общий стиль `ActionPanelSecondaryButton` (обе темы) и во внешний Border split-блока.
- **Исправлено поведение кнопки «Очистить кеш» при сворачивании/разворачивании правой панели** ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml)). У внешнего Border split-блока был локальный атрибут `Margin="8,0,8,8"`, который из-за приоритета свойств WPF перекрывал триггер стиля, задающий отступ в компактном режиме (`0,0,0,4`). Из-за этого при сворачивании панели блок «Очистить кеш» не менял отступ (оставался `8,0,8,8`) и выглядел уже/смещённым относительно соседних кнопок, а после повторного разворачивания состояние могло не восстанавливаться корректно. Локальный `Margin` убран — отступ теперь полностью управляется через `Border.Style` (включая триггер компактного режима), как у остальных кнопок панели. Теперь размер и отступы кнопки «Очистить кеш» идентичны «Закрепить»/«Удалить» и корректно переключаются между развёрнутым и компактным режимами.
- **Версия обновлена до 0.3.3.24** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.24 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.24.

## [0.3.3.23] — 2026-08-22

### Изменено

- **Кнопка «Очистить кеш» приведена к виду двойной split-кнопки по аналогии с кнопкой «Запустить 1С:Предприятие»** в WPF-версии ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml)). Ранее у кнопки очистки кеша правая панель была единым элементом, открывавшим только выпадающее меню. Теперь основная часть кнопки открывает окно очистки кеша (`ClearCacheCommand` → [`CacheCleanWindow`](Configuration Management/CacheCleanWindow.xaml)), а отдельная правая стрелка «▾» открывает выпадающее меню со следующими пунктами:
  - «Программный кеш…» → `ClearProgramCacheCommand`;
  - «Пользовательский кеш…» → `ClearUserCacheCommand`;
  - разделитель;
  - «Программный и пользовательский» → `ClearCacheBothCommand`.
  Разделитель между основной частью и стрелкой визуально объединяет блок в цельный контрол. По цвету фона (`SecondaryButtonBackgroundBrush`), hover/pressed (`SecondaryButtonHoverBrush`/`SecondaryButtonPressedBrush`), высоте, ширине и отступам блок полностью соответствует соседним кнопкам «Закрепить»/«Удалить» как в развёрнутом, так и в компактном режиме правой панели: без `MinHeight`, внутренний отступ `14,11` (в компактном — `6,8`), внешний отступ `8,0,8,8` (в компактном — `0,0,0,4`). Текст и иконка используют `ButtonTextBrush` на фоне вторичной кнопки, поэтому читаются и в светлой, и в тёмной темах. Это приводит WPF-интерфейс к поведению, уже реализованному в Linux/Avalonia-версии (см. 0.3.3.22).
- **Кнопка очистки кеша доступна даже при выбранной группе** ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs)). У команд `ClearCacheCommand`, `ClearProgramCacheCommand`, `ClearUserCacheCommand` и `ClearCacheBothCommand` убрано ограничение `CanExecute` на наличие выбранной базы — теперь они доступны всегда. При открытии окна [`OpenCacheClean`](Configuration Management/ViewModels/MainViewModel.cs) передаёт `SelectedInfobase` как `defaultSelected`: когда выбрана группа (база = null), окно очистки открывается без выделенной базы в списке; при выбранной базе она выделяется по умолчанию.
- **Версия обновлена до 0.3.3.23** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.23 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.23.

## [0.3.3.22] — 2026-08-22

### Изменено

- **Кнопка «Очистить кеш» приведена к виду двойной split-кнопки по аналогии с кнопкой «Запустить 1С:Предприятие»** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). Ранее у правой панели была только стрелка «▾» для выпадающего меню очистки кеша, а быстрая очистка выполнялась основной частью кнопки. Теперь основная часть кнопки открывает окно очистки кеша ([`CacheCleanWindow`](Configuration Management/CacheCleanWindow.Avalonia.cs)), а правая стрелка открывает выпадающее меню со следующими пунктами:
  - «Открыть окно очистки кеша…» → `ClearCacheCommand`;
  - «Программный кеш…» → `ClearProgramCacheCommand`;
  - «Пользовательский кеш…» → `ClearUserCacheCommand`;
  - разделитель;
  - «Программный и пользовательский» → `ClearCacheBothCommand` (новый пункт).
- **Кнопка очистки кеша доступна даже при выбранной группе** ([`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)). У команд `ClearCacheCommand`, `ClearProgramCacheCommand`, `ClearUserCacheCommand` и новой `ClearCacheBothCommand` убрано ограничение `CanExecute` на наличие выбранной базы — теперь они доступны всегда. При открытии окна [`OpenCacheClean`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs) передаёт `SelectedInfobase` как `defaultSelected`: когда выбрана группа (база = null), окно очистки открывается без выделенной базы в списке; при выбранной базе она выделяется в окне по умолчанию. Новая команда `ClearCacheBothCommand` добавлена и в метод `RaiseCommandCanExecuteChanged()`.
- **Версия обновлена до 0.3.3.22** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.22 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.22.

## [0.3.3.21] — 2026-08-21

### Изменено

- **Имя каталога рабочего стола Linux вынесено в локализацию** ([`InfobaseMaintenanceService.Linux.cs`](Configuration Management/Services/InfobaseMaintenanceService.Linux.cs)). В `GetDesktopDirectory` fallback-путь `~/Рабочий стол` заменён на локализованный ключ `Common.DesktopFolder` через `LocalizationManager.T(...)`. Основным способом определения каталога по-прежнему остаётся системный API `xdg-user-dir DESKTOP`, далее проверяется кандидат `~/Desktop`, и лишь затем локализованное имя папки — это корректно покрывает как русифицированные системы («Рабочий стол»), так и английские («Desktop») в зависимости от текущего языка интерфейса.
- **Версия обновлена до 0.3.3.21** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.21 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.21.

## [0.3.3.20] — 2026-08-21

### Изменено

- **Суффиксы имён шаблонов («демо»/«пустая») вынесены в локализацию** ([`CreateInfobaseWindow.xaml.cs`](Configuration Management/CreateInfobaseWindow.xaml.cs)). При подборе имени базы из шаблона (`SuggestNameFromTemplate`) удаление жёстко закодированных суффиксов `« (демо)»`/`« (demo)»` и `« (пустая)»` заменено на существующие локализованные ключи `Template.SuffixDemo` и `Template.SuffixEmpty` через `LocalizationManager.T(...)` — это корректно покрывает как русские, так и английские формулировки в зависимости от текущего языка интерфейса. Новые ключи не добавлялись: существующие полностью подходят по формату (с ведущим пробелом).
- **Версия обновлена до 0.3.3.20** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.20 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.20.

## [0.3.3.19] — 2026-08-21

### Изменено

- **Полная локализация слоя отображения значений режима запуска/типа клиента, формируемых импортёром `ibases.v8i`** ([`IbasesV8iImporter.cs`](Configuration Management/Services/IbasesV8iImporter.cs)). Импортёр `MapLaunchMode` возвращает только 4 канонических значения — «Автоматический», «Тонкий клиент», «Толстый клиент», «Веб-клиент» — и каждое из них уже полностью покрывается регистронезависимым маппингом в `ParsedLaunchMode` ([`Infobase.cs`](Configuration Management/Models/Infobase.cs:404)) через ключи `Connection.Launch*`. Тип клиента импортёром не задаётся (поле `ClientType` не заполняется), поэтому дополнительных правок `ClientTypeDisplay` не требуется. Дублирование логики между ветками `App` и `DefaultApp` в `MapLaunchMode` устранено выносом общего сопоставления в хелпер `MapSingleLaunchMode`; канонические хранимые литералы сохранены без изменений (сравнения в `OneCLauncher`/`MainViewModel`/`ConnectionSettingsViewModel` не затрагиваются).
- **Версия обновлена до 0.3.3.19** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.19 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.19.

## [0.3.3.18] — 2026-08-21

### Изменено

- **Локализован слой отображения модели `Infobase`** ([`Infobase.cs`](Configuration Management/Models/Infobase.cs)). Канонические (хранимые) значения режима запуска и типа клиента не изменяются, но при отображении маппятся на локализованный вывод: `ParsedLaunchMode` — регистронезависимое сравнение (`ToLowerInvariant`) с покрытием всех канонических формулировок («Автоматический», «Тонкий клиент», «Толстый клиент», «Толстый клиент (обычные формы)», «Веб-клиент» и их вариации, включая «…(управляемое приложение)») через ключи `Connection.Launch*`; `ClientTypeDisplay` — регистронезависимый маппинг «Тонкий»/«Толстый» на `Main.SessionClientThin`/`Main.SessionClientThickManaged`. Fallback имени базы в `Initials` («1С») заменён на локализованный `LocalizationManager.T("Model.DefaultBaseName")`; новый ключ `Model.DefaultBaseName` добавлен в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Версия обновлена до 0.3.3.18** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.18 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.18.

## [0.3.3.17] — 2026-08-21

### Изменено

- **Имена встроенных тем оформления вынесены в локализацию (слой отображения)** ([`ColorScheme.cs`](Configuration Management/Models/ColorScheme.cs), [`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs)). Канонические (хранимые) имена встроенных тем `«Светлая»`/`«Тёмная»` сохранены как стабильные ключи для сохранения/загрузки настроек и сопоставления с ресурсами темы (`LightTheme`/`DarkTheme`), а при отображении в списке тем настроек выводится локализованная подпись через `LocalizationManager.T("Theme.Light")` / `T("Theme.Dark")` (`ru.json`/`en.json`). При смене языка комбо тем перерисовывает локализованные подписи, сохранённое имя темы не меняется.
- **Версия обновлена до 0.3.3.17** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.17 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.17.

## [0.3.3.16] — 2026-08-21

### Изменено

- **Исправлен устаревший текст версии на вкладке «О программе»** ([`ru.json`](Configuration Management/Localization/Languages/ru.json), [`en.json`](Configuration Management/Localization/Languages/en.json)). В значении ключа `Settings.About.HelpText` была жёстко прописана устаревшая версия «0.2.7.24» (как в русском, так и в английском языковом файле), из-за чего на вкладке «О программе» отображался неактуальный номер сборки. Текст обновлён до текущей версии `0.3.3.16`.
- **Версия обновлена до 0.3.3.16** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.16 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.16.

## [0.3.3.15] — 2026-08-21

### Добавлено

- **Добавлены локализованные подсказки (ToolTip) кнопкам правой панели в Avalonia-версии** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). Ранее у кнопок правой панели (кроме стрелки split-кнопки «Очистка кеша») всплывающих подсказок не было вовсе. В методы `PrimaryActionButton(...)` и `SecondaryActionButton(...)` добавлен параметр подсказки, и для всех кнопок секций «Конфигуратор», «Обслуживание», «Список баз», «Отметки», а также для primary-кнопки «Запустить 1С:Предприятие» и кнопки «Выход» задан локализованный `ToolTip`. Переиспользованы существующие ключи `Main.EditBaseTooltip`, `Main.NativeStarterTooltip`, `Main.AddBaseOrGroupTooltip`, `Main.DeleteTooltip`, `Main.ToggleFavoriteTooltip`, `Main.PinBaseTooltip`, `Main.ExitTooltip`, `Main.ClearCacheTooltip` (у основной части split-кнопки). Добавлены новые ключи `Main.LaunchEnterpriseTooltip`, `Main.LaunchConfiguratorSectionTooltip`, `Main.OpenFolderTooltip`, `Main.DesktopShortcutTooltip` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Версия обновлена до 0.3.3.15** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.15 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.15.

## [0.3.3.14] — 2026-08-21

### Добавлено

- **Добавлен недостающий ключ локализации для кнопки «Удалить» в правой панели** (`Main.Delete`). В [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs) (кнопка `SecondaryActionButton("IconDelete", LocalizationManager.T("Main.Delete"), ...)`) и в [`MainWindow.xaml`](Configuration Management/MainWindow.xaml) (`Header="{loc:Loc Main.Delete}"`) использовался ключ `Main.Delete`, который **отсутствовал** в обоих файлах переводов. Из-за механизма отката (английский → русский → сам ключ) при выборе английского языка на кнопке отображался сырой ключ «Main.Delete», а не переведённый текст. Ключ добавлен в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`"Удалить"`) и [`en.json`](Configuration Management/Localization/Languages/en.json) (`"Delete"`). Подсказка кнопки использует существующий `Main.DeleteTooltip` и не изменялась. Проверка ключей показала, что все остальные колонки списка (`Column.*`), кнопки правой панели (`Main.*`) и подсказки (`*.Tooltip`) уже вынесены и переведены в обоих языковых файлах.
- **Версия обновлена до 0.3.3.14** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.14 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.14.

## [0.3.3.13] — 2026-08-21

### Изменено

- **Локализован жёстко прописанный литерал «Нет» при чтении назначенных горячих клавиш** ([`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs)). В методе `ReadHotkeyBox(...)` сравнение прочитанного значения с русским литералом `s == "Нет"` заменено на сравнение с локализованным ключом `LocalizationManager.T("Common.None")` через `string.Equals(..., StringComparison.Ordinal)`. Ранее, если интерфейс был на английском (где поле показывает «None»), защитная проверка могла не распознать пустое назначение как «нет горячей клавиши». Теперь чтение не зависит от языка интерфейса. Логика остальных методов (`HotkeyBox.cs`, `HotkeyBox.Avalonia.cs`) не изменялась — они уже используют `LocalizationManager.T("Common.None")`.
- **Версия обновлена до 0.3.3.13** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.13 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.13.

## [0.3.3.12] — 2026-08-21

### Изменено

- **Исправлена обрезка нижней части окна очистки кеша при малой высоте окна** ([`CacheCleanWindow.Avalonia.cs`](Configuration Management/CacheCleanWindow.Avalonia.cs)). Корневой `Grid` окна обёрнут во внешний вертикальный `ScrollViewer`, а списку баз задана фиксированная высота (`Height`/`MinHeight`) вместо звездной (`Star`) строки. Теперь при любом размере окна все элементы доступны без обрезки снизу: внутренний `ScrollViewer` списка баз сохраняет работоспособность, а при маленькой высоте появляется внешняя прокрутка всего содержимого. Увеличена `MinHeight` окна. Логика окна (закреплённая шапка, изменение ширины колонок, размеры кеша) не изменялась.

### Добавлено

- **Split-кнопка «Очистка кеша» в правой панели** ([`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs)). Основная часть кнопки выполняет **быструю очистку всего кеша** (программного и пользовательского) выбранной базы с предупреждением, а правая стрелка «▾» открывает выпадающее меню: «Открыть окно очистки кеша…», «Программный кеш…», «Пользовательский кеш…». Кнопка стилизована под secondary-кнопки темы (`PanelButton` получил поддержку радиуса скругления для сборки единого контрола).
- **Подключена очистка кеша в Avalonia-версии** ([`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)). Добавлены команды `QuickClearCacheCommand` (быстрая очистка всего кеша с подтверждением через `IDialogService.Confirm`), `ClearCacheCommand` / `ClearProgramCacheCommand` / `ClearUserCacheCommand` (открывают окно `CacheCleanWindow` с соответствующим типом по умолчанию через `ShowSync()`). Реализованы методы `OpenCacheClean(...)` и `CacheKindLabel(...)` по образцу WPF-версии; результат и сообщения локализованы ключами `Main.Cache*`. Диалог `CacheCleanWindow` получил публичный метод `ShowSync()` для модального показа из ViewModel.
- **Новые ключи локализации** `Main.CacheClearAllConfirm` и `Main.CacheCleanOpenDialog` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json). Пункты меню переиспользуют существующие `Main.ClearProgramCache` / `Main.ClearUserCache`, заголовок кнопки — `Main.ClearCache`.
- **Версия обновлена до 0.3.3.12** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.12 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.12.

## [0.3.3.11] — 2026-08-21

### Добавлено

- **Очистка «остатков» кеша от удалённых информационных баз** ([`OneCCacheCleaner.cs`](Configuration Management/Services/OneCCacheCleaner.cs)). В окно очистки кеша добавлен чекбокс «Остатки от удалённых баз» с индикатором занимаемого размера — он показывает каталоги кеша в `%LOCALAPPDATA%\1C\1cv8…` и `%APPDATA%\1C\1cv8…`, которые не соответствуют ни одной текущей информационной базе (например, остались после удаления базы из списка или переноса её в другое приложение). При включении опции эти каталоги удаляются вместе с кешем выбранных баз. Реализация: сервис получил методы `GetOrphanSize(...)` и `ClearOrphans(...)`, которые строят множество «защищённых» имён каталогов (ID баз и имена каталогов кеша всех текущих баз) и удаляют все прочие каталоги-кеши как по каталогам версий платформы (`8.3.24.1234`), так и напрямую в корне кеша; сами каталоги версий платформы не удаляются. Окно поддерживает очистку остатков даже без выбранных баз. Обновлены [`CacheCleanWindow.xaml`](Configuration Management/CacheCleanWindow.xaml) / [`CacheCleanWindow.xaml.cs`](Configuration Management/CacheCleanWindow.xaml.cs) (WPF) и [`CacheCleanWindow.Avalonia.cs`](Configuration Management/CacheCleanWindow.Avalonia.cs) (Linux), обработка подтверждения и сообщений — в [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs). Добавлены ключи локализации `CacheClean.OrphanCache` / `CacheClean.OrphanCacheTooltip` и `Main.CacheOrphanNote` / `Main.CacheOrphanRemoved` / `Main.CacheOrphanNone` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Версия обновлена до 0.3.3.11** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.11 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.11.

## [0.3.3.10] — 2026-08-21

### Изменено

- **Локализованы фильтры разрядности «x32» и «x64» окна выбора версии платформы** ([`PlatformVersionPickerWindow.xaml`](Configuration Management/PlatformVersionPickerWindow.xaml), [`PlatformVersionPickerWindow.Avalonia.cs`](Configuration Management/PlatformVersionPickerWindow.Avalonia.cs)). В WPF-версии литералы `Content="x32"` / `Content="x64"` радиокнопок `FilterX32`/`FilterX64` заменены на привязку `{loc:Loc PlatformVersionPicker.FilterX32}` / `PlatformVersionPicker.FilterX64`. В Avalonia-версии подписи кнопок подставляются через `LocalizationManager.T(...)`. Логика фильтрации (`OnArchFilter_Changed`) не изменялась — она опирается на состояние `IsChecked` кнопок и внутренние значения `_archFilter = "x32"/"x64"`, а не на текст `Content`, поэтому перевод безопасен. Добавлены ключи `PlatformVersionPicker.FilterX32` / `PlatformVersionPicker.FilterX64` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`x32` / `x64`) и их английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json) (`x32` / `x64`).
- **Версия обновлена до 0.3.3.10** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.10 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.10.

## [0.3.3.9] — 2026-08-21

### Изменено

- **Локализованы метки окна выбора цвета «HEX:», «R», «G», «B» и кнопка «OK»** ([`ColorPickerWindow.xaml`](Configuration Management/ColorPickerWindow.xaml), [`ColorPickerWindow.Avalonia.cs`](Configuration Management/ColorPickerWindow.Avalonia.cs)). В WPF-версии литералы `Text="HEX:"`, `Text="R"`, `Text="G"`, `Text="B"` заменены на привязку `{loc:Loc ColorPicker.HexLabel}` / `ColorPicker.ChannelRed` / `ColorPicker.ChannelGreen` / `ColorPicker.ChannelBlue`. В Avalonia-версии подписи подставляются через `LocalizationManager.T(...)` в `BuildRgbRow(...)`, а литерал «OK» кнопки подтверждения (`BuildButtons("OK", ...)`) заменён на локализованный `Common.Ok` для согласованности с WPF (там уже использовался `{loc:Loc Common.Ok}`). Добавлены ключи `ColorPicker.HexLabel` / `ColorPicker.ChannelRed` / `ColorPicker.ChannelGreen` / `ColorPicker.ChannelBlue` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`HEX:`, `R`, `G`, `B`) и их английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json) (`HEX:`, `R`, `G`, `B`).
- **Версия обновлена до 0.3.3.9** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.9 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.9.

## [0.3.3.8] — 2026-08-21

### Изменено

- **Локализованы точки прямого отображения режима запуска и типа клиента** ([`Infobase.cs`](Configuration Management/Models/Infobase.cs)). Канонические строки-идентификаторы `LaunchMode` (`"Автоматический"`, `"Тонкий клиент"`, `"Толстый клиент"`, `"Толстый клиент (обычные формы)"`, `"Веб-клиент"`) и `ClientType` (`"Тонкий"`, `"Толстый"`) не изменялись — они по-прежнему персистятся на диск и сравниваются через `StringComparison.OrdinalIgnoreCase` в [`OneCLauncher.cs`](Configuration Management/Services/OneCLauncher.cs), [`OneCLauncher.Linux.cs`](Configuration Management/Services/OneCLauncher.Linux.cs), [`IbasesV8iImporter.cs`](Configuration Management/Services/IbasesV8iImporter.cs), [`ConnectionSettingsViewModel.cs`](Configuration Management/ViewModels/ConnectionSettingsViewModel.cs). Локализация перенесена в точки отображения: свойство `ParsedLaunchMode` теперь отображает значения через ключи `Connection.LaunchAuto`/`LaunchThin`/`LaunchThickManaged`/`LaunchThickOrdinary`/`LaunchWeb`, а для типа клиента добавлено локализованное свойство `ClientTypeDisplay` (ключи `Main.SessionClientThin`/`Main.SessionClientThickManaged`), используемое в строке состояния ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs)) и в карточке базы ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml)). Неизвестные значения выводятся как есть (fallback) — обратная совместимость сохранена.
- **Версия обновлена до 0.3.3.8** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.8 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.8.

## [0.3.3.7] — 2026-08-21

### Изменено

- **Локализованы имена встроенных тем оформления «Светлая» и «Тёмная»** ([`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs), [`ColorScheme.cs`](Configuration Management/Models/ColorScheme.cs)). Канонические идентификаторы `"Светлая"`/`"Тёмная"` (константы `BuiltInLightName`/`BuiltInDarkName` и значения, возвращаемые `ColorScheme.CreateLight()`/`CreateDark()`) не изменялись — они по-прежнему пишутся на диск как идентификаторы темы и используются для сравнения при загрузке. Перевод применяется только при построении списка тем в выпадающем списке окна настроек: добавлена локализованная обёртка `LocalizedBuiltInName(...)`, которая отображает `Theme.Light`/`Theme.Dark` для встроенных тем, а кастомные пользовательские темы оставляет без перевода. Добавлены ключи `Theme.Light` / `Theme.Dark` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`"Светлая"` / `"Тёмная"`) и их английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json) (`"Light"` / `"Dark"`).
- **Версия обновлена до 0.3.3.7** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.7 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.7.

## [0.3.3.6] — 2026-08-21

### Изменено

- **Локализованы суффиксы шаблонов конфигураций «(демо)» и «(пустая)»** ([`OneCTemplateService.cs`](Configuration Management/Services/OneCTemplateService.cs), [`CreateInfobaseWindow.xaml.cs`](Configuration Management/CreateInfobaseWindow.xaml.cs)). В методе построения catalog-подобного дерева шаблонов литералы `leaf += " (демо)"` / `leaf += " (пустая)"` заменены на `LocalizationManager.T("Template.SuffixDemo")` / `LocalizationManager.T("Template.SuffixEmpty")`. Логика детекции «demo/демо», «empty/пуст» по именам каталогов/файлов (внутренние ключевые слова) не изменялась. В `SuggestNameFromTemplate` сохранено отсечение жёстко прописанных вариантов `" (демо)"`, `" (demo)"`, `" (пустая)"` и добавлено отсечение локализованных суффиксов текущего языка через `LocalizationManager.T("Template.SuffixDemo")` / `LocalizationManager.T("Template.SuffixEmpty")`. Добавлены ключи `Template.SuffixDemo` / `Template.SuffixEmpty` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`" (демо)"` / `" (пустая)"`) и их английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json) (`" (demo)"` / `" (empty)"`).
- **Версия обновлена до 0.3.3.6** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.6 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.6.

## [0.3.3.5] — 2026-08-21

### Изменено

- **Локализовано запасное имя ярлыка информационной базы** ([`InfobaseMaintenanceService.cs`](Configuration Management/Services/InfobaseMaintenanceService.cs), [`InfobaseMaintenanceService.Linux.cs`](Configuration Management/Services/InfobaseMaintenanceService.Linux.cs)). Русские литералы «База 1С» и «База_1С», использовавшиеся как запасное имя базы при создании ярлыка `.lnk`/`.desktop`, заменены на локализованный ключ `Maint.DefaultBaseName` через `LocalizationManager.T(...)`. В Windows-версии замена с санитизацией выполнена через `string.Join("_", LocalizationManager.T("Maint.DefaultBaseName").Split(Path.GetInvalidFileNameChars()))`, в Linux-версии — через существующий метод `SanitizeFileName(...)`. Добавлен ключ `Maint.DefaultBaseName` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`База 1С`) и его английский перевод в [`en.json`](Configuration Management/Localization/Languages/en.json) (`1C base`). Строка-путь `desktop = Path.Combine(home, "Рабочий стол")` в Linux-версии не затрагивалась — это детекция имени папки рабочего стола на диске, а не UI-строка.
- **Версия обновлена до 0.3.3.5** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.5 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)).

## [0.3.3.4] — 2026-08-21

### Изменено

- **Локализованы оставшиеся русские литералы в сообщениях Linux-лаунчера** ([`OneCLauncher.Linux.cs`](Configuration Management/Services/OneCLauncher.Linux.cs)). В методе `Launch(...)` при ненайденной платформе подпись разрядности (`64-бит`/`32-бит`) теперь берётся из ключей `Launcher.Bit64`/`Launcher.Bit32`, подсказка о версии платформы — из `Launcher.PlatformVersionHint`, а строка «Запрошена версия: {0}» — из `Launcher.RequestedVersionFormat`. Лог-предупреждение собрано из локализованного заголовка `Launcher.PlatformNotFoundTitle` с аргументами разрядности и подсказки. Сообщение об ошибке запуска платформы переведено на ключ `Launcher.LaunchFailedFormat` (`string.Format(LocalizationManager.T(...), ex.Message)`). В `RunDesignerBatch` сообщение «Не удалось запустить операцию» заменено на ключ `Launcher.OperationStartFailedFormat` с подстановкой текста исключения, пути к исполняемому файлу и командной строки. Внутренние идентификаторы данных (режимы запуска «Автоматический», «Тонкий клиент», «Толстый клиент», «Веб-клиент» и т.п.) не изменялись.
- **Версия обновлена до 0.3.3.4** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.4 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.4.

## [0.3.3.3] — 2026-08-21

### Изменено

- **Убран русский литерал «ОК» из Avalonia-базы модальных окон** ([`ModalWindowBase.cs`](Configuration Management/ModalWindowBase.cs)). Параметр `okText` метода [`BuildButtons()`](Configuration Management/ModalWindowBase.cs) переведён на `string? okText = null`: пустое/null значение интерпретируется как локализованный ключ `Common.Ok` через `ResolveOkText` (`string.IsNullOrEmpty(okText) ? LocalizationManager.T("Common.Ok") : okText`). Литерал по умолчанию для поля `_lastOkRaw` заменён на пустую строку (резолвится в `Common.Ok`). В [`TagInputWindow.Avalonia.cs`](Configuration Management/TagInputWindow.Avalonia.cs) вызов `BuildButtons("ОК", ...)` заменён на `BuildButtons(null, ...)`, чтобы использовалась локализованная кнопка по умолчанию. Кастомные подписи (например, `Common.Save` в `GroupEditWindow`, переменная `okText` в `NameInputWindow`, «OK» в `ColorPickerWindow`) выводятся без изменений — резолвится только пустое значение. Ключи перевода не добавлялись: используется существующий `Common.Ok` из [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Версия обновлена до 0.3.3.3** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.3 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.3.

## [0.3.3.2] — 2026-08-21

### Изменено

- **Локализованы детали истории запусков** ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs)). Строка деталей `клиент={_sessionClientMode}, арх={_sessionArchitecture}`, которая сохранялась в `LaunchHistoryEntry` и отображалась в окне «История запусков» (`Main.LaunchHistoryFormat`), теперь формируется через `string.Format(LocalizationManager.T("Main.LaunchHistorySessionDetails"), ...)`. Добавлен новый ключ `Main.LaunchHistorySessionDetails` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) (`клиент={0}, арх={1}`) и его английский перевод в [`en.json`](Configuration Management/Localization/Languages/en.json) (`client={0}, arch={1}`). Внутренние идентификаторы режима запуска (`kind`) и типов клиентов/архитектуры (`_sessionClientMode`, `_sessionArchitecture`) не изменялись. В Avalonia-версии аналогичной проблемы нет — там `AddLaunchHistory` уже вызывается с локализованным ключом без деталей.
- **Версия обновлена до 0.3.3.2** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.2 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.2.

## [0.3.3.1] — 2026-08-21

### Изменено

- **Локализованы специальные узлы дерева групп** — «Закреплённые», «Все базы» и «Без группы» ([`GroupNodeViewModel.cs`](Configuration Management/ViewModels/GroupNodeViewModel.cs), [`GroupNodeViewModel.Avalonia.cs`](Configuration Management/ViewModels/GroupNodeViewModel.Avalonia.cs), [`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs), [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs)). Ранее это были служебные узлы без модели `Group`, заголовки которых задавались жёстко прописанными русскими литералами и не переводились при смене языка интерфейса.
  - Введено разделение **внутреннего маркера** и **отображаемого имени**: у спец-узла хранится маркер (`Pinned` / `AllBases` / `NoGroup`, константы `PinnedMarker` / `AllBasesMarker` / `NoGroupMarker`), а свойство `DisplayName` для таких узлов возвращает локализованный текст через `LocalizationManager.T(...)` (ключи `Main.Pinned`, `Main.FlatAllBases`, `Group.NoGroup`). Текст обновляется автоматически при смене языка (геттер вычисляется на лету).
  - Вся логика сравнения с литералами «Закреплённые»/«Без группы»/«Все базы» заменена на сравнение по маркеру: `IsNoGroupNodeSelected()`, поиск узла «Закреплённые» в `UpdatePinnedSection`, обработка перетаскивания базы на «Закреплённые» в `MainWindow.xaml.cs`, выбор иконки спец-узла в `GroupNodeViewModel` (обе версии). Узел остаётся опознаваемым независимо от языка.
  - Для устойчивости сохранённого состояния развёрнутости добавлено свойство `NodeKey` (для реальных групп — полный путь, для спец-узлов — внутренний маркер); ключи `ToggleGroupExpanded`, `CollectGroupPaths` и `ApplyExpandedState` переведены на него, чтобы они не зависели от языка.
  - Ключи перевода уже существовали в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json) — новые ключи не добавлялись.
- **Версия обновлена до 0.3.3.1** (`Version`/`AssemblyVersion`/`FileVersion` = 0.3.3, `InformationalVersion` = 0.3.3.1 в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.3.1.

## [0.3.2.10] — 2026-08-21

### Добавлено

- **Завершено вынесение в настройки перевода (локализацию) всех оставшихся жёстко прописанных русских строк, отображаемых пользователю**. Все строки переведены на английский в [`en.json`](Configuration Management/Localization/Languages/en.json); ключи добавлены в [`ru.json`](Configuration Management/Localization/Languages/ru.json). Для формат-строк используется `string.Format(LocalizationManager.T("Key"), args...)`. Логи (`_logger.*`), `Debug.WriteLine`, внутренние технические маркеры сравнений и имена файлов/пути ОС не затрагивались.
  - **Главная ViewModel (WPF)** ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs)): тексты и заголовки диалогов (`_dialogs.ShowInfo/ShowWarning/ShowError/Confirm`), подписи кнопок, тултипы, статусы, заголовки и фильтры файловых диалогов, сообщения импорта/экспорта/синхронизации ibases.v8i, очистки кеша, ярлыков, информации о конфигурации, регистрации COM-коннектора, выгрузки .dt/.cf, тестирования ИБ и истории запусков. Ключи `Main.*` (~127). Внутренние константы сравнений («Закреплённые», «Все базы», «Без группы») не изменялись.
  - **Лаунчер 1С (WPF)** ([`OneCLauncher.cs`](Configuration Management/Services/OneCLauncher.cs)): тексты и заголовки `System.Windows.MessageBox.Show`, подписи пакетных операций конфигуратора (`OperationLabel` / `DesignerBatchOperation`), ошибки создания базы и запуска по ссылке. Ключи `Launcher.*` (43).
  - **Лаунчер 1С (Linux)** ([`OneCLauncher.Linux.cs`](Configuration Management/Services/OneCLauncher.Linux.cs)): подписи пакетных операций, отчёт о неуспехе операции, причины блокировки, ошибки `CREATEINFOBASE`. Переиспользованы существующие ключи `Launcher.*`.
  - **COM-коннектор** ([`OneCComConnector.cs`](Configuration Management/Services/OneCComConnector.cs), [`OneCComConnector.Linux.cs`](Configuration Management/Services/OneCComConnector.Linux.cs), [`OneCComConnectorRegistrar.cs`](Configuration Management/Services/OneCComConnectorRegistrar.cs)): `LastError`, тексты `DescribeProgIdStatus()`, ошибки `regsvr32`, примечания регистрации. Ключи `Com.*` / `ComReg.*` (32).
  - **Подписи шрифтов, настройки подключения, обслуживание баз** ([`ThemeManager.cs`](Configuration Management/Themes/ThemeManager.cs) / [`ThemeManager.Avalonia.cs`](Configuration Management/Themes/ThemeManager.Avalonia.cs), [`ConnectionSettingsViewModel.cs`](Configuration Management/ViewModels/ConnectionSettingsViewModel.cs), [`InfobaseMaintenanceService.cs`](Configuration Management/Services/InfobaseMaintenanceService.cs) / [`.Linux.cs`](Configuration Management/Services/InfobaseMaintenanceService.Linux.cs)): подписи элементов шрифта, подсказки разрядности и режимов запуска, сообщения стартера и физического удаления, комментарий ярлыка .desktop. Ключи `Font.*` / `Conn.*` / `Maint.*` (26).
  - **Шаблоны конфигураций и Linux-лаунчер создания ИБ** ([`OneCTemplateService.cs`](Configuration Management/Services/OneCTemplateService.cs), [`LinuxOneCServiceShims.cs`](Configuration Management/Services/LinuxOneCServiceShims.cs)): суффиксы и подписи шаблонов, ошибки `CREATEINFOBASE`. Ключи `Tpl.*` (6) + переиспользование `Launcher.Create*`.
  - **Сознательно не изменялись** ([`IbasesV8iImporter.cs`](Configuration Management/Services/IbasesV8iImporter.cs) / [`IbasesV8iExporter.cs`](Configuration Management/Services/IbasesV8iExporter.cs)): строки режимов запуска являются внутренними техническими маркерами (записываются в свойство `LaunchMode`, участвуют в сравнениях и при экспорте); отображение уже локализовано геттером `ParsedLaunchMode` в [`Infobase.cs`](Configuration Management/Models/Infobase.cs).
- **Вынесены в локализацию два оставшихся сообщения об ошибках создания информационной базы `CREATEINFOBASE` в Linux-шинме** ([`LinuxOneCServiceShims.cs`](Configuration Management/Services/LinuxOneCServiceShims.cs)). Теперь они переключаются вместе с языком интерфейса:
  - ошибка создания каталога файловой базы — вместо жёстко прописанной строки переиспользован существующий ключ `Launcher.CreateDirCreateFailedFormat` через `string.Format(...)`;
  - ошибка запуска `CREATEINFOBASE` — добавлен новый ключ `Launcher.CreateProcessErrorFormat` через `string.Format(...)`.
  - Новый ключ `Launcher.CreateProcessErrorFormat` добавлен в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английский перевод в [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Вынесено в локализацию сообщение исключения «Резервная копия не найдена.»** ([`IbasesBackupService.cs`](Configuration Management/Services/IbasesBackupService.cs)). Текст `FileNotFoundException`, который подставляется в сообщение об ошибке восстановления резервной копии (`Settings.Ibases.RestoreFailed`), теперь берётся из нового ключа `Settings.Ibases.BackupNotFound` через `LocalizationManager.T(...)`. Ключ добавлен в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английский перевод в [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Убраны жёстко прописанные русские единицы размера (fallback) из форматирования размеров** ([`CacheCleanWindow.xaml.cs`](Configuration Management/CacheCleanWindow.xaml.cs), [`CacheCleanWindow.Avalonia.cs`](Configuration Management/CacheCleanWindow.Avalonia.cs), [`Infobase.cs`](Configuration Management/Models/Infobase.cs)). В методе `FormatSize` оставался нелокализованный запасной массив `{"Б","КБ","МБ","ГБ","ТБ"}`, который срабатывал только когда ключ `CacheClean.SizeUnits` не возвращал значений (по сути мёртвый код — встроенные языки `ru`/`en` всегда задают этот ключ). Запасной массив удалён: единицы теперь берутся исключительно из локализованного ключа `CacheClean.SizeUnits` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Версия объединена и обновлена до 0.3.2.10** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj); Version/AssemblyVersion/FileVersion остались 0.3.2). Бейдж и заголовок в [`README.md`](README.md) обновлены до 0.3.2.10.

## [0.3.2.9] — 2026-08-21

### Добавлено

- **Вынесены в локализацию оставшиеся жёстко прописанные русские строки в моделях данных, отображаемые пользователю** (статусы базы, типы подключения, режимы запуска, разрядность, размер, группы и названия цветов). Внутренние технические значения (поля `_launchMode`, `ClientType`, `Architecture`, enum-маркеры подключения) не изменены — локализованы только отображаемые геттеры через `LocalizationManager.T(...)`.
  - **Модель базы** ([`Infobase.cs`](Configuration Management/Models/Infobase.cs)): разрядность `ArchitectureDisplay` (`Infobase.ArchX64/X86/Priority64/Priority32`), группа «Без группы» / «Закреплённые» (`Group.NoGroup`, переиспользован `Main.Pinned`), тип подключения `ConnectionTypeDisplay` (`Infobase.Type.File/WebServer/ClientServer`), статус базы `StatusDisplay` и причины недоступности (`Infobase.Status.*` / `Infobase.Unavailable.*`), режим запуска `ParsedLaunchMode` (`Infobase.LaunchMode.Auto`), дата последнего запуска «Не запускалась» (`Infobase.LastLaunch.Never`), история запусков «Нет записей» и формат `{0} зап., посл.: {1}` (`Infobase.History.Empty/Summary`).
  - **Единицы размера** в `FormatSize` теперь берутся из общего локализованного механизма `CacheClean.SizeUnits` («Б,КБ,МБ,ГБ,ТБ» / «B,KB,MB,GB,TB») — вместо зашитых русских Б/КБ/МБ/ГБ.
  - **Дерево групп** ([`GroupNodeViewModel.cs`](Configuration Management/ViewModels/GroupNodeViewModel.cs) и [`GroupNodeViewModel.Avalonia.cs`](Configuration Management/ViewModels/GroupNodeViewModel.Avalonia.cs)): плейсхолдеры «Без группы» / «Без названия» (`Group.NoGroup` / `Group.NoName`). Внутренние маркеры специальных узлов «Закреплённые» / «Все базы» (задаются явно при создании узлов в `MainViewModel` и используются для выбора иконки и логики) сохранены без изменений, чтобы иконки и сравнения не сломались.
  - **Названия цветов в редакторе тем** ([`ColorScheme.cs`](Configuration Management/Models/ColorScheme.cs)): `Definitions`/`GetLabel` теперь возвращают локализованные подписи через ключи `Color.*`; технические ключи ресурсов (напр. `AccentColor`) не переведены. Имена встроенных схем «Светлая»/«Тёмная» остаются внутренними идентификаторами и уже отображаются через `Theme.Light`/`Theme.Dark`.
  - Добавлены новые ключи `Infobase.*`, `Group.*`, `Color.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Версия обновлена до 0.3.2.9** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj), бейдж и заголовок в [`README.md`](README.md)).

## [0.3.2.8] — 2026-08-21

### Добавлено

- **Вынесены в локализацию оставшиеся жёстко прописанные русские строки в ViewModels главного окна — строка состояния, сообщения диалогов и тултипы.** Теперь эти тексты переключаются вместе с языком интерфейса.
  - **Avalonia/Linux** ([`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs)): строка состояния (`Готово`, `База: … — …`, `Группа: …`, `Загружено баз: N`) через ключи `Main.Ready` / `Main.StatusBase` / `Main.StatusGroup` / `Main.LoadedBases`; тултип кнопки правой панели через ключи `Main.CollapseRightPanel` / `Main.ExpandRightPanel`; запись истории запуска через ключ `Main.LaunchAction`.
  - **Сообщения диалогов** (`_dialog.*`) в [`MainViewModel.Avalonia.cs`](Configuration Management/ViewModels/MainViewModel.Avalonia.cs): редактирование/добавление базы (`Main.EditBaseInfo` / `Main.AddBaseInfo`), подтверждение удаления базы (`Main.ConfirmDeleteBase`), запрос информации о конфигурации (`Main.RefreshConfigInfoMsg`), ошибка открытия каталога (`Main.ErrOpenBaseFolder`), создание/ошибка ярлыка на рабочем столе (`Main.ShortcutCreated` / `Main.ErrShortcutCreate`), ошибка стартера 1С (`Main.ErrStartStarter`), ошибка загрузки списка баз (`Main.ErrLoadBases`).
  - **Синхронизация с ibases.v8i**: заголовок и фильтр файлового диалога (`Sync.ChooseIbasesFile` / `Sync.IbasesFilter`), статус и импорт (`Sync.Completed` / `Sync.ImportedCount`), ошибка синхронизации (`Sync.ErrSyncFailed` / `Sync.Failed`).
  - **WPF** ([`MainViewModel.cs`](Configuration Management/ViewModels/MainViewModel.cs)): слова строки состояния (`порт`, `платформа`, `пользователь`) через ключи `Main.StatusPort` / `Main.StatusPlatform` / `Main.StatusUser`; сообщения синхронизации (`Sync.PrefixExported` / `Sync.PrefixImported` / `Sync.ExportError` / `Sync.ImportError` / `Sync.AddedBases` / `Sync.UpdatedBases` / `Sync.RemovedBases` / `Sync.Skipped` / `Sync.GroupsCreated`).
  - Внутренние технические значения сессии (`SessionClient`, `SessionArch`, строки сравнений) не затрагивались — локализовано только отображение. Логи (`_logger.*`) оставлены без изменений.
  - Добавлены новые ключи в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Версия обновлена до 0.3.2.8** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj), бейдж и заголовок в [`README.md`](README.md)).

## [0.3.2.7] — 2026-08-21

### Добавлено

- **Вынесены в локализацию оставшиеся жёстко прописанные русские строки в диалоговых сервисах и точках входа приложения.** Теперь заголовки диалогов и фатальные сообщения переключаются вместе с языком интерфейса.
  - **Диалоговые сервисы** ([`IDialogService.cs`](Configuration Management/Services/IDialogService.cs), [`WpfDialogService.cs`](Configuration Management/Services/WpfDialogService.cs), [`AvaloniaDialogService.cs`](Configuration Management/Services/AvaloniaDialogService.cs)): заголовки информационных/предупреждающих/ошибочных сообщений через общие ключи `Common.Information` / `Common.Warning` / `Common.Error`; подтверждение — через новый ключ `Common.Confirm`; заголовки файловых диалогов «Открыть файл», «Сохранить файл», «Выбор папки» — через новые ключи `Dialog.OpenFile` / `Dialog.SaveFile` / `Dialog.SelectFolder`.
  - Фильтр «Все файлы (*.*)» в [`WpfDialogService.cs`](Configuration Management/Services/WpfDialogService.cs) (`BuildFilter`) и в разборе фильтра [`AvaloniaDialogService.cs`](Configuration Management/Services/AvaloniaDialogService.cs) (`BuildFileTypes`) через существующий ключ `Common.AllFiles`; кнопка «Отмена» окна сообщения Avalonia через существующий ключ `Common.Cancel`. Паттерны фильтров (расширения) не затрагивались.
  - **Точки входа приложения**: фатальные сообщения в [`App.xaml.cs`](Configuration Management/App.xaml.cs) («Ошибка интерфейса», «Критическая ошибка», «Ошибка фоновой задачи», «Не удалось запустить приложение», «Внутренняя ошибка:», заголовок окна) и [`App.axaml.cs`](Configuration Management/App.axaml.cs) — через общие ключи `App.Fatal.Interface` / `App.Fatal.Critical` / `App.Fatal.BackgroundTask` / `App.Fatal.StartupFailed` / `App.Fatal.InternalError` / `App.Fatal.Title`.
  - Значения по умолчанию параметров интерфейса `IDialogService` переведены на пустые строки (значения параметров — константы времени компиляции, в них нельзя вызывать `LocalizationManager.T`); локализованный заголовок подставляется в реализациях, если переданный параметр пуст, при этом явно переданный пользователем заголовок уважается.
  - Добавлены новые ключи в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Версия обновлена до 0.3.2.7** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj), бейдж и заголовок в [`README.md`](README.md)).

## [0.3.2.6] — 2026-08-21

### Добавлено

- **Вынесены в настройки перевода (локализацию) оставшиеся жёстко прописанные русские строки в Avalonia-контролах и код-бихайндах окон настроек.** Теперь эти элементы переключаются вместе с языком интерфейса.
  - **Компонент `?`-подсказки** ([`HelpLink.Avalonia.cs`](Configuration Management/Controls/HelpLink.Avalonia.cs)): всплывающая подсказка кнопки и заголовок «Подсказка» через ключи `HelpLink.Tooltip` / `HelpLink.Title` (те же, что в WPF-версии).
  - **Поле горячей клавиши** ([`HotkeyBox.cs`](Configuration Management/Controls/HotkeyBox.cs) и [`HotkeyBox.Avalonia.cs`](Configuration Management/Controls/HotkeyBox.Avalonia.cs)): тултип через новый ключ `Hotkey.Tooltip`; отображаемое значение «Нет» для неназначенной клавиши — через существующий ключ `Common.None`. Внутренняя логика (сброс по пустой строке, сопоставление в `ReadHotkeyBox`) не менялась.
  - **Окно «Выбор группы»** ([`GroupPickerWindow.Avalonia.cs`](Configuration Management/GroupPickerWindow.Avalonia.cs)) и **«Выбор версии платформы»** ([`PlatformVersionPickerWindow.Avalonia.cs`](Configuration Management/PlatformVersionPickerWindow.Avalonia.cs)): радио сортировки «А → Я» / «Я → А» через общие ключи `Common.SortAsc` / `Common.SortDesc`.
  - **Окно настроек (Avalonia)** ([`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs)): подпись разрядности сессии «Авто» через ключ `Main.SessionClientAuto`.
  - **Окно настроек (WPF)** ([`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs)): отображаемые названия начертаний шрифта («Обычный», «Полужирный», «Курсив», «Полужирный курсив») через ключи `Settings.Font.StyleNormal/Bold/Italic/BoldItalic` (технические значения `Weight`/`Style` не локализуются); отображаемые имена встроенных тем «Светлая»/«Тёмная» через ключи `Theme.Light` / `Theme.Dark` (внутренний идентификатор схемы сохранён — логика сохранения/загрузки не изменена).
  - Добавлены новые ключи в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Версия обновлена до 0.3.2.6** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj), бейдж и заголовок в [`README.md`](README.md)).

## [0.3.2.5] — 2026-08-21

### Добавлено

- **Вынесены в настройки перевода (локализацию) оставшиеся жёстко прописанные русские строки** в WPF-XAML-разметке (окно `CreateInfobaseWindow.xaml`, `DeleteInfobaseWindow.xaml`, компонент `HelpLink.xaml`, остатки главного окна и тексты-заглушки поля поиска во всех темах). Теперь эти элементы переключаются вместе с языком интерфейса.
  - **Окно «Создание информационной базы»** ([`CreateInfobaseWindow.xaml`](Configuration Management/CreateInfobaseWindow.xaml)): длинная подсказка `HelpLink`, подписи «Наименование:», «Тип базы:», радио «Файловая»/«Клиент-серверная», «Каталог базы:», «Обзор…», «Сервер 1С:» (с `ToolTip`), «Имя базы (Ref):», подпись шаблонов из манифестов, «Обновить»/«Файл…» (с `ToolTip`), «Версия платформы:», «Список…»/«Пути…» (с `ToolTip`), «Группа в списке:», «Выбрать…» (с `ToolTip`), кнопки «Создать» и «Отмена».
  - **Окно «Удаление информационной базы»** ([`DeleteInfobaseWindow.xaml`](Configuration Management/DeleteInfobaseWindow.xaml)): заголовок и подписи «Наименование:», «Тип:», «Путь / сервер:», «Группа:», «Платформа:», «На диске:».
  - **Компонент `?`-подсказки** ([`HelpLink.xaml`](Configuration Management/Controls/HelpLink.xaml)): всплывающая подсказка кнопки и заголовок «Подсказка».
  - **Остатки главного окна** ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml)): три длинные подсказки `HelpLink` (список баз, панель тегов, блок «Текущая сессия») и строка «Нет выбора» (заменён `TargetNullValue` на стиль-триггер с локализованным значением).
  - **Текст-заглушка поля поиска** во всех темах ([`DarkTheme.xaml`](Configuration Management/Themes/DarkTheme.xaml), [`LightTheme.xaml`](Configuration Management/Themes/LightTheme.xaml), [`ModernTheme.xaml`](Configuration Management/Themes/ModernTheme.xaml)) привязан к ключу `Main.SearchPlaceholder` (в тёмной и светлой темах исправлена повреждённая кодировка текста).
  - Добавлены ключи `HelpLink.*`, `DeleteInfobase.*`, `CreateInfobase.*`, `Main.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и английские переводы в [`en.json`](Configuration Management/Localization/Languages/en.json); использованы существующие общие ключи (`Common.Browse`, `Common.Cancel`, `CreateInfobase.FileType/ServerType/DirLabel/RefLabel/Refresh/File/List/Paths/ChooseGroup/Create` и др.).

### Изменено

- **Версия обновлена до 0.3.2.5** (`InformationalVersion` в [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj), бейдж и заголовок в [`README.md`](README.md)).

## [0.3.2.4] — 2026-08-21

### Добавлено

- **Компактный режим интерфейса** (обе платформы: **WPF** и **Avalonia/Linux**). Новая настройка «Компактный режим» уменьшает размер иконок и кнопок, сокращает отступы, внутренние поля секций-карточек, расстояния между элементами и ширину правой панели, убирая избыточное пустое пространство. Включить можно двумя способами: переключателем **в правом верхнем углу главного окна** (кнопка с иконкой сжатия рядом с настройками) и во вкладке «Настройки» окна настроек. Значение хранится в [`AppSettings.CompactMode`](Configuration Management/Models/AppSettings.cs) и применяется при запуске и сразу при переключении.
  - **WPF (Windows)**: [`ThemeManager.ApplyCompact`](Configuration Management/Themes/ThemeManager.cs) масштабирует отступы/поля/шрифты/высоты элементов главного окна на 0.8 (с сохранением исходных значений для возврата к обычному виду); применяется из [`SettingsWindow`](Configuration Management/SettingsWindow.xaml.cs) (переключатель `CompactModeCheck`) и при старте ([`App.xaml.cs`](Configuration Management/App.xaml.cs)).
  - **Avalonia (Linux)**: все метрики централизованы в [`UiMetrics`](Configuration Management/Controls/UiMetrics.Avalonia.cs) (флаг `Compact` и масштабируемые свойства `Scaled`, `SectionPad`, `ButtonPadH/V`, `RowIconBox`, `RightPanelMin/Max` и др.); переключатель — в [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs).
  - Добавлен ключ `Settings.CompactMode` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

### Изменено

- **Настройка языка интерфейса перенесена из вкладки «Цветовое оформление» во вкладку «Настройки»** (WPF-окно [`SettingsWindow.xaml`](Configuration Management/SettingsWindow.xaml)): теперь выбор языка находится рядом с поведением приложения и компактным режимом.

### Исправлено

- **Изменение шрифта теперь применяется к группам и списку баз** (Avalonia/Linux). Раньше сохранённые настройки шрифта вовсе не применялись при запуске Linux-версии, а строки дерева групп и баз задавали собственный жёсткий `FontSize`, перекрывающий унаследованный шрифт. Теперь:
  - при старте применяется общий шрифт интерфейса и шрифты отдельных областей ([`ThemeManager.ApplyElementFonts`](Configuration Management/Themes/ThemeManager.Avalonia.cs) с обходом визуального дерева по типам/именам: кнопки, поля ввода, дерево групп, правая панель `RightPanelBorder`, статус-бар `StatusBarBorder`);
  - у строк дерева убраны жёсткие размеры шрифта — они наследуют применяемый шрифт;
  - уменьшено избыточное пустое пространство (переработаны отступы и метрики).

## [0.3.2.3] — 2026-08-21

### Добавлено

- **В окне «Очистка кэша 1С» теперь показываются размеры программного и пользовательского кэша.** Рядом с чекбоксами выбора типа кэша отображается текущий занимаемый объём: «Программный кэш (1,2 ГБ)», «Пользовательский кэш (340 МБ)» и т.п. Размер вычисляется суммированием всех файлов в корневых каталогах кэша (`%LOCALAPPDATA%\1C\1cv8…` / `%APPDATA%\1C\1cv8…` на Windows; `~/.cache/1cv8`, `~/.local/share/1cv8` и `~/.1cv8/1cv8` на Linux) методом [`OneCCacheCleaner.GetSize`](Configuration Management/Services/OneCCacheCleaner.cs) и отображается в человекочитаемом виде с локализованными единицами (Б/КБ/МБ/ГБ/ТБ или B/KB/MB/GB/TB). Расчёт выполняется асинхронно в фоновом потоке, чтобы не блокировать интерфейс.
- **В списке баз окна добавлены две колонки с размером кеша для каждой базы — «Программный» и «Пользовательский».** Показывается текущий занимаемый объём программного и пользовательского кеша конкретной базы (по ID или имени базы) через [`OneCCacheCleaner.GetSize(Infobase, OneCCacheKind)`](Configuration Management/Services/OneCCacheCleaner.cs). Добавлены ключи `CacheClean.ColumnBase`, `CacheClean.ColumnProgramSize`, `CacheClean.ColumnUserSize` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
- **Шапка колонок списка закреплена вверху** и остаётся видимой при прокрутке; ширину любой колонки можно изменить перетаскиванием узкой зоны захвата на её правой границе (курсор «двусторонняя стрелка»). Изменение ширины реализовано **по образцу главного окна**: при перетаскивании меняется только целевая колонка (`startWidth + delta`), заголовок и данные изменяются синхронно, а колонка «База» до первого изменения растягивается на всё свободное место ([`SetColumnWidth`](Configuration Management/CacheCleanWindow.Avalonia.cs), [`SetColumnWidth`](Configuration Management/CacheCleanWindow.xaml.cs)).
- **Ширины всех колонок запоминаются при закрытии окна** и восстанавливаются при следующем открытии (свойства `CacheCleanBaseColumnWidth` / `CacheCleanProgramColumnWidth` / `CacheCleanUserColumnWidth` в [`AppSettings`](Configuration Management/Models/AppSettings.cs)); добавлен ключ `CacheClean.ResizeColumnTooltip` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

Реализовано для обеих платформ: **WPF (Windows)** ([`CacheCleanWindow.xaml.cs`](Configuration Management/CacheCleanWindow.xaml.cs)) и **Avalonia (Linux)** ([`CacheCleanWindow.Avalonia.cs`](Configuration Management/CacheCleanWindow.Avalonia.cs)).

## [0.3.2.2] — 2026-08-21

### Исправлено

- **Строка подключения `Connect` в файле `ibases.v8i` теперь всегда завершается знаком «;».** Ранее при синхронизации завершающая точка с запятой терялась (например, `Connect=File="D:\база"` вместо `Connect=File="D:\база";`), из-за чего **EDT** не мог открыть такие базы. Теперь строка подключения корректно заканчивается на «;» для файловых, веб- и клиент-серверных баз ([`IbasesV8iExporter`](Configuration Management/Services/IbasesV8iExporter.cs)).

## [0.3.2.1] — 2026-08-20

### Добавлено

- **Многоязычность интерфейса (русский и английский из коробки + загрузка других языков).** Встроены два языка — **русский** (`ru`) и **английский** (`en`), словари лежат в [`Localization/Languages/ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json) и встраиваются в сборку.
  - **Загрузка дополнительных языков без пересборки**: файл `*.json` в формате встроенных в папке `Languages` рядом с приложением или в каталоге данных автоматически появляется в списке языков.
  - **Выбор языка** в настройках (Windows — вкладка «Цветовое оформление», Linux — вкладка «Настройки»); применяется сразу и сохраняется.
  - **Автоопределение**: при отсутствии явного выбора берётся язык ОС (если доступен), иначе — русский.
  - **Запоминание языка** при закрытии/открытии приложения (`AppSettings.Language`).
  - **Механизм**: центральный менеджер [`LocalizationManager`](Configuration Management/Localization/LocalizationManager.cs) (переводы с откатом: текущий → английский → русский → ключ), источник [`LocalizationSource`](Configuration Management/Localization/LocalizationSource.cs) для привязок `{Binding Loc[Key]}` (Avalonia) и WPF-расширение [`LocExtension`](Configuration Management/Localization/LocExtension.cs) (`{loc:Loc Key}` через конвертер [`LocalizationValueConverter`](Configuration Management/Localization/LocalizationValueConverter.cs)), все с автообновлением при смене языка; `LocalizationManager.T("Key")` — для строк из кода.
  - **Общие кнопки диалогов «Отмена»/«ОК»** переведены в языковую модель и обновляются вживую ([`ModalWindowBase`](Configuration Management/ModalWindowBase.cs)).
  - **Заголовки всех диалоговых окон, внутренние надписи** диалогов «Удаление базы» и «Очистка кэша» переведены через `{loc:Loc Key}`.
  - **Команды контекстных меню, заголовки колонок списка баз и названия вкладок/групп настроек** переведены в языковую модель ([`MainWindow.xaml`](Configuration Management/MainWindow.xaml), [`SettingsWindow.xaml`](Configuration Management/SettingsWindow.xaml)).
  - Локализовано главное окно (обе платформы) и окно настроек. Документация по добавлению языков и переводу остальных окон — в [`Localization/README.md`](Configuration Management/Localization/README.md).
  - Реализовано для обеих платформ: **WPF (Windows)** и **Avalonia (Linux)**.
  - **Полная локализация окна настроек** ([`SettingsWindow.xaml`](Configuration Management/SettingsWindow.xaml)): подвкладки «Отображения» (Значки, Колонки, Панели, Статус/Нижняя панель, Шрифт), подписи полей, кнопки («Применить», «Обзор…», «Создать тему», «Переименовать» и др.), чекбоксы, вспомогательные/подсказочные тексты и `ToolTip` переведены через `{loc:Loc Key}`; добавлены ключи `Settings.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
  - **Правая панель сведений, блок «Текущая сессия», вкладки списка, поле поиска, тултипы и надписи главного окна** переведены в языковую модель: поля подключения («Тип», «Сервер / путь», «Сервер/База», «Строка», «Платформа», «Режим запуска», «Клиент», «Разрядность», «Параметры», «Последний запуск»), заголовки «Описание», «Теги», «Информация о подключении», «Действия», «Текущая сессия», «Режим клиента»; режимы клиента (Авто/Обычный/Толстый (управляемые)/Толстый (обычные)/Тонкий); кнопки «Изменить настройки», «Добавить базу / группу», «Избранное», «Закрепить», «Очистить кэш», «Удалить», «Перейти по ссылке», «Выход» и их тултипы с горячими клавишами (через `MultiBinding` + конвертер); тултипы колонок, сортировки, групп, тегов; текст трея (`LocalizationManager.T("App.Title")`). Добавлены ключи `Main.*`, `Common.Clear` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
  - **Полная локализация мастера добавления и мелких диалогов**: окно добавления базы/группы ([`AddEditWindow.xaml`](Configuration Management/AddEditWindow.xaml)), ввода строки подключения ([`ConnectionStringInputWindow.xaml`](Configuration Management/ConnectionStringInputWindow.xaml)), ввода ссылки ([`LinkInputWindow.xaml`](Configuration Management/LinkInputWindow.xaml)), ввода названия ([`NameInputWindow.xaml`](Configuration Management/NameInputWindow.xaml)), ввода тега ([`TagInputWindow.xaml`](Configuration Management/TagInputWindow.xaml)) и выбора цвета ([`ColorPickerWindow.xaml`](Configuration Management/ColorPickerWindow.xaml)) — все подписи, описания вариантов, кнопки («Далее», «Вставить из буфера», «Применить», «Перейти», «Отмена», «ОК», «Добавить»), `ToolTip` и подсказка `HelpLink.HelpText` переведены через `{loc:Loc Key}` / `LocalizationManager.T(...)`. Добавлены ключи `AddEdit.*`, `ConnectionStringInput.*`, `LinkInput.*`, `NameInput.Prompt`, `TagInput.Prompt`, `ColorPicker.Palette` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
  - **Полная локализация окна настроек подключения** ([`ConnectionSettingsWindow.xaml`](Configuration Management/ConnectionSettingsWindow.xaml)): вкладки («База», «Подключение», «Хранилище», «Авторизация», «Запуск», «Разрядность», «Платформа», «Идентификатор»), заголовки шапки и групп, подписи полей, варианты подключения/авторизации/запуска/разрядности с описаниями, кнопки («Выбрать…», «Обзор…», «Вставить строку подключения», «Сохранить», «Отмена», «Сгенерировать», «Копировать»), `ToolTip`, `HelpLink.HelpText`, текст подсказки о разрядности ОС и строки из кода (заголовок диалога выбора каталога, «Без группы», сообщения о вставке строки подключения) переведены через `{loc:Loc Key}` / `LocalizationManager.T(...)`. Добавлены ключи `Connection.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json)`.
  - **Полная локализация групповых и прочих диалогов**: настройка группы ([`GroupEditWindow.xaml`](Configuration Management/GroupEditWindow.xaml)), выбор родительской группы ([`GroupPickerWindow.xaml`](Configuration Management/GroupPickerWindow.xaml)), управление группами ([`GroupSettingsWindow.xaml`](Configuration Management/GroupSettingsWindow.xaml)), выбор версии платформы ([`PlatformVersionPickerWindow.xaml`](Configuration Management/PlatformVersionPickerWindow.xaml)) и конфигуратор параметров запуска ([`LaunchParametersWindow.xaml`](Configuration Management/LaunchParametersWindow.xaml)) — вкладки, заголовки групп, подписи полей, кнопки, `ToolTip`, подсказки `HelpLink.HelpText`, подписи иконок, сообщения из кода и полный справочник ключей командной строки переведены через `{loc:Loc Key}` / `LocalizationManager.T(...)`. Добавлены ключи `GroupEdit.*`, `GroupPicker.*`, `GroupSettings.*`, `PlatformVersionPicker.*`, `LaunchParams.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json)`.
  - **Локализованы строки из код-бихайндов окон** (`LocalizationManager.T(...)`): оставшиеся сообщения и статусы в [`SettingsWindow.xaml.cs`](Configuration Management/SettingsWindow.xaml.cs) (темы/схемы, ibases.v8i, пути платформ, каталоги шаблонов, дубли хоткеев, отметки времени), пункты меню трея и тултип темы в [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs), а также окна [`CreateInfobaseWindow.xaml.cs`](Configuration Management/CreateInfobaseWindow.xaml.cs), [`DeleteInfobaseWindow.xaml.cs`](Configuration Management/DeleteInfobaseWindow.xaml.cs), [`ConnectionStringInputWindow.xaml.cs`](Configuration Management/ConnectionStringInputWindow.xaml.cs) и счётчик выбора в [`CacheCleanWindow.xaml.cs`](Configuration Management/CacheCleanWindow.xaml.cs). Добавлены ключи `CreateInfobase.*`, `DeleteInfobase.*`, `CacheClean.*`, `Settings.*`, `Settings.Ibases.*`, `Main.*`, `Common.AllFiles` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).
  - **Полная локализация диалоговых окон Avalonia (Linux)** (`#if LINUX`, файлы `*.Avalonia.cs`): все оставшиеся русские надписи (заголовки окон, подписи полей, кнопки, вкладки, `Watermark`, `ToolTip`, варианты радио, сообщения/подтверждения из кода) заменены на `LocalizationManager.T("Ключ")`. Переведены окна [`AddEditWindow.Avalonia.cs`](Configuration Management/AddEditWindow.Avalonia.cs), [`CacheCleanWindow.Avalonia.cs`](Configuration Management/CacheCleanWindow.Avalonia.cs), [`ColorPickerWindow.Avalonia.cs`](Configuration Management/ColorPickerWindow.Avalonia.cs), [`ConnectionSettingsWindow.Avalonia.cs`](Configuration Management/ConnectionSettingsWindow.Avalonia.cs), [`ConnectionStringInputWindow.Avalonia.cs`](Configuration Management/ConnectionStringInputWindow.Avalonia.cs), [`CreateInfobaseWindow.Avalonia.cs`](Configuration Management/CreateInfobaseWindow.Avalonia.cs), [`DeleteInfobaseWindow.Avalonia.cs`](Configuration Management/DeleteInfobaseWindow.Avalonia.cs), [`GroupEditWindow.Avalonia.cs`](Configuration Management/GroupEditWindow.Avalonia.cs), [`GroupPickerWindow.Avalonia.cs`](Configuration Management/GroupPickerWindow.Avalonia.cs), [`GroupSettingsWindow.Avalonia.cs`](Configuration Management/GroupSettingsWindow.Avalonia.cs), [`LaunchParametersWindow.Avalonia.cs`](Configuration Management/LaunchParametersWindow.Avalonia.cs), [`LinkInputWindow.Avalonia.cs`](Configuration Management/LinkInputWindow.Avalonia.cs), [`PlatformVersionPickerWindow.Avalonia.cs`](Configuration Management/PlatformVersionPickerWindow.Avalonia.cs), [`TagInputWindow.Avalonia.cs`](Configuration Management/TagInputWindow.Avalonia.cs) и добиты оставшиеся строки в [`SettingsWindow.Avalonia.cs`](Configuration Management/SettingsWindow.Avalonia.cs) (темы, режим клиента, горячие клавиши, вкладка «О программе»). Добавлены ключи `ConnectionSettings.*`, `CreateInfobase.Header*`, `DeleteInfobase.Detail*`, `GroupEdit.Icon*Label`, `GroupSettings.AddRoot`, `PlatformVersionPicker.FilterLabel`, `LaunchParams.Header/Hint/ReferenceTitle/InputWatermark`, `ConnectionStringInput.Header/HintText/ClipboardAccessError`, `Common.SortLabel/Choose`, `Settings.Avalonia*`, `Settings.About.*` в [`ru.json`](Configuration Management/Localization/Languages/ru.json) и [`en.json`](Configuration Management/Localization/Languages/en.json).

### Исправлено

- **Исправлено падение при запуске (`XamlParseException`)** при локализации колонок/команд. WPF-расширение [`LocExtension`](Configuration Management/Localization/LocExtension.cs) переработано на привязку к источнику через конвертер [`LocalizationValueConverter`](Configuration Management/Localization/LocalizationValueConverter.cs) (без индексерного пути с точкой) и возвращает `BindingExpression`; это корректно работает с ключами вида `Column.Name`. Приложение запускается, главное окно и колонки списка баз отображаются и переключаются по языку.

## [0.3.1.2] — 2026-08-20

### Добавлено

- **Цветные иконки статуса баз в списке баз.** Слева от названия каждой базы теперь отображается векторная иконка, по которой сразу понятен тип подключения и доступность базы. Каждый статус имеет свой цвет:
  - **Файловая база** — значок папки **янтарного** цвета;
  - **База на веб-сервере** — значок глобуса **синего** цвета;
  - **Клиент-серверная база** — значок сети **фиолетового** цвета;
  - **Недоступна** — значок «крест» **красного** цвета (для файловых баз — каталог или `1Cv8.1CD` не найден на диске; для клиент-серверных и веб-баз — не заполнены параметры подключения). Реальная сетевая доступность удалённо не проверяется, чтобы не блокировать интерфейс сетевыми запросами.
- При наведении на иконку статуса показывается подсказка с пояснением (тип подключения или причина недоступности).

Реализовано для обеих платформ: **WPF (Windows)** и **Avalonia (Linux)**. Логика статуса вынесена в модель [`Infobase`](Configuration Management/Models/Infobase.cs) (`IsAvailable` / `StatusIconKey` / `StatusDisplay`); добавлены векторные иконки `IconNetwork` и `IconWeb` в [`Icons.xaml`](Configuration Management/Themes/Icons.xaml) и [`Icons.axaml`](Configuration Management/Themes/Icons.axaml); отрисовка иконки — в [`MainWindow.xaml`](Configuration Management/MainWindow.xaml) (Windows) и [`MainWindow.Avalonia.cs`](Configuration Management/MainWindow.Avalonia.cs) (Linux).

## [0.3.1.1] — 2026-08-20

### Добавлено (Linux-порт, Этапы 0–8)

- **Полный порт «Управление конфигурациями 1С» на Linux (Avalonia 11.3)**. Проект разделён на две конфигурации по ОС: Windows — WPF (`net10.0-windows`), Linux — Avalonia (`net10.0`). Windows-сборка не сломана. Детали и статус — в [`Configuration Management/LINUX_PORT.md`](Configuration Management/LINUX_PORT.md) и [`PLAN_LINUX.md`](PLAN_LINUX.md). Реализовано по этапам:
  - **Этап 0–1 (инфраструктура/csproj):** подключение Avalonia, исключение WPF/`System.Management`/`MaterialDesignThemes` из Linux-ветки, `DefineConstants=LINUX`, RID `linux-x64`/`win-x64` по ОС.
  - **Этап 2 (инфраструктура приложения):** Avalonia-`App` ([`App.axaml.cs`](Configuration Management/App.axaml.cs)), точка входа ([`Program.cs`](Configuration Management/Program.cs)), один экземпляр через файловый lock (`configuration-management.lock`) и активация через файл-сигнал `activate` (вместо `Mutex`/`user32`), обработка необработанных ошибок.
  - **Этап 3 (окна/контролы/конвертеры/темы):** порт окон на Avalonia (`*.Avalonia.cs`), контролы (GroupTreeView, LeveledTreeView, HotkeyBox и др.), конвертеры (`Converters/Avalonia/`), темы ([`ThemeManager.Avalonia.cs`](Configuration Management/Themes/ThemeManager.Avalonia.cs)); файловые диалоги через `StorageProvider` ([`AvaloniaDialogService.cs`](Configuration Management/Services/AvaloniaDialogService.cs)).
  - **Этап 4 (пути/хранилище):** общий [`PlatformPaths.cs`](Configuration Management/Services/PlatformPaths.cs) (`~/.config/ConfigurationManagement`), поиск `ibases.v8i` в `~/.1cv8/1CEStart/` и XDG-путях, кэш 1С (`~/.cache/1cv8`, `~/.local/share/1cv8`), шаблоны из `tmplts`/`1cestart.cfg`.
  - **Этап 5 (сервисы платформы 1С):** [`PlatformVersionService.Linux.cs`](Configuration Management/Services/PlatformVersionService.Linux.cs) (поиск `1cv8` в `/opt/1cv8` и др., разрядность через `readelf`), [`OneCLauncher.Linux.cs`](Configuration Management/Services/OneCLauncher.Linux.cs) (запуск `ENTERPRISE`/`DESIGNER`, пакетные операции, `CREATEINFOBASE`), [`InfobaseMaintenanceService.Linux.cs`](Configuration Management/Services/InfobaseMaintenanceService.Linux.cs), [`OneCComConnector.Linux.cs`](Configuration Management/Services/OneCComConnector.Linux.cs) (без COM — чтение конфигурации через `DESIGNER`/эвристику), чтение командной строки из `/proc/<pid>/cmdline` ([`LinuxProcess.cs`](Configuration Management/Services/LinuxProcess.cs)).
  - **Этап 6 (ярлыки/файловый менеджер/трей):** `.desktop`-ярлык вместо `.lnk`, открытие/выделение каталога через `xdg-open`/`nautilus`/`dolphin`, `xdg-open` для ссылок, `1cestart` вместо `1CEStart.exe`, трей через `TrayIcon`+`NativeMenu` без `System.Drawing`.
  - **Этап 7 (сборка/упаковка):** [`build.sh`](Configuration Management/build.sh) (single-file `linux-x64`), AppImage ([`package/linux/appimage.sh`](package/linux/appimage.sh)) и `.deb` ([`package/linux/deb/build-deb.sh`](package/linux/deb/build-deb.sh)).
  - **Этап 8 (тестирование):** подготовлен исчерпывающий чек-лист прогона на Linux-хосте — [`Configuration Management/LINUX_TESTING.md`](Configuration Management/LINUX_TESTING.md). Реальное тестирование на Linux требуется выполнить на целевой машине (на Windows-хосте недоступны Linux SDK и Linux-версия 1С).

## [0.2.7.35] — 2026-08-20

### Изменено

- **Окно «Параметры запуска» переработано в «поле ввода + справочник»** — вместо наборов галочек («Параметры-флаги», «Параметры с аргументами», «Мои параметры») вверху окна теперь находится поле ввода параметров командной строки, а ниже — справочник всех ключей запуска 1С с описанием, что делает параметр ([`LaunchParametersWindow`](Configuration Management/LaunchParametersWindow.xaml)). Двойной клик по строке справочника подставляет параметр в поле ввода ([`OnReferenceDoubleClick`](Configuration Management/LaunchParametersWindow.xaml.cs)). Удалена неиспользуемая функция пользовательского набора параметров и её редактор.

## [0.2.7.34] — 2026-08-20

### Исправлено

- **При неуспешной выгрузке `.dt` / `.cf` теперь показывается причина ошибки** — раньше при сбое пакетной операции конфигуратора процесс `1cv8.exe` запускался и мгновенно завершался, файл не создавался, а пользователь не видел, в чём дело: лог из ключа `/Out` и код возврата не читались. Теперь по завершении процесса читается временный лог 1С (с дожиданием стабилизации размера файла) и проверяется код возврата, а для выгрузки — и наличие непустого файла `.dt`/`.cf`. При неуспехе выводится окно «Ошибка операции 1С» с текстом лога 1С, кодом возврата и командной строкой ([`CompleteDesignerBatch`](Configuration Management/Services/OneCLauncher.cs), [`OnDesignerBatchCompleted`](Configuration Management/ViewModels/MainViewModel.cs)). Это позволяет определить реальную причину сбоя, например, для базы на сервере Astra Linux (аутентификация, доступ к серверу).
- **Пакетная выгрузка `.dt` / `.cf` теперь использует отдельную авторизацию конфигуратора** — [`BuildAuthArgument`](Configuration Management/Services/OneCLauncher.cs) раньше игнорировал `ConfiguratorAuth`: если для базы заданы отдельные учётные данные Конфигуратора, при пакетной выгрузке они не передавались (`/N`/`/P` не добавлялись), из-за чего конфигуратор не мог подключиться к базе и сразу завершался. Теперь `ConfiguratorAuth` используется в приоритете, как и в интерактивном запуске Конфигуратора.

## [0.2.7.33] — 2026-08-19

### Исправлено

- **Идентификатор базы теперь назначается всегда при добавлении через программу** — при добавлении существующей информационной базы через окно настройки подключения ([`ConnectionSettingsWindow`](Configuration Management/ConnectionSettingsWindow.xaml.cs)), если идентификатор не был найден ни в настройках, ни в файле `ibases.v8i`, ему автоматически присваивается новый GUID. Ранее у такой базы поле `Id` оставалось пустым, из-за чего была невозможна точечная очистка кеша 1С и некорректный экспорт в `ibases.v8i`.

## [0.2.7.32] — 2026-08-19

### Изменено

- **Кнопка «Вставить строку подключения»** в окне настроек базы больше **не показывается на всех вкладках** — она перенесена из общей нижней панели **только на вкладку «Подключение»** (ниже блока «Тип и параметры подключения»). Вместе с кнопкой перенесена и подсказка «?». В нижней панели окна остались только кнопки «Сохранить» и «Отмена».

## [0.2.7.31] — 2026-08-19

### Добавлено

- **Анимированный индикатор выгрузки `.dt` / `.cf`** в верхнем правом блоке кнопок, слева от кнопки синхронизации. Иконка автоматически **появляется**, когда запускается выгрузка информационной базы в `.dt` или конфигурации в `.cf` (пакетная операция конфигуратора через [`RunDesignerBatch`](Configuration Management/Services/OneCLauncher.cs)), и **исчезает** по её завершении. Реализовано через:
  - события [`DesignerBatchStarted`](Configuration Management/Services/OneCLauncher.cs) / [`DesignerBatchCompleted`](Configuration Management/Services/OneCLauncher.cs) и класс [`DesignerBatchInfo`](Configuration Management/Services/OneCLauncher.cs) — отслеживание запуска и завершения пакетной операции (процесс `1cv8.exe`, регистрируемый в реестре активных операций);
  - свойства [`MainViewModel.IsExporting`](Configuration Management/ViewModels/MainViewModel.cs) и [`MainViewModel.ExportIndicatorTooltip`](Configuration Management/ViewModels/MainViewModel.cs) — видимость индикатора и сводка о выгрузке;
  - анимированную иконку (`Upload` — стрелка выгрузки вверх, «подпрыгивает») в [`MainWindow.xaml`](Configuration Management/MainWindow.xaml), управление анимацией — [`MainWindow.StartExportIndicatorAnimation`](Configuration Management/MainWindow.xaml.cs) / [`MainWindow.StopExportIndicatorAnimation`](Configuration Management/MainWindow.xaml.cs).
- **Сводка о выгрузке при наведении** на индикатор: подсказка показывает операцию («Выгрузка ИБ (.dt)» / «Выгрузка конфигурации (.cf)»), имя информационной базы и путь к файлу выгрузки.

## [0.2.7.30] — 2026-08-19

### Добавлено

- **Проверка блокировки запуска конфигуратора перед выгрузкой `.dt` / `.cf` и тестированием ИБ** — перед запуском пакетной операции ([`RunDesignerBatch`](Configuration Management/Services/OneCLauncher.cs)) приложение проверяет, не запущен ли уже конфигуратор этой базы (в т.ч. открытый вручную вне приложения) или не идёт ли другая выгрузка/операция DESIGNER, и в этом случае выводит предупреждение и отменяет запуск. Реализовано через:
  - реестр активных пакетных операций, запущенных приложением ([`IsDesignerBlocked`](Configuration Management/Services/OneCLauncher.cs)) — блокирует параллельные выгрузки, процесс удаляется из реестра по завершении;
  - поиск запущенного процесса конфигуратора `1cv8.exe` / `1cv8x64.exe` для нужной базы по командной строке (`Win32_Process`, пакет [`System.Management`](Configuration Management/Configuration Management.csproj)) — обнаруживает конфигуратор, открытый даже вне приложения.

## [0.2.7.29] — 2026-08-19

### Исправлено

- **Ошибка «Платформа 1С, Не найден 1cv8.exe для режима Конфигуратор» при выгрузке `.dt` и `.cf`** — при пакетной выгрузке через конфигуратор ([`RunDesignerBatch`](Configuration Management/Services/OneCLauncher.cs)) исполняемый файл `1cv8.exe` искался строго для одной разрядности, выбранной по настройке по умолчанию (обычно 64-бит). Если платформа была установлена только в другой разрядности (например, 32-бит в `Program Files (x86)`), поиск не находил `1cv8.exe`, доходил до запасного `1CEStart.exe` (который отбрасывался) — и показывалась ошибка. Теперь, если для выбранной разрядности файл не найден, автоматически выполняется поиск по противоположной разрядности (по аналогии с запуском «1С:Предприятие» и ярлыками). Предупреждение выводится только если `1cv8.exe` не найден ни в одной разрядности.

## [0.2.7.28] — 2026-08-19

### Добавлено

- **Индивидуальная настройка шрифта для отдельных областей программы** — в **Настройки → Отображение → Шрифт** добавлен выбор **«Элемент интерфейса»**: размер, семейство и начертание шрифта можно задать отдельно для:
  - **По умолчанию** (общий шрифт всех окон);
  - **Список баз** ([`MainTree`](Configuration Management/MainWindow.xaml));
  - **Заголовки списка** ([`HeaderGrid`](Configuration Management/MainWindow.xaml));
  - **Правая панель** ([`RightPanelBorder`](Configuration Management/MainWindow.xaml));
  - **Нижняя панель (статус)** ([`StatusBarBorder`](Configuration Management/MainWindow.xaml));
  - **Вкладки** ([`TabsPanel`](Configuration Management/MainWindow.xaml));
  - **Кнопки**;
  - **Поля ввода**.
  Изменения применяются сразу (кнопка «Применить», предпросмотр) и сохраняются кнопкой «Сохранить».
- **Новая модель** [`ElementFontSettings`](Configuration Management/Models/ElementFontSettings.cs) и словарь [`AppSettings.ElementFonts`](Configuration Management/Models/AppSettings.cs): настройки каждой области хранятся отдельно и восстанавливаются при запуске.
- **Применение по областям**: [`ThemeManager.ApplyElementFonts`](Configuration Management/Themes/ThemeManager.cs) накладывает шрифт каждой области на соответствующий контейнер главного окна (сначала «По умолчанию», затем конкретные области поверх). Сохранение и предпросмотр — [`MainViewModel.SaveElementFonts`](Configuration Management/ViewModels/MainViewModel.cs) / [`MainViewModel.PreviewElementFonts`](Configuration Management/ViewModels/MainViewModel.cs); применяется при запуске в [`App.OnStartup`](Configuration Management/App.xaml.cs).

## [0.2.7.27] — 2026-08-19

### Изменено

- **Выбор размера шрифта расширен как в Microsoft Word** — в **Настройки → Отображение → Шрифт** список размеров теперь от 8 до 72 (8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 32, 36, 40, 48, 56, 64, 72) ([`FontSizeComboBox`](Configuration Management/SettingsWindow.xaml)). Список стал **редактируемым**: размер можно не только выбрать, но и **ввести вручную** (в т.ч. дробное значение).
- **Мгновенное применение шрифта ко всей программе**: кнопка «Применить» теперь меняет шрифт **сразу во всех открытых окнах** приложения (главное окно, окна настроек, диалоги) — реализовано через новый метод [`ThemeManager.ApplyFontToAllWindows`](Configuration Management/Themes/ThemeManager.cs), а не только в главном окне. Сохранение (`MainViewModel.ApplyFontSettings`) также применяет шрифт ко всем окнам.

## [0.2.7.26] — 2026-08-19

### Изменено

- **Иконки разворачивания «+»/«−»** в окне **выбора версии платформы** ([`PlatformVersionPickerWindow`](Configuration Management/PlatformVersionPickerWindow.xaml)) и в окне **выбора группы** ([`GroupPickerWindow`](Configuration Management/GroupPickerWindow.xaml)) увеличены — стали заметно крупнее и удобнее для нажатия. Изменение внесено в общий стиль элемента дерева [`ModernTreeViewItem`](Configuration Management/Themes/LightTheme.xaml) (светлая тема) и [`ModernTreeViewItem`](Configuration Management/Themes/DarkTheme.xaml) (тёмная тема).

## [0.2.7.25] — 2026-08-19

### Добавлено

- **Настройка шрифта интерфейса** — в **Настройки → Отображение → Шрифт** добавлена возможность менять **семейство шрифта** (Segoe UI, Arial, Calibri, Tahoma, Verdana, Trebuchet MS, Georgia, Times New Roman, Courier New, Consolas), **размер** (11–20) и **начертание** (Обычный / Полужирный / Курсив / Полужирный курсив) ([`SettingsWindow`](Configuration Management/SettingsWindow.xaml)). Изменения применяются к главному окну сразу по кнопке «Применить» (предпросмотр) и сохраняются кнопкой «Сохранить». Настройки хранятся в [`AppSettings.FontFamily`](Configuration Management/Models/AppSettings.cs), [`AppSettings.FontSize`](Configuration Management/Models/AppSettings.cs), [`AppSettings.FontWeight`](Configuration Management/Models/AppSettings.cs) и [`AppSettings.FontStyle`](Configuration Management/Models/AppSettings.cs).
- **Применение шрифта при запуске**: сохранённый шрифт применяется к главному окну при старте приложения ([`App.OnStartup`](Configuration Management/App.xaml.cs)). Реализовано через новый метод [`ThemeManager.ApplyFont`](Configuration Management/Themes/ThemeManager.cs), который распространяет семейство, размер и начертание через наследуемые свойства `TextElement` на дочерние элементы, не переопределяющие их явно. Сохранение и применение шрифта выполняет [`MainViewModel.ApplyFontSettings`](Configuration Management/ViewModels/MainViewModel.cs).

## [0.2.7.24] — 2026-08-19

### Изменено

- **Окно ввода названия темы** (создание/переименование темы в «Цветовое оформление») переоформлено в стиле Material Design по аналогии с другими окнами приложения ([`NameInputWindow`](Configuration Management/NameInputWindow.xaml)): стандартная рамка окна с иконкой, корректные тёмная/светлая темы, поле ввода в стиле `ModernTextBox`, кнопки «Отмена» и «ОК» в стиле `ModernButton`. Прежнее простое окно заменено — кнопки больше не обрезаются и полностью помещаются.

## [0.2.7.23] — 2026-08-19

### Исправлено

- **Надпись «Цветовое оформление» во вкладках окна настроек** больше не обрезается: ширина колонки боковых вкладок увеличена с 180 до 235 px ([`SettingsTabItem`](Configuration Management/SettingsWindow.xaml)), чтобы длинный заголовок новой вкладки полностью помещался.

## [0.2.7.22] — 2026-08-19

### Добавлено

- **Вкладка «Цветовое оформление» в окне настроек** — сюда вынесены настройки цветов программы ([`SettingsWindow`](Configuration Management/SettingsWindow.xaml)). Новая вкладка позволяет:
  - **Выбирать тему оформления**: встроенные «Светлая» и «Тёмная», а также созданные пользователем темы ([`SchemeComboBox`](Configuration Management/SettingsWindow.xaml)).
  - **Изменять отдельные цвета** темы (акцент, боковая панель, фон карточек, текст, границы, строки списка, полосы прокрутки и др.) через диалог выбора цвета ([`ColorPickerWindow`](Configuration Management/ColorPickerWindow.xaml)); изменения применяются сразу по кнопке «Применить» (предпросмотр) и сохраняются кнопкой «Сохранить».
  - **Создавать свои темы** на основе текущих цветов («Создать тему»), переименовывать и удалять пользовательские темы.
  - **Выгружать/загружать цветовые схемы** в JSON-файл («Выгрузить…»/«Загрузить…»), а также сбрасывать цвета темы к значениям по умолчанию.
- **Модель цветовой схемы** [`ColorScheme`](Configuration Management/Models/ColorScheme.cs): именованный набор цветов, встроенные светлая/тёмная схемы, сериализация в JSON для выгрузки/импорта и сохранения пользовательских тем.
- **Динамическое применение цветов**: [`ThemeManager`](Configuration Management/Themes/ThemeManager.cs) теперь применяет цветовую схему поверх базовой темы — ресурсы цветов/кистей светлой и тёмной тем переведены на `DynamicResource`, поэтому изменения цветов применяются к интерфейсу без перезапуска. Пользовательские темы сохраняются в каталоге `%APPDATA%\ConfigurationManagement\ColorSchemes`.
- **Хранение активной схемы** в настройках ([`AppSettings.ActiveColorScheme`](Configuration Management/Models/AppSettings.cs)): при запуске применяется сохранённая пользовательская схема, при её отсутствии — встроенная по выбранной теме.

## [0.2.7.21] — 2026-08-19

### Изменено

- **Кнопки сортировки в окне выбора родительской группы** ([`GroupPickerWindow`](Configuration Management/GroupPickerWindow.xaml)): в шапку над деревом добавлены кнопки **A→Z / Z→A** (иконки `SortAscending`/`SortDescending`, как в главном окне), позволяющие переключать сортировку групп по наименованию по возрастанию/убыванию. По умолчанию — **по возрастанию (А→Я)**, как и в основном дереве. Выбранное направление применяется сразу и к корневым группам, и ко всем вложенным подгруппам рекурсивно ([`OnSortAsc_Click`](Configuration Management/GroupPickerWindow.xaml.cs) / [`OnSortDesc_Click`](Configuration Management/GroupPickerWindow.xaml.cs)).
- **Кнопка подсказки «?» у поля «Родительская группа»** в окне настройки группы ([`GroupEditWindow`](Configuration Management/GroupEditWindow.xaml)) перенесена **справа от поля ввода** (после кнопки «Выбрать…»), а не перед полем. Теперь подпись «Родительская группа:» и сам значок подсказки не обрезаются и полностью помещаются.

## [0.2.7.20] — 2026-08-19

### Изменено

- **Сортировка групп по наименованию (по возрастанию) в окне выбора родительской группы** ([`GroupPickerWindow`](Configuration Management/GroupPickerWindow.xaml)): корневые группы и все вложенные подгруппы в дереве выбора родителя теперь упорядочены по имени (А→Я), как и в основном дереве списка баз. Ранее порядок был произвольным (по порядку в списке). Пункт «Без группы»/корень по-прежнему остаётся первым. Сортировка выполняется в [`GroupPickerWindow`](Configuration Management/GroupPickerWindow.xaml.cs) с использованием `SortChildrenRecursive`.

## [0.2.7.19] — 2026-08-19

### Исправлено

- **Положение и монитор окна теперь корректно восстанавливаются при запуске.** Ранее сохранялся только размер, а позиция игнорировалась: в [`MainWindow.xaml`](Configuration Management/MainWindow.xaml) задан `WindowStartupLocation="CenterScreen"`, из-за чего WPF при показе окна переопределял установленные `Left`/`Top` по центру экрана. При восстановлении сохранённой позиции теперь явно включается `WindowStartupLocation.Manual` ([`ApplySavedWindowLayout`](Configuration Management/MainWindow.xaml.cs)).

## [0.2.7.18] — 2026-08-19

### Добавлено

- **Запоминание расположения, размера и монитора главного окна** с восстановлением при следующем запуске:
  - Приложение запоминает **размер**, **позицию** и **состояние** окна (обычное / развёрнутое), а также **монитор, на котором окно было закрыто**, и возвращает эти параметры при запуске. При нескольких мониторах окно снова открывается на том же экране ([`ApplySavedWindowLayout`](Configuration Management/MainWindow.xaml.cs)).
  - **Настройка вынесена в окно настроек**: новый переключатель **«Запоминать расположение, размер и монитор окна»** в **Настройки → Настройки → Поведение приложения** ([`RememberWindowLayoutCheck`](Configuration Management/SettingsWindow.xaml)). При отключённой опции сохранённый макет окна сбрасывается, и при следующем запуске окно открывается по центру экрана.
  - Настройка хранится в [`AppSettings.RememberWindowLayout`](Configuration Management/Models/AppSettings.cs); если сохранённый монитор больше недоступен (например, отключён), окно корректно ограничивается рабочей областью доступного экрана, оставаясь видимым.

## [0.2.7.17] — 2026-08-19

### Добавлено

- **Вкладка «Хранилище» и раздельные настройки авторизации в окне настроек подключения**:
  - **«Хранилище»** — хранение данных подключения к хранилищу конфигурации 1С: адрес сервера, имя хранилища, логин и пароль ([`RepositorySettings`](Configuration Management/Models/RepositorySettings.cs), [`Infobase.Repository`](Configuration Management/Models/Infobase.cs)).
  - **«Авторизация»** — отдельная вкладка с двумя независимыми настройками: **«Авторизация в 1С:Предприятие»** (пользователь и пароль для запуска клиента, например тестовый пользователь) и **«Авторизация в Конфигураторе»** (отдельные учётные данные для запуска Конфигуратора, «под собой»). Прежняя вкладка **«Аутентификация»** заменена этим разделом.
  - **Обе настройки авторизации хранятся отдельно** и независимо: [`Infobase.EnterpriseAuth`](Configuration Management/Models/Infobase.cs) (для «1С:Предприятие») и [`Infobase.ConfiguratorAuth`](Configuration Management/Models/Infobase.cs) (для Конфигуратора), модель [`InfobaseAuthSettings`](Configuration Management/Models/InfobaseAuthSettings.cs). Настройки не копируются друг из друга и не зависят от параметров подключения базы.
  - **Учёт при запуске**: при запуске «1С:Предприятие» применяется `EnterpriseAuth`, при запуске Конфигуратора — `ConfiguratorAuth` ([`OneCLauncher`](Configuration Management/Services/OneCLauncher.cs)). Для баз, сохранённых до появления этих настроек (без отдельной авторизации), используется авторизация информационной базы (обратная совместимость).
  - **Исправлено сохранение при редактировании**: при изменении свойств существующей базы настройки авторизации Предприятия ([`Infobase.EnterpriseAuth`](Configuration Management/Models/Infobase.cs)), Конфигуратора ([`Infobase.ConfiguratorAuth`](Configuration Management/Models/Infobase.cs)) и хранилища ([`Infobase.Repository`](Configuration Management/Models/Infobase.cs)) теперь корректно сохраняются ([`EditInfobase`](Configuration Management/ViewModels/MainViewModel.cs)). Ранее они не копировались из диалога в базу, из-за чего авторизации могли путаться или теряться.
  - **Исправлено сохранение режима авторизации Предприятия**: выбранный режим «Запрашивать имя и пароль» в авторизации «1С:Предприятие» больше не перезаписывается на «Вход автоматически» при повторном открытии. Причина — устаревшая логика миграции применялась при каждой загрузке к уже сохранённой отдельной настройке ([`ConnectionSettingsViewModel.LoadFrom`](Configuration Management/ViewModels/ConnectionSettingsViewModel.cs)). Теперь миграция старого формата применяется только для баз без сохранённой `EnterpriseAuth` (обратная совместимость).

## [0.2.7.16] — 2026-08-19

### Изменено

- **Разделение «Толстый клиент» по режиму форм в «Текущей сессии»** в правой панели главного окна — по аналогии с настройками подключения базы 1С (вкладка «Запуск»): прежний переключатель **«Толстый»** переименован в **«Толстый (управляемые формы)»** (`/RunModeManagedApplication`), а рядом добавлен новый **«Толстый (обычные формы)»** (`/RunModeOrdinaryApplication`). Выбор применяется только к следующему запуску «1С:Предприятие», настройки базы не изменяются.
  - В enum [`SessionClientMode`](Configuration Management/Models/SessionLaunchModes.cs) добавлено значение `ThickOrdinary`; режим форм учитывается в логике запуска [`LaunchEnterpriseWithSessionOverrides`](Configuration Management/ViewModels/MainViewModel.cs) (явный выбор толстого клиента задаёт `/RunModeManagedApplication` или `/RunModeOrdinaryApplication`).
  - Обновлён текст подсказки блока «Текущая сессия».

## [0.2.7.15] — 2026-08-19

### Добавлено

- **Запрос и заполнение информации о конфигурации** ([`ConfigurationInfoService`](Configuration Management/Services/ConfigurationInfoService.cs)):
  - **Точечно из контекстного меню**: новый пункт **«Обновить информацию о конфигурации»** у выбранной базы — запрашивает наименование и версию конфигурации (COM-коннектор 1С или эвристика по файловой базе) и сразу заполняет поля, перезаписывая ранее заданные значения. Выполняется в фоне, чтобы не блокировать интерфейс, по завершении показывается результат (конфигурация и версия).
  - **По всем базам 1С**: кнопка **«Запросить информацию о конфигурации по всем базам 1С»** на верхней панели (рядом с синхронизацией) последовательно обновляет информацию по всем базам с подтверждением перед началом и сводным итогом (обновлено / ошибок / всего) по завершении.
  - Метод [`ReadAndApply`](Configuration Management/Services/ConfigurationInfoService.cs) читает и сразу применяет данные к базе; команды [`RefreshConfigurationInfo`](Configuration Management/ViewModels/MainViewModel.cs) и [`RefreshAllConfigurationInfo`](Configuration Management/ViewModels/MainViewModel.cs) добавлены в [`MainViewModel`](Configuration Management/ViewModels/MainViewModel.cs).

## [0.2.7.14] — 2026-08-19

### Изменено

- **Окно выбора версии платформы** ([`PlatformVersionPickerWindow`](Configuration Management/PlatformVersionPickerWindow.xaml)) доработано по аналогии со стартером 1С:
  - **Сортировка по умолчанию — по убыванию**: свежие релизы (например `8.3.27.x`) показываются сверху; направление по-прежнему можно переключить кнопками **A↓Z / Z↓A** (иконки `SortAscending`/`SortDescending`, как в главном окне при сортировке групп).
  - **Текущая версия базы подсвечивается жирным** и автоматически **позиционируется в дереве** при открытии. Если у базы указана **частичная версия** (линия `8.3`, группа сборок `8.3.19` или с разрядностью — `8.3 (64)`, `8.3.19 (64)`), повторяется «трюк 1С»: выбирается **максимальная доступная сборка** в пределах заданной линии/группы (с учётом разрядности), как это делает сам лаунчер платформы.
  - **Дерево полностью разворачивается** при открытии (линии → группы сборок → сборки), а не только первый уровень, как раньше (раскрывалась лишь половина поддеревьев).

## [0.2.7.13] — 2026-08-19

### Исправлено

- **Выпадающие списки (ComboBox) снова открываются** в окне «Настройки» (в том числе выбор режима и момента синхронизации) и в других местах приложения. Причина — ненадёжный невидимый переключатель на всю ширину (`FullDropDownToggle`) с режимом нажатия `Press` в шаблоне [`ModernComboBox`](Configuration Management/Themes/LightTheme.xaml), из-за которого список не раскрывался. Шаблон переведён на стандартный механизм WPF: раскрытие обрабатывает сам `ComboBox`, а кнопка-стрелка открывает список в обоих режимах (выбора и редактируемом). Исправление применено в светлой, тёмной и базовой темах.
- **Чекбоксы переоформлены в стиле Material Design**: добавлен единый шаблон [`ModernMaterialCheckBox`](Configuration Management/Themes/LightTheme.xaml) (квадрат со скруглением, акцентная заливка и галочка/тире при выборе, подсветка рамки при наведении, состояние «не определено»). Стиль применён глобально через неявные стили в светлой и тёмной темах, а окна с собственными стилями чекбоксов (Настройки, Подключение, Удаление, Параметры запуска) теперь наследуют его.

### Добавлено

- **Выбор шаблона даты и времени для имени файла при выгрузке**: в **Настройки → Базы → Список информационных баз** добавлен выпадающий список **«Шаблон даты и времени»** с предпросмотром примера. Шаблон (формат `DateTime`) подставляется вместо жёсткого `yyyyMMdd_HHmmss` при формировании имени файла выгрузки (экспорт списка в JSON, выгрузка ИБ в `.dt`, выгрузка конфигурации в `.cf`). Можно выбрать один из готовых шаблонов или ввести свой. Настройка сохраняется в [`AppSettings.ExportTimestampFormat`](Configuration Management/Models/AppSettings.cs) и используется в [`BuildExportFileName`](Configuration Management/ViewModels/MainViewModel.cs) (например `База_2026-08-19_07-43-12.dt`).
- **Интерактивный выбор каталога файловой базы**: в окне **«Настройки подключения»** для файловой версии рядом с полем «Путь к файлу» добавлена кнопка **«Обзор…»**, открывающая диалог выбора папки (каталога информационной базы на диске). Ранее путь можно было ввести только вручную.

## [0.2.7.12] — 2026-08-19

### Добавлено

- **Дата-время в имени файла при выгрузке**: в имя файла при выгрузке теперь автоматически добавляется отметка даты и времени (например `База_20260819_074312.dt`). Это касается всех операций выгрузки — **экспорта списка баз в JSON**, **выгрузки ИБ в `.dt`** и **выгрузки конфигурации в `.cf`**. Поведение управляется флажком **«Добавлять дату-время к имени файла при выгрузке»** в **Настройки → Базы → Список информационных баз** (включён по умолчанию). При выключенном флажке имя файла формируется без суффикса даты-времени, как раньше. Реализовано через новую настройку [`AddTimestampToExportFileName`](Configuration Management/Models/AppSettings.cs) и метод [`BuildExportFileName`](Configuration Management/ViewModels/MainViewModel.cs) в [`MainViewModel`](Configuration Management/ViewModels/MainViewModel.cs).

## [0.2.7.11] — 2026-08-19

### Исправлено

- **Запуск платформ из дополнительных папок (Настройки → Платформы)**: версии, установленные в нестандартных каталогах (произвольная вложенность без обязательного подкаталога `1cv8`), теперь корректно запускаются. Ранее список версий формировался гибким рекурсивным поиском, а лаунчер искал исполняемый файл только по стандартному макету `<корень>\1cv8\<версия>\bin`, из-за чего версия из дополнительной папки отображалась в списке, но не находилась при запуске — ни для конкретной версии базы, ни при выборе новейшей установленной версии, ни в приоритетных режимах разрядности. В [`PlatformVersionService`](Configuration Management/Services/PlatformVersionService.cs) добавлено гибкое разрешение каталога версии (`ResolveVersionBinDirectory` / `FindPlatformVersionDirs`), а [`OneCLauncher`](Configuration Management/Services/OneCLauncher.cs) теперь использует его вместо жёсткой схемы поиска.

## [0.2.7.10] — 2026-08-18

### Добавлено

- **Приложение запоминает последнюю выделенную строку списка баз** (базу или группу) и восстанавливает её при следующем запуске: выделение, правая панель и состояние кнопок возвращаются к тому же элементу. Идентификатор выбранной базы или полный путь выбранной группы сохраняются в настройках ([`AppSettings`](Configuration Management/Models/AppSettings.cs)) и автоматически обновляются при каждом изменении выделения. Для восстановления строка и нужная ветка дерева заранее раскрываются в модели ([`PrepareLastSelectionExpansion`](Configuration Management/ViewModels/MainViewModel.cs)) и выделяются после загрузки окна ([`RestoreLastSelection`](Configuration Management/MainWindow.xaml.cs)).

## [0.2.7.9] — 2026-08-18

### Исправлено

- **Панель отборов по тегам обновляется после правки и удаления тега**: при удалении тега из базы или изменении набора тегов через окно редактирования базы облако тегов на панели отборов пересобирается, а теги, которых больше нет ни на одной базе, автоматически убираются из активного фильтра. Ранее отбор по удалённому тегу «зависал»: чип исчезал из панели, но фильтр продолжал применяться и скрывал базы, при этом снять его было невозможно. Реализовано в [`PruneActiveTagFilters`](Configuration Management/ViewModels/MainViewModel.cs) и вызовах после [`RemoveTag`](Configuration Management/ViewModels/MainViewModel.cs), [`AddTag`](Configuration Management/ViewModels/MainViewModel.cs), [`AddTagInline`](Configuration Management/ViewModels/MainViewModel.cs) и [`EditInfobase`](Configuration Management/ViewModels/MainViewModel.cs).

## [0.2.7.8] — 2026-08-18

### Исправлено

- **Сортировка групп теперь работает и для корневых групп**: при нажатии кнопок «Сортировать по возрастанию/убыванию» упорядочиваются и корневые группы, и все вложенные подгруппы рекурсивно (ранее менялся порядок только вложенных групп). Сортировка выполняется в [`RebuildGroupTree`](Configuration Management/ViewModels/MainViewModel.cs) и применяется сразу.
- **Кнопки сортировки больше не обрезаются**: ширина колонки кнопок «развернуть/свернуть/сортировать» в заголовке увеличена до 144 px, чтобы все четыре кнопки в стиле Material Design помещались полностью.

## [0.2.7.7] — 2026-08-18

### Добавлено

- **Кнопки сортировки групп** в заголовке списка баз (слева, рядом с «Развернуть/Свернуть все группы»): **«Сортировать группы по возрастанию»** (А→Я) и **«Сортировать группы по убыванию»** (Я→А). Кнопки выполнены в стиле **Material Design** и сортируют подгруппы по имени рекурсивно — включая вложенные группы («группа в группе»). Выбранное направление запоминается и применяется при последующих перестройках дерева.
- В [`GroupNodeViewModel`](Configuration Management/ViewModels/GroupNodeViewModel.cs) добавлен метод `SortChildrenRecursive`, в [`MainViewModel`](Configuration Management/ViewModels/MainViewModel.cs) — команды `SortGroupsAscendingCommand` / `SortGroupsDescendingCommand`.

## [0.2.7.6] — 2026-08-18

### Изменено

- **Веб-клиент доступен только при подключении через веб-сервер**: если тип подключения базы не «Веб-сервер», пункт «Веб-клиент» во вкладке **«Запуск»** настроек подключения недоступен (ранее был доступен и для клиент-серверной базы). При выборе несовместимого типа подключения режим запуска сбрасывается в «Автоматический».

## [0.2.7.5] — 2026-08-18

### Добавлено

- **Толстый клиент разделён по режиму форм** — во вкладке **«Запуск»** настроек подключения базы добавлен пункт **«Толстый клиент (обычные формы)»**, а существующий **«Толстый клиент»** теперь запускает **управляемые формы** (`/RunModeManagedApplication`). Раньше толстый клиент всегда открывал обычные формы, из-за чего база на управляемых формах запускалась в обычных.
- В [`OneCLauncher`](Configuration Management/Services/OneCLauncher.cs) режим форм задаётся явно (тип `OneCRunMode` и перегрузка запуска), а не выводится из типа клиента: тонкий → управляемые, толстый → управляемые/обычные по выбору.

## [0.2.7.4] — 2026-08-18

### Добавлено

- **Общая настройка разрядности по умолчанию** в **Настройки → Платформы**: если у информационной базы не указана собственная разрядность, запуск выполняется выбранной разрядностью — **64 бит (рекомендуется)** или **32 бит**. По умолчанию — **64 бит (x64)**. Изменение вступает в силу сразу и применяется для последующих запусков, а также для пакетных операций Конфигуратора (выгрузка/тест) и ярлыков.

## [0.2.7.3] — 2026-08-18

### Изменено

- Кнопки **«Выбрать все»** и **«Снять все»** в окне **«Очистка кэша 1С»** переработаны: убраны чекбоксы (квадратики) слева от иконок — теперь это компактные кнопки с иконкой и подписью, которые **подсвечиваются фоном при наведении** (и слегка прижимаются при нажатии). Логика выбора/снятия всех баз сохранена.

## [0.2.7.2] — 2026-08-18

### Добавлено

- В окне **«Очистка кэша 1С»** добавлено **поле поиска базы**: фильтрует список баз по имени (и строке подключения) прямо во время ввода.
- «Снять все» теперь выполнено **чекбоксом в том же стиле, что и «Выбрать все»** (единообразный вид элементов управления списком баз).

### Изменено

- Чекбоксы **«Программный кэш»** и **«Пользовательский кэш»** визуально разнесены — между ними добавлен разделитель и увеличен отступ, чтобы они не выглядели слипшимися.

## [0.2.7.1] — 2026-08-18

### Добавлено

- В окне **«Очистка кэша 1С»** добавлена ссылка **«Снять все»** — быстро снимает выделение со всех баз списка (полезно, когда часть баз уже отмечена и чекбокс «Выбрать все» находится в промежуточном состоянии).

### Изменено

- Чекбоксы окна (тип кэша, «Выбрать все», список баз) оформлены в стиле **Material Design**: скруглённый квадрат с цветной заливкой, галочкой или тире (промежуточное состояние) и подсветкой рамки при наведении.

## [0.2.7.0] — 2026-08-18

### Добавлено

- **Очистка кэша разделена на два независимых действия**: **«Очистка программного кэша»** (каталог `%LOCALAPPDATA%\1C\1cv8…`) и **«Очистка пользовательского кэша»** (каталог `%APPDATA%\1C\1cv8…`). Это разные данные, по-разному влияющие на информационные базы.
- Кнопка **«Очистить кэш»** в правой панели и пункт контекстного меню базы стали **выпадающим меню** с выбором: программный кэш / пользовательский кэш / **оба одновременно**.
- Новое окно **«Очистка кэша 1С»**: позволяет выбрать тип кэша (программный, пользовательский или оба) и **набор информационных баз**, для которых выполнить очистку, включая пункт **«Выбрать все базы»** и счётчик выбранных баз. Очистка по прежнему точечная — по ID (GUID) базы.

### Изменено

- Сервис [`OneCCacheCleaner`](Configuration Management/Services/OneCCacheCleaner.cs) расширен: добавлен тип кэша `OneCCacheKind` и поддержка очистки сразу нескольких баз.

## [0.2.6.39] — 2026-08-18

### Добавлено

- **Гиперссылки-вопросы «?» в интерфейсе** — переиспользуемый элемент [`HelpLink`](Configuration%20Management/Controls/HelpLink.xaml): круглый значок «?» рядом с ключевыми и неочевидными элементами формы. По клику открывается всплывающая подсказка с описанием поведения элемента и способов взаимодействия с ним (закрывается повторным кликом или кликом в любом другом месте окна). Добавлены в: шапку и список баз главного окна, панель отбора по тегам, блок «Текущая сессия», окно настроек (горячие клавиши, синхронизация ibases.v8i), настройки подключения базы («Вставить строку подключения»), мастер «Что добавить?», окно группы (родительская группа), окно выбора родительской группы, создание/удаление ИБ и конфигуратор параметров запуска.

## [0.2.6.38] — 2026-08-18

### Добавлено

- **Свободное назначение горячих клавиш действий** по аналогии с конфигуратором 1С: в **Настройки → Клавиши** вместо выпадающего списка используются поля записи комбинации. Достаточно установить курсор в поле и нажать нужное сочетание — поддерживаются **Ctrl, Shift, Alt и Win** (например `Ctrl+Shift+F`). `Backspace`/`Delete` сбрасывают назначение, `Esc` отменяет ввод.
- **Горячие клавиши для вкладок списка баз**: отдельные комбинации для переключения на **«Все базы»**, **«Избранное»** и **«Недавние»** (по умолчанию не назначены, задаются в окне **Настройки → Клавиши**).
- Проверка уникальности комбинации теперь распространяется и на новые горячие клавиши вкладок (одна комбинация — одно действие).

### Изменено

- Сохранённые горячие клавиши (`Ctrl+…`/`Shift+…`/`Alt+…`) полностью совместимы с прежними значениями и применяются сразу после сохранения настроек.

## [0.2.6.36] — 2026-08-18

### Изменено

- Во вкладках **«Избранное»** и **«Недавние»**, а также при **отборе по тегу** (и при **поиске**) группы и закреплённые базы **временно скрываются** — вместо дерева групп показывается единый плоский список найденных баз. Это устраняет дубли, когда закреплённая база попадала и в узел «Закреплённые», и в свою группу. При возврате к вкладке «Все базы» дерево групп и закрепления восстанавливаются.

## [0.2.6.35] — 2026-08-18

### Исправлено

- **Окно выбора родительской группы**: иконка группы теперь отображается на цвете самой группы (как в списке баз), поэтому она **видна и в светлой, и в тёмной теме** и совпадает с тем, как иконка выглядит у самой группы. Ранее иконка рисовалась белым цветом на фоне темы и в светлой теме была не видна.

## [0.2.6.34] — 2026-08-18

### Добавлено

- **Выбор родительской группы** переработан по аналогии с выбором установленной версии платформы: окно **«Выбор родительской группы»** выполнено в едином стиле (зелёная кнопка «Выбрать», «Отмена» с красной обводкой, двойной щелчок — подтверждение).
- В окне выбора родительской группы для каждой группы отображаются её **цвет** и **иконка** (как задано в настройках группы), чтобы родительскую группу было легко узнать.

### Изменено

- Из окна настройки группы убрана кнопка **«Без родителя»** — теперь сделать группу корневой можно пунктом «Корневая группа» в окне выбора родительской группы. Освободившееся место позволило **увеличить поле названия родительской группы**.
- Окно настройки группы автоматически подстраивает высоту под содержимое активной вкладки (с ограничением по высоте экрана), чтобы **все данные во вкладках помещались на экран**.

## [0.2.6.33] — 2026-08-18

### Изменено

- Блок **«Вид узла "Без группы"»** убран из **Настройки → Отображение → Панели** — теперь оформление узла «Без группы» (цвет заголовка, цвет иконки и иконка) меняется только через **«Изменить настройки»** в главном окне, как у обычных групп.
- В окне настройки узла «Без группы» показываются те же вкладки, что и у обычной группы (**Основные / Цвет / Иконка**), но поля **«Наименование»** и **«Родительская группа»** (а также описание) заблокированы — изменить их нельзя.

## [0.2.6.32] — 2026-08-18

### Добавлено

- **Настройку узла «Без группы»** (базы без группы, корневой уровень) теперь можно менять прямо из главного окна по аналогии с обычной группой: команда **«Изменить настройки»** для узла «Без группы» открывает окно с вкладками **«Цвет»** и **«Иконка»**. Цвет заголовка, цвет иконки и иконка сохраняются в настройках приложения (единые с блоком **Настройки → Отображение → Панели → Вид узла «Без группы»**).

## [0.2.6.31] — 2026-08-18

### Добавлено

- Окно **настройки группы** разделено на **горизонтальные вкладки** «Основные / Цвет / Иконка» по аналогии с окном настроек (равная ширина, акцентная линия снизу, кнопки «Сохранить»/«Отмена» закреплены внизу).
- Реализована возможность **изменять группу без названия** — сохранение больше не требует наименования; группы без имени (например, импортированные из `ibases.v8i`) можно редактировать. В дереве такие группы отображаются как **«Без названия»**.

## [0.2.6.30] — 2026-08-18

### Добавлено

- В окне **Настройки → Отображение → Панели** добавлен переключатель **«Показывать пустые группы (без баз)»** — пустые группы отображаются в дереве в своей иерархии (внутри своей родительской группы).
- В настройках появился блок **«Вид узла "Без группы"»**: цвет заголовка, цвет иконки и иконка служебного узла, в который попадают базы без группы.

### Изменено

- Кнопки **«Сохранить»** и **«Отмена»** в окне добавления/редактирования группы окрашены в едином стиле с настройками: зелёная «Сохранить» и «Отмена» с красной обводкой.

## [0.2.6.29] — 2026-08-18

### Добавлено

- В окне группы кнопка **«Без родителя»** (корневая группа).
- Переключатель **пустых групп** на верхней панели (рядом с «показывать группы»).

### Исправлено

- Окно **добавления/редактирования группы**: вертикальная прокрутка, фиксированные кнопки «Сохранить»/«Отмена» внизу (больше не «уезжают» за экран).

## [0.2.6.28] — 2026-08-17

### Исправлено

- Вкладка **Платформа**: сломанная вёрстка (поля конфигурации оказались внутри строки версии) — отдельные строки «Версия», «Конфигурация», «Параметры».
- После **Выбрать…** версия платформы снова подставляется в поле; разрядность обновляется только если в выборе указаны (32)/(64).

## [0.2.6.27] — 2026-08-17

### Изменено

- Выпадающие списки **сервер** и **порт** в свойствах базы используют тот же стиль `ModernComboBox`, что и выбор горячих клавиш (скругление, пункты с подсветкой). Редактируемый ввод сохранён (`IsEditable` + `PART_EditableTextBox`).

## [0.2.6.26] — 2026-08-17

### Исправлено

- Ошибки привязки `IsSelected` в окне выбора версии платформы: у `PlatformVersionGroup` добавлено свойство `IsSelected` (стиль `ModernTreeViewItem`).

## [0.2.6.25] — 2026-08-17

### Исправлено

- В контекстном меню трея снова **видны иконки**. PackIcon вне WPF-дерева давал пустой bitmap; иконки рисуются GDI+ по тем же символам MD (`Play`, `Wrench`, `Database`, `Cog`, `Sync`, `ExitToApp`, `Application`).

## [0.2.6.24] — 2026-08-17

### Изменено

- Иконки **контекстного меню трея** рисуются теми же **MaterialDesign PackIcon**, что и в главном окне (`Play`, `Wrench`, `Database`, `Cog`, `Sync`, `ExitToApp` и т.д.).

## [0.2.6.23] — 2026-08-17

### Исправлено

- Подписи кнопок **1С:Предприятие**, **Конфигуратор**, **1С:Стартер** больше не обрезаются: убран `ClipToBounds`, уменьшены отступы и ширина кнопки ▼.

## [0.2.6.22] — 2026-08-17

### Добавлено

- **Двойной щелчок** по ячейке «Версия платформы» в списке баз открывает окно выбора версии (без полного редактирования свойств).

## [0.2.6.21] — 2026-08-17

### Исправлено

- Ошибки компиляции в `ConfigurationInfoService`: добавлен `using System.IO`, тип подключения `ConnectionType.ClientServer`.

## [0.2.6.20] — 2026-08-17

### Добавлено

- **Авточтение имени и версии конфигурации** через COM `V83.COMConnector` (и эвристика по `1Cv8.1CD` для версии). Фоново при запуске и по «Обновить».

### Изменено

- Кнопки **1С:Предприятие** / **Конфигуратор**: единый split-блок (основное действие + ChevronDown), без «оторванной» кнопки меню.
- **Иконки меню трея** перерисованы в стиле Material Design (Play, БД, шестерёнка и т.д.) ближе к иконкам главного окна.

## [0.2.6.19] — 2026-08-17

### Добавлено

- Колонка **«Конфигурация»** (название и версия конфигурации 1С) в списке баз; переключатель в **Настройки → Отображение**.
- Поля `ConfigurationName` / `ConfigurationVersion` у базы (сохраняются в JSON).

### Изменено

- Обновлены **иконки приложения и трея** (чистый градиент, цилиндр БД, акцентный бейдж).
- Улучшены **контекстные меню** запуска: больше отступы, MinWidth, тень, стиль `ModernContextMenu` на меню кнопок 1С/Конфигуратор.

## [0.2.6.18] — 2026-08-17

### Исправлено

- Ошибка компиляции **CS1503**: `LaunchCommand` передаёт лямбду `p => Launch(p)` вместо method group (у `Launch` появился необязательный параметр `runAsAdmin`).

## [0.2.6.17] — 2026-08-17

### Исправлено

- Ошибка компиляции **CS1501**: у `OneCLauncher.Launch` добавлен параметр `runAsAdmin` (5-й аргумент) — сигнатура совпадает с вызовом из `OneCLauncherService`.

## [0.2.6.16] — 2026-08-17

### Добавлено

- Кнопки **1С:Предприятие** и **Конфигуратор** — выпадающее меню (как StartManager): запуск с выбором параметров, с аутентификацией (только Предприятие), **от имени администратора**.
- Кнопка **1С:Стартер** — открывает родной `1CEStart.exe` для сверки списка баз.

## [0.2.6.15] — 2026-08-17

### Изменено

- В **компактной правой панели** разделители между блоком действий, кнопкой «По ссылке» и «Выход» отображаются так же, как в полной панели.

## [0.2.6.14] — 2026-08-17

### Исправлено

- Ошибка компиляции **CS0246**: добавлен `using System.Windows.Controls` в `ConnectionSettingsWindow.xaml.cs` (тип `PasswordBox`).

## [0.2.6.13] — 2026-08-17

### Исправлено

- **Двусторонняя синхронизация с ibases.v8i**: при выгрузке удаляются из файла базы, которых нет в приложении; при загрузке удаляются из приложения базы с ID 1С, которых нет в файле. В режиме «Двусторонняя» сначала выгрузка, затем загрузка.
- **Логин и пароль** больше не затираются при импорте из ibases.v8i, если в файле они пустые.
- **Пароль** вводится в `PasswordBox` (скрытые символы) в настройках подключения и в параметрах запуска.

## [0.2.6.12] — 2026-08-17

### Изменено

- В **окне выбора платформы** и в **Настройки → Платформы** у узлов дерева свои значки: папка (линия 8.3), открытая папка (группа сборок 8.3.27), зелёный куб (x64), фиолетовый куб (x32), голубой значок приложения (без разрядности).

## [0.2.6.11] — 2026-08-17

### Изменено

- **Окно выбора версии платформы** приведено к виду стартера 1С: фильтр **Все / x32 / x64**, сортировка **A→Z / Z→A**, дерево **8.3 → 8.3.27 → 8.3.27.2214 (x64)** (линия → группа сборок → полная версия).

## [0.2.6.10] — 2026-08-17

### Изменено

- **Новые иконки приложения и трея**: современный скруглённый тайл с градиентом (indigo → blue → cyan), белый цилиндр БД и янтарный бейдж с галочкой. Файлы `app.ico` (16–256 px) и `tray.ico` (16–64 px). Векторная `AppIcon` в `App.xaml` обновлена в том же стиле. Трей загружает `tray.ico` с приоритетом.

## [0.2.6.9] — 2026-08-17

### Исправлено

- В **окне выбора платформы** и в **Настройки → Платформы** группировка по линии **8.2 / 8.3 / 8.5** сразу развёрнута (линия → разрядность → сборка), учитываются дополнительные пути поиска платформ.

### Отменено

- Группировка **основного списка баз** по версии платформы (кнопка «8.x» и настройка) — не требуется; группировка 8.x нужна только в выборе платформы.

## [0.2.6.8] — 2026-08-17

### Исправлено

- Значок **избранного (★)** в строке базы выровнен по горизонтали с **иконкой папки** родительской группы: отступ блока ★ / 📌 / название = `(Level − 1) × 18 + 26` (26 px — ширина кнопки разворота группы).

## [0.2.6.7] — 2026-08-17

### Исправлено

- **Выравнивание колонок данных** в списке баз при вложенных группах: колонки версии, режима запуска, сервера/пути, даты запуска и размера больше не смещаются относительно заголовков таблицы. Отступ `ItemsPresenter` убран; иерархия показывается сдвигом только нужных элементов.
- **Уровень вложенности (`Level`)** корректно считается для вложенных узлов: добавлен `LeveledTreeViewItem`, который выставляет `Level = parent.Level + 1` при создании дочерних контейнеров (раньше `Level` оставался 0 у всех вложенных строк).
- **Кнопка разворота группы (Expander)** сдвигается по `Level × 18 px`, заголовок группы с выделением оказывается сразу после неё — иерархия «группа в группе» читается явно.
- **Блок ★ / 📌 / название базы** собран в один горизонтальный ряд без лишних промежутков; отступ для баз — `(Level − 1) × 18 px`, старт с колонки 0, чтобы названия оказывались **под иконкой папки** родительской группы, а не правее неё.
- Теги в строке базы используют тот же отступ, что и блок названия.

## [0.2.6.6] — 2026-08-17

### Исправлено

- Исправлено отображение **смещения вложенных групп** в дереве списка баз («группа в группе»). Раньше заголовки вложенных групп сдвигались через `RenderTransform`, который ненадёжно пересчитывался при виртуализации списка и не влиял на компоновку, из-за чего было непонятно, что одна группа находится внутри другой. Теперь отступ вложенности для заголовков групп применяется через `Margin` (влияет на раскладку и корректно обновляется), поэтому заголовки групп на каждом уровне наглядного сдвигаются вправо, и иерархия «группа в группе» читается явно.

## [0.2.6.5] — 2026-08-17

### Исправлено

- Исправлена ошибка **перетаскивания группы в другую группу**: раньше информационные базы, находящиеся в перетаскиваемой группе (и во вложенных подгруппах), теряли свою группу и «уезжали» в **«Без группы»**. Причина — в пересчёте путей `Infobase.Group`: смешивались нормализованные и ненормализованные представления пути, а для самой перемещаемой группы маппинг старого пути на новый мог отсутствовать. Из-за этого итоговый путь базы не совпадал с путём группы в дереве. Теперь:
  - маппинг «старый путь → новый путь» для **самой перемещаемой группы** добавляется гарантированно;
  - префиксная обработка вложенных путей выполняется по единому каноническому (нормализованному) пути;
  - добавлен фолбэк: если путь базы относится к перемещаемой подветке (сама группа или вложенная), он пересчитывается по старому корневому пути.
  В результате базы корректно переносятся вместе со своей группой и не попадают в «Без группы».

## [0.2.6.4] — 2026-08-17

### Изменено

- Кнопки **«Развернуть все группы»** и **«Свернуть все группы»** возвращены в **заголовок таблицы списка баз** (отдельная колонка слева), откуда они ранее были перенесены в верхнюю панель. Возвращена и компенсирующая колонка в строках данных, чтобы колонки строк и заголовка были выровнены.

## [0.2.6.2] — 2026-08-17

### Изменено

- Исправлено **изменение ширины колонок списка баз** в главном окне:
  - Теперь **все** колонки — «Название», «Версия платформы», «Режим запуска», «Сервер/База», «Последний запуск» и «Размер» — можно изменять по ширине, перетаскивая разделитель на правой границе колонки. Ранее часть колонок нельзя было растянуть (в частности, когда соседняя колонка была скрыта или не имела разделителя).
  - При перетаскивании разделителя **данные двигаются синхронно с заголовком** (раньше разделитель мог двигать только заголовок, оставляя данные на месте).
  - Исправлено **смещение данных влево относительно заголовков колонок**, возникавшее при включении новых колонок и при включённых кнопках «развернуть/свернуть все группы»: колонки строк теперь выровнены с колонками заголовка.

## [0.2.6.1] — 2026-08-16

### Добавлено

- В **правой панели** главного окна в самом низу добавлена кнопка **«Выход»**. Она **всегда полностью завершает работу приложения**, игнорируя настройку **«Закрывать в трей»** — в отличие от обычного закрытия окна (крестиком), которое при включённой опции сворачивает программу в трей. Это удобно, когда включено поведение «свернуть в трей» и нужно быстро полностью выйти из программы без открытия контекстного меню значка в трее.

## [0.2.5.99] — 2026-08-16

### Изменено

- В окне **«Настройки подключения»** (используется и при создании новой базы, и при правке свойств) на вкладке **«Подключение»** поле **«Порт сервера»** теперь оформлено как **редактируемый выпадающий список** — по аналогии с полем **«Сервер»**. Список наполняется портами из существующих клиент-серверных баз списка (без дублей, по возрастанию). Это удобно, когда сервер один, а инстансы висят на разных портах: порт можно выбрать из уже используемых или ввести вручную, не обращаясь к вводу строки подключения вручную.

## [0.2.5.98] — 2026-08-16

### Изменено

- В окне **«Настройки подключения»** (используется и при создании новой базы, и при правке свойств) кнопка **«Вставить строку подключения»** перенесена из вкладки «Подключение» в **нижнюю панель окна, слева**, поэтому она **всегда доступна** независимо от выбранной вкладки.
- По клику кнопка открывает **отдельное окно ввода строки подключения** с полем для ввода и кнопкой **«Вставить из буфера»** (читает строку из буфера обмена). Если в буфере обмена лежит строка, удовлетворяющая критериям ссылки на информационную базу (строка подключения 1С с параметрами `Srvr`/`Ref`/`File`/`WS`, ссылка-протокол `e1c://`, веб-адрес `http(s)://`, путь к файловой базе или `server\base`), она **автоматически подставляется** в поле при открытии окна.
- Кнопка **«Применить»** в окне ввода разбирает строку подключения 1С и заполняет поля настроек базы (тип подключения, сервер/порт, имя базы, путь файла или URL веб-публикации, пользователь и пароль). Если наименование базы не задано, подставляется имя базы (`Ref`) или каталога файловой базы.
- При открытии окна ввода поле заполняется текущей строкой подключения базы, если она задана.

## [0.2.5.96] — 2026-08-16

### Изменено

- Разделены переключатели видимости тегов:
  - **Верхняя кнопка «Показывать теги»** включает/выключает и **панель быстрого отбора тегов** (облако тегов над списком), и **теги в строках списка баз**.
  - **Кнопка тегов в заголовке списка баз** (значок тега слева от колонки «Название») включает/выключает **только теги в строках списка баз** и не затрагивает панель быстрого отбора тегов вверху. Верхняя кнопка при этом остаётся включённой, пока панель тегов сверху видна.

## [0.2.5.95] — 2026-08-16

### Изменено

- Переключатель видимости тегов в **заголовке списка баз** (значок тега слева от колонки «Название») теперь влияет **только на отображение тегов в строках списка** и больше не затрагивает настройку «Показывать панель быстрого отбора по тегам» (облако тегов над списком). Верхняя кнопка «Показывать теги» на верхней панели по-прежнему включает/выключает теги **и в списке, и в панели быстрого отбора**. Панель быстрого отбора по тегам управляется отдельно: верхней кнопкой «теги» и настройкой в окне настроек.

## [0.2.5.94] — 2026-08-16

### Изменено

- Переключатель видимости тегов в **заголовке списка баз** (значок тега слева от колонки «Название») теперь влияет **только на отображение тегов в строках списка** и больше не затрагивает настройку «Показывать панель быстрого отбора по тегам» (облако тегов над списком). Панель быстрого отбора по тегам теперь управляется исключительно своей отдельной настройкой в окне настроек и не меняется при переключении видимости тегов в списке.

## [0.2.5.93] — 2026-08-16

### Добавлено

- В окне **«Настройки подключения»** (используется и при **создании новой базы**, и при **правке свойств** существующей) на вкладке **«Подключение»** добавлена кнопка **«Вставить строку подключения»**. При нажатии строка подключения 1С читается из **буфера обмена** и разбивается по полям:
  - `File="C:\path"` → тип **«Файловая база»** + поле **«Путь к файлу»**;
  - `Srvr="host";Ref="base";Usr="user";Pwd="pass"` → тип **«Сервер 1С:Предприятия»** + поля **«Сервер»** (с выделением порта в отдельное поле), **«Имя базы»**, **«Пользователь»**, **«Пароль»**;
  - `WS="http://server/base"` → тип **«Веб-сервер»** + поле **«URL публикации»**.
  Если поле **«Наименование»** не заполнено, оно автоматически подставляется из имени базы (`Ref`) или имени каталога файловой базы.

## [0.2.5.92] — 2026-08-16

### Изменено

- В строке заголовков списка баз рядом с командами **«Развернуть/свернуть все группы»** неактивный значок «Избранное» (★) заменён на **переключатель видимости тегов** — значок тега (🏷). Нажатие включает/выключает отображение тегов баз в списке, синхронно с настройкой «Показывать теги» на верхней панели и панелью быстрого отбора по тегам. Состояние сохраняется в настройках приложения.

## [0.2.5.91] — 2026-08-16

### Исправлено

- Исправлен выбор **версии платформы 1С** при запуске базы. Раньше при выборе установленной версии использовалось **строковое сравнение** номеров сборок, из-за чего платформа с номером `8.3.10.x` ошибочно считалась **младше** `8.3.9.x` и запускалась не та (более старая) версия. Теперь версии сравниваются **численно по сегментам** (`8.3.10` > `8.3.9`), и всегда запускается действительно новейшая установленная сборка нужной разрядности.
- Исправлен выбор **разрядности платформы** при запуске. Если в поле «Версия платформы» базы явно указана конкретная сборка с суффиксом разрядности (например `8.3.27.1688 (64)`), этот суффикс ранее **игнорировался** — разрядность бралась только из настройки «Разрядность» (по умолчанию «приоритет 32»), и платформа могла запуститься в 32-битной сборке, хотя пользователь выбрал именно 64-битную. Теперь явно указанный суффикс разрядности в версии платформы имеет **приоритет** над настройкой разрядности.

## [0.2.5.90] — 2026-08-16

### Изменено

- Исправлена навигация по списку баз **стрелками** `↑`/`↓`/`←`/`→`. Раньше при фокусе на строке дерева стрелки «прыгали» на кнопки внутри строки (избранное, закрепление, теги), и выделение двигалось непредсказуемо. Теперь стрелки всегда перемещают выделение **по строкам дерева**:
  - `↑`/`↓` — переход на предыдущую/следующую видимую строку (базу или группу) с учётом развёрнутости групп;
  - `←`/`→` — сворачивание/разворачивание выбранной группы.
- Клик по строке базы или группы в списке переводит клавиатурный фокус на эту строку, чтобы последующие нажатия стрелок управляли выделением в дереве, а не кнопками строки.

## [0.2.5.89] — 2026-08-16

### Изменено

- В окне **«Настройки подключения»** поле **«Сервер»** на вкладке «Подключение» теперь является **выпадающим списком** (можно выбрать или ввести вручную). Список содержит серверы 1С, используемые в других клиент-серверных базах текущего списка (без дублей, отсортированы по алфавиту). Это ускоряет ввод и исключает опечатки при указании существующих серверов.
- Для поля «Сервер» отключён кастомный стиль `ModernComboBox` (его шаблон не содержит `PART_EditableTextBox` и блокировал ручной ввод): используется стандартный редактируемый `ComboBox`, который поддерживает как выбор из списка, так и ввод имени сервера вручную.

## [0.2.5.88] — 2026-08-16

### Изменено

- В окне **«Настройки подключения»** вкладки переведены в **вертикальный вид (слева)**, как в окне «Настройки»: акцентная полоса и подсветка выбранной вкладки, названия выровнены по левому краю. Набор вкладок не изменился: **База / Подключение / Аутентификация / Запуск / Разрядность / Платформа / Идентификатор**. Ширина окна уменьшена, т.к. горизонтальная панель вкладок больше не требуется.

## [0.2.5.87] — 2026-08-16

### Изменено

- В окне **«Настройки подключения»** вкладка **«Подключение»** разделена на вкладки **«База»** (наименование, группа, описание), **«Подключение»** (тип и параметры подключения) и **«Аутентификация»** — прокрутка на этих вкладках больше не нужна.
- Вкладка **«Запуск»** разделена на вкладки **«Запуск»** (режим клиента), **«Разрядность»** и **«Платформа»** (версия платформы и параметры запуска). Каждая вкладка теперь содержит один логический блок без вертикальной прокрутки; общее количество вкладок окна — **База / Подключение / Аутентификация / Запуск / Разрядность / Платформа / Идентификатор**.

## [0.2.5.86] — 2026-08-16

### Изменено

- В окне «Настройки» вкладка **«Дополнительно»** переименована в **«Базы»** (список информационных баз: каталоги шаблонов, экспорт/загрузка, обслуживание, опасные операции).
- Блок **«Поведение приложения»** (разрешение нескольких экземпляров, значок в трее, сворачивание в трей, `Esc` → трей) вынесен из вкладки **«Клавиши»** в отдельную главную вкладку **«Настройки»**. Теперь вкладка «Клавиши» содержит только горячие клавиши и порядок избранного `Alt+1…9`.

## [0.2.5.85] — 2026-08-16

### Изменено

- В окне «Настройки» вкладка **«Клавиши»** (горячие клавиши запуска, порядок избранного `Alt+1…9` и поведение приложения) вынесена из подвкладок «Отображение» в **отдельную главную вкладку** — теперь она расположена ниже вкладки «Отображение».

## [0.2.5.84] — 2026-08-16

### Добавлено

- Кнопка **«Перейти по ссылке»** в правой панели (после всех кнопок действий) — аналог одноимённой функции стандартного загрузчика 1С: открывает поле ввода, куда можно вставить ссылку на информационную базу, и запускает её в «1С:Предприятие».
  - Поддерживаемые форматы ссылки: ссылка-протокол `e1c://...` (передаётся стандартному загрузчику 1С — обработчику протокола), файловая база (`C:\1C\База` или `File="C:\1C\База"`), клиент-серверная (`server\База` или `Srvr="server";Ref="База"`), веб-клиент (`http://server/base`, `https://server/base`).
- В окне «Перейти по ссылке» кнопки **«Перейти»** и **«Отмена»** выполнены выраженными (как в настройках): акцентная зелёная основная кнопка и вторичная красная кнопка отмены.

## [0.2.5.83] — 2026-08-16

### Добавлено

- **`Ctrl+F`** — фокус в поле поиска (с выделением текущего текста) из любого места главного окна, в т.ч. когда фокус в другом поле ввода.

## [0.2.5.82] — 2026-08-14

### Изменено

- Меню трея: убран зазор между пунктом базы и подменю «1С:Предприятие / Конфигуратор» (подменю сдвигается вплотную).

## [0.2.5.81] — 2026-08-14

### Исправлено

- Иконка в системном трее: `Icon` назначается **до** `Visible` (иначе Win10/11 может не показать); fallback-создание NotifyIcon; `app.ico` как EmbeddedResource; размер 16×16.

## [0.2.5.80] — 2026-08-14

### Исправлено

- Промежуток между названиями баз и панелью действий: колонка **Имя** растягивается (`*`), убрана пустая колонка-заполнитель справа в строках списка.

## [0.2.5.79] — 2026-08-14

### Изменено

- Убран лишний зазор между списком баз и панелью действий (компактный режим): меньше отступы, без двойной рамки.

## [0.2.5.78] — 2026-08-14

### Исправлено

- Иконка в трее при **PublishSingleFile**: берётся из иконки exe (`ExtractAssociatedIcon`), WPF-ресурса `app.ico` и файла рядом с приложением — больше не пропадает.

## [0.2.5.77] — 2026-08-14

### Изменено

- Шаблоны конфигураций: разбор **1cv8.mft** (как в стартере 1С) — дерево по полю **Catalog** (через «/»), Source → .cf/.dt.
- Без манифеста — запасной обход Vendor\Config\Version.
- Убрана искусственная группировка «Конфигурации / Демо / Пустые» поверх путей.

## [0.2.5.76] — 2026-08-14

### Изменено

- Меню трея: современный вид — иконки у пунктов, Segoe UI, скруглённый hover, заголовки секций, цветные разделители.

## [0.2.5.75] — 2026-08-14

### Изменено

- Диалог **«Выбор версии платформы»** — тот же вид, что в Настройки → Платформы (иконки, карточки, путь к сборке).

## [0.2.5.74] — 2026-08-14

### Изменено

- Убрана кнопка **«+» (добавить)** из верхней панели (рядом с Все / Избранное / Недавние). Добавление остаётся в правой панели и в контекстном меню.

## [0.2.5.73] — 2026-08-14

### Изменено

- Выбор платформы **везде одинаково**: линия (8.3 / 8.5) → разрядность (64/32) → сборка **с путём** (Настройки, диалог выбора, создание ИБ).
- Настройки → Платформы: та же иерархия, что в «Выбор версии платформы».
- Заголовок колонки «Название» привязан к той же ширине, что и строки списка.

## [0.2.5.72] — 2026-08-14

### Добавлено

- Удаление ИБ: диалог со сведениями о базе и галочкой **«Физически удалить каталог базы с диска»** (только файловые, с повторным подтверждением).

### Исправлено

- Сброс фильтра тегов: чипы снова снимаются (RefreshTagFilterItems при Clear / Toggle).
- Очистка поиска: отменяется debounce, фильтр не «возвращается».
- Поиск учитывает путь файловой базы и строку подключения (сервер/Ref).

## [0.2.5.71] — 2026-08-14

### Добавлено

- Создание ИБ из шаблона: **группировка** шаблонов в дереве — Демо / Пустые / Разработчик → Конфигурация → Версия.

### Изменено

- Список шаблонов заменён на TreeView с иерархией (как в стартере 1С).

## [0.2.5.70] — 2026-08-14

### Исправлено

- Кнопки «Развернуть / свернуть все группы» в заголовке списка: вынесены в отдельную колонку фиксированной ширины (56 px), больше не обрезаются.

## [0.2.5.69] — 2026-08-14

### Добавлено

- Настройки → Дополнительно: кнопка **Изменить…** для каталога шаблонов.
- Иконки на кнопках «Добавить / Изменить / Удалить / Из 1С» в блоке каталогов шаблонов.

### Исправлено

- Настройки → Дополнительно: добавлена прокрутка (ScrollViewer), контент больше не обрезается.
- Кнопки «Развернуть / свернуть все группы» в заголовке списка: больше не пропадают после уменьшения отступа у листьев (резерв ширины колонки).

### Изменено

- Блок «Каталоги шаблонов» в Дополнительно: список выше, кнопки с иконками, логичнее расположен.

## [0.2.5.68] — 2026-08-14

### Исправлено

- Создание ИБ: кнопка **Выбрать…** для группы.
- Настройки: выделение вкладки слева не обрезается снизу.

## [0.2.5.67] — 2026-08-14

### Исправлено

- Список баз: убрано пустое место слева (expander у листьев Collapsed, меньший отступ вложенности).
- В заголовке колонок видны иконки ★ и 📌.

## [0.2.5.66] — 2026-08-14

### Изменено

- Меню трея: **недавние базы** (до 7) с подменю «1С:Предприятие» / «Конфигуратор»; убраны неочевидные пункты «Запустить выбранную…».

## [0.2.5.65] — 2026-08-14

### Исправлено

- Кнопки ОК/Отмена в конфигураторе параметров запуска — корректная строка снизу (Grid.Row).
- Убрана команда **«Тестирование ИБ»** из контекстного меню.

## [0.2.5.64] — 2026-08-14

### Добавлено

- Каталоги шаблонов — список в настройках (Дополнительно), кнопка «Из 1С».
- Esc → в трей (настройка «По Esc сворачивать в трей»).
- Вкладка **О программе** (Sivatorov, Infostart, GitHub).

### Исправлено

- Поле тега закрывается по **Esc** и при клике вне поля.
- Меньше пустого места перед кнопкой избранного.
- Окно создания ИБ — прокрутка, элементы не обрезаются.

## [0.2.5.63] — 2026-08-14

### Изменено

- Выбор платформы: группировка по линии **8.3 / 8.5** и по **разрядности** (64/32).
- Поиск платформ: рекурсивный обход доп. путей (E:\1cPlatform), определение 32/64 по PE.
- Поле поиска **без тегов**; у панели тегов кнопка **Очистить**.
- Сообщение синхронизации скрывается через **10 секунд**.
- Список платформ обновляется при запуске с учётом доп. путей.

## [0.2.5.62] — 2026-08-14

### Исправлено

- Каталог шаблонов: приоритет пути из настроек стартера 1С и стандартного  (как у 1С по умолчанию).

## [0.2.5.61] — 2026-08-14

### Исправлено

- CS1503 в OneCTemplateService: StartsWith с строкой "." вместо char.

## [0.2.5.60] — 2026-08-14

### Исправлено

- CS7036: корректный вызов Color.FromRgb для цвета выделения иконки.

## [0.2.5.59] — 2026-08-14

### Добавлено

- Создание ИБ **из шаблона**: список установленных конфигураций из стандартных каталогов `tmplts` (как в стартере 1С), плюс выбор файла вручную.

### Изменено

- Кнопки **Далее / Создать / Отмена** в мастере добавления и создания ИБ — в стиле окна настроек (зелёная / красная).

## [0.2.5.58] — 2026-08-14

### Изменено

- Палитра иконок группы: **тёмный фон кнопок**, чтобы белые и цветные иконки всегда были видны.
- Расширен набор иконок групп (пользователи, синхронизация, бэкап, конфигурация, журнал и др.).

## [0.2.5.57] — 2026-08-14

### Изменено

- Кнопка **«Добавить базу / группу»** в правой панели перенесена рядом с **«Изменить настройки»**.

## [0.2.5.56] — 2026-08-14

### Изменено

- Иконки групп по умолчанию **белые** (`#FFFFFF`), чтобы были видны на цветном фоне заголовка.

## [0.2.5.55] — 2026-08-14

### Добавлено

- **Создание ИБ**: пустая база или из шаблона (.cf / .dt) через `CREATEINFOBASE` (файловая и клиент-серверная).
- Кнопка **Добавить** в верхней панели и в правой панели действий.
- Сохранение настроек **текущей сессии** (режим клиента и разрядность) между запусками.

### Изменено

- Мастер добавления: «Существующая база» / «Создать пустую» / «Создать из шаблона» / «Группа».

## [0.2.5.54] — 2026-08-14

### Изменено

- Настройка группы: **отдельный цвет заголовка** и **отдельный цвет иконки** (две палитры).
- В списке иконка рисуется цветом `IconColor`, фон строки — `Color`.

## [0.2.5.53] — 2026-08-14

### Исправлено

- Выгрузка **.dt / .cf**: корректный формат ключей 1С (`/DumpIB"путь"`, `/DumpCfg"путь"`), отказ от 1CEStart.exe, `/DisableStartupMessages`.
- **Ярлык на рабочем столе** — как у стандартного стартера 1С: цель `1cv8.exe`, аргументы `ENTERPRISE /F"..."|/S"..."`, иконка платформы.

### Удалено

- Блокировка файловой базы (маркер `1Cv8.blocked`) — функция убрана из меню и модели.

## [0.2.5.52] — 2026-08-14

### Изменено

- Редактор группы: цвет применяется к **иконкам** в палитре (превью), выбранная иконка подсвечивается цветом группы.
- Правая панель: у кнопок **Изменить**, **Избранное**, **Закрепить**, **Очистить кэш**, **Удалить** отображаются назначенные **горячие клавиши**.

## [0.2.5.51] — 2026-08-14

### Добавлено (StartManager, пункты 3–5)

- Колонка **«Размер»** для файловых ИБ (настройка видимости в Отображение → Колонки).
- **Выгрузка .dt** и **.cf**, **тестирование ИБ** (`/IBCheckAndRepair -TestOnly`) через пакетный DESIGNER — контекстное меню.
- **Блокировка файловой базы** (маркер `1Cv8.blocked` в каталоге) — 🔒 в колонке размера.
- **История запусков** (до 30 записей на базу): пополняется при запуске/выгрузке/тесте, просмотр из контекстного меню.

## [0.2.5.50] — 2026-08-14

### Добавлено (по аналогии со StartManager)

- **Открыть каталог** файловой базы в проводнике (контекстное меню).
- **Ярлык на рабочем столе** для выбранной базы.
- **Удалить отсутствующие файловые базы** (нет каталога / 1Cv8.1CD) — Настройки → Дополнительно → Обслуживание.
- **Завершить процессы 1С** (1cv8, 1cv8c и др.) — там же.

## [0.2.5.49] — 2026-08-14

### Добавлено

- Расширенные **горячие клавиши** (Настройки → Отображение → Клавиши): 1С:Предприятие, Конфигуратор, Избранное, Изменить, Удалить, Очистить кэш, Добавить базу, Закрепить.
- Поддержка жестов: F2–F12, Delete, Insert, Ctrl/Shift-комбинации; «Нет» — не назначено.
- Проверка при сохранении: **одна клавиша — одно действие**.
- Подсказки клавиш в контекстном меню и на кнопках панели действий.

## [0.2.5.48] — 2026-08-14

### Изменено

- Блок **Текущая сессия** доступен и в **компактном** режиме правой панели.
- Видимость блока настраивается: **Настройки → Отображение → Панели** («Блок „Текущая сессия“»).

## [0.2.5.47] — 2026-08-14

### Добавлено

- Правая панель: блок **Текущая сессия** — выбор режима клиента (Авто / Обычный / Толстый / Тонкий) и **разрядности** (Авто / 32 / 64) для запуска 1С:Предприятие без изменения настроек базы.

## [0.2.5.46] — 2026-08-14

### Исправлено

- Боковое меню настроек: на выделенной вкладке текст и значки были жёлтыми на жёлтом фоне. Теперь тёмный цвет (`ButtonTextBrush`), одинаковая ширина пунктов, иконки привязаны к цвету вкладки.

## [0.2.5.45] — 2026-08-14

### Изменено

- Подвкладки **Отображение**: равная ширина (`UniformGrid`), общая линия снизу, выделение только нижней акцент-полосой (без жёлтой заливки) — симметричнее и не конфликтует с боковым меню.

## [0.2.5.44] — 2026-08-14

### Исправлено

- **CS0029** в `PlatformVersionPickerWindow`: список версий приведён к `List<PlatformVersionInfo>` после изменения модели группы.

## [0.2.5.43] — 2026-08-14

### Исправлено

- Кнопки панели **Действия**: подписи и иконки всегда читаемы в светлой и тёмной теме (кремовый фон вторичных кнопок, тёмный `ButtonTextBrush` на иконках/тексте; акцентные цвета избранного/удаления с достаточным контрастом).

## [0.2.5.42] — 2026-08-14

### Добавлено

- В списке версий платформы (настройки → Платформы) под каждой версией показывается **путь к папке** установки.

## [0.2.5.41] — 2026-08-14

### Изменено

- Подвкладки **Отображение** в один ряд: короткие подписи (Значки, Колонки, Панели, Статус, Клавиши), компактные отступы; полные названия в подсказках.

## [0.2.5.40] — 2026-08-14

### Изменено

- Вкладка **Отображение**: подвкладки (Значки и кнопки, Колонки списка, Панели, Нижняя панель, Клавиши и поведение) расположены **горизонтально** сверху, а не вертикально слева.

## [0.2.5.39] — 2026-08-14

### Исправлено

- Номер избранного (цифра Alt+N) в списке баз не помещался полностью: колонка ★ расширена с 26 до 38 px, у бейджа добавлены `MinWidth` и центрирование текста.

## [0.2.5.38] — 2026-08-14

### Исправлено

- Открытие **настроек** падало: у `FavoriteHotkeysList` одновременно были `DisplayMemberPath` и `ItemTemplate`.
- Кнопка **Избранное** в правой панели: цвет текста `FavoriteBrush` (читается в тёмной теме).

### Изменено

- Окна выбора платформы и параметров запуска — цветные кнопки «Выбрать/ОК» и «Отмена» как в настройках.
- Вкладка **Идентификатор**: компактные поля, кнопка **Сгенерировать** новый ID.

## [0.2.5.37] — 2026-08-14

### Исправлено

- Ошибка **CS0103** `FavoriteHotkeysList`: восстановлен UI порядка избранного (Alt+1…9), горячих клавиш F3/F4 и опций поведения во вкладке «Отображение → Клавиши и поведение».

## [0.2.5.36] — 2026-08-14

### Исправлено

- Тёмная тема: номер избранного (Alt+N) — жёлтый бейдж с тёмным текстом; подписи «Избранное» и др. на панели действий — `TextPrimary`.
- Вторичный текст в тёмной теме светлее (`#CBD5E1`).

### Изменено

- Список версий платформы в настройках — иконки и карточки строк.
- Вкладка **Отображение**: вертикальные группы — Значки и кнопки, Колонки списка, Панели, Нижняя панель.

## [0.2.5.35] — 2026-08-14

### Изменено

- Окно подключения: компактнее поля на вкладках **Запуск** и **Идентификатор**.
- Вкладка **Подключение**: тип подключения и аутентификация — карточки в стиле вкладки «Запуск».

## [0.2.5.34] — 2026-08-14

### Изменено

- Окно **настроек подключения** разбито на вкладки: **Подключение**, **Запуск**, **Идентификатор**.
- **Режим запуска** оформлен карточками с описанием (как разрядность платформы).

## [0.2.5.33] — 2026-08-14

### Исправлено

- Красные полосы в списке баз: `Validation.ErrorTemplate` отключён у `TreeViewItem`, привязка `IsExpanded` переведена на **OneWay** (TwoWay давал ошибки привязки на листьях).
- Развернуть все: без отключения дерева и без многократных layout-проходов; обновление только узлов-групп.

## [0.2.5.32] — 2026-08-14

### Исправлено (производительность)

- **Развернуть все группы**: убраны пакетные `PropertyChanged` (ухудшали отзывчивость). Разворот идёт **по уровням TreeViewItem** через layout-проходы; свёртка — мгновенное схлопывание корневых контейнеров. Без `ReplaceGroupNodes`.

## [0.2.5.31] — 2026-08-14

### Исправлено (производительность)

- **Развернуть все группы**: убрана полная пересборка дерева (`ReplaceGroupNodes`). Состояние в модели + пакетные уведомления `IsExpanded` через Dispatcher — UI не блокируется.

## [0.2.5.30] — 2026-08-14

### Исправлено (производительность)

- **Развернуть / свернуть все группы**: без лавины `PropertyChanged` на каждый узел. Состояние задаётся «молча», затем одно обновление дерева. Сохранение настроек — отложенное.

## [0.2.5.29] — 2026-08-14

### Изменено

- Кнопки **развернуть / свернуть все группы** перенесены **влево от заголовков колонок** списка.
- Иконки `ExpandAll` / `CollapseAll` — понятнее назначение; подсветка при наведении (`HeaderIconButton`).

## [0.2.5.28] — 2026-08-14

### Добавлено

- Разрядность запуска клиента по документации **1С:Предприятие** — 4 режима:
  - **Приоритет 32 (x86)** — по умолчанию в 1С
  - **Приоритет 64 (x86-64)**
  - **Только 32 (x86)**
  - **Только 64 (x86-64)**
- При приоритетных режимах лаунчер выбирает более новую установленную версию между 32/64.
- Подсказка по ОС и описание выбранного режима в окне подключения.

### Изменено

- Интерфейс выбора разрядности: карточки с пояснениями (как в современном UI настроек).
- Отображение разрядности в строке состояния: «Приоритет 32/64», «32 (x86)», «64 (x86-64)».

## [0.2.5.27] — 2026-08-14

### Исправлено

- **Режим запуска и другие настройки базы** после «Сохранить» больше не затираются: при правке/добавлении/удалении выполняется только **выгрузка** в ibases.v8i, без импорта поверх локальных изменений.
- RadioButton в окне подключения: отдельные **GroupName** для типа подключения, аутентификации, режима запуска и разрядности (корректное переключение).

### Изменено

- Окно настроек подключения: иконки в заголовках секций (как в настройках программы), `LaunchMode` уведомляет UI при изменении.

## [0.2.5.26] — 2026-08-14

### Исправлено (скорость списка баз)

- Перестройка дерева: без лавины `NotifyCountChanged` на каждое добавление базы.
- Замена корня списка — **новая коллекция** вместо Clear/Add (один сброс TreeView).
- Debounce поиска **90 мс** (было 180).
- Виртуализация **вложенных** уровней TreeView (раньше виртуализировался только корень).

## [0.2.5.25] — 2026-08-14

### Изменено

- Верхняя панель: у режимов **Все / Избранное / Недавние** добавлены иконки (база, звезда, часы).
- Переключатель **темы** — иконки луны/солнца в зависимости от текущей темы.

## [0.2.5.24] — 2026-08-14

### Исправлено

- В панели всех тегов снова видна **иконка тега**: `Fill` был привязан к `ToggleButton` вместо родительской `Button`.

## [0.2.5.23] — 2026-08-14

### Исправлено

- Кнопки **развернуть / свернуть** все группы снова видны (иконки `UnfoldMore` / `UnfoldLess`).

### Изменено

- «Показывать группы» — только **иконка папки**; при включении — акцентная подсветка.
- Быстрый переключатель **тегов** (иконка тега) рядом с группами: показ/скрытие тегов в списке и панели отбора.
- Кнопка **«Добавить»** убрана с верхней панели.
- Иконка тега (`IconTag`) используется в фильтре, чипах, строках списка и правой панели.

## [0.2.5.22] — 2026-08-14

### Исправлено (производительность UI)

- **GroupColorConverter / GroupTextColorConverter**: убран поиск группы через `GetFullPath` по всему списку на каждую отрисовку строки (было O(n²)). Кисти кэшируются и `Freeze()`.
- Заголовки групп берут готовые `HeaderBrush` / `HeaderTextBrush` с узла дерева.
- **FullPath** и **ContainsInfobases** кэшируются в `GroupNodeViewModel`.
- Индексация дерева при перестройке использует кэш `FullPath`, без повторных `GetFullPath`.
- **IconKeyToGeometryConverter**: кэш Geometry по ключу иконки.
- `ReplaceGroupNodes` не трогает UI, если набор корневых узлов не изменился.

## [0.2.5.21] — 2026-08-14

### Изменено

- Верхняя панель ближе к компактному макету: **развернуть/свернуть** группы — только иконки; **добавить**, синхронизация, тема, настройки — иконки справа.
- Кнопка **«Добавить»** перенесена на верхнюю панель (убрана из правой).
- Кнопки **«1С:Предприятие»** и **«Конфигуратор»** отделены визуальным разделителем от остальных действий.
- Вкладка списка: «Все» вместо «Все базы».

### Добавлено

- В нижней панели можно показывать **ID** информационной базы (настройка во вкладке «Отображение»).

## [0.2.5.20] — 2026-08-14

### Изменено

- Компактная правая панель: ширина **по содержимому** (Auto) — кнопки и панель подстраиваются под длину надписей, без фиксированной ширины.

## [0.2.5.19] — 2026-08-14

### Изменено

- Компактная правая панель: нормальные короткие подписи кнопок («1С:Предприятие», «Конфигуратор», «Изменить», «Закрепить», «Очистить кэш» и т.д.) вместо чрезмерно усечённых; ширина панели ~156px.

## [0.2.5.18] — 2026-08-14

### Исправлено

- Кнопка «свернуть/показать правую панель» в светлой теме: при наведении больше не заливается светлым фоном (иконка остаётся читаемой на тёмной строке состояния). Стиль `StatusBarIconButton`.

### Изменено

- Компактный режим правой панели: ширина ~128px, короткие подписи кнопок (1С, Конф., Правка, Пин, Кэш…), уменьшенные отступы.

## [0.2.5.17] — 2026-08-14

### Добавлено

- Кнопка **справа внизу** главного окна: показать/скрыть подробности правой панели.
- Компактный режим правой панели — **только кнопки действий** без сведений о базе/группе.
- Настройки вкладки **«Отображение»**:
  - правая панель (подробности вкл/выкл);
  - **нижняя панель**: путь/сервер, порт, платформа, разрядность, режим запуска, тип клиента, тип подключения, пользователь.

### Исправлено

- Поле ввода тега в строке выбранной базы: явный фон, рамка и цвет текста, чтобы поле было видно на фоне выделения.

## [0.2.5.16] — 2026-08-14

### Добавлено

- **Дополнительные пути поиска платформы 1С** на вкладке «Платформы»: пользователь может указать нестандартные/портативные каталоги; они учитываются при поиске версий и при запуске клиента.
- Единая кнопка **«Синхронизация»** с `ibases.v8i` в главном окне (и пункт в меню трея): выполняет загрузку/выгрузку/двустороннюю синхронизацию по режиму из настроек.

### Изменено

- При **поиске**, фильтре по **тегам**, режимах **«Избранное»** и **«Недавние»** группы, в которых есть подходящие базы, **автоматически разворачиваются**. Сохранённое состояние свёртки применяется только без активных фильтров.

### Исправлено

- Повторный запуск при запрете нескольких экземпляров: окно снова **открывается** (в том числе из трея) без предупреждающего сообщения — через именованное событие активации.
- Импорт из `ibases.v8i`: порт из `Srvr="host:port"` корректно заполняется в поле «Порт»; при экспорте и запуске нестандартный порт снова передаётся как `host:port`.

## [0.2.5.15] — 2026-08-13

### Удалено (мёртвый код)

- `Themes/ModernTheme.xaml` — не подключался.
- `LaunchViewModel` — не использовался (запуск через команды MainViewModel).
- `MetadataNodeViewModel` — UI метаданных отсутствует.
- Конвертеры без привязок: `ClientTypeToIcon`, `ConnectionTypeToIcon`, `NullToBool`, `GroupExpanded`, `GroupExpandSymbol`, `GroupVisibility`, `MultiValueToArray`, `GroupFullPath`.
- Неиспользуемое поле `_treeScrollAttached`.

## [0.2.5.14] — 2026-08-13

### Изменено

- Окно **настроек подключения к базе** приведено к стилю настроек программы: шапка с иконкой, прокручиваемый контент, **Сохранить** (зелёная) / **Отмена** (красная обводка).

## [0.2.5.13] — 2026-08-13

### Исправлено

- Высота фона тега в списке баз уменьшена до **16px** (без лишних отступов кнопок).

## [0.2.5.12] — 2026-08-13

### Исправлено

- ComboBox в настройках сжимался до 1 символа («F», «1»): ToggleButton теперь на всю ширину ComboBox; заданы явные Width для хоткеев и ibases.

## [0.2.5.11] — 2026-08-13

### Исправлено

- ComboBox горячих клавиш в настройках: вместо «•» снова отображаются **F2–F12** (SelectedItem + ContentStringFormat).

## [0.2.5.10] — 2026-08-13

### Исправлено / UI

- Теги в списке баз — **меньше вертикальные отступы** фона чипа.
- ComboBox в настройках — выбранное значение через `TextBlock` (крупный читаемый текст).
- **Сохранить** — зелёный фон; **Отмена** — красная обводка и иконка (без «тревожного» красного фона).
- Правая панель при выборе **группы** — отображается **иконка группы** из настроек группы.

## [0.2.5.9] — 2026-08-13

### Исправлено

- Binding Error `IsExpanded` на `Infobase`: привязка переведена на `TreeViewItem.IsExpanded` (кнопка разворота групп).

## [0.2.5.8] — 2026-08-13

### Исправлено

- Удаление тега крестиком в **списке баз** — UI обновляется (новый экземпляр списка + `PropertyChanged`).
- ComboBox в настройках (хоткеи, ibases) — **шире и выше**, крупнее шрифт.
- Шрифт тегов в списке баз увеличен до **12**.

## [0.2.5.7] — 2026-08-13

### Производительность

- Добавление/удаление тега **без** полной перестройки дерева; только уведомление UI и отложенное сохранение.
- Поиск: **debounce 180 ms** вместо перестройки на каждый символ.
- Включена **виртуализация** TreeView (Recycling).
- Фильтр тегов через `HashSet`; облако тегов не пересобирается, если ничего не изменилось.
- Убраны лишние `GetFullPath` и двойные `Refresh` при фильтрации.

## [0.2.5.6] — 2026-08-13

### Исправлено

- **Облако тегов** — `ObservableCollection` + `RefreshTagFilterItems` при добавлении/удалении тега.
- **Правая панель** — отступы слева/справа у кнопок (не вплотную к краям).
- **ComboBox в настройках** (Отображение и ibases) — выше/шире, выбранное значение читается.
- **Теги в списке баз** — компактная высота (~16px).

## [0.2.5.5] — 2026-08-13

### Изменено

- Верхняя панель **в один ряд**: группировка / развернуть / свернуть | поиск | Все / Избранное / Недавние | действия.
- **Облако тегов** обновляется при изменении списка баз и тегов.
- Теги в строках списка — **компактнее**.
- Правая панель как на макете: широкие кнопки, «Запустить 1С:Предприятие» / «Открыть конфигуратор» с отображением **горячих клавиш** (из настроек).

## [0.2.5.4] — 2026-08-13

### Исправлено

- **Горячие клавиши в настройках** — ComboBox шире/выше, выбранная клавиша читается.
- **Вкладка «Отображение»** — переключатели-тумблеры вместо галочек.
- **Правая панель** — кнопки не на всю ширину (max ~240px), подписи всегда слева.

## [0.2.5.3] — 2026-08-13

### Исправлено

- **Теги в поиске** — слева от текста, курсор правее чипов; компактная высота чипов.
- **Кнопки правой панели** — без выбора базы по центру, при выборе — выравнивание влево.
- **Облако тегов (тёмная тема)** — читаемый текст: акцент на выбранных, контрастный контур на обычных.
- **Настройки → Отображение** — иконки у пунктов, современный вид по аналогии с «Дополнительно».

## [0.2.5.2] — 2026-08-13

### Исправлено

- **Тёмная тема**: добавлены стили сегментов/иконок; вторичные кнопки — контур + светлый текст (читаемость); теги деталей на теме.
- **Теги в поиске**: фильтр на `ObservableCollection` — чипы появляются и убираются крестиком.
- **Вертикальная прокрутка списка**: принудительный scrollbar, отключена виртуализация, стабильный wheel-scroll.
- **Правая панель**: отдельно «1С:Предприятие» и «Конфигуратор»; блок управления; **Удалить** внизу за разделителем; подписи слева.

## [0.2.5.1] — 2026-08-13

### Изменено

- Выбранные **теги отображаются в поле поиска** с кнопкой ✕ у каждого тега.
- Крестик справа в поиске **очищает текст и все теги**.
- Убрана кнопка «Сбросить» у панели тегов.
- **Правая панель расширена** (~360px): карточка базы с типом подключения, сервером, строкой, платформой, режимом, клиентом, параметрами, датой запуска, описанием и тегами; для группы — имя и путь.

## [0.2.5.0] — 2026-08-13

### Добавлено

- **Вкладки списка**: «Все базы», «Избранное», «Недавние» (по дате последнего запуска).
- **Мультифильтр по тегам** — можно выбрать несколько тегов; фильтр работает вместе с поиском по имени.
- **Чипы выбранных тегов** над списком; повторный клик снимает тег.
- **Синхронизация ibases** на главном окне: кнопки «Из ibases» / «В ibases».
- **Расширенное меню трея**: Открыть, Запуск Предприятие/Конфигуратор, ibases, Настройки, Выход.

### Изменено

- Верхняя панель в стиле прототипа: поиск, сегменты, иконка настроек.
- Кнопки «Развернуть все» / «Свернуть все» с подписями.
- Стилизованное поле поиска — текст не перекрывает лупу.
- Выбранный тег визуально выделен (янтарный чип).
- Версия программы: **0.2.5.0**.

## [0.2.4.0] — 2026-08-13

### Добавлено

- **Сообщение при повторном запуске** — если экземпляр уже открыт, показывается окно «Приложение уже запущено», затем активируется существующее окно.
- **Значок в системном трее** — опция «Показывать значок в трее» (включена по умолчанию); двойной клик открывает окно.
- **Авторазворот групп** при поиске и фильтре по тегу — группы с подходящими базами разворачиваются автоматически.
- **Настраиваемые горячие клавиши** запуска «1С:Предприятие» и «Конфигуратор» (F2–F12) в настройках.

### Изменено

- Кнопки «Развернуть все» / «Свернуть все» — с текстом и иконками, понятнее по назначению.
- Версия программы: **0.2.4.0**.

## [0.2.3.2] — 2026-08-13

### Исправлено

- **Запуск приложения** — глобальная обработка необработанных исключений с показом текста ошибки (раньше окно могло просто не появиться).
- Инициализация трея и хоткеев перенесена на `Loaded` (безопаснее для STA).
- Подключены ресурсы MaterialDesign (`BundledTheme` + Defaults) в `App.xaml`.
- `RuntimeIdentifier` / SingleFile применяются только в Release publish, обычный `dotnet run` больше не ломается.
- `app.ico` копируется в выходную папку для иконки трея.

### Изменено

- Версия программы: **0.2.3.2**.

## [0.2.3.1] — 2026-08-13

### Исправлено

- **Сортировка по заголовку** — дерево списка теперь реально пересортировывается при клике по «Название» / «Последний запуск» (раньше сортировка затрагивала только `CollectionView`, а не дерево групп).
- **Запуск избранных по Alt+1…9** — запуск идёт напрямую через лаунчер; добавлен надёжный обработчик `PreviewKeyDown` (в т.ч. NumPad); слоты назначаются уже существующим избранным при старте.

### Добавлено

- **Цифры 1–9 у звезды** — рядом со ★ отображается номер горячей клавиши назначенного слота.
- **Настройка порядка горячих клавиш** — в «Настройки → Отображение» список слотов избранного с кнопками «Вверх» / «Вниз».

### Изменено

- Версия программы: **0.2.3.1**.

## [0.2.3.0] — 2026-08-13

### Добавлено

- **Горячие клавиши избранного (`Alt+1`…`Alt+9`)** — при добавлении базы в избранное автоматически назначается свободный слот; нажатие запускает базу в режиме «1С:Предприятие». Список слотов сохраняется между сессиями.
- **Закрытие в системный трей** — опция в «Настройки → Запуск приложения». При закрытии окна приложение сворачивается в трей; двойной клик по иконке восстанавливает окно, пункт «Выход» в меню трея полностью закрывает программу.
- **Сортировка по клику на заголовок колонки** — клик по «Название» или «Последний запуск» меняет поле и направление сортировки (повторный клик — реверс). По умолчанию — по наименованию. Выбор сохраняется в настройках.
- **Горячие клавиши запуска** — `F3` запускает выбранную базу в режиме «1С:Предприятие», `F4` — в режиме «Конфигуратор». Подсказки отображаются в контекстном меню (`InputGestureText`).

### Изменено

- **Контекстное меню базы** — пункт «Изменить настройки» перенесён сразу после пунктов запуска (Предприятие / Конфигуратор); пункт «Удалить» отделён разделителем и размещён внизу меню.
- Версия программы: **0.2.3.0**.

## [0.2.2.2] — 2026-08-13

### Добавлено

- **Горячие клавиши запуска** — `F3` (Предприятие), `F4` (Конфигуратор).

### Изменено

- Порядок пунктов контекстного меню базы: «Изменить настройки» рядом с запуском, «Удалить» отдельно внизу.
- Версия программы: **0.2.2.2**.

## [0.2.2.1] — 2026-08-13

### Обзор
Полировка UX после 2.2.0: стабильный drag-and-drop групп и баз, живые счётчики, подписи разрядности без имён exe.

### Исправлено
- **Разрядность в настройках базы** — убраны устаревшие подписи `(1cv8.exe)` / `(1cv8x64.exe)`; отображается только «32-битная» / «64-битная». Поиск исполняемых файлов при запуске не изменился (современный 64-бит — `1cv8.exe` в Program Files).
- **Перетаскивание группы** — payload фиксируется на `MouseDown`; пути баз нормализуются и переиндексируются по `GetFullPath`; UI сразу совпадает с состоянием после перезапуска (больше не «все в Без группы» до рестарта).
- **Счётчики «(N)»** — `NotifyCountChanged` при ★/📌 и изменении состава группы.

### Добавлено
- **Порядок баз в группе (`SortOrder`)** — при перетаскивании базы на строку другой базы вставка выполняется **перед** ней; drop на заголовок группы — в конец списка.

### Версия
- Версия программы увеличена до **0.2.2.1**.

## [0.2.2.0] — 2026-08-13

### Обзор
Релиз по результатам доработок UX и производительности списка баз: исправлена прокрутка, ускорены избранное/закрепление и фильтр «Только избранные», улучшены перенос групп, выбор группы и платформы, убрано дублирующее управление группами из настроек.

### Добавлено
- **`GroupPickerWindow`** — полноценное окно выбора группы в виде **дерева** (вместо выпадающего списка): двойной щелчок подтверждает выбор, пункт «Без группы» / «Корневая группа».
- **Контекст при добавлении** — при создании базы или группы автоматически подставляется группа, где сейчас курсор (выбранный узел дерева или группа выбранной базы).

### Исправлено
- **Полосы прокрутки и колесо мыши** — область списка стояла в строке `Grid` с `Height="Auto"` при пустой строке `*`; `TreeView` измерялся по полной высоте контента → `ScrollableHeight = 0`. Строка списка переведена на `Height="*"`.
- **Кастомный `ScrollBar` (светлая/тёмная темы)** — `Track.Value` был `TemplateBinding` (OneWay), ползунок не перетаскивался; привязка заменена на **TwoWay**.
- **Клик по группе «улетал» вверх** — WPF вызывал `BringIntoView` при `IsSelected`/`Focus`. Добавлен обработчик `RequestBringIntoView` (`Handled = true`), вызовы `Focus()` при выборе убраны; позиция прокрутки не меняется.
- **Перетаскивание группы** — раньше менялся только `ParentId`, пути `Infobase.Group` не обновлялись, визуально «переезжали» лишь базы. Теперь переносится **вся ветка** (подгруппы + базы), пересчитываются полные пути; исправлена проверка циклов (нельзя бросить группу внутрь собственного потомка).

### Изменено
- **Значки свернуть/развернуть** — рамка с акцентом, **«+»** (свёрнута) / **«−»** (развёрнута), динамическая подсказка, заливка при наведении.
- **Избранное (★) и закрепление (📌)** — без полной `RebuildGroupTree` и синхронного `Save` на каждый клик: иконка через `INotifyPropertyChanged`, блок «Закреплённые» обновляется точечно, сохранение JSON с **debounce 400 мс** в фоне.
- **Фильтр «Только избранные»** — без дорогого `InfobasesView.Refresh`; дерево строится через `EnumerateFilteredInfobases`; настройки пишутся отложенно.
- **Виртуализация списка** — `VirtualizingPanel` + `VirtualizingStackPanel`: при большом числе баз создаются в основном видимые строки (прокрутка сохранена за счёт строки `*`).
- **Сериализация `infobases.json`** — без отступов, `WhenWritingNull` (быстрее запись на диск).
- **Настройки базы / группы** — поле группы: только чтение + кнопка «Выбрать…» → дерево; родитель группы — то же.
- **Окно выбора платформы** — больше размер, дерево на всю высоту окна, удобнее просмотр версий.
- **Настройки приложения** — вкладка **«Группы» удалена**; группы создаются и правятся из главного окна (Добавить / Изменить / контекстное меню).

### Исправлено (счётчики и DnD)
- **Счётчик «(N)» в группах и «Закреплённые»** — `TotalInfobaseCount` не поднимал `PropertyChanged`; после 📌/★ число не обновлялось. Добавлены `NotifyCountChanged` и подписка на `Infobases`/`Children`.
- **Перетаскивание группы** — в `Drop` сначала обрабатывается группа (не базы); drop на строку базы = drop на родительскую группу; UI сразу перестраивается (`RebuildGroupTree`), без «плоского» вида до перезапуска.
- **WPF DnD: payload с MouseDown** — раньше объект брался в `MouseMove` (под курсором уже могла быть дочерняя база → «переезжали» только базы). Payload фиксируется на `PreviewMouseLeftButtonDown`; `DataObject` с явными форматами.
- **DnD группы: UI сразу как после перезапуска** — индексация путей в `RebuildGroupTree` по `GetFullPath` + нормализация; remap путей баз устойчивее к разделителям; всегда Save + Rebuild после переноса.
- **DnD базы с позицией** — свойство `SortOrder`; drop на строку базы вставляет **перед** ней внутри группы.

### Версия
- Версия программы увеличена до **0.2.2.0**.

## [0.2.1.1] — 2026-08-13

### Обзор
Промежуточные правки прокрутки и UX списка (вошли в **0.2.2.0** в полном объёме).

### Версия
- Версия **0.2.1.1** (superseded by 0.2.2.0).

## [0.2.1.0] — 2026-08-13

### Обзор
Минорный релиз с доработками интерфейса, надёжности запуска 1С (32/64 бит), синхронизации `ibases.v8i` и удобства работы со списком баз.

### Добавлено
- **Один экземпляр приложения** — проверка повторного запуска через `Mutex`; повторный старт активирует уже открытое окно. Настройка «Разрешить несколько экземпляров» в **Настройки → Отображение**.
- **Версия в заголовке** главного окна (`Управление конфигурациями 1С v0.2.1.0`).
- **Панель быстрого отбора по тегам** над списком баз; настройка показа панели в параметрах отображения.
- **Перетаскивание (drag-and-drop)** баз и групп в дереве списка (перенос базы в группу, смена родителя группы).
- **Резервные копии `ibases.v8i`** перед синхронизацией/экспортом: `ibases.v8i.bak_yyyyMMdd_HHmmss`, хранение N последних копий, кнопка «Восстановить из последней копии» в настройках синхронизации.
- **Классическая раскладка окна подключения** (параметры подключения → режим запуска → разрядность/версия платформы).
- **Подключение к веб-серверу** и условный выбор веб-клиента.
- **Режимы аутентификации** как в стандартном лаунчере 1С: запрос логина/пароля, сохранённые учётные данные, аутентификация ОС (`/N`, `/P`, `/WA+`).
- **Выбор значка и цвета группы** (палитра Windows-стиля); отображение значков групп в списке баз.
- **Значки на вкладках** окна настроек и обновлённые иконки команд/панелей.
- Пункт **«Добавить...»** в контекстном меню списка баз.
- **Автоматическая сборка**: GitHub Actions (`build.yml`, `release.yml`), скрипты `build.ps1` / `build.sh`, solution-файл.

### Изменено
- **Поиск исполняемых файлов 1С** с учётом разрядности и типа клиента:
  - 64-бит: `Program Files\1cv8\<ver>\bin\` — `1cv8.exe` (современные), `1cv8x64.exe` (старые), тонкий клиент — `1cv8c.exe`;
  - 32-бит: `Program Files (x86)\1cv8\<ver>\bin\` — `1cv8.exe` / `1cv8c.exe`;
  - очистка суффикса `(32)`/`(64)` из поля версии; понятное сообщение, если платформа не найдена.
- Кнопки **свернуть/развернуть все** скрываются, если группировка выключена.
- Команды **удаления базы и группы** объединены в одну («Удалить») с обязательным подтверждением; кнопка удаления визуально отделена от остальных действий.
- Улучшена **читаемость тёмной темы** (контраст подписей и выделенных элементов).
- Окно настроек: прокрутка вкладки «Отображение», уменьшено **мерцание вкладок** (фиксированная рамка, hover только у невыбранной вкладки).

### Исправлено
- **Прокрутка списка (повторно)**: убран внешний `ScrollViewer` вокруг дерева; `TreeView` занимает строку `*` и использует **свои** вертикальную и горизонтальную полосы; колесо всегда крутит внутренний `ScrollViewer` дерева (Shift — горизонталь).
- **Прокрутка списка**: вертикальная полоса и колесо — через внутренний ScrollViewer TreeView; горизонталь — внешний ScrollViewer (Shift+колесо). Исправлено ошибочное направление колеса.
- **Иерархия групп при импорте ibases.v8i**: корректный разбор Name+Folder (в т.ч. путь в имени секции), сопоставление ID по полному пути, нормализация разделителей `\` / `/` → ` / `, базы с неизвестной группой не теряются.
- **Прокрутка списка баз**: отключена виртуализация TreeView при внешнем ScrollViewer (из‑за неё `ScrollableHeight` был 0); исправлен шаблон `CardScrollViewer` (привязки полос); колесо мыши — вертикаль, Shift+колесо — горизонталь.
- Не обновлялись **значки групп** в списке (привязка через `IconKeyToGeometryConverter`).
- Синтаксические ошибки сборки (дублирование `FindAncestor`, незакрытые строковые константы в `MainViewModel`).
- Ошибки разметки вкладки «Отображение» после добавления панели тегов и опций запуска.

### Версия
- Версия программы увеличена до **0.2.1.0**.

## [0.2.0.0] — 2026-08-12

### Обзор
Стабильный **мажорный** релиз 0.2.0.0, закрепивший рефакторинг серии 0.1.10.x как основу архитектуры приложения. Версия отражает завершение перехода на полноценный MVVM с Dependency Injection, асинхронную запись данных, логирование и модульное тестирование, а также полный редизайн интерфейса на Material Design.

### Добавлено
- **DI-контейнер** (`AppServices` + `Microsoft.Extensions.DependencyInjection`) — централизованная регистрация сервисов (репозиторий, лаунчер, диалоги, логгер, sync-сервис) и резолв главного окна из контейнера.
- **Сервисный слой с интерфейсами** — `IInfobaseRepository`, `IOneCLauncher`, `IPlatformVersionService`, `IIbasesSyncService`, `IDialogService`, `IAppLogger` для заменяемости и тестируемости компонентов.
- **Файловое логирование** — `FileAppLogger`: журнал в `%AppData%/ConfigurationManagement/logs/` с ротацией (14 дней / 5 МБ).
- **Модульные тесты (xUnit)** — проект `ConfigurationManagement.Tests`: проверка иерархии групп (`GroupHierarchyHelper`) и round-trip репозитория.
- **Асинхронная атомарная запись** JSON (`SaveAsync` / `SaveGroupsAsync` / `SaveSettingsAsync`) через временный файл и `File.Replace` — защита данных от повреждения при сбое.
- **UserControl-компоненты** `Controls/GroupTreeView` и `Controls/InfobaseListView` — заготовки для декомпозиции главного окна.
- Полный набор векторных иконок в `Themes/Icons.xaml` и подключение **Material Design Icons** (`PackIcon`, пакет `MaterialDesignThemes` 5.2.1, `BundledTheme` Amber/Lime).

### Изменено
- **Разделение `MainViewModel`** — логика запуска вынесена в `LaunchViewModel` (композиция), команды делегируют в единый `LaunchCommand` с `LaunchKind`; `MainViewModel` остаётся фасадом для XAML-привязок.
- **Отказ от `MessageBox` в ViewModel** — все подтверждения и сообщения выведены через `IDialogService`.
- **Интерфейс на Material Design** — все эмодзи и кастомные Path заменены на цветные `PackIcon`; обновлены иконки команд, диалогов и вкладок настроек; современные chevron для сворачивания групп и сплошные разделители колонок заменены на тонкие линии.
- **Виртуализация дерева групп** — `IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`.
- **Новая иконка приложения** — обновлён `AppIcon` и сгенерирован многоразмерный `app.ico`.
- Версия программы увеличена до **0.2.0.0**.

## [0.1.10.4] — 2026-08-12

### Исправлено
- **Время последнего запуска** не обновлялось сразу после запуска базы: свойство `Infobase.LastLaunchDate` сделано с уведомлением `INotifyPropertyChanged` и дополнительно поднимает `LastLaunchDisplay`.

### Изменено
- **Настройки → Группы**: кнопки управления (Добавить, Подгруппу, Изменить, Удалить, Свернуть/Развернуть все) переведены с эмодзи на цветные `PackIcon`; expander дерева групп — современные chevron вместо «+/−».
- **Настройки → все вкладки**: эмодзи на кнопках (Обновить платформы, импорт/экспорт, очистка, Сохранить/Отмена) заменены на цветные Material Design иконки.
- **Разделители заголовков колонок** в списке баз: сплошные полосы заменены на тонкие современные линии (1px, полупрозрачные) с широкой зоной захвата; под заголовками добавлена нижняя граница.

## [0.1.10.3] — 2026-08-12

### Исправлено
- **Тёмная тема / вкладки настроек**: надписи на вкладках (Платформы, Отображение, Группы и т.д.) теперь корректно отображаются — в шаблоне `SettingsTabItem` добавлен `TextElement.Foreground="{TemplateBinding Foreground}"`.

### Изменено
- **Иконка приложения**: полностью обновлён `AppIcon` (DrawingImage) — градиентный фон, современный цилиндр БД + шестерёнка; сгенерирован новый многоразмерный `app.ico`.
- **Свернуть/развернуть все группы**: заменены устаревшие Path на цветные плитки `IconCollapseAllColored` / `IconExpandAllColored` (индиго/фиолетовый).
- **Свернуть/развернуть отдельную группу**: вместо текста «+/−» используются современные chevron-иконки (`IconChevronRight` / `IconChevronDown`) с hover-эффектом (акцент + белый цвет).
- **Цветные иконки команд**: Settings, Тема, Конфигуратор, Изменить, Избранное, Закрепить, контекстное меню (Play, Cog, Star, Pin, Broom, ContentCopy, Pencil, Delete) получили выразительные цвета (синий, золотой, фиолетовый, бирюзовый, красный).
- В `Icons.xaml` добавлены геометрии: `IconCollapseAll`, `IconExpandAll`, `IconChevronRight`, `IconChevronDown` и цветные DrawingImage для collapse/expand/sync/import/export.

## [0.1.10.2] — 2026-08-12

### Добавлено
- **Material Design Icons** через пакет `MaterialDesignThemes` 5.2.1 (`PackIcon`).
- Подключены `BundledTheme` (Amber/Lime) и MaterialDesign3.Defaults.

### Изменено
- Все иконки интерфейса переведены с кастомных Path на `materialDesign:PackIcon` (Kind: Play, Cog, Star, Pin, Delete, Pencil, Plus, Close, ContentCopy, ChevronDown, WeatherNight и др.).
- Версия **0.1.10.2**.

## [0.1.10.1] — 2026-08-12

### Добавлено
- **Icons.xaml** — полный набор векторных иконок (Path Geometry) в стиле Fluent/Windows 11.
- Цветные DrawingImage-плитки для основных действий и статусов.

### Изменено
- Все эмодзи в `MainWindow.xaml` и `ConnectionSettingsWindow.xaml` заменены на векторные Path-иконки.
- Контекстное меню, кнопки запуска, избранное, закрепление, удаление и т.д. используют иконки из `Themes/Icons.xaml`.
- `App.xaml` подключает словарь иконок.

## [0.1.10.0] — 2026-08-12

### Добавлено
- **DI** (`AppServices` + Microsoft.Extensions.DependencyInjection): репозиторий, лаунчер, диалоги, логгер, sync-сервис.
- **IDialogService** — все подтверждения/ошибки из MainViewModel без MessageBox.
- **Единая LaunchCommand** + `LaunchKind` + `LaunchViewModel` (композиция).
- **IAppLogger / FileAppLogger** — лог в `%AppData%/ConfigurationManagement/logs/`.
- **Async Save** (`SaveAsync` / `SaveGroupsAsync` / `SaveSettingsAsync`) и атомарная запись JSON.
- **UserControl-заготовки** `Controls/GroupTreeView`, `Controls/InfobaseListView`.
- **Unit-тесты** (xUnit): `GroupHierarchyHelper`, round-trip репозитория.
- Интерфейсы: `IInfobaseRepository`, `IOneCLauncher`, `IPlatformVersionService`, `IIbasesSyncService`.

### Изменено
- Виртуализация TreeView: `IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`.
- Версия **0.1.10.0**.

## [0.1.9.1] — 2026-08-12

### Улучшено
- **Атомарное сохранение JSON** (`infobases.json`, `groups.json`, `settings.json`) — запись через временный файл снижает риск повреждения данных при сбое.
- **Обработка ошибок авто-синхронизации** — вместо пустых `catch` ошибка пишется в статусную строку и в Debug-лог.
- **RelayCommand** — конструктор без параметра, `RaiseCanExecuteChanged`, добавлен `AsyncRelayCommand` для неблокирующих операций.
- **ViewModelBase** — уведомление связанных свойств одним вызовом `SetProperty`.
- **IDialogService / WpfDialogService** — задел под полноценный MVVM без MessageBox в ViewModel.

### Изменено
- Версия программы увеличена до **0.1.9.1**.

## [0.1.9.0] — 2026-08-12

### Исправлено
- **Прокрутка списка баз колесом мыши при большом количестве баз без групп** — из-за виртуализации элементов `TreeView` внешний `ScrollViewer` ошибочно считал, что прокручивать нечего (`ScrollableHeight == 0`), и колесо мыши не срабатывало. Виртуализация отключена (`VirtualizingStackPanel.IsVirtualizing="False"`), прокрутка переведена в попиксельный режим (`CanContentScroll="False"`), поэтому внешний контейнер теперь корректно видит полную высоту содержимого и прокручивает список.

### Изменено
- Версия программы увеличена до **0.1.9.0**.

## [0.1.8.0] — 2026-08-11

### Добавлено
- **Мастер добавления элементов** — при добавлении в список открывается диалог выбора типа («Что добавить в список?»), аналогичный стартовому окну «1С:Предприятие»: можно создать информационную базу или группу. (`AddEditWindow`)
- **Управление группами в окне настроек** — на вкладке управления группами можно создавать корневые группы и подгруппы, редактировать и удалять группы, сворачивать/разворачивать все группы дерева. При создании/редактировании группы доступен выбор родительской группы с отображением полного пути и защитой от циклических ссылок.
- **Варианты платформы с разрядностью** — при поиске установленных платформ 1С разрядность определяется по каталогу установки (`Program Files` — 64-бит, `Program Files (x86)` — 32-бит). Список установленных версий отображается в формате «8.3.25.1234 (32)/(64)», в том числе для единого исполняемого файла `1cv8.exe` в современных версиях (8.3.22+ и 8.5.x).
- **Запуск конкретной установленной версии платформы** — если для базы задана версия платформы, лаунчер ищет исполняемый файл `1cv8.exe`/`1cv8x64.exe` в каталоге именно этой версии нужной разрядности, а не использует общий лаунчер `1CEStart.exe`.

### Изменено
- **Управление группами** — окно `GroupSettingsWindow` заменено на встроенное управление группами в окне настроек приложения; удаление группы с содержимым (подгруппами или базами) запрещается с объяснением причины.
- **Импорт иерархии групп из `ibases.v8i`** — полные пути групп вида «Родитель\Дочерняя» разбираются на сегменты (по ключу `Folder`), недостающие родительские группы создаются автоматически с корректным `ParentId`.
- **Построение дерева групп** — добавлена защита от циклических ссылок родителя (A→B→A): группы, участвующие в цикле, делаются корневыми, что исключает `StackOverflowException` при рекурсивном обходе.
- Версия программы увеличена до **0.1.8.0**.

## [0.1.7.0] — 2026-08-11

### Добавлено
- **Гибкие триггеры автоматической синхронизации с `ibases.v8i`** — в окне настроек можно выбрать момент автоматической синхронизации: при запуске приложения, через заданный интервал (в минутах) или по расписанию в указанное время (`IbasesSyncTrigger`).
- **Генерация ID для создаваемых групп** — при импорте групп без ID (GUID) в `ibases.v8i` для них создаётся новый идентификатор, что гарантирует корректную связь «родитель–потомок» в иерархии групп.

### Изменено
- **Экспорт в `ibases.v8i`** — улучшена обработка существующего файла:
  - устранение дубликатов секций с одинаковым именем (при конфликте записи-группы и записи-базы приоритет сохраняется за базой);
  - существующие секции-группы только обновляются (имя и иерархия), новые секции-группы при выгрузке не создаются — папки в 1С отображаются по `Folder`-ссылкам баз, что исключает появление лишних групп.
- **Импорт иерархии групп из `ibases.v8i`** — пути групп «Родитель\Дочерняя» разбираются на сегменты, недостающие родительские группы создаются автоматически с корректным `ParentId`.
- Версия программы увеличена до **0.1.7.0**.

## [0.1.6.0] — 2026-08-11

### Изменено
- **Выравнивание заголовков колонок списка баз** — заголовки колонок («Версия платформы», «Режим запуска», «Сервер/База», «Последний запуск») теперь прижаты к левому краю и сокращаются многоточием при нехватке места, как и данные в строках списка.
- **Окно настроек (вкладка «ibases.v8i»)** — убрана отдельная кнопка «Сохранить» на вкладке; настройки синхронизации сохраняются общей кнопкой «Сохранить» внизу окна настроек.
- Версия программы увеличена до **0.1.6.0**.

## [0.1.5.0] — 2026-08-11

### Добавлено
- **Экспорт списка баз и групп в стандартный файл `ibases.v8i`** — новый сервис `IbasesV8iExporter` выгружает базы и группы приложения в файл 1С, добавляя новые записи и обновляя существующие (по совпадению имени базы). Группы записываются секциями без строки подключения, для баз переносятся версия платформы, режим запуска (`App`/`DefaultApp`) и дополнительные параметры запуска.
- **Синхронизация с файлом `ibases.v8i`** — настройка режима автоматической синхронизации в окне настроек (вкладка «Дополнительные функции»):
  - режимы: **отключена**, **только загрузка** (из файла в приложение), **только выгрузка** (из приложения в файл), **двусторонняя**;
  - указание пути к файлу вручную или автоматическое использование стандартного пути 1С;
  - кнопки «Загрузить» и «Выгрузить» для ручного запуска импорта/экспорта;
  - автоматическая синхронизация при изменении списка баз (добавление, редактирование, удаление) в выбранном режиме.
- **Сохранение размеров, позиции и состояния главного окна** между запусками приложения (включая корректное восстановление развёрнутого состояния и защиту от выхода окна за пределы рабочей области экрана).

### Изменено
- Версия программы увеличена до **0.1.5.0**.

## [0.1.4.0] — 2026-08-10

### Добавлено
- **Иерархия групп («группа в группе»)** — поддержка вложенных групп, как в типовом списке баз 1С:
  - новая модель иерархии через свойство `ParentId` у группы;
  - дерево групп с подгруппами и базами в единой коллекции для вложенного отображения;
  - построение дерева групп, поиск группы по полному пути и защита от циклических ссылок (хелпер `GroupHierarchyHelper`);
  - группы, не содержащие баз (в том числе при активном фильтре «Только избранные»), автоматически скрываются.
- **Импорт иерархии групп из `ibases.v8i`** — при импорте пути групп вида «Родитель\Дочерняя» разбираются на сегменты, недостающие родительские группы создаются автоматически и выставляется `ParentId`.
- **Табличное представление списка баз (`DataGrid`)** — колонки «Наименование», «Версия платформы», «Тип подключения», «Режим запуска», «Сервер/База», кнопки «Избранное» (★) и «Закрепить» (📌), современные стили заголовков и подсветка строк/ячеек под обе темы.
- **Сохранение ширины колонок** таблицы между запусками приложения (сохраняются в `settings.json`).
- **Отдельная группа «Закреплённые»** — закреплённые базы выводятся вверху таблицы отдельной группой, независимо от их группы.
- **Копирование строки подключения** в буфер обмена.
- **Выделение строки под курсором при правом клике**, чтобы команды контекстного меню применялись к нужной базе.
- **Добавление тега прямо в строке названия** (инлайн).

### Изменено
- **Кнопка переключения группировки** с динамическим текстом («📁 Скрыть группы» / «📁 Показывать группы»).
- **Символы «+»/«−»** для сворачивания/разворачивания групп вместо chevron-стрелки.
- **Кастомные полосы прокрутки** — стилизованный скроллбар, автоматически скрывающийся, когда не требуется.
- Список баз теперь сортируется: сначала закреплённые базы, затем по наименованию.
- Версия программы увеличена до **0.1.4.0**.

## [0.1.2.0] — 2026-08-07

### Изменено
- **Скрытие пустых групп при активном фильтре «Только избранные»** — при включённом режиме избранного в дереве групп отображаются только группы, в которых есть хотя бы одна избранная база. Корневые группы, не содержащие баз в текущем фильтре, скрываются; при снятии фильтра пустые группы снова отображаются.
- Дерево групп теперь перестраивается сразу при переключении избранного для базы, а не только при переключении фильтра.
- Версия программы увеличена до **0.1.2.0**.

## [0.1.1.0] — 2026-08-06

### Добавлено
- **Отдельное окно настроек приложения** (`SettingsWindow`), открываемое по кнопке «⚙ Настройки» в главном окне.
- **Секция «Установленные платформы»** в окне настроек:
  - отображение списка установленных версий платформы 1С;
  - кнопка **«🔄 Обновить список»** для повторного сканирования каталогов `Program Files\1cv8` и `Program Files (x86)\1cv8`;
  - **группировка** версий по мажорной версии (например, `8.3.27`) с сортировкой по убыванию;
  - строка статуса с количеством найденных версий.
- **Сервис поиска установленных версий** `PlatformVersionService` — сканирует каталоги 1С и возвращает отсортированный по убыванию список версий.
- **Окно выбора версии платформы** (`PlatformVersionPickerWindow`) со сгруппированными версиями, открываемое из окна настроек подключения.
- **Выбор установленной версии платформы** в окне настроек подключения:
  - поле «Версия платформы» заменено на редактируемый выпадающий список;
  - кнопка «📋» открывает окно выбора версии со сгруппированными версиями.
- **Сохранение списка установленных версий** в настройках приложения (`settings.json`).

### Исправлено
- Читаемость текста списка версий платформы в тёмной теме (явные цвета текста для узлов дерева).
- Отображение конкретных версий платформы в дереве (корректный шаблон дочерних узлов).

### Изменено
- Версия программы увеличена до **0.1.1.0**.

## [0.1.0.0] — 2026-08-06

### Добавлено
- Первоначальный выпуск приложения «Управление конфигурациями 1С».
- Запуск информационных баз в режимах «1С:Предприятие» и «Конфигуратор».
- Выбор типа клиента (тонкий/толстый) и разрядности (32/64 бит).
- Поддержка веб-клиента.
- Управление списком баз: добавление, редактирование, удаление, группировка, избранное, закрепление, поиск, теги.
- Управление группами с цветовой маркировкой.
- Импорт из `ibases.v8i`, экспорт/импорт списка баз в JSON.
- Очистка локального кеша 1С.
- Светлая и тёмная темы оформления.