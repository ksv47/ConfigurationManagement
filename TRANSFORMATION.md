# Workspace 1С — трансформация проекта

Проект переименован и частично приведён к концепции **1C Workspace / Workspace 1С**,
описанной в дизайн- и архитектурных обсуждениях.

## Что сделано в этой итерации

### 1. Брендинг
- Product / AssemblyTitle → **Workspace 1С**
- AssemblyName → `Workspace1C` (имя exe и avares)
- Заголовок приложения (App.Title) в ru/en локализации
- README: позиционирование как «менеджер рабочих окружений 1С (Developer Workspace)»

### 2. Терминология (локализация)
- «Базы» в ключевых местах → «окружения»
- «Добавить базу» → «Добавить окружение»
- Поиск: «проектов, окружений, платформ»
- Группы: «Проекты / Группы»

### 3. Премиальная тёмная тема
Обновлены цвета DarkTheme (WPF + Avalonia) ближе к дизайн-системе:
- Background `#09090B`
- Surface `#18181B`
- Border `#27272A`
- Primary Amber `#F59E0B`
- Text `#FAFAFA` / `#A1A1AA`

### 4. Пути данных
Каталог настроек/логов/схем: `%AppData%/Workspace1C` (Windows) и `~/.config/Workspace1C` (Linux)

### 5. Идентификаторы single-instance / drag-drop
Обновлены под новое имя сборки.

## Что сохранено без ломающих изменений
- Существующая dual-UI структура (WPF + Avalonia)
- Модели Infobase / Group (полный рефакторинг в Project/Environment — следующий этап)
- Все сервисы запуска, кэша, ibases.v8i, платформ
- ViewModels и окна

## Рекомендуемые следующие этапы (по плану из чата)
1. Введение сущностей Project / Environment поверх текущих Group / Infobase
2. Выделение Domain / Application / Infrastructure (сейчас логика частично в UI)
3. Правая панель диагностики в стиле дизайн-макетов
4. Command Palette (Ctrl+K) как first-class
5. Профили запуска как отдельная сущность
6. Постепенный перенос бизнес-логики из code-behind/ViewModel в Application-сервисы

## Сборка
```bash
# Windows
dotnet build "Configuration Management/Configuration Management.csproj" -c Release

# Linux
dotnet build "Configuration Management/Configuration Management.csproj" -c Release
```
