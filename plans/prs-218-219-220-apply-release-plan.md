# План: применение open PR #220 #219 #218 → версии → сборка → релиз

Репозиторий: `sivatorov/ConfigurationManagement` (ветка `main`, текущая локальная версия **0.3.7.1**).
Режим планирования: Архитектор. Каждый пункт — отдельная задача (mode `code`). Issues самостоятельно **не закрываем**.

## Обзор открытых pull request'ов

Через GitHub API проверены все open PR (`is:pr is:open`) — их **три**, все от контрибьютора `ksv47`, все целятся в `main` с базового коммита `89428469`:

| PR | Title / issue | Затронутые файлы | Ветка |
|----|---------------|------------------|-------|
| #220 | «Очистка кэша: объём в отчёте по базам и опечатка в подсказке» (issue #178) | `Localization/Languages/ru.json`, `en.json`, `ViewModels/MainViewModel.Avalonia.cs`, `ViewModels/MainViewModel.Tools.cs` | WPF + Linux |
| #219 | «Фатальные сообщения: запасной текст вместо ключей локализации в обеих ветках» (issue #213) | `App.xaml.cs`, `App.axaml.cs` | WPF + Linux |
| #218 | «Linux/Avalonia: при скрытой колонке “Действия” список уезжает вправо на узком окне» (issue #191) | `Views/MainWindow.Avalonia.cs` | только Linux/Avalonia |

### Проверка конфликтов
Все три PR основаны на одном коммите `main`, а наборы изменяемых файлов **не пересекаются**:
- #220 правит только Localization + ViewModels;
- #219 правит только App.*;
- #218 правит только MainWindow.Avalonia.cs.

Вывод: **конфликтов между PR нет**, применяются последовательно без разрешения merge-конфликтов.
(Внимание: перед применением проверить чистоту рабочей копии и что HEAD совпадает с `89428469` — в репо могут оставаться незакоммиченные правки из предыдущего плана по issues 216/214/213/178/175.)

## Версионирование (после каждого изменения)

Текущая **0.3.7.1**. Каждый применённый PR поднимает версию в `Configuration Management.csproj` во всех 4 полях `<Version>/<AssemblyVersion>/<FileVersion>/<InformationalVersion>`:

- после #220 → **0.3.7.2**
- после #219 → **0.3.7.3**
- после #218 → **0.3.7.4**

После каждого PR:
- запись в `CHANGELOG.md` (блок `## [x.y.z] — дата` с секциями Исправлено/Добавлено);
- правка бейджа/упоминания версии в `README.md` (и описание настройки там, где уместно);
- файл `_release/<version>.md`;
- комментарий в связанный issue «Исправлено в версии X» (issue **не закрывать**).

## Содержание каждого PR (для применения как локальных правок)

### #220 → 0.3.7.2 (объём в отчёте очистки кэша)
- `ru.json`: `Main.CacheCleaned` += « (объём {3})»; `CacheClean.OrphanCacheTooltip`: «кеша» → «кэша».
- `en.json`: `Main.CacheCleaned` += « ({3})».
- `MainViewModel.Tools.cs` (WPF) и `MainViewModel.Avalonia.cs` (Linux): в `OpenCacheClean`/`QuickClearCache` добавить вычисление `basesSize = OneCCacheCleaner.GetSize(...)` до `Clear(...)` и передать `Infobase.FormatSize(basesSize)` 4-м аргументом в `Main.CacheCleaned`.

### #219 → 0.3.7.3 (запасной текст в фатальных сообщениях)
- `App.xaml.cs` (WPF): перевести заголовки `App.Fatal.Interface/Critical/BackgroundTask/Title/InternalError` на `TOr(...)` с русским запасным текстом (как уже сделано на пути запуска).
- `App.axaml.cs` (Avalonia): те же три обработчика на `TOr(...)`.

### #218 → 0.3.7.4 (выравнивание колонок при скрытой «Действия»)
- `MainWindow.Avalonia.cs`: в `AlignHeaderToRows` исключить звёздную колонку имени из обеих сумм ведущих колонок (`i < NameRowColumn`, `i < NameHeaderColumn`), чтобы компенсатор не зависел от собственного прошлого значения и список не разрастался до десятков тысяч точек.

## Сборка исполняемых файлов (отдельная задача, после всех PR)

Собрать **один** исполняемый файл для Windows и **один** для Linux финальной версии **0.3.7.4**:
- Windows/WPF: `Configuration Management/build-windows-single-file.ps1` → `dist/win-x64/ConfigurationManagement.exe` (запуск на Windows).
- Linux/Avalonia: `Configuration Management/build-linux-single-file.sh` с `FORCE_LINUX=1` (кросс-сборка с Windows) → `dist/linux-x64/ConfigurationManagement`.
- Проверить, что обе публикации проходят без ошибок (dotnet build/publish Release).

## Публикация и релиз (отдельная задача)

1. `git add` изменённых файлов (исключая `plans/`, `dist/`, артефакты), `git commit` с сообщением о применённых PR и версии **0.3.7.4**.
2. `git push origin main`.
3. `git tag v0.3.7.4` и `git push origin v0.3.7.4`.
4. `gh release create v0.3.7.4` с прикреплёнными `ConfigurationManagement.exe` (Windows) и `ConfigurationManagement` (Linux), тело из `CHANGELOG.md` / `_release/0.3.7.4.md`.
5. Issues (#178, #213, #191) **не закрывать** — пользователь закрывает сам после проверки.

## Декомпозиция задач (каждая в новой задаче, mode `code`)

1. Применить PR #220 → версия 0.3.7.2 (+ CHANGELOG/README/_release/комментарий в issue #178).
2. Применить PR #219 → версия 0.3.7.3 (+ CHANGELOG/README/_release/комментарий в issue #213).
3. Применить PR #218 → версия 0.3.7.4 (+ CHANGELOG/README/_release/комментарий в issue #191).
4. Собрать single-file исполняемые файлы для Windows и Linux (версия 0.3.7.4).
5. Push + тег + GitHub Release v0.3.7.4 (issues не закрывать).