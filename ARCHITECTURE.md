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
| `MainViewModel.cs` | ~1160 | поля, конструктор, коллекции, версии платформы, настройки ibases, тип `TagFilterItem` |
| `MainViewModel.Sync.cs` | ~260 | синхронизация с ibases.v8i (таймер, импорт/экспорт) |
| `MainViewModel.Display.cs` | ~870 | колонки, теги-фильтры, сессия, статус-бар, раскладка окна, объявления команд |
| `MainViewModel.Commands.cs` | ~950 | реализации команд: выбор, добавление, правка, удаление, избранное, закрепление, хоткеи |
| `MainViewModel.Launch.cs` | ~530 | запуск 1С, сохранение списка, фильтр, смена языка |
| `MainViewModel.Theme.cs` | ~610 | темы, цветовые схемы, шрифты, свёрнутые группы, `RebuildGroupTree` |
| `MainViewModel.Tools.cs` | ~1680 | импорт/экспорт, кеш, конфигурация, COM-регистрация, дампы, теги, перемещение групп, поведение |

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
проверки дубликатов хоткеев. `SettingsWindow` теперь делегирует в VM всю чистую бизнес-логику,
оставляя в view только работу с WPF-контролами и диалогами. Сборка Windows: **0 ошибок**.

## 5. Рекомендуемые следующие шаги

1. **Разбить Avalonia-аналоги** тем же приёмом частичных классов:
   `MainViewModel.Avalonia.cs` (3768) и `MainWindow.Avalonia.cs` (3906) — как это сделано
   для WPF-версий `MainWindow` и `SettingsWindow`.
2. **Углубить MVVM-вынос** из окон: перенести в `SettingsViewModel` больше бизнес-логики
   (синхронизацию ibases.v8i, платформы, шаблоны, шрифты) там, где это возможно без
   переделки XAML-привязок; рассмотреть отдельные VM для крупных диалогов.
3. **Выделить сервисы** из `MainViewModel` (например, `TagsFilterService`,
   `FavoritesHotkeyService`), чтобы ещё сильнее разгрузить VM.
4. **Проверить Linux-конфигурацию на Linux-хосте**: после переноса окон в `Views/` глобы
   `.csproj` обновлены согласованно, но сборка Avalonia возможна только на Linux (на Windows
   условие `IsOSPlatform('Windows')` включает WPF). Обязательно прогнать `dotnet build -c Debug`
   на Linux перед релизом.