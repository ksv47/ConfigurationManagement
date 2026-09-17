#if WINDOWS
using System.Windows;
using System.Windows.Input;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;

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
        /// для справочника в нижней части окна. Логику построения делегирует
        /// общему сервису <see cref="OneCLaunchArgumentParser"/> (ПЗ-5).
        /// </summary>
        private List<OneCLaunchParameterReference> BuildReferenceCatalog()
        {
            return OneCLaunchArgumentParser.BuildReferenceCatalog(_customParams).ToList();
        }

        /// <summary>
        /// Подставляет параметр из справочника в поле «Параметры» по двойному клику.
        /// </summary>
        private void OnReferenceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstReference.SelectedItem is OneCLaunchParameterReference item)
                InsertCustomText(item.Key);
        }

        /// <summary>
        /// Добавляет текст в поле «Параметры», разделяя пробелом.
        /// </summary>
        private void InsertCustomText(string text)
        {
            var updated = OneCLaunchArgumentParser.AppendParameter(TxtCustom.Text, text);
            if (updated == TxtCustom.Text)
                return;

            TxtCustom.Text = updated;
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
            if (LstReference.SelectedItem is not OneCLaunchParameterReference { IsCustom: true } item)
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

    }
}
#endif