# План: мерж origin/main, версионирование 0.3.7.14, сборка и релиз

**Репозиторий**: `sivatorov/ConfigurationManagement` (ветка `main`)
**Проект**: C#/.NET 10 — WPF (Windows, `net10.0-windows`) + Avalonia 11 (Linux, `net10.0`)
**Локальная версия**: `0.3.7.13` (commit `981b791`)
**Целевая версия**: `0.3.7.14`
**Дата анализа**: 2026-09-11
**Требование ТЗ**: каждый пункт — отдельная задача (`new_task`); планирование/декомпозиция — режим Архитектора.

---

## 0. Исходное состояние (диагностика)

- Локальная ветка `main` отстаёт от `origin/main` на **9 коммитов** (fast-forward возможен):
  1. `3e9b391` Linux: индикатор хода скачивания обновления
  2. `3852eec` Индикатор обновления: без владельца, вне панели задач, приём файла по размеру
  3. `09ee2d5` Linux: сценарий-помощник обновления пишет журнал
  4. `4ca6a08` Журнал помощника: не умирать без журнала, честный код перезапуска, отметка таймаута
  5. `ff8973d` Linux: обновление не срабатывало из сборок, собранных на Windows (issue #225)
  6. `c5d6037` Merge PR #231
  7. `fa3b27b` Merge branch
  8. `647245a` Merge PR #230 linux-fixes-updater-crlf
  9. `3bd974b` Merge PR #229 linux-fixes-update-progress
- В рабочем дереве есть **staged-удаления** временных файлов (`_comment_*.md`, `_release/*.md`, `plans/*.md`) — блокируют fast-forward.

## 1. Правила (инварианты для всех задач)

1. Версия живёт **только** в `Configuration Management.csproj` в 4 полях:
   `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` (сейчас `0.3.7.13`).
   `AssemblyInfo.cs` / `VersionInfo.cs` не трогаем.
2. После мержа версия поднимается `0.3.7.13 → 0.3.7.14` **однократно**, и обязательно:
   - запись в верх `CHANGELOG.md` (`## [0.3.7.14] — 2026-09-11`);
   - бейдж версии в `README.md` (строка 3);
   - заметка `_release/0.3.7.14.md`.
3. Обе платформы обязаны собираться:
   - Windows: `dotnet build "Configuration Management/Configuration Management.csproj"`
   - Linux: `dotnet build "Configuration Management/Configuration Management.csproj" -p:ForceLinux=true`
4. `dist/`, временные артефакты — вне git (`.gitignore` / `.codeassistantignore`).

---

## 2. Декомпозиция на задачи (каждая — отдельная `new_task`)

| № | Задача | Режим | Вход | Выход |
|---|--------|-------|------|-------|
| 1 | Реконсиляция дерева + мерж | code | дерево с staged-удалениями, origin/main | чистое дерево на `origin/main` (`3bd974b`) |
| 2 | Документирование + версия | code | `origin/main` | правки csproj/CHANGELOG/README/`_release/0.3.7.14.md` |
| 3 | Сборка исполняемых файлов | code | код 0.3.7.14 | `dist/win-x64/ConfigurationManagement.exe`, `dist/linux-x64/ConfigurationManagement` |
| 4 | Публикация и релиз | code | версия + артефакты | push, тег `v0.3.7.14`, GitHub Release |

### Задача 1 — Реконсиляция дерева + мерж
1. `git status` — подтвердить staged-удаления временных файлов.
2. Урегулировать staged-удаления: commit «chore: удалить временные файлы анализа/планов» 
   (это housekeeping-правка, осмысленный коммит) — **до** мержа, чтобы разблокировать fast-forward.
3. `git pull --ff-only origin main` (или `git merge origin/main` если ff не пройдёт) — влить 9 коммитов.
4. Проверка: `git log --oneline -1` = `3bd974b`, `git status` чистое.
5. Базовые сборки обеих платформ (WPF + Linux ForceLinux) — контрольная точка перед правками.

### Задача 2 — Документирование влитых фиксов + поднятие версии
1. Поднять `0.3.7.13 → 0.3.7.14` во всех 4 полях `Configuration Management.csproj`.
2. Добавить секцию `## [0.3.7.14] — 2026-09-11` в начало `CHANGELOG.md`, описав влитые Linux-фиксы:
   - индикатор хода скачивания обновления (без владельца окна, вне панели задач, приём по размеру);
   - сценарий-помощник обновления пишет журнал; устойчивость журнала, честный код перезапуска, отметка таймаута;
   - исправление «обновление не срабатывало из сборок, собранных на Windows» (#225).
   Каждый пункт — со ссылкой на исходник и GitHub-issue где применимо.
3. Обновить бейдж версии в `README.md` (строка 3): `Версия-0.3.7.14`.
4. Создать `_release/0.3.7.14.md` с текстом для GitHub Release.
5. Сборки обеих платформ (Windows + Linux ForceLinux) — подтвердить «0 ошибок».

### Задача 3 — Сборка исполняемых файлов
1. Windows (на Windows-хосте):
   `cd "Configuration Management" && .\build-windows-single-file.ps1`
   → `dist/win-x64/ConfigurationManagement.exe` (один `.exe`).
2. Linux: кросс-сборка из Windows `FORCE_LINUX=1 ./build-linux-single-file.sh Release`
   (нужен bash: git-bash/WSL) → `dist/linux-x64/ConfigurationManagement`;
   альтернатива — положиться на CI [`release.yml`](.github/workflows/release.yml) (собирает на ubuntu-latest при пуше тега).
3. Проверить: ровно один `.exe` и один бинарник Linux, размеры > 0, нет `.dll` рядом.

### Задача 4 — Публикация и релиз
1. Commit (код + csproj + CHANGELOG + README + `_release/0.3.7.14.md`) с сообщением о версии `0.3.7.14`.
2. `git push origin main`.
3. `git tag v0.3.7.14` → `git push origin v0.3.7.14`.
4. GitHub Release: `gh release create v0.3.7.14 dist/win-x64/ConfigurationManagement.exe ... --title "0.3.7.14" --notes "$(cat _release/0.3.7.14.md)"`.
   CI [`release.yml`](.github/workflows/release.yml) дополнительно прикрепит Linux-артефакт автоматически
   (overwrite: true — допустимо).

---

## 3. Риски
- **Fast-forward блокируется** staged-удалениями → сначала отдельный cleanup-commit (Задача 1).
- **Кросс-сборка Linux из Windows** может требовать bash (git-bash/WSL) и зависимостей Avalonia →
  запасной вариант — CI `release.yml` на ubuntu-latest.
- **Расхождение версий** csproj/CHANGELOG/README/тега → вести централизованно в Задаче 2.
- **Регрессии** после мержа → контрольные сборки обеих платформ в каждой задаче.

## 4. Критерии готовности
- Локальная `main` = `origin/main` (`3bd974b`), дерево чистое.
- Версия `0.3.7.14` согласована в csproj/CHANGELOG/README/`_release/`.
- В `dist/` ровно один `.exe` и один бинарник Linux; обе платформы собираются.
- Изменения в `main`; создан GitHub Release `v0.3.7.14` с обоими артефактами.