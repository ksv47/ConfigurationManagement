# План: исправление issue #217 (разделение колонки «Конфигурация») и релиз 0.3.6.100

## Контекст

- Репозиторий: `sivatorov/ConfigurationManagement`, ветка `main`.
- Последний релиз: `v0.3.6.99` (09.09.2026). Целевая новая версия: **`0.3.6.100`** (перед правкой сверить текущее значение в `Configuration Management.csproj`).
- Проект двухплатформенный: WPF (`net10.0-windows`, символ `WINDOWS`) и Avalonia (`net10.0`, символ `LINUX`). Linux-сборка на Windows через `-p:ForceLinux=true`.

## Scope (отбор issues по критериям «нет комментариев ИЛИ последний комментарий не от sivatorov»)

Из 12 открытых issues под критерии попадает только **#217** (0 комментариев) → правка по описанию.

Остальные 11 issues имеют последний комментарий от `sivatorov` → вне scope (уже закрыты предыдущими версиями 0.3.6.64…0.3.6.99).

### Аномалия #175 (НЕ кодовая правка)
Последний комментарий на #175 от `sivatorov` — буквально `@_comment_175.md` (не развёрнутый черновик `_comment_175.md`). Сама правка уже в 0.3.6.99. Нужно решение пользователя: переопубликовать корректный текст комментария (#175).

## Issue #217 — Разделить колонку «Конфигурация» + переименовать «Версия платформы»

Запрос (автор 7OH, без комментариев):
1. Разделить колонку «Конфигурация» на две:
   - «Конфигурация» — показывать только название (`Infobase.ConfigurationName`);
   - новая колонка «№ релиза» — показывать только номер версии (`Infobase.ConfigurationVersion`).
2. Переименовать заголовок «Версия платформы» → «Платформа».

### Модель и настройки
- `Models/Infobase.cs`: поля `ConfigurationName` / `ConfigurationVersion` уже есть — новых полей не нужно. При необходимости добавить аккуратное отображение версии (триминг), но можно биндиться напрямую.
- `Models/AppSettings.cs`: добавить `bool ShowConfigurationVersionColumn { get; set; } = true;` и `double ConfigurationVersionColumnWidth { get; set; }`.
- `ViewModels/MainViewModel.cs` (WPF) и `MainViewModel.Avalonia.cs`:
  - загружать/сохранять `ShowConfigurationVersionColumn`, `ConfigurationVersionColumnWidth`;
  - свойство `ShowConfigurationVersionColumn`, `ConfigurationVersionColumnWidth` + `OnPropertyChanged`;
  - в `ApplyColumnVisibility`/`SetColumnVisibility`/`ColumnWidth`-свичах добавить ключ `ConfigurationVersion`;
  - в сохранённый `ColumnOrder` при отсутствии вставить ключ `ConfigurationVersion` сразу после `Configuration` (миграция для существующих профилей). Неизвестные ключи уже пропускаются — обратная совместимость сохранена.

### WPF (`Views/MainWindow.xaml`, `ViewModels/MainViewModel.Display.cs`)
- Добавить новую `ColumnDefinition` для «№ релиза» сразу после колонки «Конфигурация»; сдвинуть индексы последующих колонок (сейчас «Конфигурация» на индексе 11 в заголовке и строках). Это три сетки: заголовок, шаблон строки базы, шаблон строки группы.
- Заголовок: `TextBlock` с ключом локализации нового заголовка.
- Ячейка строки: `TextBlock` с `{Binding ConfigurationVersion}`.
- Колонка «Конфигурация»: сменить привязку с `{Binding ConfigurationDisplay}` на `{Binding ConfigurationName}`.
- Добавить сплиттер для новой колонки (аналог `ConfigurationSplitter`).
- Меню скрытия колонки (контекстное меню заголовка, `OnColumnHeaderContextMenu_Hide`) — добавить пункт для новой колонки.
- `MainViewModel.Display.cs` — свойство/сохранение ширины и видимости новой колонки.
- Карточка деталей выбранной базы (строки 2019–2021) оставить показом `ConfigurationDisplay` (название + версия) — это панель сведений, а не колонка.

### Avalonia (`Views/MainWindow.Avalonia.cs`)
- `ListColumns()`: добавить `case "ConfigurationVersion"` → `Add(_vm.ShowConfigurationVersionColumn, "ConfigurationVersion", "Column.ConfigurationVersion", _vm.ConfigurationVersionColumnWidth, 80)`.
- `ColumnValue()`: добавить `"ConfigurationVersion" => ib.ConfigurationVersion ?? string.Empty`; изменить `"Configuration"` на `ib.ConfigurationName ?? string.Empty`.
- `BuildColumnHeader()`/заголовочная часть: ключ добавленного столбца разворачивается автоматически через `ListColumns` (проверить согласованность шапки и строк, как уже сделано для остальных колонок).
- `SettingsWindow.Avalonia.cs` (список видимых колонок ~780–800) и `SettingsWindow.Display.cs` (WPF ~46–80): добавить `"ConfigurationVersion" => "Column.ConfigurationVersion"` и привязку видимости.

### Локализация (`Localization/Languages/ru.json`, `en.json`)
- `Column.Version`: `Версия платформы` → `Платформа` (ru) / `Platform version` → `Platform` (en). Учесть, что ключ также используется в статус-баре (`DisplayCheck("Column.Version", …)` для `StatusShowPlatformVersion`) — изменение применится и там (согласовано с духом запроса; при необходимости вынести отдельный ключ для статус-бара).
- Новый ключ `Column.ConfigurationVersion`: `№ релиза` (ru) / `Release` (en).
- `Settings.Columns.Configuration`: `Конфигурация (название и версия)` → `Конфигурация`; при необходимости добавить `Settings.Columns.ConfigurationVersion` = `Релиз конфигурации` / `Configuration release`.

## Версия, CHANGELOG, README
- Поднять `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` в `Configuration Management.csproj` до `0.3.6.100`.
- Добавить раздел `[0.3.6.100]` в `CHANGELOG.md` (описание правки #217, ссылка на issue).
- Обновить `README.md` при необходимости.
- Создать `_release/0.3.6.100.md` по образцу существующих заметок.

## Комментарии к issues
- К **#217** добавить комментарий от `sivatorov`: что исправлено и в версии `0.3.6.100`, ссылка на CHANGELOG. Закрыть issue.
- По **#175** (по решению пользователя): заменить/дополнить сломанный плейсхолдер корректным текстом из `_comment_175.md` (что исправлено в 0.3.6.99).

## Сборка
- Windows (WPF, single-file, self-contained): `dotnet publish "Configuration Management/Configuration Management.csproj" -c Release` (или `build-windows-single-file.ps1`).
- Linux (Avalonia, single-file, self-contained): `dotnet publish ... -c Release -p:ForceLinux=true` (или `build-linux-single-file.sh` / `.ps1`).
- Проверить успешность обеих сборок.

## Публикация и релиз
- `git add`, коммит («Fix issue #217, bump version to 0.3.6.100»), push в `origin/main`.
- Создать GitHub-релиз `v0.3.6.100` с прикреплёнными single-file исполняемыми файлами (Windows и Linux) и описанием изменений.

## Декомпозиция на задачи (каждый пункт — отдельная задача)
1. Архитектурное планирование (текущий документ).
2. Правка #217 (WPF + модель + настройки + локализация).
3. Правка #217 (Avalonia + окно настроек).
4. Поднятие версии до 0.3.6.100 + CHANGELOG.md + README + `_release/0.3.6.100.md`.
5. Комментарии к issues (#217, при необходимости #175).
6. Сборка single-file исполняемых файлов Windows и Linux.
7. Push изменений + создание GitHub-релиза `v0.3.6.100`.