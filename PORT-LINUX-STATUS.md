# Состояние: сборка Linux/Avalonia

Дата среза: 22.08.2026. Форк проекта `sivatorov/ConfigurationManagement`.
Всё, что относится к сборке, лежит в этой папке.

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

## Что осталось: 90 ошибок

Полный список с путями и строками: `_port/errors-remaining.txt`.
Полный лог сборки: `_port/build-2026-08-22.log`.

Группы по существу:

| Группа | Примерно | Суть |
|---|---|---|
| `ToolTip` как свойство | 14 | в Avalonia это присоединённое свойство, ставится `ToolTip.SetTip(control, text)` |
| `Visibility` | 8 | WPF-перечисление, в Avalonia булево `IsVisible`. Затрагивает `Converters/Avalonia/InverseBoolToVisibilityConverter.Avalonia.cs`, конвертер надо переписать на bool |
| `Control.Background`, `Padding`, `FontSize`, `FontWeight`, `FontFamily`, `FontStyle` | 18 | в Avalonia эти свойства объявлены на `TemplatedControl`, а не на `Control`. Нужна смена типа переменных или приведение |
| `GetObservable`, `GetResourceObservable` | 12 | другой механизм ресурсов, `Application` в Avalonia не имеет этих методов напрямую |
| `ShowDialogSync` через чужой тип | 12 | `protected`-метод `ModalWindowBase.ShowDialogSync` вызывается у экземпляра соседнего окна. Чинится сменой модификатора в `ModalWindowBase.cs` |
| `ControlTheme.Template`, `FuncTreeDataTemplate` с двумя аргументами, сеттер `Window.Owner` | 8 | API Avalonia 11 изменился, нужен разбор замысла автора |
| `CapturePointer`, `ReleasePointerCapture`, `GetVisualChildren`, приведение кистей, `Application.Windows`, `Shutdown` | 18 | замена вызовов на аналоги Avalonia. `Shutdown` берётся из `IClassicDesktopStyleApplicationLifetime`, `GetVisualChildren` требует `using Avalonia.VisualTree` |

Распределение по файлам: `MainWindow.Avalonia.cs` 34, `GroupEditWindow.Avalonia.cs` 12,
`Themes/ThemeManager.Avalonia.cs` 9, `ViewModels/MainViewModel.Avalonia.cs` 7,
`CacheCleanWindow.Avalonia.cs` 7, остальные по мелочи.

## Чего не покрывает компиляция

Код ни разу не исполнялся. Даже когда он соберётся, темы, ресурсы, иконка в трее
и дерево баз запустятся в первый раз именно у нас. Рантайм-дефекты придётся ловить
отдельно, и их объём сейчас предсказать нечем.

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
