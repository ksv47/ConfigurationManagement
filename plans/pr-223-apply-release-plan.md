# План: применение open PR #223 → версия → сборка → релиз

Репозиторий: `sivatorov/ConfigurationManagement` (ветка `main`, локальная версия **0.3.7.8**, HEAD `0ccf97a`).
Режим планирования: Архитектор. Каждый пункт — отдельная задача (mode `code`). Issues **не закрываем**.

## Обзор открытых pull request'ов

Через GitHub API (`/pulls?state=open`) проверены все open PR — на момент проверки открыт **один**:

| PR | Title / issue | Затронутые файлы | Ветка (fork ksv47) |
|----|---------------|------------------|--------------------|
| #223 | «Linux: автообновление закрывало приложение в пакетной установке» (issue #153) | `Configuration Management/Services/UpdateService.Avalonia.cs` | `linux-fixes-autoupdate-153` |

Ранее открытые PR #218/#219/#220 уже применены в коммите `107c6f7` и закрыты, поэтому в списке их нет.

### Проверка конфликтов
PR #223 меняет один файл — `Services/UpdateService.Avalonia.cs` (метод `DownloadAndInstallAutoAsync`). Локальные коммиты после базового (`107c6f7`) — `0ccf97a` («fix #216, #214, #175, #153») — этот файл **не трогали**: текущее содержимое метода совпадает с «до»-версией патча. Вывод: **конфликтов нет**, патч применяется чисто без разрешения merge-конфликтов.

## Что делает PR #223
В `DownloadAndInstallAutoAsync` (автоматический режим автообновления) цель установки определяется и проверяется `GetSelfUpdateBlocker(target)` **до** скачивания, как уже сделано в режиме с вопросом `DownloadAndInstallAsync`. Если самообновление недоступно (deb в `/usr/bin`, AppImage — каталог не на запись), показывается информационное сообщение со ссылкой на выпуск, и приложение **продолжает работу** вместо молчаливого запуска помощника и закрытия процесса. Порядок веток ошибок `DownloadFailed`/`InstallFailed` при этом меняется местами. Windows/WPF не затронут.

## Версионирование
Текущая **0.3.7.8**. После применения PR #223 версия поднимается во всех четырёх полях `<Version>/<AssemblyVersion>/<FileVersion>/<InformationalVersion>` в `Configuration Management.csproj`:
- после #223 → **0.3.7.9**

После применения: запись в `CHANGELOG.md` (блок `## [0.3.7.9] — 2026-09-10`, секция Исправлено), бейдж версии в `README.md` (строка 3), файл `_release/0.3.7.9.md`, комментарий в issue #153 «Исправлено в версии 0.3.7.9» (issue **не закрывать**).

## Декомпозиция задач (каждая в новой задаче, mode `code`)

1. **Применить PR #223** → версия 0.3.7.9 (+ CHANGELOG/README/_release/комментарий в issue #153).
2. **Собрать** single-file исполняемые файлы для Windows и Linux (версия 0.3.7.9):
   - Windows/WPF: `build-windows-single-file.ps1` → `dist/win-x64/ConfigurationManagement.exe`.
   - Linux/Avalonia: `build-linux-single-file.sh` с `FORCE_LINUX=1` → `dist/linux-x64/ConfigurationManagement`.
3. **Публикация и релиз**: `git add`/`commit`/`push origin main`, тег `v0.3.7.9`, `gh release create v0.3.7.9` с прикреплёнными exe (Windows) и бинарником (Linux), тело из `_release/0.3.7.9.md`. Issues (#153) **не закрывать**.