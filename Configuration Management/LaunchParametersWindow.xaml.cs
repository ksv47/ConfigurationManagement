using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;

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

            // Ключ перевода описания параметра по его ключу командной строки.
            void Add(string key)
            {
                var locKey = "LaunchParams.Ref." + key.Trim('/').Replace("-", "");
                list.Add(new ParamRef(key, LocalizationManager.T(locKey)));
            }

            // Параметры-флаги.
            Add("/DisableStartupMessages");
            Add("/DisableStartupDialogs");
            Add("/DisableSplash");
            Add("/WA-");
            Add("/Debug");
            Add("/AllowExecuteScheduledJobs");
            Add("/RunModeManagedApplication");
            Add("/RunModeOrdinaryApplication");
            Add("/UpdateCfg");
            Add("/TestServer");
            Add("/RestoreIB");
            Add("/DumpIB");
            Add("/DumpCfg");
            Add("/LoadCfg");
            Add("/CheckConfig");
            Add("/UpdateConfigDumpCfg");
            Add("/CreateInfobase");
            Add("/Command");
            Add("/ManagedClient");
            Add("/ThickClient");
            Add("/UpdateConfiguration");

            // Параметры с аргументами.
            Add("/UC");
            Add("/L");
            Add("/Out");
            Add("/C");
            Add("/Execute");
            Add("/DumpResult");
            Add("/N");
            Add("/P");
            Add("/S");
            Add("/F");
            Add("/Ref");
            Add("/Server");
            Add("/Srvr");
            Add("/IBName");
            Add("/DBMS");
            Add("/DBSrvr");
            Add("/DBUID");
            Add("/DBPwd");
            Add("/App");
            Add("/ConfigurationRepository");
            Add("/ConfigurationRepositoryUser");
            Add("/ConfigurationRepositoryPwd");
            Add("/DisplayAllFunctions");
            Add("/WSNamespace");
            Add("/IBSecurity");
            Add("/CPUSecurity");
            Add("/SaveAgent");
            Add("/ConfigurationName");
            Add("/RegisterExternalDataSource");
            Add("/UnregisterExternalDataSource");
            Add("/SqlDump");

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