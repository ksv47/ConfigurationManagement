#if WINDOWS
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
        /// <summary>Пользовательские параметры, добавленные в справочник (issue #141).</summary>
        private readonly List<string> _customParams;

        /// <summary>Обратный вызов для сохранения списка пользовательских параметров при изменении.</summary>
        private readonly Action<IReadOnlyList<string>>? _onCustomParametersChanged;

        /// <summary>
        /// Создаёт диалог конфигуратора параметров запуска.
        /// </summary>
        /// <param name="currentParameters">Текущая строка параметров для предзаполнения.</param>
        /// <param name="customParameters">Пользовательские параметры, дополняющие справочник (необязательно).</param>
        /// <param name="onCustomParametersChanged">Обратный вызов сохранения изменённого списка пользовательских параметров (необязательно).</param>
        public LaunchParametersWindow(
            string currentParameters,
            IReadOnlyList<string>? customParameters = null,
            Action<IReadOnlyList<string>>? onCustomParametersChanged = null)
        {
            InitializeComponent();
            _onCustomParametersChanged = onCustomParametersChanged;
            _customParams = new List<string>();
            if (customParameters != null)
                _customParams.AddRange(customParameters.Where(p => !string.IsNullOrWhiteSpace(p)));
            TxtCustom.Text = currentParameters ?? string.Empty;
            RefreshReference();
        }

        /// <summary>
        /// Итоговая строка параметров запуска.
        /// </summary>
        public string Result { get; private set; } = string.Empty;

        /// <summary>Возвращает обновлённый список пользовательских параметров справочника (issue #141).</summary>
        public IReadOnlyList<string> CustomParameters => _customParams;

        /// <summary>
        /// Перестраивает список справочника с учётом пользовательских параметров.
        /// </summary>
        private void RefreshReference()
        {
            LstReference.ItemsSource = null;
            LstReference.ItemsSource = BuildReferenceCatalog();
        }

        /// <summary>
        /// Строит каталог всех ключей командной строки 1С с описаниями
        /// для справочника в нижней части окна.
        /// </summary>
        private List<ParamRef> BuildReferenceCatalog()
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

            // Пользовательские параметры (issue #141): добавляются в конец списка,
            // помечаются, чтобы их можно было отличить от встроенных и удалить.
            // Формат элемента: «ключ» либо «ключ<TAB>комментарий» (комментарий из поля
            // TxtNewComment) — ключ и описание разделяются табуляцией, поэтому ключ
            // командной строки подставляется в поле «Параметры» без комментария.
            foreach (var custom in _customParams)
            {
                var parts = (custom ?? string.Empty).Split('\t');
                var key = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var comment = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var description = string.IsNullOrWhiteSpace(comment)
                    ? LocalizationManager.T("LaunchParams.CustomMarker")
                    : comment;
                list.Add(new ParamRef(key, description, isCustom: true));
            }

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

        // ============ Пользовательские параметры (issue #141) ============

        /// <summary>Добавляет введённый в поле текст как пользовательский параметр справочника.</summary>
        private void AddCustomParameter()
        {
            var key = (TxtNewParam.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return;
            var comment = (TxtNewComment.Text ?? string.Empty).Trim();
            var entry = string.IsNullOrWhiteSpace(comment) ? key : key + "\t" + comment;

            // Проверяем совпадение именно по ключу (до табуляции), а не по всей записи.
            if (_customParams.Any(p =>
                string.Equals((p ?? string.Empty).Split('\t')[0].Trim(), key, StringComparison.OrdinalIgnoreCase)))
                return;
            _customParams.Add(entry);
            TxtNewParam.Clear();
            TxtNewComment.Clear();
            PersistCustomParameters();
            RefreshReference();
            TxtNewParam.Focus();
        }

        /// <summary>Удаляет выбранный пользовательский параметр из справочника.</summary>
        private void RemoveSelectedCustomParameter()
        {
            if (LstReference.SelectedItem is not ParamRef { IsCustom: true } item)
                return;
            _customParams.RemoveAll(p =>
                string.Equals((p ?? string.Empty).Split('\t')[0].Trim(), item.Key, StringComparison.OrdinalIgnoreCase));
            PersistCustomParameters();
            RefreshReference();
        }

        /// <summary>Сохраняет список пользовательских параметров через обратный вызов.</summary>
        private void PersistCustomParameters()
        {
            _onCustomParametersChanged?.Invoke(_customParams);
        }

        private void OnAddParam_Click(object sender, RoutedEventArgs e) => AddCustomParameter();

        private void OnRemoveParam_Click(object sender, RoutedEventArgs e) => RemoveSelectedCustomParameter();

        private void OnNewParamKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddCustomParameter();
                e.Handled = true;
            }
        }

        private void OnReferenceKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedCustomParameter();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Запись справочника параметров командной строки 1С.
        /// </summary>
        private sealed class ParamRef
        {
            public ParamRef(string key, string description, bool isCustom = false)
            {
                Key = key;
                Description = description;
                IsCustom = isCustom;
            }

            public string Key { get; }
            public string Description { get; }
            public bool IsCustom { get; }
        }
    }
}
#endif