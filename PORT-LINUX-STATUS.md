# Состояние: сборка и запуск Linux/Avalonia

Дата среза: 22.08.2026. Форк проекта `sivatorov/ConfigurationManagement`.
Всё, что относится к сборке, лежит в этой папке.

## Где мы сейчас

Linux-цель **собирается без ошибок и запускается**. Главное окно открывается,
отрисовывается, тема и иконки на месте (`_port/screenshot-first-run.png`).
Пройдено: 90 ошибок компиляции закрыты тремя группами, затем четыре дефекта,
вскрывшихся на первом исполнении.

Дальше идёт функциональная проверка: ни один сценарий работы с базами
на Linux ещё не проходили.

## Короткий вывод

Апстрим под Linux **никогда не компилировался**. Windows/WPF-ветка у автора рабочая,
а Avalonia-код писался без единого прогона компилятора. Собрать проект под Linux
означает доделать порт, а не запустить сборку.

Доказательства, а не впечатление:

* В пятнадцати файлах окон (`AddEditWindow.Avalonia.cs`, `CacheCleanWindow.Avalonia.cs`,
  `SettingsWindow.Avalonia.cs` и других) не было строки `using Avalonia;`. Без неё не
  разрешается даже `Thickness`, базовый тип отступов.
* `git log -S "using Avalonia;" -- "Configuration Management/AddEditWindow.Avalonia.cs"`
  не даёт ни одного коммита из 72. Строки не было никогда.
* CI отсутствует: каталога `.github` в репозитории нет.

Собрать WPF-вариант на Linux нельзя в принципе: `net10.0-windows` с `UseWPF` требует
Windows, кросс-сборки WPF не существует. Для Windows-варианта нужна Windows-машина
с .NET 10 SDK и скрипт `Configuration Management/build.ps1`.

## Окружение

* Ubuntu 24.04.4, .NET SDK 10.0.111 из системного репозитория (`sudo apt-get install dotnet-sdk-10.0`).
* `dotnet restore` проходит, Avalonia 11.3.20 скачивается без проблем.
* Аппаратных и сетевых препятствий нет, упирается только код.

## Ветки

* `main` — чистый апстрим, `dac6458`. Не трогать, чтобы `git pull` от автора шёл без конфликтов.
* `linux-port` — наши правки. Текущая ветка.

## Что уже сделано (коммит 7cc1ba3)

Механическая часть, 19 файлов:

* добавлены `using Avalonia`, `using Avalonia.Controls.Primitives`,
  `using Avalonia.Controls.Presenters` в файлы, где их не хватало;
* `GetContainerForItemOverride()` заменён на
  `CreateContainerForItemOverride(object? item, int index, object? recycleKey)`
  в `Controls/LeveledTreeView.Avalonia.cs` и `Controls/LeveledTreeViewItem.Avalonia.cs`.
  Старая сигнатура это API Avalonia 0.10, в 11.x её нет;
* `ToggleButton.OnToggle()` заменён на `Toggle()` в `MainWindow.Avalonia.cs`.
  `OnToggle` это WPF, в Avalonia виртуальный метод называется `Toggle`;
* создан `Controls/PasswordBox.Avalonia.cs`: своя реализация поверх `TextBox`
  с `PasswordChar`, свойством `Password` и событием `PasswordChanged`.
  В Avalonia контрола `PasswordBox` нет, а в проекте он нигде не был определён,
  хотя `ConnectionSettingsWindow.Avalonia.cs` использует его в трёх полях;
* в `ConnectionStringInputWindow.Avalonia.cs` добавлен
  `using Configuration_Management.Services;` для `IDialogService`;
* в `MainWindow.Avalonia.cs` снята неоднозначность `Path` между
  `System.IO.Path` и `Avalonia.Controls.Shapes.Path` (строка 44 квалифицирована явно).

## Важная особенность диагностики

Первый прогон показал всего 8 ошибок. Это обманчиво: все восемь были в объявлениях
(поля, сигнатуры методов), из-за чего Roslyn не переходил к связыванию тел методов.
После их устранения вскрылось 245 ошибок, после добавления `using` осталось 90.
Вывод на будущее: пока в объявлениях есть ошибки, счётчик ошибок ничего не говорит
о реальном объёме работы.

## Что сделано после коммита 7cc1ba3

Четыре коммита, по группам.

### `81ecdd0` Однозначные правки, 90 -> 45

* `ToolTip` в Avalonia присоединённое свойство: 14 мест переведены на
  `ToolTip.SetTip(control, text)`.
* `ModalWindowBase.ShowDialogSync` из `protected` в `public`: метод вызывается
  у экземпляра соседнего окна, а не через наследование. 12 мест.
* `ThemeManager`: добавлены `using Avalonia.Styling` (`ThemeVariant`),
  `Avalonia.VisualTree` (`GetVisualChildren`), `Avalonia.Controls.ApplicationLifetimes`;
  `Application.Current.Windows` заменено на `desktop.Windows`.
* `App`: добавлен собственный `Shutdown`, у Avalonia `Application` его нет.
* `CapturePointer` / `ReleasePointerCapture` заменены на `e.Pointer.Capture(...)`.
* `Path`: алиас на `Avalonia.Controls.Shapes.Path` в `GroupEditWindow`,
  явная квалификация `System.IO.Path.Combine` в `MainWindow`.

### `e173046` Visibility и Control против TemplatedControl, 45 -> 15

* `InverseBoolToVisibilityConverter` переписан на `bool`: перечисления
  `Visibility` в Avalonia нет, видимостью управляет `IsVisible`. Тот же приём
  автор уже применил сам в `GroupVisibilityConverter.Avalonia.cs`, поэтому
  замысел угадывать не пришлось.
* `Background`, `BorderBrush`, `BorderThickness`, `Padding` объявлены на
  `TemplatedControl`, а не на `Control`: поправлены `TemplateBinding`,
  сеттер стиля и `ThemeBrushes.Bind`.
* Шрифт в `ThemeManager.ApplyFont` ставится через присоединённые
  `TextElement.SetFontFamily` и соседние: они принимают `Control`
  и наследуются вниз по дереву, что и написано в авторском комментарии.
* У `Grid` и `StackPanel` нет `Padding`: отступ статус-бара переехал на его
  собственный внешний `Border`, отступ содержимого диалога стал `Margin`.
* Кисти hover-состояния объявлены как `IBrush`: `var` выводил
  `IImmutableSolidColorBrush`, а ресурс темы отдаёт `IBrush`.
* `GetObservable` это метод расширения, нужен явный получатель `this`.
  У `IsCheckedProperty` тип `bool?`, добавлена проекция `v => v == true`.

### `0e20110` Последняя группа, 15 -> 0

* `ControlTheme` не имеет свойства `Template`. Шаблон задаётся сеттером
  `TemplatedControl.TemplateProperty` в коллекции `Setters`. Замысел автора
  читается однозначно: свой шаблон `Border` плюс `ContentPresenter` вместо
  хрома Fluent.
* `FuncTreeDataTemplate` принимает три аргумента, а не два: первым идёт
  совпадение по типу (`typeof(object)`), функция построения принимает ещё
  и `INameScope`, поэтому обёрнута в `(item, _) => BuildTreeRow(item)`.
  Четыре места.
* `Window.Owner` имеет `protected set`. Владелец задаётся перегрузкой
  `Show(owner)`, она же выставляет `Owner` внутри.
* `TrayIcon.SetIcons` принимает `Application`, а не окно.
* `RelayCommand`: метод без параметров вместе с предикатом `_ => ...`
  не даёт подходящей перегрузки, метод обёрнут в `_ => Method()`. Семь команд.

Предупреждений осталось 29, все авторские и все были до правок:
устаревшие `ToggleButton.Checked` / `Unchecked`, устаревший
`IClipboard.GetTextAsync`, нуллабельность, два `CA1416`.

## Что вскрылось на первом исполнении

Код до этого не запускался ни разу, поэтому дефекты ловились по одному.

### `1ef9ef7` Два падения на старте

* `UiMetrics.AddBrushTransition` и `AddOpacityTransition` падали
  с `NullReferenceException`: `Animatable.Transitions` в Avalonia по умолчанию
  `null`, коллекцию нужно создать. Компилятор об этом предупреждал (`CS8602`),
  автор предупреждение не видел. Лог: `_port/run-crash-transitions.log`.
* `TemplateBinding` внутри шаблонов `PanelButton` и `SegmentButton` падал
  с `NotSupportedException`: у `Bind` приоритет по умолчанию `LocalValue`,
  а у выражения `TemplateBinding` приоритет `Template`, и они конфликтуют.
  Тринадцать привязок переведены на индексаторную форму
  `control[!Property] = new TemplateBinding(...)`, она берёт приоритет
  из самой привязки. Лог: `_port/run-crash-templatebinding.log`.

### `0a1355b` Тема оформления и выход на старте

* `App.axaml` подключал только `Themes/Icons.axaml`, но не
  `Themes/LightTheme.axaml`. Из-за этого 13 ключей ресурсов, которые
  запрашивает код (`AccentBrush`, `TextPrimaryBrush`, `CardBackgroundBrush`,
  `ItemHoverBrush`, `SecondaryButton*Brush`, `TextOnAccentBrush`,
  `FavoriteBrush` и другие), не разрешались вовсе: иконки на кнопках
  оставались без `Fill` и были невидимы, акцентный фон primary-кнопок
  не применялся. Работали только семь ключей, которые случайно совпали
  с тем, что публикует `ThemeManager.ApplyColors` (ключ схемы плюс `Brush`):
  `AccentColorBrush`, `BorderColorBrush`, `ContentBackgroundColorBrush`
  и ещё четыре. Сравнение до и после:
  `_port/screenshot-no-theme-brushes.png` и `_port/screenshot-first-run.png`.
* `desktop.Shutdown` нельзя вызывать из `OnFrameworkInitializationCompleted`:
  он гасит `Dispatcher` до входа в цикл сообщений, и `MainLoop` падает
  с `InvalidOperationException`. Оба вызова относятся к этапу запуска
  (второй экземпляр и фатальная ошибка), поэтому выход делается через
  `Environment.Exit`. Проверено исполнением: запуск второго экземпляра
  при живом первом даёт код возврата 0 и пустой вывод.

## Известные дефекты, ещё не закрытые

* **Модальность диалогов имитируется, а не обеспечивается.**
  `ModalWindowBase.ShowDialogSync` и `AvaloniaDialogService.ShowModalSync`
  делают `Show()` плюс цикл `Dispatcher.UIThread.RunJobs()` с `Thread.Sleep(10)`.
  Окно-владелец при этом остаётся кликабельным: пользователь может повторно
  запустить ту же команду или открыть второй диалог. В Avalonia для этого есть
  `Window.ShowDialog(owner)`. Это авторская архитектура, а не следствие наших
  правок, поэтому трогать её без отдельного решения не стали.
* **Подписки на ресурсы темы без отписки.** `ThemeBrushes.Bind` и
  `IconHelper.MakeIcon` вызывают `GetResourceObservable(...).Subscribe(...)`
  и выбрасывают возвращённый `IDisposable`. Наблюдатель держит сильную ссылку
  на контрол, а observable живёт у `Application.Current`. Для главного окна
  безвредно, но каждый показ модального диалога навсегда укореняет его
  визуальное дерево. Тоже авторское, диффом не вводилось.
* **`HeaderBrush` и `HeaderTextBrush`** запрашиваются в `MainWindow`,
  но не определены ни в словаре темы, ни в `ColorScheme`. Чем их заменить,
  из кода не следует, нужен ответ автора или решение по месту.
* **Сегмент «Все»** в верхней панели: акцентная подложка уже текста,
  надпись частично перекрыта. Видно на `_port/screenshot-first-run.png`.
* **Функциональность не проверялась.** Добавление базы, синхронизация
  с `ibases.v8i`, запуск 1С, очистка кеша, дерево групп, настройки, трей:
  ни один сценарий на Linux не прогонялся.

## Как проверялись правки

Планка сравнения бралась не по памяти, а по реальному API: reference-сборки
и XML-документация лежат локально в
`~/.nuget/packages/avalonia/11.3.20/ref/net8.0/`. Плюс два встречных аудита
изменений (codex и вторая модель), их вердикты в `_port/audit-codex-g1g2.md`.
Аудит нашёл дефект с `desktop.Shutdown`, который компилятор пропускает.

## Чего не покрывает компиляция

Компиляция и запуск говорят только о том, что окно строится. Ни одна прикладная
операция не выполнялась: дерево баз наполнялось пустым списком, иконка в трее
на GNOME Shell без AppIndicator может не показаться вовсе, диалоги ни разу
не открывались. Всё это ловится только прогоном сценариев.

## Лицензия

Файла лицензии в репозитории нет ни в одном коммите. README называет проект
Open Source и ссылается на публикацию https://infostart.ru/1c/articles/2764888/,
но текста лицензии нет. Если форк выкладывается публично с нашими правками,
условия стоит уточнить у автора.

## Команды

Сборка Linux:

```bash
cd ~/project/ConfigurationManagement/"Configuration Management"
dotnet build "Configuration Management.csproj" -c Release \
  -p:RuntimeIdentifier= -p:SelfContained=false -p:PublishSingleFile=false
```

Публикация self-contained single-file после того, как сборка пройдёт:

```bash
cd ~/project/ConfigurationManagement/"Configuration Management"
./build.sh Release publish   # результат в publish/linux-x64
```

Упаковка: `package/linux/appimage.sh`, `package/linux/deb/build-deb.sh`.
