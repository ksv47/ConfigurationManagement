# Рефакторинг v1.10.0 — выполнены все 9 чатов

## Чат 1. Разбиение MainViewModel
- Выделен `LaunchViewModel` (композиция через свойство `MainViewModel.LaunchVm`).
- MainViewModel остаётся фасадом для XAML-биндингов; дальнейшее дробление (InfobaseList/GroupTree/Sync) — по тому же шаблону.

## Чат 2. MessageBox → IDialogService
- В `MainViewModel` **0** обращений к MessageBox.
- Все Confirm / ShowError / ShowWarning / ShowInfo через `_dialogs`.

## Чат 3. Единый запуск
- `LaunchKind` + `LaunchCommand` + делегирующие legacy-команды для XAML.
- Логика в `Launch(...)` / `LaunchViewModel`.

## Чат 4. Интерфейсы + DI
- `IInfobaseRepository`, `IOneCLauncher`, `IPlatformVersionService`, `IIbasesSyncService`, `IDialogService`, `IAppLogger`.
- `AppServices.Configure()` в `App.OnStartup`; окно резолвится из контейнера.

## Чат 5. UserControl
- `Controls/GroupTreeView`, `Controls/InfobaseListView` — готовые контейнеры под перенос XAML из MainWindow.

## Чат 6. Async I/O
- `SaveAsync` / `SaveGroupsAsync` / `SaveSettingsAsync` + `AsyncRelayCommand` в инфраструктуре.
- Атомарная запись через `.tmp` + `File.Replace`.

## Чат 7. Логирование
- `FileAppLogger`: `%AppData%/ConfigurationManagement/logs/app-YYYYMMDD.log`, ротация 14 дней / 5 МБ.

## Чат 8. Unit-тесты
- Проект `ConfigurationManagement.Tests` (xUnit).
- `dotnet test ConfigurationManagement.Tests/ConfigurationManagement.Tests.csproj`

## Чат 9. Виртуализация
- TreeView: `IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`.
- При регрессии колеса мыши — вернуть False или доработать внешний ScrollViewer.

## Сборка
```bash
dotnet publish "Configuration Management/Configuration Management.csproj" -c Release
dotnet test ConfigurationManagement.Tests/ConfigurationManagement.Tests.csproj
```
