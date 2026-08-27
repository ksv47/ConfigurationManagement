# Архитектура проекта «Управление конфигурациями 1С»

## 1. Целевая платформа и стратегия

**Приоритет — Windows (WPF).** Linux (Avalonia) — вторичная цель.

Проект собирается из одного файла [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj)
с условной компиляцией:

| Платформа | TFM | UI-фреймворк | Символ |
|-----------|-----|--------------|--------|
| Windows (основная) | `net10.0-windows` | WPF | `WINDOWS` (задаётся автоматически TFM) |
| Linux (вторичная) | `net10.0` | Avalonia | `LINUX` (`DefineConstants`) |

Код, специфичный для платформы, выносится в файлы-близнецы с суффиксами:
- Windows: базовое имя без суффикса (`MainWindow.xaml.cs`, `MainViewModel.cs`).
- Linux: суффикс `.Avalonia.cs` / `.Linux.cs` (`MainWindow.Avalonia.cs`, `OneCLauncher.Linux.cs`).

Включение/исключение файлов регулируется условными глобами в конце `.csproj`
(блок `ItemGroup Condition="!$([MSBuild]::IsOSPlatform('Windows'))"`).

## 2. Иерархия папок (целевая)

```
Configuration Management/
├── Program.cs                  # Точка входа (Windows #else / Linux #if LINUX)
├── App.xaml / App.xaml.cs      # Запуск, обработчики фатальных ошибок (Windows)
├── App.axaml / App.axaml.cs    # (Linux)
├── AppServices.cs              # DI-контейнер (Microsoft.Extensions.DependencyInjection)
├── Models/                     # Чистые .NET-модели (без зависимостей от UI)
│   ├── Group.cs, Infobase.cs, AppSettings.cs, ColorScheme.cs ...
├── Services/                   # Бизнес-логика и интеграция с 1С
│   ├── I*.cs                   # Интерфейсы
│   ├── *.cs                    # Реализации (Windows)
│   ├── *.Linux.cs              # Реализации (Linux)
├── ViewModels/                 # Логика представления (MVVM)
│   ├── MainViewModel.cs        # Каркас: поля, конструктор, коллекции, команды
│   ├── MainViewModel.Sync.cs   # Синхронизация ibases.v8i
│   ├── MainViewModel.Display.cs# Колонки, сессия, статус-бар, раскладка окна
│   ├── MainViewModel.Commands.cs # Реализации команд CRUD, избранное, закрепление
│   ├── MainViewModel.Launch.cs # Запуск 1С, сохранение, фильтр, язык
│   ├── MainViewModel.Theme.cs  # Темы, цветовые схемы, шрифты, дерево групп
│   ├── MainViewModel.Tools.cs  # Импорт/экспорт, кеш, COM, дамп, теги, перемещение
│   └── *.Avalonia.cs           # (Linux-аналоги)
├── Converters/                 # WPF-конвертеры
│   └── Avalonia/               # Avalonia-конвертеры
├── Themes/                     # WPF-темы (.xaml/.cs) и Avalonia (.axaml)
├── Localization/               # Локализация (LocalizationManager, Languages/)
├── Controls/                   # Пользовательские контролы (WPF)
└── Views/                      # Окна (view): *.xaml + code-behind + *.Avalonia.cs
    ├── MainWindow.*.cs         # Главное окно (каркас + partial-блоки)
    ├── SettingsWindow.*.cs     # Окно настроек (каркас + partial-блоки)
    └── *Window.xaml/.xaml.cs   # Прочие окна и модальные диалоги
```

Пространство имён корневое — `Configuration_Management` (см. `RootNamespace`),
логически разбито на `Models`, `Services`, `ViewModels`, `Converters`, `Themes`,
`Localization`.

## 3. Разделение ответственности

- **Models/** — данные и их сериализация; не зависят от UI.
- **Services/** — работа с 1С (запуск, COM-коннектор, кеш, синхронизация ibases,
  резервные копии, шаблоны) и инфраструктура (лог, диалоги, профили).
- **ViewModels/** — состояние и команды интерфейса (MVVM). Не ссылаются на конкретные
  окна, только на `IDialogService`, `IInfobaseRepository` и т.п.
- **Окна (*.xaml)** — тонкие «view»: только разметка и код, обслуживающий визуальное
  дерево (drag&drop, трей, хоткеи), без бизнес-логики.

## 4. Выполненный рефакторинг

### Разбиение монолита `MainViewModel`
Было: один файл **5929 строк** (~180 методов) со смешанными обязанностями.

Стало: **частичный класс** `public partial class MainViewModel : ViewModelBase`,
разбитый на 7 файлов по функциональным блокам:

| Файл | Строк | Содержимое |
|------|-------|------------|
| `MainViewModel.cs` | 1059 | поля, конструктор, коллекции, версии платформы, настройки ibases, тип `TagFilterItem` |
| `MainViewModel.Sync.cs` | 235 | синхронизация с ibases.v8i (таймер, импорт/экспорт) |
| `MainViewModel.Display.cs` | 727 | колонки, теги-фильтры, сессия, статус-бар, раскладка окна, объявления команд |
| `MainViewModel.Commands.cs` | 858 | реализации команд: выбор, добавление, правка, удаление, избранное, закрепление, хоткеи |
| `MainViewModel.Launch.cs` | 492 | запуск 1С, сохранение списка, фильтр, смена языка |
| `MainViewModel.Theme.cs` | 545 | темы, цветовые схемы, шрифты, свёрнутые группы, `RebuildGroupTree` |
| `MainViewModel.Tools.cs` | 1491 | импорт/экспорт, кеш, конфигурация, COM-регистрация, дампы, теги, перемещение групп, поведение |

Содержимое методов сохранено без изменений (разбиение выполняется скриптом
[`tools/split_mainviewmodel.ps1`](tools/split_mainviewmodel.ps1) по границам методов),
поэтому поведение не изменилось. Сборка Windows (WPF) проверена: **0 ошибок**.

Это снижает риск конфликтов при параллельной разработке и облегчает поиск по коду.

### Реорганизация view-папки
Все окна (view) перенесены из корня проекта в подпапку [`Views/`](Configuration Management/Views):
`*.xaml`, WPF-код за разметкой (`*.xaml.cs`) и Avalonia-аналоги (`*.Avalonia.cs`),
а также базовый класс [`ModalWindowBase.cs`](Configuration Management/Views/ModalWindowBase.cs).
Пространства имён и `x:Class` не менялись, поэтому DI и XAML-привязки не пострадали.
Глобы в `.csproj` (секция Linux) согласованно обновлены (`Views\...`), сборка Windows проверена: **0 ошибок**.

### Разбиение окон на частичные классы
Тем же приёмом, что и `MainViewModel`, разбиты два крупнейших code-behind:

| Файл | Было | Стало |
|------|------|-------|
| [`MainWindow.xaml.cs`](Configuration Management/Views/MainWindow.xaml.cs) | 3058 строк | каркас (~435) + 9 partial-файлов: `.Tray`, `.Hotkeys`, `.Tree`, `.Columns`, `.Tags`, `.Language`, `.Scroll`, `.DragDrop`, `.Events` |
| [`SettingsWindow.xaml.cs`](Configuration Management/Views/SettingsWindow.xaml.cs) | 1971 строка | каркас + 8 partial-файлов: `.Profile`, `.Language`, `.Schemes`, `.Display`, `.Fonts`, `.Hotkeys`, `.Sync`, `.Platforms` |

Содержимое методов сохранено без изменений (только перемещение между файлами),
поэтому поведение не изменилось. Сборка Windows (WPF) проверена: **0 ошибок**.

### Выделение логики из интерфейса
Создана модель представления [`ViewModels/SettingsViewModel.cs`](Configuration Management/ViewModels/SettingsViewModel.cs)
(WINDOWS-only, зарегистрирована в DI), которая инкапсулирует состояние и бизнес-операции
вкладки «Цветовое оформление»: разрешение/валидацию/локализацию имён тем, рабочие копии
правок, персист изменённых тем и CRUD пользовательских схем, а также чистое преобразование
проверки дубликатов хоткеев. `SettingsWindow` делегирует в VM всю чистую бизнес-логику,
оставляя в view только работу с WPF-контролами и диалогами. Сборка Windows: **0 ошибок**.

VM углублена и для других вкладок настроек (без изменения XAML-привязок, поведение прежнее):
- **Синхронизация ibases.v8i** ([`SettingsWindow.Sync.cs`](Configuration Management/Views/SettingsWindow.Sync.cs)):
  рабочее состояние (`Sync` — вложенный класс `IbasesSyncSettings`: режим/путь/момент синхронизации)
  и его чистые преобразования — разрешение отображаемого пути (`ResolveDisplayPath`), построение
  локализованного статус-текста (`BuildStatusText`) и разбор интервала (`ParseInterval`, дефолт 30).
  В code-behind остаётся только чтение/запись значений контролов и диалог выбора файла.
- **Шрифт интерфейса** ([`SettingsWindow.Fonts.cs`](Configuration Management/Views/SettingsWindow.Fonts.cs)):
  рабочие копии настроек шрифтов областей (`ElementFonts`) с загрузкой (`LoadElementFontWorkingCopies`)
  и гарантированным созданием области (`EnsureElementFont`). Code-behind читает значения из контролов
  в модель и применяет их, но само хранение/подготовку рабочих копий ведёт VM.

Также создана модель представления [`ViewModels/ProfilesViewModel.cs`](Configuration Management/ViewModels/ProfilesViewModel.cs)
(WINDOWS-only, зарегистрирована в DI), выносящая из [`Views/ProfilesWindow.xaml.cs`](Configuration Management/Views/ProfilesWindow.xaml.cs)
всю бизнес-логику окна учётных записей: валидацию имени, построение списка профилей, выбор
текущей записи, CRUD через `IProfileService` (создание/переименование/смена пароля/удаление
с подтверждением через `IDialogService`) и локализацию подписи текущего профиля.
Окно стало тонкой «view»: оно лишь задаёт `DataContext`, связывает контролы через `{Binding}`
и передаёт пароль из `PasswordBox` в свойство `ProfilesViewModel.Password` (пароль не является
DependencyProperty и не поддерживает двустороннюю привязку). Кнопки используют команды
`CreateCommand`/`SaveCommand`/`DeleteCommand`, ошибки отображаются через `ErrorMessage`/`HasError`.
Сборка Windows: **0 ошибок**.

### Разбиение на блоки (Windows-приоритет)
- Из [`Services/ComReadHost.cs`](Configuration Management/Services/ComReadHost.cs) (~1335 строк,
  крупнейший Windows-монолит) выделены контрактные типы протокола — перечисление
  [`ComFailureKind`](Configuration Management/Services/ComReadHost.Types.cs) и результат
  [`ComReadResult`](Configuration Management/Services/ComReadHost.Types.cs) — в отдельный
  файл-блок [`ComReadHost.Types.cs`](Configuration Management/Services/ComReadHost.Types.cs).
  Тело самого хоста осталось на месте: это критичный и сильно связный код (жизненный цикл
  агента, протокол и диагностика переплетены, методы родителя вызывают методы агента),
  поэтому ручной разнос методов по partial-файлам здесь не выполнялся — это рекомендованный
  следующий шаг ниже.
- Консолидирован DI-контейнер [`AppServices.cs`](Configuration Management/AppServices.cs):
  общие регистрации сервисов вынесены за пределы `#if WINDOWS`/`#else`, внутри веток остались
  только платформозависимые. Это убирает дублирование и делает Windows-приоритет явным:
  Windows дополнительно регистрирует `IDialogService` (WPF), регистратор COM-коннектора
  и Windows-only ViewModel (`SettingsViewModel`, `ProfilesViewModel`); Linux — только
  `IDialogService` (Avalonia). Сборка Windows: **0 ошибок**.

### Разбиение `OneCLauncher` на частичные классы
Windows-сервис [`Services/OneCLauncher.cs`](Configuration Management/Services/OneCLauncher.cs)
(~1274 строк) разбит на частичный класс `public static partial class OneCLauncher`
по функциональным секциям. Содержимое методов сохранено дословно (только перемещение),
поведение и публичный API не изменились:

| Файл | Строк | Содержимое |
|------|-------|------------|
| `OneCLauncher.cs` | 575 | usings, перечисления `OneCLaunchMode`/`OneCClientType`/`OneCRunMode`/`OneCArchitecture`, поля `DefaultArchitecture`/`_activeBatchProcesses`, события `DesignerBatchStarted`/`Completed`, методы запуска (`Launch`, `GetRunModeFromLaunchMode`, `GetArchitecture`, `ResolveArchitecture`, `FindBestVersionDir`, `CompareVersionDirs`, `BuildArguments`, `LaunchWebClient`, `FindExecutable`) |
| `OneCLauncher.DesignerBatch.cs` | 386 | пакетные операции DESIGNER (`RunDesignerBatch`, `GetBaseConnectionToken`, `RegisterBatchProcess`, `CompleteDesignerBatch`, `ReadLogFile`, `TruncateLogTail`, `PruneDeadBatchProcesses`, `IsDesignerBlocked`, `IsConfiguratorRunningForBase`) и типы `DesignerBatchOperation`/`DesignerBatchInfo` |
| `OneCLauncher.Arguments.cs` | 347 | сборка аргументов и ссылок (`BuildConnectionArgument`, `BuildAuthArgument`, `ResolveThickClientExe`, `BuildEnterpriseShortcutArguments`, `LaunchByLink`, `ParsedLink`, `ParseLink`, `CreateInfoBase`) |

Новые partial-файлы добавлены в список исключений Linux-сборки в `.csproj`
(рядом с `OneCLauncher.cs`), поэтому Avalonia-сборка не затрагивается.
Сборка Windows (WPF): **0 ошибок**.

## 5. Рекомендуемые следующие шаги

1. **Разбить Avalonia-аналоги** тем же приёмом частичных классов:
   `MainViewModel.Avalonia.cs` (3282) и `MainWindow.Avalonia.cs` (3483) — как это сделано
   для WPF-версий `MainWindow` и `SettingsWindow`.
2. **`ComReadHost.cs` остаётся монолитом** (решение после анализа). Разделители «сторона
   родителя» / «сторона агента» — условные комментарии, а не чистые границы: поля,
   хелперы (`Encode`, `TryDecode`) и единая таблица `TokenMap` используются обеими сторонами,
   `Read` вызывает агентные помощники напрямую, поэтому разносить тело по partial-файлам
   небезопасно. При необходимости рефакторинга — только точечно и с обязательной проверкой
   сборки и сравнением набора методов.
3. **Продолжить MVVM-вынос** из окон: в `SettingsViewModel` уже перенесены синхронизация
   ibases.v8i и рабочие копии шрифтов; следующий кандидат — блок «Платформы» (`PlatformVersionService`
   сканирование и группировка версий), остающиеся поля которого пока завязаны на `PlatformsTree`
   и `Dispatcher`. Также рассмотреть отдельные VM для крупных диалогов (например, окно выбора
   групп / редактирования), если их логика явно бизнесовая и отвязывается от контролов.
4. **Выделить сервисы** из `MainViewModel` (например, `TagsFilterService`,
   `FavoritesHotkeyService`), чтобы ещё сильнее разгрузить VM.
5. **Проверить Linux-конфигурацию на Linux-хосте**: после переноса окон в `Views/` глобы
   `.csproj` обновлены согласованно, но сборка Avalonia возможна только на Linux (на Windows
   условие `IsOSPlatform('Windows')` включает WPF). Обязательно прогнать `dotnet build -c Debug`
   на Linux перед релизом.