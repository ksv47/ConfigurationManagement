using System.Windows;
using System.Windows.Input;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог-конфигуратор параметров запуска платформы 1С.
    /// Состоит из поля ввода параметров и справочника ключей командной строки,
    /// из которого параметр подставляется в поле двойным кликом.
    /// </summary>
    public partial class LaunchParametersWindow : Window
    {
        /// <summary>
        /// Создаёт диалог конфигуратора параметров запуска.
        /// </summary>
        /// <param name="currentParameters">Текущая строка параметров для предзаполнения.</param>
        public LaunchParametersWindow(string currentParameters)
        {
            InitializeComponent();
            TxtCustom.Text = currentParameters ?? string.Empty;
            LstReference.ItemsSource = BuildReferenceCatalog();
        }

        /// <summary>
        /// Итоговая строка параметров запуска.
        /// </summary>
        public string Result { get; private set; } = string.Empty;

        /// <summary>
        /// Строит каталог всех ключей командной строки 1С с описаниями
        /// для справочника в нижней части окна.
        /// </summary>
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

        /// <summary>
        /// Подставляет параметр из справочника в поле «Параметры» по двойному клику.
        /// </summary>
        private void OnReferenceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstReference.SelectedItem is ParamRef item)
                InsertCustomText(item.Key);
        }

        /// <summary>
        /// Добавляет текст в поле «Параметры», разделяя пробелом.
        /// </summary>
        private void InsertCustomText(string text)
        {
            var insert = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(insert))
                return;

            if (string.IsNullOrWhiteSpace(TxtCustom.Text))
                TxtCustom.Text = insert;
            else
                TxtCustom.Text = TxtCustom.Text.TrimEnd() + " " + insert;

            TxtCustom.CaretIndex = TxtCustom.Text.Length;
            TxtCustom.Focus();
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = (TxtCustom.Text ?? string.Empty).Trim();
            DialogResult = true;
        }

        /// <summary>
        /// Запись справочника параметров командной строки 1С.
        /// </summary>
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