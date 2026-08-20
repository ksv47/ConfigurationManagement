#if LINUX
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог-конфигуратор параметров запуска платформы 1С. Состоит из поля ввода
    /// параметров и справочника ключей командной строки, из которого параметр
    /// подставляется в поле двойным кликом. Avalonia/Linux-версия WPF-окна
    /// <see cref="LaunchParametersWindow"/>.
    /// </summary>
    public class LaunchParametersWindow : ModalWindowBase
    {
        private readonly TextBox _txtCustom;

        /// <summary>
        /// Создаёт диалог конфигуратора параметров запуска.
        /// </summary>
        /// <param name="currentParameters">Текущая строка параметров для предзаполнения.</param>
        public LaunchParametersWindow(string currentParameters)
        {
            Title = "Параметры запуска 1С";
            Width = 620;
            Height = 560;
            MinWidth = 540;
            MinHeight = 480;

            _txtCustom = new TextBox
            {
                Text = currentParameters ?? string.Empty,
                Padding = new Thickness(8, 6),
                AcceptsReturn = true,
                MinHeight = 90,
                Watermark = "Например: /DisableStartupMessages /DisableSplash"
            };

            Content = BuildRoot();
        }

        /// <summary>Итоговая строка параметров запуска.</summary>
        public string Result { get; private set; } = string.Empty;

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = "Параметры запуска платформы 1С",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var hint = new TextBlock
            {
                Text = "Введите параметры командной строки или выберите их из справочника ниже двойным кликом.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);

            Grid.SetRow(_txtCustom, 2);
            grid.Children.Add(_txtCustom);

            // Справочник параметров
            var refLabel = new TextBlock { Text = "Справочник ключей командной строки:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
            Grid.SetRow(refLabel, 3);
            grid.Children.Add(refLabel);

            var list = new ListBox();
            list.ItemsSource = BuildReferenceCatalog();
            list.ItemTemplate = new FuncDataTemplate<ParamRef>((item, _) =>
            {
                var panel = new Grid { Margin = new Thickness(2, 3) };
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

                var key = new TextBlock { Text = item.Key, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(key, 0);
                panel.Children.Add(key);

                var desc = new TextBlock { Text = item.Description, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(desc, 1);
                panel.Children.Add(desc);
                return panel;
            });
            list.DoubleTapped += (_, e) =>
            {
                if (list.SelectedItem is ParamRef item)
                {
                    InsertCustomText(item.Key);
                    e.Handled = true;
                }
            };

            var listBorder = new Border
            {
                Child = new ScrollViewer
                {
                    Content = list,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8, 8)
                },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(listBorder, 4);
            grid.Children.Add(listBorder);

            // Кнопки
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            var cancel = new Button { Content = "Отмена", MinWidth = 100, IsCancel = true };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);
            var ok = new Button { Content = "ОК", MinWidth = 110, IsDefault = true };
            ok.Click += (_, _) => OnOk_Click();
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 5);
            grid.Children.Add(buttons);

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            return grid;
        }

        /// <summary>Добавляет текст в поле «Параметры», разделяя пробелом.</summary>
        private void InsertCustomText(string text)
        {
            var insert = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(insert))
                return;

            if (string.IsNullOrWhiteSpace(_txtCustom.Text))
                _txtCustom.Text = insert;
            else
                _txtCustom.Text = _txtCustom.Text.TrimEnd() + " " + insert;

            _txtCustom.CaretIndex = _txtCustom.Text.Length;
            _txtCustom.Focus();
        }

        private void OnOk_Click()
        {
            Result = (_txtCustom.Text ?? string.Empty).Trim();
            DialogResult = true;
            Close();
        }

        /// <summary>Строит каталог ключей командной строки 1С для справочника.</summary>
        private static List<ParamRef> BuildReferenceCatalog()
        {
            var list = new List<ParamRef>();
            void Add(string key, string description) => list.Add(new ParamRef(key, description));

            // Параметры-флаги.
            Add("/DisableStartupMessages", "Отключить стартовые сообщения");
            Add("/DisableStartupDialogs", "Отключить стартовые диалоги");
            Add("/DisableSplash", "Отключить стартовую заставку");
            Add("/WA-", "Не ждать завершения (запуск в фоне)");
            Add("/Debug", "Режим отладки");
            Add("/AllowExecuteScheduledJobs", "Разрешить выполнение регламентных заданий");
            Add("/RunModeManagedApplication", "Запустить в режиме тонкого клиента (управляемое приложение)");
            Add("/RunModeOrdinaryApplication", "Запустить в режиме толстого клиента (обычное приложение)");
            Add("/UpdateCfg", "Обновить конфигурацию базы");
            Add("/TestServer", "Проверить работоспособность кластера серверов");
            Add("/RestoreIB", "Восстановить информационную базу из выгрузки (.dt)");
            Add("/DumpIB", "Выгрузить информационную базу в файл (.dt)");
            Add("/DumpCfg", "Выгрузить конфигурацию в файл (.cf)");
            Add("/LoadCfg", "Загрузить конфигурацию из файла (.cf)");
            Add("/CheckConfig", "Проверить конфигурацию");
            Add("/UpdateConfigDumpCfg", "Обновить конфигурацию и выгрузить её в файл (.cf)");
            Add("/CreateInfobase", "Создать информационную базу по файлу выгрузки");
            Add("/Command", "Выполнить команду после запуска");
            Add("/ManagedClient", "Запустить тонкий клиент (управляемое приложение)");
            Add("/ThickClient", "Запустить толстый клиент");
            Add("/UpdateConfiguration", "Обновить конфигурацию базы");

            // Параметры с аргументами.
            Add("/UC", "Код разрешения запуска");
            Add("/L", "Язык интерфейса (например ru, en)");
            Add("/Out", "Путь к файлу вывода служебных сообщений");
            Add("/C", "Строка параметров запуска (передаётся в приложение)");
            Add("/Execute", "Путь к внешней обработке или отчёту для выполнения");
            Add("/DumpResult", "Путь к файлу выгрузки результата (например, после /Execute)");
            Add("/N", "Имя пользователя");
            Add("/P", "Пароль пользователя");
            Add("/S", "Адрес сервера 1С:Предприятия");
            Add("/F", "Путь к файловой информационной базе");
            Add("/Ref", "Имя информационной базы на сервере");
            Add("/Server", "Имя сервера 1С:Предприятия");
            Add("/Srvr", "Имя сервера 1С:Предприятия (синоним /S)");
            Add("/IBName", "Имя информационной базы в списке баз");
            Add("/DBMS", "Тип СУБД (MSSQLServer, PostgreSQL, Oracle, IBMDB2)");
            Add("/DBSrvr", "Имя сервера СУБД");
            Add("/DBUID", "Имя пользователя СУБД");
            Add("/DBPwd", "Пароль пользователя СУБД");
            Add("/App", "Выбор приложения (Designer / Enterprise)");
            Add("/ConfigurationRepository", "Имя хранилища конфигурации");
            Add("/ConfigurationRepositoryUser", "Пользователь хранилища конфигурации");
            Add("/ConfigurationRepositoryPwd", "Пароль пользователя хранилища конфигурации");
            Add("/DisplayAllFunctions", "Отображать все функции (для запуска тонкого клиента)");
            Add("/WSNamespace", "Пространство имён веб-сервиса");
            Add("/IBSecurity", "Ключ безопасности информационной базы");
            Add("/CPUSecurity", "Ключ безопасности сеанса");
            Add("/SaveAgent", "Сохранить кэш агента сервера");
            Add("/ConfigurationName", "Имя конфигурации для запуска");
            Add("/RegisterExternalDataSource", "Зарегистрировать внешний источник данных");
            Add("/UnregisterExternalDataSource", "Снять регистрацию внешнего источника данных");
            Add("/SqlDump", "Сброс SQL-запросов в файл");

            return list;
        }

        /// <summary>Запись справочника параметров командной строки 1С.</summary>
        private sealed class ParamRef
        {
            public ParamRef(string key, string description)
            {
                Key = key;
                Description = description;
            }

            public string Key { get; }
            public string Description { get; }
        }
    }
}
#endif