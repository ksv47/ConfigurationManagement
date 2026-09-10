# План: исправление issues #204–#210 и релиз 0.3.6.93

## Контекст

- Репозиторий: `sivatorov/ConfigurationManagement` (ветка `main`, чистое рабочее дерево).
- Текущая версия: `0.3.6.92` (все 4 поля в `Configuration Management.csproj`).
- Проект двухплатформенный: WPF (`net10.0-windows`, символ `WINDOWS`) и Avalonia (`net10.0`, символ `LINUX`). Linux-сборка на Windows: `-p:ForceLinux=true`.
- Открытые issues обработаны: из 27 только **7** требуют правок — все **без комментариев** (`#204…#210`). Остальные имеют последний комментарий автора (`sivatorov`) и пропускаются.

Все 7 issues составлены одним автором-рецензентом с точными указаниями файлов/строк и предложенными правками. Правки выполняются **на основе описания**.

## Порядок работы (каждый пункт — отдельная задача)

1. Внесение исправлений по каждому issue (см. ниже).
2. Поднятие версии до `0.3.6.93` + записи в `CHANGELOG.md` и `README`.
3. Добавление комментариев к исправленным issues на GitHub (что исправлено, в какой версии).
4. Сборка single-file исполняемых файлов для Windows и Linux.
5. Push изменений и создание GitHub-релиза `v0.3.6.93`.

---

## Issue #204 — Горячие клавиши (3 расхождения)

Файлы: `Controls/HotkeyBox.cs`, `Controls/HotkeyBox.Avalonia.cs`, код проверки дублей.

1. **Принятие клавиши без модификатора** (WPF): в `HotkeyBox.OnPreviewKeyDown` (строка 95 `Value = FormatCombo(...)`) и в Avalonia `HotkeyBox.Avalonia.cs` (строка 84) — отбраковывать комбинацию без модификатора для буквенных/цифровых клавиш. Без модификатора допустимы только `F1…F24`, `Delete`, `Insert`. При попытке — не фиксировать значение, показать подсказку/оставить прежнее.
   - Реализация: метод вида `RequiresModifier(Key)` (`true` для `Key.A…Z`, `Key.D0…D9`, `Key.NumPad0…9` и т.п.); если `mods == None && RequiresModifier(key)` — не записывать. Аналогично для Avalonia.
2. **Имя действия в предупреждении о конфликте**: для действия «Сбросить теги» вместо `Main.ClearTags` выводить локализованное название. Найти формирование текста предупреждения о конфликте (настройки горячих клавиш) и использовать название действия через `LocalizationManager`, а не имя поля/перечисления.
3. **Linux: Ctrl+D (панель информации, #172) не участвует в проверке дублей**: добавить этот хоткей в перечень проверяемых на дубли в Avalonia-сборке.

## Issue #205 — Значение с `"` молча выпадает из аргументов запуска 1С

Файлы: `Services/OneCLauncher.Arguments.cs`, окно/модель свойств базы (`Views/ConnectionSettingsWindow*`, `ViewModels/ConnectionSettingsViewModel.cs`).

- Правило `IsSafeCliValue` оставить (защита от инъекции ключей 1С).
- **Выбранный вариант 2 (закрывает раньше)**: не принимать значение, содержащее `"` или управляющий символ, в окне свойств базы при сохранении — показать сообщение с объяснением причины («значение содержит символ «"», оно не может быть передано в командную строку 1С»). Поля: путь файловой базы, адрес сервера, имя базы, веб-адрес, логин, пароль (то, что идёт в аргументы `/F /S /WS /N /P`).
- Дополнительно: в `OneCLauncher` предусмотреть метод, сообщающий, какое поле небезопасно, чтобы предупреждение указывало конкретное поле (защита от молчаливого обрыва аргумента в других местах запуска).

## Issue #206 — Windows: «Отмена» в настройках не отменяет смену языка

Файл: `Views/SettingsWindow.Language.cs` (обработчик `OnLanguage_Changed`, строка 41–52).

- Не применять язык сразу в `SelectionChanged`. Запомнить выбранный язык (поле окна).
- Применять язык (`_viewModel.ApplyLanguage(...)` + `RefreshSchemeComboBox()`) **в обработчике «Сохранить»**.
- При «Отмена» ничего не применять — язык остаётся прежним (соответствует поведению Avalonia-сборки).
- Найти обработчики Ok/Cancel в `Views/SettingsWindow.xaml.cs`.

## Issue #207 — Windows: расписание синхронизации и шаблон даты

1. `ViewModels/MainViewModel.Sync.cs` (около строки 155): заменить `TimeSpan.TryParse` на строгий разбор времени суток — `TimeSpan.TryParseExact` с шаблонами `hh\:mm` и `h\:mm` + проверка «меньше суток» (как в `ViewModels/MainViewModel.Avalonia.cs:4416`).
2. `ViewModels/MainViewModel.Tools.cs` (около строки 1385): обернуть `DateTime.Now.ToString(format)` в `try/catch (FormatException)` с откатом к `yyyyMMdd_HHmmss` (как в `MainViewModel.Avalonia.cs:2949-2958`).
3. Дополнительно: валидация поля времени синхронизации и шаблона даты при вводе/сохранении, показ примера корректного значения.

## Issue #208 — Windows: тема перезаписывается без подтверждения и теряется при сбое переименования

1. `Views/SettingsWindow.Schemes.cs`:
   - Создание (строки ~339–350): перед сохранением проверить `ThemeManager.FindCustomScheme` — при совпадении имени спросить подтверждение замены.
   - Импорт (строки ~454–462): перед `AdoptImportedScheme` проверить `FindCustomScheme`, при совпадении подтвердить замену (как в Avalonia `SettingsWindow.Avalonia.cs:1509-1512`).
2. `ViewModels/SettingsViewModel.cs` (`RenameCustomScheme`, строки ~218–220): сначала `SaveCustomColorScheme(toSave)` под новым именем, и только после успешной записи `DeleteCustomColorScheme(oldName)` (сейчас порядок обратный).

## Issue #209 — Удаление учётной записи: каталог данных удаляется до записи реестра

Файл: `Services/ProfileService.cs` (строки ~160–172).

1. Поменять порядок: сначала успешно сохранить реестр (`SaveRegistry`), затем удалять каталог данных профиля.
2. Не подавлять ошибку записи `profiles.json` целиком — показывать сообщение пользователю и/или писать в журнал (`IAppLogger`), чтобы расхождение не оставалось незамеченным. При неудачной записи — не удалять каталог и вернуть профиль в список.

## Issue #210 — Три окна без точек входа + ключи локализации

Удалить мёртвый код:
- `GroupSettingsWindow`: `Views/GroupSettingsWindow.xaml`, `Views/GroupSettingsWindow.xaml.cs`, `Views/GroupSettingsWindow.Avalonia.cs`.
- `TagInputWindow`: `Views/TagInputWindow.xaml`, `Views/TagInputWindow.axaml`, `Views/TagInputWindow.xaml.cs`.
- `ProfilesWindow` (только WPF-пара): `Views/ProfilesWindow.xaml`, `Views/ProfilesWindow.xaml.cs` (в Avalonia одноимённое окно живое — `.Avalonia.cs` оставить).

Перед удалением убедиться, что нет ссылок из другого кода (поиск по дереву). Проверить `csproj`, что файлы не перечислены явно (используется SDK-глобbing — удаление физических файлов достаточно).

Недостающие ключи локализации (`GroupSettings.*`, `TagInput.*`) — не добавлять (окна удаляются, ключи останутся неиспользуемыми). Решение задокументировать в комментарии к issue.

---

## Версия, CHANGELOG, README

- Поднять `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion` в `Configuration Management.csproj` до `0.3.6.93`.
- Добавить раздел `[0.3.6.93]` в `CHANGELOG.md` (по образцу предыдущих записей, с описанием каждого исправления и ссылками на issues).
- Обновить `README.md` при необходимости (упоминание новых возможностей/исправлений).
- Создать `_release/0.3.6.93.md` (по образцу существующих заметок релиза в `_release/`).

## Комментарии к issues

Для каждого из #204…#210 добавить комментарий от `sivatorov`: что исправлено, в какой версии (`0.3.6.93`), ссылка на раздел CHANGELOG. Затем закрыть issues (при необходимости).

## Сборка

- Windows (WPF, single-file, self-contained): `dotnet publish "Configuration Management/Configuration Management.csproj" -c Release`.
- Linux (Avalonia, single-file, self-contained): `dotnet publish ... -c Release -p:ForceLinux=true` (или скрипты `build-windows-single-file.ps1` / `build-linux-single-file.sh` / `build-linux-single-file.ps1`).
- Проверить успешность обеих сборок.

## Публикация и релиз

- `git add`, коммит (например, «Fix issues #204-#210, bump version to 0.3.6.93»), push в `origin/main`.
- Создать GitHub-релиз `v0.3.6.93` с прикреплёнными single-file исполняемыми файлами (Windows и Linux) и описанием изменений (из CHANGELOG).