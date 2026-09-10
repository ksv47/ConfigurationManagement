# План: исправление issue #175 + релиз

Дата: 2026-09-09
Ветка/репозиторий: https://github.com/sivatorov/ConfigurationManagement
Текущая версия в репозитории: **0.3.6.98**

## 1. Анализ открытых issues

Проверены все открытые issues через GitHub API. Правило отбора: правки требуют только те
issues, где **нет комментариев** ИЛИ **последний комментарий не от автора (sivatorov)**.

| Issue | Последний комментарий | Решение |
|-------|----------------------|---------|
| #202, #191, #189, #188, #178, #213, #212, #210, #204, #171, #166, #165, #164, #162, #161, #153 | от sivatorov | пропустить |
| #174 | 7OH: «Спасибо — теперь работает» | подтверждение, правок нет |
| **#175** | **7OH: запрос фичи** | **исправить в этом цикле** |

Единственный issue, требующий новой работы — **#175**.

### Что просит пользователь (последние комментарии 7OH, 2026-09-09)
1. Ниже поля шаблона имени COM-коннектора добавить **два поля**:
   - слева — редактируемое поле **версии** (по умолчанию `8.3.45.6789`, можно укоротить);
   - справа — **интерактивный предпросмотр** итоговой строки после применения шаблона,
     реагирующий на изменение и шаблона, и версии.
2. **Аккуратная обрезка неиспользуемых сегментов версии:** для шаблона
   `V%V12%_%V3%_%V4%.ComConnector` и версии `8.3.27` должно получаться
   `V83_27.ComConnector`, а не `V83_27_.ComConnector` (при отсутствии части удалять
   и предшествующий разделитель).

---

## 2. Текущая реализация (для справки)

- Логика разворота шаблона: `OneCComConnector.ExpandTemplate`
  (`Configuration Management/Services/OneCComConnector.cs:112-136`), сейчас делает просто
  `template.Replace("%V12%", v12).Replace("%V3%", v3).Replace("%V4%", v4)` — без обрезки
  разделителей. **Внимание:** `OneCComConnector.cs` входит только в Windows-сборку;
  в Linux-сборке используется `OneCComConnector.Linux.cs`.
- UI поля шаблона:
  - Avalonia: `Configuration Management/Views/SettingsWindow.Avalonia.cs:320-358`
    (поля `comTemplateHint`, `comTemplateRow`, `comTemplateBox`).
  - WPF: разметка в `Configuration Management/Views/SettingsWindow.xaml` (элемент
    `ComConnectorNameTemplateBox`), код в `SettingsWindow.xaml.cs:64-66` (чтение) и
    `SettingsWindow.xaml.cs:275-276` (запись).
- Ключи локализации префикса: `Settings.General.ComConnectorTemplate*`
  (ru.json / en.json).
- Версия приложения: `Configuration Management/VersionInfo.cs`,
  `Configuration Management/Configuration Management.csproj`.

---

## 3. Реализация фичи #175

### 3.1. Новый общий помощник `Services/ComConnectorTemplate.cs`

Оба билда (WPF и Avalonia) должны использовать единую логику предпросмотра. Так как
`OneCComConnector.cs` не компилируется в Linux-сборку, выносим разворот в **новый общий
файл**, входящий в обе сборки (дефолтный glob csproj):

```csharp
namespace Configuration_Management.Services;

public static class ComConnectorTemplate
{
    /// <summary>Разворачивает шаблон по версии. Пустая строка версии/шаблона или
    /// невозможность разобрать версию -> null.</summary>
    public static string? Expand(string? template, string? platformVersion)
    {
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(platformVersion))
            return null;

        var seg = platformVersion.Split('.');
        if (seg.Length == 0) return null;

        var v12 = Digits(seg.Length > 1 ? seg[0] + seg[1] : seg[0]);
        var v3  = Digits(seg.Length > 2 ? seg[2] : "");
        var v4  = Digits(seg.Length > 3 ? seg[3] : "");
        if (v12.Length == 0) return null;

        return Apply(template, v12, v3, v4);
    }

    /// <summary>Применяет значения плейсхолдеров с обрезкой разделителей перед
    /// пустыми сегментами (issue #175). Пример: "V%V12%_%V3%_%V4%.ComConnector"
    /// + 8.3.27 -> "V83_27.ComConnector".</summary>
    private static string Apply(string template, string v12, string v3, string v4)
    {
        // Токенизация: литералы между плейсхолдерами + значения плейсхолдеров.
        // lit[i] — литерал ПЕРЕД плейсхолдером ph[i]; tail — литерал ПОСЛЕ последнего.
        var ph = new List<string>();
        var lit = new List<string>();
        ParseTokens(template, lit, ph, out var tail);

        var sb = new StringBuilder();
        for (int i = 0; i < ph.Count; i++)
        {
            var value = ph[i] switch { "%V12%" => v12, "%V3%" => v3, _ => v4 };
            if (value.Length > 0)
            {
                sb.Append(lit[i]);  // разделитель перед текущим плейсхолдером
                sb.Append(value);
            }
            // пустой сегмент: его значение и разделитель lit[i] опускаются
        }
        sb.Append(tail); // суффикс ProgID (.ComConnector) сохраняется всегда
        return sb.ToString();
    }
}
```

- `ParseTokens` разбивает шаблон по плейсхолдерам `%V12%`/`%V3%`/`%V4%` в порядке
  появления: `lit[0]` — текст до первого плейсхолдера, `lit[i]` — текст между
  `ph[i-1]` и `ph[i]`, `tail` — текст после последнего плейсхолдера.
- Если шаблон не содержит ни одного плейсхолдера — вернуть шаблон как есть
  (эквивалент прежнего поведения).
- `Digits(string)` — прежний хелпер, берёт только цифры из сегмента.

### 3.2. Рефакторинг `OneCComConnector.ExpandTemplate`

В `Configuration Management/Services/OneCComConnector.cs:112-136` метод `ExpandTemplate`
заменить на делегирование общему помощнику, чтобы поведение при подключении совпадало
с предпросмотром:

```csharp
internal static string? ExpandTemplate(string? template, string? platformVersion)
    => ComConnectorTemplate.Expand(template, platformVersion);
```

Хелпер `Digits` перенести/продублировать в `ComConnectorTemplate`.

### 3.3. UI: окно настроек Avalonia (`SettingsWindow.Avalonia.cs`)

После блока `comTemplateRow` (строка ~358) добавить блок «предпросмотра»:

- Новая строка (`StackPanel` Horizontal):
  - подпись (ключ `Settings.General.ComConnectorPreviewVersionLabel`);
  - `TextBox` версии `previewVersionBox`, `Text = "8.3.45.6789"`, ширина ~120;
  - подпись/значок «→»;
  - read-only `TextBox` или `TextBlock` результата `previewResultBox`
    (ключ `Settings.General.ComConnectorPreviewResultLabel` не требуется — это просто
    вывод, можно подписью-префиксом).
- Подписка на события изменения: `comTemplateBox.TextChanged` и
  `previewVersionBox.TextChanged` → метод `UpdateComConnectorPreview()`.
- `UpdateComConnectorPreview()`: вызывает `ComConnectorTemplate.Expand(
  comTemplateBox.Text, previewVersionBox.Text)`; результат пишется в `previewResultBox`
  (при `null` — placeholder `"—"` или подсказка, что версия/шаблон пусты).
- `comTemplateBox` и `previewVersionBox` привязаны к тому же стилю `ModernTextBox`.
- Сохранение: в блоке сохранения (строка ~2477) менять только `comTemplateBox.Text`
  (поле версии — исключительно для предпросмотра, в настройки не сохраняется).

### 3.4. UI: окно настроек WPF (`SettingsWindow.xaml` + `.xaml.cs`)

Аналогично Avalonia:
- В `SettingsWindow.xaml` рядом с `ComConnectorNameTemplateBox` добавить
  `ComConnectorPreviewVersionBox` (TextBox, `Text="8.3.45.6789"`) и
  `ComConnectorPreviewResultBox` (read-only TextBox).
- В `SettingsWindow.xaml.cs`:
  - подписка `ComConnectorNameTemplateBox.TextChanged` и
    `ComConnectorPreviewVersionBox.TextChanged` → `UpdateComConnectorPreview()`;
  - обработчик вызывает `ComConnectorTemplate.Expand(...)` и заполняет
    `ComConnectorPreviewResultBox.Text`;
  - в `Save` (строка ~275) сохранять только шаблон (поле версии не сохраняется).

### 3.5. Локализация (ru.json / en.json)

Добавить ключи префикса `Settings.General.ComConnectorPreview*`:
- `ComConnectorPreviewVersionLabel` — «Версия для предпросмотра» / "Version for preview";
- `ComConnectorPreviewResultLabel` — «Результат» / "Result";
- (опц.) `ComConnectorPreviewEmpty` — «—» / "—", либо подсказка при пустом шаблоне/версии.

---

## 4. Версия, CHANGELOG, README

- Поднять версию **0.3.6.98 → 0.3.6.99** в `VersionInfo.cs` и `csproj`.
- `CHANGELOG.md`: новый раздел `[0.3.6.99]` с описанием фичи #175
  (интерактивный предпросмотр имени COM-коннектора, аккуратная обрезка сегментов версии,
  правки в WPF и Avalonia).
- `README.md`: если там описывается настройка «Имя COM-коннектора», дополнить
  упоминанием предпросмотра и поведения обрезки.

---

## 5. Сборка исполняемых файлов

Использовать существующие скрипты (обязательно по одному для каждой ОС):
- Windows: `Configuration Management/build-windows-single-file.ps1`;
- Linux: `Configuration Management/build-linux-single-file.sh`
  (или `build.ps1`/`build.sh` с флагом `-p:ForceLinux=true`, как указано в планах ранее).

Проверить, что обе сборки проходят без ошибок.

---

## 6. Комментарий к issue #175

После успешной сборки оставить комментарий от имени sivatorov: что исправлено, в какой
версии (0.3.6.99), кратко как проверить (ввести версию и посмотреть предпросмотр; пример
`V%V12%_%V3%_%V4%.ComConnector` + `8.3.27` → `V83_27.ComConnector`).

---

## 7. Push + релиз

- Закоммитить изменения (сообщение: `Fix issue #175: COM connector template live preview + segment trimming; bump to 0.3.6.99`).
- Запушить в `origin/main`.
- Создать новый GitHub Release `v0.3.6.99` с приложенными сборками Windows и Linux
  (как в прошлых релизах, например v0.3.6.98).

---

## 8. Декомпозиция на отдельные задачи

Каждый пункт ниже — отдельная новая задача (по требованию пользователя):
1. Реализация фичи #175 (разделы 3.1–3.5) — code mode.
2. Комментарий к issue #175 (раздел 6) — после правок.
3. Версия + CHANGELOG + README (раздел 4) — code mode.
4. Сборка Windows и Linux (раздел 5) — code mode.
5. Push + релиз (раздел 7) — code mode.

Порядок обязателен: реализация → (сборка для проверки) → версия/доки →
комментарий → push → релиз.