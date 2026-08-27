# Портирование на Linux — документ Этапов 0–7

> Сопроводительный документ к плану [`PLAN_LINUX.md`](../PLAN_LINUX.md).
> Статус: **Этап 0–7 выполнен** (инфраструктура, Avalonia UI, адаптация путей и хранилища, сервисы платформы 1С, ярлыки/файловые менеджеры/трей, сборка и упаковка).

---

## 1. Поддерживаемые дистрибутивы и разрядность (Этап 0)

Целевые ОС и платформа для Linux-сборки приложения «Управление конфигурациями 1С».

| Дистрибутив | Семейство | Архитектура | Примечание |
|-------------|-----------|-------------|------------|
| **Ubuntu LTS** (22.04+, 24.04+) | Debian-подобное (`.deb`) | `x64` (amd64) | Основная среда разработки и тестирования |
| **Debian** (12+) | Debian-подобное (`.deb`) | `x64` (amd64) | Совместим с Ubuntu |
| **ALT Linux** (10, 9) | RPM/ALT (`.rpm`) | `x64` | Сертифицируемый РФ-дистрибутив |
| **Astra Linux** (SE 1.7+, Смоленск 1.7+) | Debian-подобное (`.deb`) | `x64` | Сертифицируемый РФ-дистрибутив |

**Разрядность:**
- Сборка — **64-разрядная** (`linux-x64` / `amd64`). Публикация `dotnet publish -r linux-x64`.
- 32-разрядная (`linux-x86`) **не поддерживается** на этапе портирования.
- Платформа 1С:Предприятие на Linux (клиент `1cv8`) также собирается в 64-разрядной версии; поддержка запуска 32-разрядной платформы на Linux не предусматривается.

**Минимальная версия .NET:** .NET 10 SDK (для сборки), self-contained публикация не требует установки runtime.

---

## 2. Аудит `*.xaml` на несовместимость с Avalonia (Этап 0)

Ниже — сводный список конструкций WPF, которые встречаются в XAML-файлах и **несовместимы / требуют правок** при переносе в Avalonia. Полный перенос разметки запланирован на Этап 3; здесь зафиксированы категории и примеры.

### 2.1. Пространство имён и корневой URI WPF
- Все окна используют `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` и `mc:Ignorable="d"`. В Avalonia корневое пространство имён другое (`https://github.com/avaloniaui`), `mc:Ignorable` для синтаксиса XAML отличается.
- [`App.xaml`](App.xaml:1) — корень `<Application>`, `pack://` URI и `DrawingImage` для `AppIcon`.

### 2.2. MaterialDesignThemes (PackIcon) — главный блокер
Почти все окна используют `materialDesign:PackIcon` из Windows-библиотеки **MaterialDesignThemes** (для Этапа 1 пакет исключён из Linux-конфигурации). Примеры:
- [`MainWindow.xaml`](MainWindow.xaml:184) — десятки `PackIcon` (FolderOutline, Sync, Cog, Star, Pin и т.д.).
- [`SettingsWindow.xaml`](SettingsWindow.xaml:263) — иконки вкладок и кнопок.
- [`ConnectionSettingsWindow.xaml`](ConnectionSettingsWindow.xaml:171).
- [`PlatformVersionPickerWindow.xaml`](PlatformVersionPickerWindow.xaml:127) — `PackIcon.Style` с `DataTrigger` на `Kind`.
- [`CacheCleanWindow.xaml`](CacheCleanWindow.xaml:36).
- [`CreateInfobaseWindow.xaml`](CreateInfobaseWindow.xaml:172) и др.
- Также в [`App.xaml`](App.xaml:11) — `<materialDesign:BundledTheme .../>` и `pack://`-ссылка на `MaterialDesignThemes.Wpf`.

**Замена:** `Material.Icons.Avalonia` (аналог `PackIcon`) или собственные `PathIcon`/`DrawingImage` (часть иконок уже рисуется вручную для трея).

### 2.3. `Style.Triggers` / `DataTrigger` / `MultiDataTrigger` / `ControlTemplate.Triggers`
Используются повсеместно. В Avalonia аналог — `Style.Classes` + `:pseudo` + `DataTrigger`-селекторы. Примеры:
- [`ModernTheme.xaml`](Themes/ModernTheme.xaml:63) — `ControlTemplate.Triggers`, `Style.Triggers`.
- [`DarkTheme.xaml`](Themes/DarkTheme.xaml:621) — `MultiDataTrigger`.
- [`PlatformVersionPickerWindow.xaml`](PlatformVersionPickerWindow.xaml:133) — `Style.Triggers` с `DataTrigger`.
- [`ConnectionSettingsWindow.xaml`](ConnectionSettingsWindow.xaml:267) — `Style.Triggers` с `DataTrigger`.

### 2.4. `ControlTemplate` с `TemplateBinding` и `TargetType`
Стили кнопок/переключателей/полей переопределяют шаблоны через `ControlTemplate` + `TemplateBinding`. В Avalonia используются `ControlTheme`/`Template` с `TemplateBinding`, но API и синтаксис отличаются. Массово в:
- [`ModernTheme.xaml`](Themes/ModernTheme.xaml:47), [`DarkTheme.xaml`](Themes/DarkTheme.xaml:187).
- Практически во всех окнах (шаблон кнопки «Сохранить/Отмена»).

### 2.5. Кастомные элементы шаблонов WPF
- `PART_ContentHost` в шаблонах `TextBox` ([`ModernTheme.xaml`](Themes/ModernTheme.xaml:166)) — в Avalonia имена частей шаблона иные.
- `Popup` + `PlacementTarget`/`Placement` ([`Controls/HelpLink.xaml`](Controls/HelpLink.xaml:61)) — в Avalonia `Popup.Placement` другой.
- `ScrollViewer` с `ComputedVerticalScrollBarVisibility` / `ComputedHorizontalScrollBarVisibility` ([`MainWindow.xaml`](MainWindow.xaml:75)) — в Avalonia свойства переименованы.
- `SnapsToDevicePixels` ([`DarkTheme.xaml`](Themes/DarkTheme.xaml:252), [`CacheCleanWindow.xaml`](CacheCleanWindow.xaml:34)) — в Avalonia отсутствует.

### 2.6. Привязки и шаблоны данных
- `MultiBinding` + `IMultiValueConverter` ([`MainWindow.xaml`](MainWindow.xaml:473), `Converters/MultiValueToArrayConverter.cs`) — в Avalonia `MultiBinding` отсутствует; заменяется `MultiBinding` из `Avalonia.Data` либо конвертерами с несколькими параметрами.
- `RelativeSource AncestorType` / `TemplatedParent` / `Self` ([`MainWindow.xaml`](MainWindow.xaml:385)) — в Avalonia `RelativeSource.FindAncestor<Type>` / `Self` / `TemplatedParent`.
- `ElementName` ([`Controls/HelpLink.xaml`](Controls/HelpLink.xaml:62), [`MainWindow.xaml`](MainWindow.xaml:456)) — в Avalonia есть, но синтаксис имени окна отличается.
- `HierarchicalDataTemplate` / `DataTemplate` — в Avalonia есть, но имена/синтаксис близки; требует проверки.
- `ConverterParameter` и `StringFormat` — в основном переносимы.

### 2.7. WPF-конвертеры по умолчанию
- `BooleanToVisibilityConverter` ([`MainWindow.xaml`](MainWindow.xaml:30), [`SettingsWindow.xaml`](SettingsWindow.xaml:25), [`ConnectionSettingsWindow.xaml`](ConnectionSettingsWindow.xaml:23)) — в Avalonia нет встроенного; в проекте уже есть свой `InverseBoolToVisibilityConverter`, требуется аналог для прямого.
- `DynamicResource` / `StaticResource` — в Avalonia есть, но механика `DynamicResource` отличается (отслеживание изменений).

### 2.8. Ресурсы и темы
- `ResourceDictionary.MergedDictionaries` + `pack://` ([`App.xaml`](App.xaml:9)) — в Avalonia `Styles`/`Resources` подключаются иначе (`.axaml`).
- Темы [`LightTheme.xaml`](Themes/LightTheme.xaml) / [`DarkTheme.xaml`](Themes/DarkTheme.xaml) / [`ModernTheme.xaml`](Themes/ModernTheme.xaml) — стили `TargetType` с `BasedOn`, `x:Key`; переносятся в `Styles` с селекторами.

### 2.9. Отсутствующие в Avalonia элементы/свойства
- `GroupBox` (используется в `ModernTheme.xaml` и окнах) — в Avalonia нет `GroupBox` из коробки (заменяется `Border`+`TextBlock`).
- `TabStripPlacement` в `TabControl` ([`GroupEditWindow.xaml`](GroupEditWindow.xaml:74)) — в Avalonia `TabControl.TabStripPlacement` есть, но в ограниченном виде.
- `Expander`/`DockPanel`/`WrapPanel` — в Avalonia есть, но поведение/имена свойств отличаются.

### 2.10. Итоговая сводка по файлам

| Категория | Кол-во файлов | Типичные файлы |
|-----------|---------------|----------------|
| `materialDesign:PackIcon` | 15+ | MainWindow, SettingsWindow, ConnectionSettingsWindow, окна мастеров |
| `Style.Triggers`/`DataTrigger`/`MultiDataTrigger` | 10+ | MainWindow, ConnectionSettingsWindow, темы |
| `ControlTemplate`+`TemplateBinding` | 10+ | темы, все диалоги с кнопками |
| `MultiBinding` | 3 | MainWindow (ширина колонок, отступы) |
| `BooleanToVisibilityConverter` | 3 | MainWindow, SettingsWindow, ConnectionSettingsWindow |
| `pack://` / WPF-ресурсы | 1 | App.xaml |
| `PART_ContentHost`, `Popup`, WPF-`ScrollViewer` | 3 | ModernTheme, HelpLink, MainWindow |

> Вывод: XAML-разметка сильно завязана на WPF-специфику (PackIcon, триггеры стилей, шаблоны). Массовый перенос — задача Этапа 3; для Этапа 1 достаточно исключить Windows-зависимые пакеты и подготовить каркас.

---

## 3. Этап 1: что сделано в csproj

- [`Configuration Management.csproj`](Configuration Management.csproj) разделён на две конфигурации по ОС через `$([MSBuild]::IsOSPlatform('Windows'))`:
  - **Windows:** `net10.0-windows`, `UseWPF=true`, `UseWindowsForms=true`, `ApplicationIcon=app.ico`, пакеты `MaterialDesignThemes` + `System.Management`.
  - **Linux:** `net10.0` (без `-windows`), `OutputType=Exe`, без `UseWPF`/`UseWindowsForms`, без `System.Management`/`MaterialDesignThemes`, с `DefineConstants=LINUX`.
- Подключены Avalonia-пакеты (только Linux): `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` (dev-only).
- Условная компиляция: `#if WINDOWS` работает автоматически из `net10.0-windows`; `#if LINUX` задано через `DefineConstants`.
- RID публикации зависит от ОС: `win-x64` / `linux-x64`.
- `build.sh` теперь ОС-агностичен (`linux-x64` / `win-x64`).

> Ожидаемо: на этом этапе Linux-сборка **не компилируется полностью**, т.к. `.cs`-файлы ещё содержат WPF-типы (`System.Windows`, `MaterialDesignThemes`, `System.Management`, `Microsoft.Win32`). Порт кода — начиная с Этапа 2.

---

## 4. Этап 4: адаптация путей и хранилища (Linux-специфика 1С)

Windows-поведение полностью сохранено: правки кроссплатформенные (`#if LINUX`/`#if WINDOWS`).

### 4.1. Данные приложения (`InfobaseRepository`, `FileAppLogger`)

Введён общий помощник [`Services/PlatformPaths.cs`](Services/PlatformPaths.cs) — единая точка расчёта каталога данных и логов.

| Назначение | Windows | Linux |
|------------|---------|-------|
| Каталог данных (`infobases.json`, `groups.json`, `settings.json`) | `%APPDATA%\ConfigurationManagement` | `~/.config/ConfigurationManagement` (`XDG_CONFIG_HOME` или `~/.config`) |
| Каталог логов | `%APPDATA%\ConfigurationManagement\logs` | `~/.config/ConfigurationManagement\logs` |

`Environment.SpecialFolder.ApplicationData` на Linux и так указывает на `~/.config`, но путь задан явно (с учётом `XDG_CONFIG_HOME`), чтобы не зависеть от реализации рантайма.

### 4.2. Файл списка баз `ibases.v8i` (`IbasesV8iImporter.FindDefaultPath`)

| Windows | Linux |
|---------|-------|
| `%APPDATA%\1C\1CEStart\ibases.v8i` | `~/.1cv8/1CEStart/ibases.v8i`; `~/.local/share/1cv8/1CEStart/ibases.v8i`; `~/.local/share/1C/1CEStart/ibases.v8i`; `$XDG_DATA_HOME/1cv8/1CEStart/ibases.v8i` |

### 4.3. Кэш 1С (`OneCCacheCleaner.GetCacheRoots`, интерфейс `OneCCacheKind` сохранён)

| Тип | Windows | Linux |
|-----|---------|-------|
| Программный (`Program`) | `%LOCALAPPDATA%\1C\1cv8` | `~/.cache/1cv8` (`XDG_CACHE_HOME`) |
| Пользовательский (`User`) | `%APPDATA%\1C\1cv8` | `~/.local/share/1cv8` (`XDG_DATA_HOME`); `~/.1cv8/1cv8` |

### 4.4. Шаблоны конфигураций (`OneCTemplateService`)

| Назначение | Windows | Linux |
|------------|---------|-------|
| Каталог по умолчанию | `%PUBLIC%\Documents\1C\1cv8\tmplts` | `~/.local/share/1C/1cv8/tmplts` (`XDG_DATA_HOME`) |
| Системные | — | `/opt/1cv8/<версия>/tmplts`, `/usr/share/1cv8/tmplts` |
| Источник настроенных путей | Реестр `Software\1C\...` + `1cestart.cfg` | `1cestart.cfg` из `~/.1cv8/1CEStart/` (реестр убран под `#if WINDOWS`) |
| Разбор путей из `1cestart.cfg` | Windows-регулярка (`C:\...`) | Linux-регулярка (`/...`) |

Файл [`Services/OneCTemplateService.cs`](Services/OneCTemplateService.cs) теперь компилируется и в Linux-сборке (убран из `<Compile Remove>` в csproj); чтение реестра полностью обёрнуто в `#if WINDOWS`.

### 4.5. COM-сервисы (`OneCComConnector`, `OneCComConnectorRegistrar`) — Этап 5

На Linux COM отсутствует. Замена реализована в [`Services/OneCComConnector.Linux.cs`](Services/OneCComConnector.Linux.cs):

- `OneCComConnector` реализует `IOneCComConnector` **без COM**:
  - `Connect` возвращает `null` (COM-подключение недоступно);
  - `ReadConfigurationInfo` читает версию конфигурации по эвристике файла `1Cv8.1CD`, а для клиент-серверных баз и файловых — через пакетный режим конфигуратора `DESIGNER /DumpCfg` (сборка версии из выгрузки). Требований WMI/ProgID/реестра нет.
  - `BuildConnectString` строит `File=`/`Srvr=`/`WS=` строку (информационно).
- `OneCComConnectorRegistrar` и `IOneCComConnectorRegistrar` — no-op: COM не регистрируется.
- Из DI (Linux-ветка [`AppServices.cs`](AppServices.cs)) регистратор COM убран; остаётся только `IOneCComConnector` → `OneCComConnector`.
- WPF-оригинал [`Services/OneCComConnector.cs`](Services/OneCComConnector.cs) и [`OneCComConnectorRegistrar.cs`](Services/OneCComConnectorRegistrar.cs) остаются только в Windows-сборке.

### 4.6. Сервисы платформы (`PlatformVersionService`, `OneCLauncher`, `InfobaseMaintenanceService` — Linux, Этап 5)

Полноценные Linux-реализации вынесены в отдельные файлы `*.Linux.cs` (включаются дефолтным глобом, обёрнуты в `#if LINUX`). Временные заглушки [`Services/LinuxStubs.cs`](Services/LinuxStubs.cs) и [`Services/LinuxOneCServiceShims.cs`](Services/LinuxOneCServiceShims.cs) удалены из Linux-сборки (`<Compile Remove>` в csproj).

- [`Services/PlatformVersionService.Linux.cs`](Services/PlatformVersionService.Linux.cs) — поиск установленных версий по корням `/opt/1cv8`, `/opt/1cv8.x86_64`, `/opt/1cv8.x86`, `~/.1cv8`, `/usr/share/1cv8`, `/usr/bin`, `/usr/local/1cv8` и дополнительным папкам. Бинарники **без `.exe`**: `1cv8`, `1cv8c`, `1cv8s`, `1cv8a`, `ragent`. Структура `/opt/1cv8/<вер>/bin/1cv8`. Разрядность определяется по имени каталога (`x86_64`/`i386`) и ELF-классу через `readelf`; резолв симлинка `/usr/bin/1cv8`. Возвращает `PlatformVersionInfo` (Display, путь). Сохранены методы, используемые окнами: `ParseVariant`, `FormatVariant`, `GetVersionLine`, `GetVersionBuildGroup`, `FormatArchitectureLabel`, `BuildGroupedTree`, `FindInstalledVersions`, `FindInstalledVersionInfos`, `GetSearchRoots`, `ResolveVersionBinDirectory`, `FindPlatformVersionDirs`, `SetAdditionalSearchPaths`.
- [`Services/OneCLauncher.Linux.cs`](Services/OneCLauncher.Linux.cs) — запуск базы в режимах `ENTERPRISE`/`DESIGNER` (конфигуратор) через `/opt/1cv8/<вер>/bin/1cv8` (или `1cv8c`) через `Process.Start` **без** `UseShellExecute`. Командная строка 1С совместима: `ENTERPRISE`, `CONFIG`, `/F"<путь>"`, `/S"<сервер>\<база>"`, `/RunModeManagedApplication`, `/RunModeOrdinaryApplication`, `/N`/`/P`, `/WS`, дополнительные параметры. Также здесь определены общие перечисления `OneCLaunchMode`/`OneCClientType`/`OneCRunMode`/`OneCArchitecture` (в Windows — в `OneCLauncher.cs`). Реализованы пакетные операции `DESIGNER` (`DumpIB`, `DumpCfg`, `IBCheckAndRepair -TestOnly`), `CreateInfoBase` (CREATEINFOBASE), `ResolveThickClientExe`, `BuildEnterpriseShortcutArguments`, `BuildConnectionArgument`, `BuildAuthArgument`, `GetBaseConnectionToken`, `IsDesignerBlocked`, события `DesignerBatchStarted/Completed`. `OneCLauncherService : IOneCLauncher` — полноценная Linux-реализация.
- [`Services/InfobaseMaintenanceService.Linux.cs`](Services/InfobaseMaintenanceService.Linux.cs) — завершение процессов 1С (`Process.GetProcessesByName("1cv8")`/`1cv8c`/`1cv8s`/`ragent` + `Kill`, плюс `pkill -f 1cv8`), `CountOneCProcesses`, операции с файловыми базами (`GetFileBaseDirectory`, `FileBaseExists`, блокировка `1Cv8.blocked`, размер, физическое удаление), открытие каталога через `xdg-open`, ярлык `.desktop` (вместо `.lnk`).
- [`Services/LinuxProcess.cs`](Services/LinuxProcess.cs) — чтение командной строки процессов из `/proc/<pid>/cmdline` (аналог `Win32_Process`), перечисление процессов 1С, завершение и `pkill`.
- Чтение командной строки процессов (`IsConfiguratorRunningForBase`) — по `/proc` вместо WMI: проверка, что в `cmdline` процесса `1cv8` есть `DESIGNER` и токен подключения базы.
- Регистрация в DI (Linux-ветка [`AppServices.cs`](AppServices.cs)): `IOneCLauncher` → `OneCLauncherService`, `IPlatformVersionService` → `PlatformVersionServiceAdapter`, `IOneCComConnector` → `OneCComConnector`; `IOneCComConnectorRegistrar` не подключается.

---

## 5. Ярлыки, файловые менеджеры, открытие папок, трей (Этап 6)

Этап 6 закрывает «повседневные» сценарии рабочего стола на Linux. Windows-поведение не изменено: все правки — в `*.Linux.cs` / `*.Avalonia.cs` и под `#if LINUX`.

### 5.1. Открытие каталога и выделение файла 1Cv8.1CD

[`Services/InfobaseMaintenanceService.Linux.cs`](Services/InfobaseMaintenanceService.Linux.cs) — вместо `explorer.exe /select,`:

- Если путь указывает на **файл** `1Cv8.1CD` (или каталог базы содержит его) — файл **выделяется** в менеджере: приоритет `nautilus --select` / `dolphin --select` (поддерживают выделение файла), затем `gio open` (открывает каталог файла), в крайнем случае `xdg-open` каталога.
- Если путь — **каталог** — открывается `xdg-open <path>`.
- Если путь не существует — открывается родительский каталог.
- Запуск — через `Process.Start` с `UseShellExecute=false`, `CreateNoWindow=true`, `ArgumentList` (корректно для исполняемых файлов из `PATH`).

### 5.2. Ярлык на рабочем столе `.desktop` (вместо `.lnk`)

[`CreateDesktopShortcut`](Services/InfobaseMaintenanceService.Linux.cs) создаёт файл `*.desktop` на рабочем столе (определяется через `xdg-user-dir DESKTOP`, fallback `~/Desktop`, `~/Рабочий стол`) или в `~/.local/share/applications`:

- `Type=Application`, `Terminal=false`, `Name=`, `Comment=`, `Categories=Office;`, `StartupNotify=true`, `StartupWMClass=1cv8`.
- `Exec=<путь к 1cv8> ENTERPRISE /F"..."` (цель — `1cv8`, а не `1CEStart.exe`; путь резолвится через `OneCLauncher.ResolveThickClientExe`).
- `Icon=` указывает на исполняемый `1cv8` (или может быть заменён на имя темы/PNG).
- `%` в `Exec` экранируется (`%%`) как управляющие коды полей desktop-спецификации.
- Файлу выставляются права `chmod +x` (`File.SetUnixFileMode`), что необходимо для отображения ярлыка на большинстве DE. Базовая запись файла достаточна; `update-desktop-database` не обязателен.

### 5.3. Открытие веб-ссылок / URL (`e1c://`, `http(s)://`)

- [`OneCLauncher.LaunchByLink`](Services/OneCLauncher.Linux.cs) — аналог «Перейти по ссылке»: `e1c://…`, `http://`, `https://` открываются системным обработчиком через `xdg-open <url>`.
- Файловая (`File="..."`, путь) и клиент-серверная (`Srvr="...";Ref="..."`, `server\base`) ссылки запускаются через платформу `1cv8` (`ENTERPRISE /F…` / `/S…`).
- Веб-клиент `LaunchWebClient` также использует `xdg-open` (URL).

### 5.4. Трей (`MainWindow.Avalonia.cs`)

[`MainWindow.Avalonia.cs`](MainWindow.Avalonia.cs) использует `TrayIcon` + `NativeMenu` (Avalonia) **без `System.Drawing`**:

- Иконка трея загружается из PNG (`tray_icon_preview.png`) через `Avalonia.Media.Imaging.Bitmap` + `WindowIcon` (не из `System.Drawing`). PNG добавлены как `EmbeddedResource` в Linux-ItemGroup csproj, чтобы иконка была доступна независимо от рабочего каталога.
- Меню трея: «Показать окно», **«Недавние базы»** (быстрый запуск), **«Предприятие»/«Конфигуратор»** для выбранной базы, «Синхронизация с ibases.v8i», «Настройки», «Выход».
- **Закрытие в трей**: `OnClosing` отменяет закрытие и прячет окно (`Hide()`), пока пользователь не выберет «Выход» (команда сбрасывает флаг и вызывает `Shutdown`).
- Восстановление из трея — «Показать окно» / `ShowAndActivate()` (также вызывается при повторном запуске через файл-сигнал активации).

**Ограничение по DE:** на GNOME Shell (Wayland/X11) без установленного расширения **AppIndicator** системный трей может не отображаться. Avalonia `TrayIcon` использует статус-иконку GTK; на GNOME её нет по умолчанию. Рекомендуется ставить расширение AppIndicator (ubuntu-appindicator). Окно при этом продолжает работать в обычном режиме — трей не является критичным для функциональности. На KDE Plasma, Xfce, Cinnamon и других DE с поддержкой системного трея иконка отображается штатно.

### 5.5. Запуск родного стартера 1С (`1cestart`)

- [`InfobaseMaintenanceService.OpenNativeStarter`](Services/InfobaseMaintenanceService.Linux.cs) — на Linux стартер называется **`1cestart`** (без `.exe`).
- Поиск: `/opt/1cv8/<вер>/common/1cestart` (через `FindPlatformVersionDirs`), затем `/opt/1cv8/common/1cestart`, `/opt/1cv8.x86_64/common/1cestart`, `/usr/bin/1cestart`, `/usr/local/bin/1cestart`, `~/.1cv8/1CEStart/1cestart`.
- Запуск — `Process.Start` без `UseShellExecute`.

### 5.6. Команды и UI

- В [`MainViewModel.Avalonia.cs`](ViewModels/MainViewModel.Avalonia.cs) добавлены команды `OpenInfobaseFolderCommand`, `CreateDesktopShortcutCommand`, `OpenNativeStarterCommand` и свойство `RecentInfobases` (для меню трея).
- В правой панели [`MainWindow.Avalonia.cs`](MainWindow.Avalonia.cs) добавлены кнопки «Открыть папку базы», «Ярлык на рабочем столе», «Запустить стартер 1С».

### 5.7. Проверка на Windows-only вызовы

`MainWindow.Avalonia.cs` не использует `explorer.exe`, `.lnk`, COM, `System.Drawing` — все операции вынесены в Linux-сервисы (`InfobaseMaintenanceService.Linux.cs`, `OneCLauncher.Linux.cs`). Windows-сборка не затрагивается (правки только под `#if LINUX` / в `*.Linux.cs` / `*.Avalonia.cs`).

---

## 6. Этап 7: Сборка и упаковка

Этап 7 настраивает публикацию single-file для Linux и упаковку в AppImage / `.deb`. Windows-публикация (`win-x64`) не изменена — скрипты разделяют ОС.

### 6.1. Публикация single-file linux-x64

- [`build.sh`](build.sh) ОС-агностичен: `linux-x64` / `win-x64` определяются по `uname`. Публикация:
  ```bash
  dotnet publish "Configuration Management.csproj" -c Release -r linux-x64 --self-contained true \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:EnableCompressionInSingleFile=true -o publish/linux-x64
  ```
  Результат — один исполняемый файл `ConfigurationManagement` (self-contained, без установки .NET Runtime).
- Выделенный скрипт [`build-linux-single-file.sh`](build-linux-single-file.sh) собирает **ровно один** исполняемый файл в `dist/<RID>/ConfigurationManagement` (по умолчанию `linux-x64`), удаляя `.pdb` и побочные файлы из выходного каталога:
  ```bash
  ./build-linux-single-file.sh                # Release, linux-x64
  ./build-linux-single-file.sh Debug          # другой конфиг
  RID=linux-arm64 ./build-linux-single-file.sh   # другой RID
  ```
  Скрипт запускается **только на Linux** (TFM `net10.0` выбирается по ОС в csproj) и перед публикацией проверяет наличие .NET SDK 10.
- Иконки в single-file: PNG для трея (`tray_icon_preview.png`) и иконка приложения (`app_icon_preview.png`) добавлены как `EmbeddedResource` в Linux-ItemGroup [`Configuration Management.csproj`](Configuration Management.csproj) (Этап 6) и попадают в сборку независимо от рабочего каталога. Внешняя иконка для пакета — `package/linux/app.png`.
- [`build.ps1`](build.ps1) — только Windows (`win-x64`); Linux-порт его не затрагивает.

### 6.2. Упаковка

- **AppImage** — [`package/linux/appimage.sh`](../package/linux/appimage.sh): берёт single-file из `publish/linux-x64`, собирает `AppDir` (`usr/bin`, `usr/share/applications`, `usr/share/icons/hicolor/256x256/apps`), создаёт `AppRun` и вызывает `appimagetool` (или `linuxdeploy`). Результат — `package/linux/out/ConfigurationManagement-x86_64.AppImage`.
- **.deb** — [`package/linux/deb/DEBIAN/control`](../package/linux/deb/DEBIAN/control) + [`package/linux/deb/build-deb.sh`](../package/linux/deb/build-deb.sh): собирает staging (`usr/bin`, `usr/share/applications`, `usr/share/icons`), генерирует `.deb` через `dpkg-deb --build --root-owner-group`. Результат — `package/linux/deb/out/configuration-management_<версия>_amd64.deb`.
- **.rpm / snap** — необязательно; `.deb` может быть пересобран через `alien`. Скрипты для `.rpm`/snap не созданы.
- **Иконка** — `package/linux/app.png` (переиспользует `app_icon_preview.png`); в пакетах кладётся как `configuration-management.png` в `hicolor/256x256/apps`, а `.desktop` ссылается на `Icon=configuration-management`.
- **`.desktop`** — [`package/linux/configuration-management.desktop`](../package/linux/configuration-management.desktop): `Name=Управление конфигурациями 1С`, `Exec=ConfigurationManagement`, `Icon=configuration-management`, `Type=Application`, `Categories=Office;Development;`, `StartupWMClass=ConfigurationManagement`.

### 6.3. Статус этапа

- [x] Публикация single-file linux-x64 в `build.sh`
- [x] `build.ps1` (Windows) не сломан
- [x] Иконка `app.png` для Linux
- [x] `.desktop`-файл для пакета
- [x] Скрипт AppImage (`AppDir` + `appimagetool`/`linuxdeploy`)
- [x] Скрипт `.deb` (`DEBIAN/control` + `dpkg-deb`)
- [x] `README.md` — раздел «Сборка и установка на Linux», обновлены «Требования»
- [ ] Фактическая публикация/упаковка на Linux (требует Linux-хоста) — на машине Windows не выполняется
- [ ] `.rpm` / snap (необязательно)