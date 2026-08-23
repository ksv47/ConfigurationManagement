# План: аудит и устранение оставшихся недочётов локализации

## Контекст

Проект «Управление конфигурациями 1С» поддерживает локализацию на двух платформах:

- **WPF / Windows** — привязки `{loc:Loc Key}` и `LocExtension` ([`LocExtension.cs`](../Configuration Management/Localization/LocExtension.cs)).
- **Avalonia / Linux** — код через `LocalizationManager.T("Key")`, пересборка окон по событию `LanguageChanged`.

Языковые файлы: [`ru.json`](../Configuration Management/Localization/Languages/ru.json) и
[`en.json`](../Configuration Management/Localization/Languages/en.json).
Менеджер: [`LocalizationManager`](../Configuration Management/Localization/LocalizationManager.cs)
(откат: текущий язык → en → ru → сам ключ).

### Что уже реализовано (проверено по коду, версия 0.3.3.38)

| Пункт | Статус | Где |
|---|---|---|
| Динамическая смена языка главного окна (Linux) | ✅ | подписка `LanguageChanged` + `OnLanguageChanged` → `RebuildAfterLanguageChange` ([`MainWindow.Avalonia.cs`](../Configuration Management/MainWindow.Avalonia.cs)) |
| Названия колонок | ✅ | ключи `Column.*` ([`ListColumns()`](../Configuration Management/MainWindow.Avalonia.cs)) |
| Кнопки правой панели | ✅ | ключи `Main.*` (`BuildRightPanel`) |
| Тултипы кнопок | ✅ | ключи `Main.*Tooltip` (`ToolTip.SetTip`) |
| Смена языка в настройках сразу | ✅ | `SettingsWindow.OnLanguageChanged` пересобирает окно; WPF — `OnLanguage_Changed` |
| Сохранение языка при закрытии | ✅ | `ApplyLanguage` → `settings.Language` + `SaveSettingsSilently`; `ExitApplication` фиксирует текущий язык |

Аудит на захардкоженные русские UI-строки (0.3.3.37) не выявил пользовательских строк вне локализации;
остались только строки журналов (`_logger.*`) и внутренние идентификаторы режимов клиента/запуска.

### Оставшиеся потенциальные недочёты (требуют автоматической проверки)

1. **Расхождение наборов ключей `ru.json` ↔ `en.json`** — ключ есть в `ru`, нет в `en` →
   англ. пользователь увидит русский fallback.
2. **Ключ, используемый в коде/XAML, но отсутствующий в `ru.json`** — вместо текста показывается «сырой» ключ.
3. **Захардкоженные русские UI-строки** в динамически строящихся местах (контекстные меню дерева/трей, статус-бар) —
   страховочная проверка.

---

## Шаг 0 — Автоматический аудит (обнаружение дефектов)

Написать вспомогательный скрипт и запустить его. Результат — отчёт `tools/l10n-audit-report.txt`.

Скрипт должен:

1. Извлечь множества ключей из `ru.json` и `en.json` (секция `strings`).
2. Вывести разность:
   - `keys_in_ru_not_in_en` — ключи без английского перевода;
   - `keys_in_en_not_in_ru` — «мёртвые» ключи (для информации).
3. Просканировать `*.cs` / `*.xaml` / `*.axaml` (кроме файлов `Languages/*.json` и комментариев) по паттернам:
   - `LocalizationManager.T("KEY")` (и `string.Format(LocalizationManager.T("KEY"), ...)`);
   - `{loc:Loc KEY}`;
   - `Loc["KEY"]` / `{Binding Loc[KEY]}`;
   - `LocalizationManager.T($"...")` не поддерживается (интерполяция) — ключи в таких случаях обрабатывать отдельно.
4. Вывести ключи из кода, которых нет в `ru.json`.
5. Записать итог в `tools/l10n-audit-report.txt`.

Форматы скриптов: `tools/l10n-audit.ps1` (Windows) и/или `tools/l10n-audit.sh` (Linux).
Парсинг JSON допустимо делать с `ConvertFrom-Json` (PowerShell) или через простой grep/awk (bash).

**Ожидание:** версия 0.3.3.38 и предыдущие аудиты предполагают, что расхождений и пропущенных ключей
нет. Цель шага — подтвердить это автоматически, а при обнаружении передать найденные дефекты в задачи A/B.

---

## Задача A — Расхождение ключей ru↔en

**Если** найдены ключи `keys_in_ru_not_in_en`:

- Добавить недостающие английские переводы в [`en.json`](../Configuration Management/Localization/Languages/en.json).
- (Опционально) удалить «мёртвые» ключи из `en.json`/`ru.json`, если они точно нигде не используются.
- **Бамп версии:** `0.3.3.39` — `InformationalVersion` в
  [`Configuration Management.csproj`](../Configuration Management/Configuration%20Management.csproj)
  (и `Version`/`AssemblyVersion`/`FileVersion`, если этого требует конвенция релизов); обновить
  `Settings.About.HelpText` («Версия: …») в `ru.json` и `en.json`; обновить бейдж/заголовок в
  [`README.md`](../README.md); добавить запись в [`CHANGELOG.md`](../CHANGELOG.md).

## Задача B — Отсутствующие ключи из кода/XAML

**Если** код ссылается на ключ, которого нет в `ru.json`:

- Добавить ключ в `ru.json` и `en.json` (русский текст по смыслу места использования + английский перевод).
- **Бамп версии** до следующей (например `0.3.3.40`), обновить CHANGELOG/README как в задаче A.

## Задача C — Страховочная проверка динамических UI-мест

- Проверить контекстные меню дерева/трей и статус-бар в
  [`MainWindow.Avalonia.cs`](../Configuration Management/MainWindow.Avalonia.cs) на захардкоженные
  русские строки (вне `LocalizationManager.T(...)`).
- При нахождении — вынести в ключи, **бамп версии**, CHANGELOG/README.
- Если дефектов нет — закрыть без бампа.

---

## Правила выполнения

- После **каждого** исправления: бамп версии + запись в `CHANGELOG.md` + обновление `README.md`.
- Если по итогам аудита дефектов нет — в `CHANGELOG.md` добавляется запись о повторной валидации
  без изменения номера версии (или с согласованным бампом).
- Не трогать строки журналов (`_logger.*`) и внутренние идентификаторы режимов клиента/запуска —
  это технические значения, не отображаемые пользователю.