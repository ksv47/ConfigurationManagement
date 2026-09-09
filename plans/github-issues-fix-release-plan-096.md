# План: исправление issues #214, #210, #204, #200, #188, #174, #163 и релиз 0.3.6.96

## Контекст

- Репозиторий: `sivatorov/ConfigurationManagement` (ветка `main`, чистое рабочее дерево).
- Текущая версия: `0.3.6.95` (4 поля в `Configuration Management.csproj`).
- Проект двухплатформенный: WPF (`net10.0-windows`, символ `WINDOWS`) и Avalonia (`net10.0`, символ `LINUX`). Linux-сборка на Windows: `-p:ForceLinux=true`.
- Открытых issues: 22. По критериям (нет комментариев ИЛИ последний комментарий не от `sivatorov`) целевые — **7**: `#214, #210, #204, #200, #188, #174, #163`. Остальные имеют последний комментарий автора и пропускаются.
- Правило исправления: где нет комментариев — по описанию (`#214`); где есть и последний не мой — по последнему комментарию не от меня (`#210, #204, #200, #188, #174, #163`).
- Все 7 issues группируются в **одну версию `0.3.6.96`** и **один релиз** (решение пользователя).

## Порядок работы (каждый пункт — отдельная задача)

1. Внесение исправлений по каждому issue (см. ниже).
2. Поднятие версии до `0.3.6.96` + записи в `CHANGELOG.md` и `README.md`.
3. Добавление комментариев к исправленным issues на GitHub (что исправлено, в какой версии).
4. Сборка single-file исполняемых файлов для Windows и Linux.
5. Push изменений и создание GitHub-релиза `v0.3.6.96`.

---

## Issue #214 — Компактный режим 2 (нет комментариев; правка по описанию)

**Симптом:** в 0.3.6.94 опять «прыгает» компактность: включаешь компактный режим, выходишь из программы, входишь — снова средняя компактность. Возвращаешь переключателем (двойным переключением), открываешь свойства базы, сохраняешь — и компактность опять «разъезжается».

**Требуется разобраться** в механизме применения и сохранения компактного режима в WPF-сборке:

- Применение при запуске: `Views/MainWindow.xaml.cs` (событие `Loaded`, `ApplyCompact`), `Views/MainWindow.Events.cs` (`OnCompactMode_Toggled`, `AlignHeaderToData`).
- `MainViewModel.Display.cs` (`CompactMode`, `ApplyCompactMode`), `Themes/ThemeManager.cs` (`ApplyCompact`).
- Сохранение: `MainViewModel.Launch.cs` (`SaveSettings`, поле `CompactMode = _compactMode`), `Services/InfobaseRepository.cs` (`SaveSettings`).
- Переключение в окне настроек: `Views/SettingsWindow.Display.cs`, `Views/SettingsWindow.xaml.cs` (`OnCompactMode_Toggled`, флаг `_suppressCompactEvent`).

**Гипотезы для проверки (WPF):**
1. Значение `CompactMode` сохраняется, но при запуске применяется до построения дерева (или не применяется вовсе) → вид «средней» плотности. Проверить, что `ApplyCompact` вызывается на `Loaded` после построения дерева и что `_viewModel.CompactMode` читается из сохранённых настроек.
2. «Разъезжание» после сохранения свойств базы: сохранение свойств базы (окно подключения) вызывает пересборку/сохранение настроек, которое либо сбрасывает `CompactMode`, либо ломает выравнивание колонок (`AlignHeaderToData`). Проверить `Views/ConnectionSettingsWindow*` — что после Ok вызывается `SaveSettings`, не перезаписывающий компактность.
3. Двойное переключение, которое «возвращает» компактность, указывает на то, что применяются две метрики (0.8 и 1.0) без должной синхронизации — вероятно, переключатель и фактическое состояние рассинхронизированы при запуске/после сохранения.

**Ожидаемый результат:** компактный режим восстанавливается корректно при каждом запуске и не слетает после сохранения свойств базы; выравнивание колонок заголовка остаётся согласованным.

**Файлы:** `Views/MainWindow.xaml.cs`, `Views/MainWindow.Events.cs`, `ViewModels/MainViewModel.Display.cs`, `Themes/ThemeManager.cs`, `Views/ConnectionSettingsWindow.xaml.cs`, `Views/ConnectionSettingsWindow.Avalonia.cs`, `Views/SettingsWindow.xaml.cs`.

---

## Issue #210 — Три окна остались в сборке без точек входа (посл. комментарий ksv47)

Остался **один неработоспособный мёртвый файл**: `Views/TagInputWindow.Avalonia.cs` (класс никто не создаёт; его разметка `TagInputWindow.axaml` уже удалена, а конструктор содержит `AvaloniaXamlLoader.Load(this)` — при попытке создать окно вызовет исключение).

**Правка:** удалить файл `Views/TagInputWindow.Avalonia.cs`.

**Проверки:**
- Убедиться, что нигде нет ссылок на `TagInputWindow` (поиск по дереву).
- `csproj` использует SDK-globbing — удаления физического файла достаточно; убедиться, что файл не перечислен явно в `ItemGroup`.

---

## Issue #204 — Горячие клавиши: сохранённое сочетание без модификатора обрывает регистрацию (посл. комментарий ksv47)

В силе только первый пункт (второй снят автором). `MainWindow.Hotkeys.cs` не вошёл в прошлую правку: `TryParseKeyGesture` пропускает `D`, а `new KeyBinding(command, Key.D, ModifierKeys.None)` бросает `NotSupportedException`. Это гасит общий `try` в `MainWindow.xaml.cs` (строки 168–193), молча отключая Alt+1…9, восстановление последней базы и выравнивание заголовка списка для пользователей со старым `settings.json`.

**Правка (файл `Views/MainWindow.Hotkeys.cs`):**
1. **Отбраковывать значения без модификатора при чтении**: в `TryParseKeyGesture` — если `modifiers == None` и клавиша буквенная/цифровая (не F1…F24, Delete, Insert), возвращать `false` (значение игнорируется). Это защищает от старых настроек.
2. **Изолировать каждую привязку**: в `RegisterLaunchHotkeys` оборачивать создание/добавление каждого `KeyBinding` в отдельный `try/catch`, чтобы одно неверное значение не снимало остальные привязки.
3. **Вынести регистрацию хоткеев из общего `try`** в `MainWindow.xaml.cs` (строки 168–193): `RegisterLaunchHotkeys()`/`RegisterFavoriteHotkeys()` должны выполняться так, чтобы исключение не блокировало подписки на разворот узлов дерева и `RestoreLastSelection()`.

**Файлы:** `Views/MainWindow.Hotkeys.cs`, `Views/MainWindow.xaml.cs`.

---

## Issue #200 — Смена пользователя (посл. комментарий 7OH)

Два замечания:
1. **После добавления или изменения пользователя видимость кнопки не обновляется.** Кнопка «Смена пользователя» видна при `SwitchUserVisible` (профилей > 1). После создания/изменения/удаления профиля в окне настроек главное окно не уведомляется об изменении количества профилей.
   - Правка: после изменения списка профилей (`ProfileService` / `ProfilesViewModel`) поднять событие/уведомление главного окна о пересчёте `SwitchUserVisible`, либо сделать `SwitchUserVisible` зависимым от события изменения профилей (`IProfileService.ProfilesChanged`), и пересчитать видимость кнопки в `MainWindow` (WPF) и в `MainWindow.Avalonia.cs`.
2. **Окно выбора не скинится** (не применяется тема/скин MaterialDesign). `LoginWindow` открывается без применения активной темы.
   - Правка: при показе `LoginWindow` применять ту же тему/схему, что и к главному окну (MaterialDesign скин, аналогично `UpdateThemeButton`/применению схемы). Проверить `Views/LoginWindow.xaml.cs` и `Views/LoginWindow.Avalonia.cs`.

**Файлы:** `ViewModels/MainViewModel.SwitchUser.cs`, `Services/ProfileService.cs`, `ViewModels/ProfilesViewModel.cs`, `Views/LoginWindow.xaml.cs`, `Views/LoginWindow.Avalonia.cs`, `Views/MainWindow.xaml.cs`, `Views/MainWindow.Avalonia.cs`.

---

## Issue #188 — Первый запуск нового профиля: NaN в размерах окна (посл. комментарий ksv47)

Правка раскладки главного окна сделана, но сбой воспроизводился и позже: проверка от `NaN`/бесконечности есть только на раскладку главного окна, а другие дробные настройки (ширины колонок, размеры шрифтов) пишутся без неё. Одно испорченное число уносит весь `settings.json`.

**Правка (файл `Services/InfobaseRepository.cs`):** в `SettingsJsonOptions` (для `SaveSettings`) добавить
```csharp
NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
```
чтобы `NaN`/±Infinity сериализовались как литералы, не роняя весь файл настроек.

**Дополнительно (рекомендуется):** в местах записи дробных ширин колонок (главный список, окно очистки кэша, размеры шрифтов) оставить существующие точечные проверки, т.к. глобальная опция закрывает именно «одно число убивает весь файл».

**Файл:** `Services/InfobaseRepository.cs`.

---

## Issue #174 — Кнопка определения свойств конфигурации (посл. комментарий 7OH)

Два замечания:
1. **Сразу писать, каким КОМ-коннектором идёт подключение.** В прогрессе/сообщении сейчас просто «КОМ».
   - Правка: в обработчике «Определить» (`Views/ConnectionSettingsWindow.xaml.cs`, `Views/ConnectionSettingsWindow.Avalonia.cs`) и в `Services/ConfigurationInfoService.cs` показывать конкретный ProgID / версию платформы, которым пытается подключиться (поля `LastUsedProgId`/`LastUsedPlatformVersion` уже добавлены ранее).
2. **При импорте не определять свойства автоматически.** Фоновое определение свойств при импорте баз — не нужно.
   - Правка: отключить автоматический запуск определения свойств при импорте (найти точку фонового чтения — `MainViewModel.Tools.cs:1140` и место запуска при импорте), оставить определение только по явной команде пользователя (кнопка «Определить» / явное «Обновить»).

**Файлы:** `Services/ConfigurationInfoService.cs`, `Services/IOneCComConnector.cs`, `Services/OneCComConnector.cs`, `Services/OneCComConnector.Linux.cs`, `Views/ConnectionSettingsWindow.xaml.cs`, `Views/ConnectionSettingsWindow.Avalonia.cs`, `ViewModels/MainViewModel.Tools.cs`.

---

## Issue #163 — Миграция со StartManager (посл. комментарий 7OH)

Просьба: **при импорте разделять адрес хранилища на две части** — отделять последний разделитель пути и остаток класть в поле «Имя хранилища».

**Правка (файл `Services/StartManagerImporter.cs`, метод `BuildRepository`, строки ~547–557):**
- Сейчас разделение ищет только обратный слеш `\` (`LastIndexOf('\\')`). StorageDir имеет вид `tcp://server:1542/ИмяХранилища` — с обычным `/`.
- Расширить разделение: учитывать и `/`, и `\` как последний разделитель пути. Если последний сегмент — это имя хранилища (а не протокол/порт), класть его в `RepositoryName`, остальную часть — в `Server` (следуя логике `ConnectionSettingsViewModel.ParseRepositoryServer`, issue #140).
- Не выделять в имя хранилища компонент протокола (например, для `tcp://server:1542` без имени — не ломать `Server`).

**Файл:** `Services/StartManagerImporter.cs`.

---

## Версия, CHANGELOG, README

- Поднять `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` в `Configuration Management.csproj` до `0.3.6.96`.
- Добавить раздел `[0.3.6.96]` в `CHANGELOG.md` (по образцу предыдущих записей, описание каждого исправления со ссылками на issues).
- Обновить `README.md` при необходимости.
- Создать `_release/0.3.6.96.md` (по образцу существующих заметок релиза в `_release/`).

## Комментарии к issues

Для каждого из #214, #210, #204, #200, #188, #174, #163 добавить комментарий от `sivatorov`: что исправлено, в какой версии (`0.3.6.96`), ссылка на раздел CHANGELOG. Затем закрыть issues (если уместно).

## Сборка

- Windows (WPF, single-file, self-contained): `.\build-windows-single-file.ps1` (или `dotnet publish "Configuration Management/Configuration Management.csproj" -c Release`).
- Linux (Avalonia, single-file, self-contained): `.\build-linux-single-file.ps1` (`-p:ForceLinux=true`).
- Проверить успешность обеих сборок.

## Публикация и релиз

- `git add`, коммит (например, «Fix issues #214,#210,#204,#200,#188,#174,#163, bump version to 0.3.6.96»), push в `origin/main`.
- Создать GitHub-релиз `v0.3.6.96` с прикреплёнными single-file исполняемыми файлами (Windows и Linux) и описанием изменений (из CHANGELOG).