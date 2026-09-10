# План: обработка открытых issues, исправление, сборка и релиз

**Репозиторий**: `sivatorov/ConfigurationManagement` (ветка `main`)
**Проект**: C#/.NET 10, WPF (Windows, `net10.0-windows`) + Avalonia 11 (Linux, `net10.0`)
**Текущая версия**: `0.3.6.79` (задана в 4 полях `Configuration Management.csproj`)
**Требование**: каждый из 8 шагов выполняется в **отдельной новой задаче** (через `new_task`).

---

## 0. Принципы выполнения (инварианты для всех задач)

1. Перед началом — `git status`, зафиксировать чистое состояние рабочего дерева.
2. Версия **живёт только в `Configuration Management.csproj`** в четырёх полях
   `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` (сейчас `0.3.6.79`).
   - `AssemblyInfo.cs` — только `ThemeInfo`, **не трогаем**.
   - `VersionInfo.cs` — только читает версию из атрибутов сборки, **не трогаем**.
3. После **каждой правки кода** версия поднимается на единицу в последней части
   (`0.3.6.79 → 0.3.6.80 → …`), добавляется запись в `CHANGELOG.md` (верх блока, формат
   Keep a Changelog), обновляется бейдж версии в `README.md` (строка 3) и создаётся
   заметка `_release/<version>.md`.
4. Итоговый релиз использует **финальную** версию после всех правок (последнее значение).
5. Обе платформы обязаны собираться после каждой правки:
   - Windows: `dotnet build "Configuration Management/Configuration Management.csproj"`
   - Linux: `dotnet build "Configuration Management/Configuration Management.csproj" -p:ForceLinux=true`
6. `dist/`, `publish/`, временные артефакты анализа — вне git (`.gitignore` / `.codeassistantignore`).

---

## 1. Подключение к GitHub и проверка открытых issues

**Цель**: получить полный список открытых issues с описанием и комментариями.

**Инструменты**:
- `gh auth status` — проверить авторизацию (токен/PAT, учётная запись `sivatorov`).
- `gh issue list --repo sivatorov/ConfigurationManagement --state open --json number,title,state,createdAt,updatedAt,author,body,comments,labels`
- Альтернатива (REST): `GET /repos/sivatorov/ConfigurationManagement/issues?state=open&per_page=100`
  + `GET /repos/sivatorov/ConfigurationManagement/issues/{n}/comments`

**Выход**: сохранённый JSON/Snapshot списка открытых issues (во временный файл вне git, например `_analysis/open_issues_full.json` — см. `.codeassistantignore`).

**Зависимость**: следующий шаг ждёт этот список.

---

## 2. Анализ issues по критериям отбора

**Критерий отбора** (из ТЗ):
- issues **без комментариев** → изменения делать по тексту описания;
- issues, где **последний комментарий не от меня** (не автор `sivatorov`) → ориентироваться
  на **последний комментарий не от меня**;
- issues, где последний комментарий мой → пропускаются (уже ожидают моей реакции/не требуют правок).

**Действия**:
- Определить мой логин (`gh api user --jq .login` или `gh api user`).
- Для каждого issue вычислить: `count(comments) == 0` OR `last_comment.author != me`.
- Отфильтровать отобранные, сформировать таблицу: № issue, заголовок, база для правки
  (описание / последний чужой комментарий).
- Для отобранных — указать предполагаемую область кода (по тексту), платформу
  (Windows/WPF, Linux/Avalonia, обе).

**Выход**: файл `_analysis/selected_issues.md` со списком кандидатов на исправление.

---

## 3. Внесение исправлений в программу

**Действия** (для каждого отобранного issue, по одному issue на мини-цикл):
- Прочитать затрагиваемые файлы (`search_files`, `read_file`).
- Внести правку в общий код и/или обе платформенные реализации (WPF `.xaml`/`.xaml.cs`
  и Avalonia `.axaml`/`.Avalonia.cs`), следуя существующим паттернам кода.
- Убедиться, что состав сборки для каждой ОС управляется csproj корректно
  (`Compile Remove/Include`, условные секции `BuildLinux`).
- Прогнать сборку обеих платформ (см. шаг 0, п.5).
- Зафиксировать связь правка ↔ № issue в комментарии/коммите.

**Инструменты**: `dotnet build`, `search_files`, `read_file`, `apply_diff`/`write_to_file`.

**Зависимость**: вход от шага 2. Выход → шаги 4 и 5.

---

## 4. Добавление комментариев к исправленным issues

**Действия**:
- Для каждого исправленного issue — `gh issue comment <номер> --body "<что исправлено, в какой версии>"`.
- Формат комментария: краткое описание правки + номер версии, в которой вошло исправление
  (напр. «Исправлено в 0.3.6.80: …»).
- После комментария закрыть issue (`gh issue close <номер>`) при условии, что правка решает проблему.

**Инструменты**: `gh issue comment`, `gh issue close`.

---

## 5. Обновление версии, CHANGELOG и README

**Действия** (цикл по каждой микро-правке из шага 3):
- Поднять версию в 4 полях csproj (`0.3.6.79 → 0.3.6.80 → …`).
- Добавить запись в верх `CHANGELOG.md` (`## [0.3.6.80] — <дата>`), ссылки на файлы/конфигурацию.
- Обновить бейдж версии в `README.md` (строка 3).
- Создать/обновить `_release/<version>.md`.

> Примечание: пользовательский пункт 5 идёт отдельной задачей, но по логике версия
> поднимается сразу после каждой правки (пункт 5 ТЗ «после каждого изменения»).
> Рекомендуется выполнять пункты 3 и 5 в связке, финально согласуя документы здесь.

**Инструменты**: `apply_diff` для csproj, CHANGELOG, README; `write_to_file` для `_release/`.

---

## 6. Сборка исполняемых файлов для Windows и Linux

**Windows** (на Windows-хосте):
- `cd "Configuration Management" && .\build-windows-single-file.ps1` → `dist/win-x64/ConfigurationManagement.exe`
- Проверить, что в папке только один `.exe`.

**Linux**:
- Вариант A (реальный Linux / WSL / CI): `cd "Configuration Management" && ./build-linux-single-file.sh` → `dist/linux-x64/ConfigurationManagement`.
- Вариант B (кросс с Windows): `FORCE_LINUX=1 ./build-linux-single-file.sh`.
- Вариант C (надёжный): rely on CI `release.yml` (собирает Linux на `ubuntu-latest` по тегу).
- Проверить, что в папке только один бинарник.

**Инструменты**: PowerShell `build-windows-single-file.ps1`, bash `build-linux-single-file.sh`, либо CI.

**Риск**: кросс-сборка Avalonia с Windows может отличаться от нативной; при проблемах — WSL/CI.

---

## 7. Выкладывание изменений на GitHub и создание релиза

**Действия**:
- Проверить, что `dist/`, `publish/`, `_analysis/` не попадут в коммит (`.gitignore`).
- `git add` (код, csproj, CHANGELOG, README, `_release/`, при необходимости планы),
  осмысленные коммиты с привязкой к № issues.
- `git push origin main`.
- Создать тег: `git tag v<финальная_версия>` (напр. `v0.3.6.8X`) → `git push origin v<финальная_версия>`.
- Создать релиз: `gh release create v<финальная_версия> dist/win-x64/ConfigurationManagement.exe dist/linux-x64/ConfigurationManagement --title "..." --notes "$(cat _release/<version>.md)"`.
- Описание релиза — из `CHANGELOG.md`/`_release/<version>.md`.
- Убедиться, что CI `release.yml` не конфликтует с ручной загрузкой Linux-артефакта
  (overwrite: true в воркфлоу перезапишет asset — допустимо).

**Инструменты**: `git`, `gh release create`, `gh auth`.

---

## Декомпозиция на задачи (каждая — отдельная `new_task`)

| № | Задача | Режим | Вход | Выход |
|---|--------|-------|------|-------|
| 1 | Подключиться к GitHub, собрать открытые issues | code | репозиторий, `gh auth` | `_analysis/open_issues_full.json` |
| 2 | Проанализировать issues (критерий отбора) | code/ask | список issues | `_analysis/selected_issues.md` |
| 3 | Внести исправления в программу | code | выбранные issues | собранные обе платформы, правки кода |
| 4 | Комментарии к исправленным issues | code | правки+версии | комментарии, закрытые issues |
| 5 | Версия, CHANGELOG, README, `_release/` | code | правки | обновлённые документы |
| 6 | Сборка Windows и Linux | code | итоговый код | `dist/win-x64/*.exe`, `dist/linux-x64/ConfigurationManagement` |
| 7 | git push + релиз | code | итоговая версия, артефакты | тег, GitHub Release |

> Задачи 1–7 выполняются строго последовательно (цепочка зависимостей).
> Внутри шага 3 цикл «правка → версия → CHANGELOG → README» повторяется на каждый issue.

---

## Риски и зависимости

### Риски
- **Нет авторизации `gh`** → шаг 1 не выполнится; нужен `gh auth login` или `GITHUB_TOKEN`.
- **Неоднозначность правки** для issues без комментариев (правка по описанию) — риск неверной
  интерпретации; требуется проверка сборки и визуальная/логическая сверка.
- **Кросс-сборка Linux с Windows** может дать иное поведение Avalonia → fallback на WSL/CI (`release.yml`).
- **Конфликт ручного релиза и CI**: `release.yml` срабатывает на тег и перезапишет Linux-asset —
  согласовать порядок (создать тег → дать CI прикрепить Linux, либо загрузить оба asset вручную).
- **Попадание мусора в коммит** (`dist/`, артефакты анализа) — контролировать через `.gitignore`.
- **Регрессии на одной платформе** при правке общей логики → обязательная сборка обеих платформ.
- **Расхождение версий** между csproj / CHANGELOG / README / тегом / комментариями к issue →
  вести версию централизованно и сверять перед релизом.

### Зависимости
- Шаг 2 зависит от шага 1 (список issues).
- Шаг 3 зависит от шага 2 (отобранные issues).
- Шаги 4 и 5 зависят от шага 3 (фактические правки + номера версий).
- Шаг 6 зависит от шага 5 (финальная версия и код).
- Шаг 7 зависит от шага 6 (артефакты) и шага 5 (версия/описание).

---

## Критерии готовности
- Получен и проанализирован список открытых issues; отобраны кандидаты по критерию ТЗ.
- Каждый исправленный issue получил комментарий с номером версии; issue при необходимости закрыт.
- Версия согласована в csproj / CHANGELOG / README / теге / заметке `_release/`.
- Обе платформы собираются; в `dist/` ровно один `.exe` (Win) и один бинарник (Linux).
- Изменения запушены в `main`; создан GitHub Release с тегом `v<версия>` и обоими артефактами.