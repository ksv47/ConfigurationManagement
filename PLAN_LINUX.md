# План портирования «Управление конфигурациями 1С» на Linux

## 1. Анализ текущей архитектуры

### 1.1. Технологический стек (Windows)

| Компонент | Текущее решение | Примечание |
|-----------|----------------|------------|
| UI-фреймворк | **WPF** (`UseWPF=true`) | ~20 окон XAML, кастомные контролы, конвертеры, 4 темы |
| Трей | **WinForms** (`UseWindowsForms=true`) | `NotifyIcon`, `ContextMenuStrip`, GDI+ (`System.Drawing`) |
| Отрисовка иконок трея | **GDI+** (`System.Drawing`) | Ручная отрисовка Material-иконок в `Bitmap` |
| Целевой фреймворк | `net10.0-windows` | Windows-only TFM |
| DI | `Microsoft.Extensions.DependencyInjection` | Переносится без изменений |
| Иконки UI | `MaterialDesignThemes 5.2.1` (PackIcon) | WPF-библиотека, требует замены |
| WMI / процессы | `System.Management` + `Win32_Process` | Windows-only, требует замены |
| Реестр | `Microsoft.Win32.Registry` | Windows-only |
| COM | `WScript.Shell` (ярлыки .lnk), `V83.COMConnector` (1С) | Windows-only |
| P/Invoke | `user32.dll` (`SetForegroundWindow`, `ShowWindow`) | Windows-only |
| Проводник | `explorer.exe` | Windows-only |

### 1.2. Инвентаризация платформозависимого кода

| Файл | Платформозависимость |
|------|----------------------|
| [`App.xaml.cs`](Configuration Management/App.xaml.cs:289) | P/Invoke `user32.dll` для активации окна; `Mutex`/`EventWaitHandle` с `Global\` (на Linux — другое пространство имён) |
| [`MainWindow.xaml.cs`](Configuration Management/MainWindow.xaml.cs:490) | Трей (WinForms `NotifyIcon`), контекстное меню трея, отрисовка иконок через `System.Drawing`, DPI/мониторы |
| [`Services/OneCLauncher.cs`](Configuration Management/Services/OneCLauncher.cs:925) | Поиск `1cv8.exe`/`1cv8c.exe`/`1CEStart.exe`, чтение командной строки через WMI, пакетные операции `DESIGNER` |
| [`Services/PlatformVersionService.cs`](Configuration Management/Services/PlatformVersionService.cs:160) | Поиск версий по `Program Files`, `1cv8*.exe` |
| [`Services/InfobaseMaintenanceService.cs`](Configuration Management/Services/InfobaseMaintenanceService.cs:188) | Ярлыки `.lnk` через COM `WScript.Shell`; `explorer.exe`; завершение процессов 1С |
| [`Services/OneCComConnector.cs`](Configuration Management/Services/OneCComConnector.cs:251) | COM-коннектор `V83.COMConnector`, реестр для ProgID |
| [`Services/OneCComConnectorRegistrar.cs`](Configuration Management/Services/OneCComConnectorRegistrar.cs:237) | `regsvr32.exe`, реестр, `System32`/`SysWOW64` |
| [`Services/OneCTemplateService.cs`](Configuration Management/Services/OneCTemplateService.cs:222) | Пути `%PUBLIC%`, `%APPDATA%`, реестр `Software\1C\...` |
| [`Services/OneCCacheCleaner.cs`](Configuration Management/Services/OneCCacheCleaner.cs:164) | Пути `%LOCALAPPDATA%`/`%APPDATA%` |
| [`Services/IbasesV8iImporter.cs`](Configuration Management/Services/IbasesV8iImporter.cs:322) | Путь `%APPDATA%\1C\1CEStart\ibases.v8i` |
| [`Services/InfobaseRepository.cs`](Configuration Management/Services/InfobaseRepository.cs:28) | Каталог данных `%APPDATA%\ConfigurationManagement` |
| Окна (`*Window.xaml.cs`) | `Microsoft.Win32.OpenFolderDialog`/`SaveFileDialog`/`OpenFileDialog` |
| [`ConnectionSettingsWindow.xaml.cs`](Configuration Management/ConnectionSettingsWindow.xaml.cs:97) | `Microsoft.Win32.OpenFolderDialog` (нет в стандартном Avalonia) |

### 1.3. Ключевые выводы

1. **WPF не работает нативно на Linux.** Нужен перенос UI на кроссплатформенный фреймворк.
2. **Половина сервисов** завязана на Windows-специфику 1С (`1cv8.exe`, COM, реестр, `.lnk`, `ibases.v8i` в `%APPDATA%`).
3. **На Linux платформа 1С устроена иначе**: исполняемые файлы без `.exe` (`1cv8`, `1cv8c`, `1cestart`), конфиги в `~/.1cv8/1CEStart/`, нет COM-коннектора (есть CLI и `-Fr` режимы), иные пути кэша и шаблонов.
4. **Модели, ViewModels, конвертеры, DI, JSON-хранилище — переносимы почти без изменений** (это чистый .NET).

---

## 2. Выбор стратегии и фреймворка

### 2.1. Варианты

| Вариант | Плюсы | Минусы | Вердикт |
|---------|-------|--------|---------|
| **Avalonia UI** | Ближайший синтаксис к WPF (`XAML`, `DataGrid`, `Styles`, `Converters`); перенос XAML-разметки минимален; отличная поддержка Linux; готовый трей | Часть WPF-API переименована/отличается (ресурсы, `GridLength`, шрифты) | **Рекомендуется** |
| Uno Platform | Хорош для WinUI/UWP | Другой синтаксис, хуже поддержка десктопного Linux | Нет |
| .NET MAUI | Официальный | Нет полноценной поддержки десктопного Linux | Нет |
| WPF под Wine | Минимум работы | Не настоящий порт; проблемы с треем, DPI, зависимостями | Только как временный вариант |

### 2.2. Рекомендация

**Портировать UI на Avalonia 11.x**, а сервисы — на **условную компиляцию/абстракции** под Linux-специфику платформы 1С. Подход «одна кодовая база + платформенные абстракции» сохранит возможность собирать и Windows-версию.

---

## 3. Этапы портирования

### Этап 0. Подготовка (основание решения)
- [ ] Зафиксировать поддерживаемые дистрибутивы (Ubuntu/Debian, ALT Linux, Astra Linux) и разрядность.
- [ ] Определить минимально поддерживаемую версию Linux-платформы 1С:Предприятие (8.3.x).
- [ ] Провести аудит всех `*.xaml` на несвойственные Avalonia конструкции.

### Этап 1. Смена target framework и подключение Avalonia
- [ ] В [`Configuration Management.csproj`](Configuration Management/Configuration Management.csproj:5) убрать `net10.0-windows` → `net10.0` (+ отдельная конфигурация для Windows `net10.0-windows`).
- [ ] Убрать `UseWPF`, `UseWindowsForms`, `ApplicationIcon` для Linux-конфигурации.
- [ ] Подключить NuGet: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`/`Simple`, `Avalonia.Fonts.Inter`, `Avalonia.Diagnostics` (dev).
- [ ] Заменить `MaterialDesignThemes` на аналог (например `Material.Icons.Avalonia` или собрать иконки вручную, как сейчас для трея).
- [ ] Убрать `System.Management` (Windows-only).
- [ ] Добавить директивы `#if WINDOWS` / `#if LINUX` (или отдельные файлы сервисов).

### Этап 2. Порт инфраструктуры приложения
- [ ] **`App.xaml`/`App.xaml.cs`**: заменить `Application` WPF на `Avalonia.Application`; `OnStartup` → `OnFrameworkInitializationCompleted`; регистрация DI.
- [ ] **Обработка ошибок**: перенести `DispatcherUnhandledException`/`AppDomain` в эквиваленты Avalonia.
- [ ] **Один экземпляр**: заменить `Mutex`+`EventWaitHandle` на cross-platform (файловый lock `FileStream` с `FileShare.None` в каталоге приложения) — `Global\`-имен не существует на Linux.
- [ ] **Активация окна из второго экземпляра**: вместо `user32.dll` использовать файл-сигнал (создание файла `activate`) либо локальный сокет/Unix-сокет.
- [ ] **DI-контейнер** `AppServices.cs`: заменить регистрацию `MainWindow`/`WpfDialogService` на Avalonia-версии.

### Этап 3. Порт окон и контролов (XAML → Avalonia)
- [ ] Механический перевод разметки `*Window.xaml` (≈20 окон) в Avalonia XAML. Ключевые соответствия:
  - `DataGrid` → `DataGrid` (Avalonia имеет свой; часть свойств и шаблонов отличается).
  - `ResourceDictionary` → `ResourceDictionary` (Avalonia: `Styles` + `Resources`).
  - `System.Windows.Thickness` → `Thickness` (Avalonia), `GridLength` → `GridLength`.
  - `IValueConverter` → `IValueConverter` (Avalonia: `object? Convert(object? value, ...)`).
  - `MessageBox` → свой диалог (Avalonia не имеет `MessageBox` из коробки) — расширить `WpfDialogService`.
  - `OpenFileDialog`/`SaveFileDialog` → `StorageProvider` (Avalonia).
  - `OpenFolderDialog` (нет в Avalonia) → реализовать через `StorageProvider` (папки) или сторонний пакет.
- [ ] Кастомные контролы [`Controls/`](Configuration Management/Controls/LeveledTreeView.cs): `GroupTreeView`, `InfobaseListView`, `LeveledTreeView` — переписать под Avalonia (дерево и виртуализация списка).
- [ ] Конвертеры [`Converters/`](Configuration Management/Converters/GroupColorConverter.cs) — почти все переносятся «как есть», правка сигнатур.
- [ ] Темы [`Themes/`](Configuration Management/Themes/ThemeManager.cs) — переписать `LightTheme.xaml`/`DarkTheme.xaml` под Avalonia `Styles`; `ThemeManager.ApplyScheme` — на динамическое изменение `ResourceDictionary`.
- [ ] **Трей**: заменить WinForms `NotifyIcon` на `Avalonia.Controls.Notifications` / пакет `Avalonia.Controls.TrayIcon` (есть в Avalonia 11) или `AvaloniaUI.Hydra`.
- [ ] **Отрисовка иконок трея**: заменить `System.Drawing` на `Avalonia.Media.DrawingContext` / рендер в `WriteableBitmap`.

### Этап 4. Адаптация путей и хранилища (Linux-специфика 1С)
- [ ] **Данные приложения**: `%APPDATA%\ConfigurationManagement` → `~/.config/ConfigurationManagement` (через `Environment.SpecialFolder.ApplicationData` — на Linux он уже указывает туда, проверить).
- [ ] **`ibases.v8i`**: на Linux платформа хранит список в `~/.1cv8/1CEStart/ibases.v8i` (или `~/.local/share/1cv8`). Обновить `IbasesV8iImporter.FindDefaultPath()`.
- [ ] **Кэш 1С**: каталоги `~/.1cv8/1cv8/<ver>/...` вместо `%LOCALAPPDATA%\1C\1cv8`. Обновить `OneCCacheCleaner`.
- [ ] **Шаблоны**: `~/.local/share/1C/1cv8/tmplts` / системные `/opt/1cv8/.../tmplts`. Обновить `OneCTemplateService`; убрать чтение реестра, заменить чтением `1cestart.cfg` (он в `~/.1cv8/1CEStart/`).

### Этап 5. Порт сервисов запуска и платформы 1С
- [ ] **`PlatformVersionService`**: искать `1cv8` (без `.exe`) в `/opt/1cv8`, `~/.1cv8`, `/usr/bin`; бинарники `1cv8`, `1cv8c`, `1cv8s`, `ragent`. Определение архитектуры через `file`/`readelf` вместо `.exe`.
- [ ] **`OneCLauncher`**: командная строка 1С на Linux **совместима** (`ENTERPRISE`, `/F`, `/S`, `/RunModeManagedApplication` и т.д.), но пути разделители и запуск через `/opt/1cv8/<ver>/bin/1cv8`. Убрать `1CEStart.exe`/`1cv8x64.exe`, WMI.
- [ ] **Чтение командной строки процессов** (`Win32_Process`): заменить на чтение `/proc/<pid>/cmdline` (Linux).
- [ ] **Завершение процессов 1С**: `Process.GetProcessesByName("1cv8")` (без `.exe`) + `Kill()`, либо `pkill`.
- [ ] **COM-коннектор** (`OneCComConnector`): на Linux COM отсутствует. Использовать **`1cv8` в режиме `-Fr <база> -Execute ...`** / `DESIGNER` для чтения метаданных, либо **вызов `rac`/`1cv8`** через CLI. Заменить `IOneCComConnector` на реализацию через командную строку. `OneCComConnectorRegistrar` — удалить/заглушить (нет COM-регистрации).

### Этап 6. Ярлыки, файловые менеджеры, открытие папок
- [ ] **Ярлык на рабочем столе**: вместо `.lnk` (COM `WScript.Shell`) создавать **`.desktop`-файл** (`~/.local/share/applications/*.desktop`) или на рабочем столе.
- [ ] **Открытие каталога в файловом менеджере**: вместо `explorer.exe /select,` — `xdg-open`/`xdg-open <path>` (или `gio open`, `nautilus`/`dolphin`).
- [ ] **Открытие ссылок/веб**: `xdg-open` для URL.
- [ ] **Запуск стартера 1С** (`1CEStart.exe` → `1cestart` в `/opt/1cv8/.../common/`).

### Этап 7. Сборка и упаковка
- [ ] Обновить [`build.sh`](Configuration Management/build.sh) и [`build.ps1`](Configuration Management/build.ps1) под Avalonia+Linux.
- [ ] Self-contained single-file публикация: `dotnet publish -r linux-x64 --self-contained true -p:PublishSingleFile=true`.
- [ ] Упаковка: **AppImage** / **.deb** / **.rpm** / snap (по выбору).
- [ ] Иконка приложения: перенести `app.ico` → `app.png`/`.desktop` `Icon=`.
- [ ] Обновить `README.md` и `CHANGELOG.md` (раздел «Linux»).

### Этап 8. Тестирование
- [ ] Смок-тест запуска на чистой Ubuntu.
- [ ] Проверка всех сценариев: запуск баз, конфигуратор, выгрузка `.dt`/`.cf`, очистка кэша, синхронизация `ibases.v8i`, трей, темы, хоткеи, перетаскивание.
- [ ] Совместимость с Linux-версией 1С 8.3 (тонкий/толстый клиент).
- [ ] Тесты на многомониторной конфигурации и DPI (Avalonia поддерживает HiDPI).

---

## 4. Риски и сложности

| Риск | Описание | Митигация |
|------|----------|-----------|
| **Объём UI-порта** | ~20 окон, кастомные контролы, 4 темы | Механический конвертер XAML + ручная доводка; приоритизация окон |
| **Нет COM-коннектора на Linux** | Чтение метаданных конфигурации иначе | Альтернатива через `1cv8 -Execute`/`DESIGNER`; возможно, сокращение функциональности на первом этапе |
| **Отличия Linux-версии 1С** | Иные пути, бинарники, отсутствие `ibases.v8i`-совместимости | Отдельные реализации сервисов через абстракции (`#if LINUX`) |
| **Кастомный список/DatGrid** | Сложная виртуализация, группировка, колонки | Переписать `LeveledTreeView` под Avalonia; тестировать на больших списках баз |
| **Трей** | Разные DE (GNOME/KDE) по-разному работают с треем | Использовать `TrayIcon` Avalonia + AppIndicator (пакет `AvaloniaGtkAppIndicator`) |
| **Пути и разделители** | Обратные слэши в данных/путях | Покрыть `Path.DirectorySeparatorChar`; миграция/нормализация путей существующих данных |

---

## 5. Оценка трудозатрат (ориентировочно)

| Этап | Трудоёмкость |
|------|--------------|
| Этап 0–1 (инфраструктура, csproj, Avalonia) | 1–2 дня |
| Этап 2 (App, DI, один экземпляр, ошибки) | 2–3 дня |
| Этап 3 (окна/контролы/конвертеры/темы) | 10–20 дней |
| Этап 4 (пути, хранилище, ibases.v8i) | 3–4 дня |
| Этап 5 (сервисы платформы 1С, launcher) | 5–8 дней |
| Этап 6 (ярлыки, xdg-open) | 1–2 дня |
| Этап 7 (сборка/упаковка) | 2–3 дня |
| Этап 8 (тестирование) | 5–10 дней |
| **Итого** | **≈ 30–50 человеко-дней** |

---

## 6. Рекомендуемый порядок внедрения

1. Сначала перенести **инфраструктуру** и **одно простое окно** (например `NameInputWindow`) — доказать, что Avalonia работает.
2. Перенести **хранилище и пути** (`InfobaseRepository`, `IbasesV8iImporter`, `OneCCacheCleaner`) — база для всего остального.
3. Перенести **`PlatformVersionService` + `OneCLauncher`** — критично для основного сценария «запуск базы».
4. Перенести **главное окно** (`MainWindow`) и трей.
5. Остальные окна и сервисы — по убыванию приоритета.
6. Упаковка и тестирование на целевых дистрибутивах.