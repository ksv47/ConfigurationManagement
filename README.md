# Управление конфигурациями 1С: форк с Linux-портом

Форк проекта [sivatorov/ConfigurationManagement](https://github.com/sivatorov/ConfigurationManagement):
десктопного приложения для управления информационными базами 1С:Предприятие 8.3
на .NET 10, WPF под Windows и Avalonia 11 под Linux. Полное описание
возможностей, требований и настроек живёт в
[README апстрима](https://github.com/sivatorov/ConfigurationManagement#readme),
история изменений — в [CHANGELOG.md](CHANGELOG.md).

Форк существует ради Linux-части: доведение сборки и запуска под Avalonia,
приведение интерфейса к разметке Windows-версии окно за окном, разбор жалоб
пользователей Linux-сборок. Правки не остаются здесь: каждый готовый кусок
уходит в апстрим отдельным pull request, список наших слияний виден
[в PR апстрима](https://github.com/sivatorov/ConfigurationManagement/pulls?q=is%3Apr+author%3Aksv47).
В [релизах этого форка](https://github.com/ksv47/ConfigurationManagement/releases)
лежат собранные AppImage и deb для тех, кому нужна свежая Linux-сборка
до того, как автор соберёт её у себя.

## Установка на Linux

Скачать AppImage или deb из [релизов](https://github.com/ksv47/ConfigurationManagement/releases):

```bash
chmod +x ConfigurationManagement-*-x86_64.AppImage && ./ConfigurationManagement-*-x86_64.AppImage
# или
sudo dpkg -i configuration-management_*_amd64.deb
```

### Обновление

Обновляться нужно вручную отсюда же: скачать свежий пакет из релизов форка и
заменить им прежний (deb ставится поверх обычным `dpkg -i`).

Встроенная проверка обновлений смотрит в релизы апстрима, а не в релизы этого
форка, поэтому она сообщает о версии автора и ведёт на его страницу выпусков,
где AppImage и deb не публикуются. Заменить себя из пакета программа не
пытается: она показывает сообщение и ничего не скачивает. Если проверка мешает,
её можно выключить в настройках, раздел «Настройки», «Поведение приложения».

## Сборка из исходников

Нужен .NET SDK 10. Под Linux собирается Avalonia-цель, под Windows — WPF,
целевая платформа выбирается автоматически.

```bash
cd "Configuration Management"
dotnet build "Configuration Management.csproj" -c Release   # сборка
./build.sh Release publish                                  # self-contained в publish/linux-x64
```

Упаковка: `package/linux/appimage.sh` и `package/linux/deb/build-deb.sh`.
