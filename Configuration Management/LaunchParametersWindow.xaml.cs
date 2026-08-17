using System.Text;
using System.Windows;

namespace Configuration_Management
{
    /// <summary>
    /// Диалог-конфигуратор параметров запуска платформы 1С.
    /// Позволяет выбрать параметры командной строки из документации
    /// (флаги и параметры с аргументами) и собрать итоговую строку.
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
            ParseParameters(currentParameters);
        }

        /// <summary>
        /// Итоговая строка параметров запуска.
        /// </summary>
        public string Result { get; private set; } = string.Empty;

        /// <summary>
        /// Разбирает строку параметров и заполняет элементы управления.
        /// </summary>
        private void ParseParameters(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
                return;

            var tokens = Tokenize(parameters);

            foreach (var token in tokens)
            {
                var key = token.Key;
                var value = token.Value;

                // Сопоставляем параметры без учёта регистра.
                switch (key.ToUpperInvariant())
                {
                    case "/DISABLESTARTUPMESSAGES": ChkDisableStartupMessages.IsChecked = true; break;
                    case "/DISABLESTARTUPDIALOGS": ChkDisableStartupDialogs.IsChecked = true; break;
                    case "/DISABLESPLASH": ChkDisableSplash.IsChecked = true; break;
                    case "/WA-": ChkWait.IsChecked = true; break;
                    case "/DEBUG": ChkDebug.IsChecked = true; break;
                    case "/ALLOWEXECUTESCHEDULEDJOBS": ChkAllowScheduledJobs.IsChecked = true; break;
                    case "/RUNMODEMANAGEDAPPLICATION": ChkRunManaged.IsChecked = true; break;
                    case "/RUNMODEORDINARYAPPLICATION": ChkRunOrdinary.IsChecked = true; break;
                    case "/UPDATECFG": ChkUpdateCfg.IsChecked = true; break;
                    case "/TESTSERVER": ChkTestServer.IsChecked = true; break;
                    case "/UC": ChkUC.IsChecked = true; TxtUC.Text = value; break;
                    case "/L": ChkLang.IsChecked = true; TxtLang.Text = value; break;
                    case "/OUT": ChkOut.IsChecked = true; TxtOut.Text = value; break;
                    case "/C": ChkC.IsChecked = true; TxtC.Text = value; break;
                    case "/EXECUTE": ChkExecute.IsChecked = true; TxtExecute.Text = value; break;
                    case "/DUMPRESULT": ChkDumpResult.IsChecked = true; TxtDumpResult.Text = value; break;
                    case "/N": ChkUser.IsChecked = true; TxtUser.Text = value; break;
                    case "/P": ChkPwd.IsChecked = true; TxtPwd.Password = value; break;
                    default:
                        // Неизвестный параметр добавляем в произвольные.
                        AppendCustom(token.Raw);
                        break;
                }
            }
        }

        /// <summary>
        /// Разбивает строку параметров на токены (ключ + значение).
        /// </summary>
        private static List<ParamToken> Tokenize(string parameters)
        {
            var result = new List<ParamToken>();
            var parts = SplitCommandLine(parameters);

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (!part.StartsWith('/'))
                {
                    // Значение без ключа — добавляем как произвольный параметр.
                    result.Add(new ParamToken(part, string.Empty, part));
                    continue;
                }

                // Определяем, есть ли у параметра значение (следующий токен без '/').
                string value = string.Empty;
                if (i + 1 < parts.Count && !parts[i + 1].StartsWith('/'))
                {
                    value = parts[i + 1].Trim('"');
                    i++;
                }

                result.Add(new ParamToken(part, value, value.Length > 0 ? $"{part} \"{value}\"" : part));
            }

            return result;
        }

        /// <summary>
        /// Разбивает строку командной строки на токены, учитывая кавычки.
        /// Значения в кавычках (включая пробелы) считаются одним токеном.
        /// Это позволяет корректно сопоставлять параметры с аргументами,
        /// содержащими пробелы (например, пути к файлам).
        /// </summary>
        private static List<string> SplitCommandLine(string input)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            foreach (var ch in input)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    current.Append(ch);
                }
                else if (ch == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }

        /// <summary>
        /// Добавляет произвольный параметр в поле ручного ввода.
        /// </summary>
        private void AppendCustom(string raw)
        {
            if (string.IsNullOrWhiteSpace(TxtCustom.Text))
                TxtCustom.Text = raw;
            else
                TxtCustom.Text += " " + raw;
        }

        /// <summary>
        /// Собирает итоговую строку параметров из выбранных элементов.
        /// </summary>
        private string BuildParameters()
        {
            var sb = new StringBuilder();

            void AddFlag(string flag)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(flag);
            }

            void AddValue(string key, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(key).Append(" \"").Append(value.Trim()).Append('"');
            }

            if (ChkDisableStartupMessages.IsChecked == true) AddFlag("/DisableStartupMessages");
            if (ChkDisableStartupDialogs.IsChecked == true) AddFlag("/DisableStartupDialogs");
            if (ChkDisableSplash.IsChecked == true) AddFlag("/DisableSplash");
            if (ChkWait.IsChecked == true) AddFlag("/WA-");
            if (ChkDebug.IsChecked == true) AddFlag("/Debug");
            if (ChkAllowScheduledJobs.IsChecked == true) AddFlag("/AllowExecuteScheduledJobs");
            if (ChkRunManaged.IsChecked == true) AddFlag("/RunModeManagedApplication");
            if (ChkRunOrdinary.IsChecked == true) AddFlag("/RunModeOrdinaryApplication");
            if (ChkUpdateCfg.IsChecked == true) AddFlag("/UpdateCfg");
            if (ChkTestServer.IsChecked == true) AddFlag("/TestServer");

            if (ChkUC.IsChecked == true) AddValue("/UC", TxtUC.Text);
            if (ChkLang.IsChecked == true) AddValue("/L", TxtLang.Text);
            if (ChkOut.IsChecked == true) AddValue("/Out", TxtOut.Text);
            if (ChkC.IsChecked == true) AddValue("/C", TxtC.Text);
            if (ChkExecute.IsChecked == true) AddValue("/Execute", TxtExecute.Text);
            if (ChkDumpResult.IsChecked == true) AddValue("/DumpResult", TxtDumpResult.Text);
            if (ChkUser.IsChecked == true) AddValue("/N", TxtUser.Text);
            if (ChkPwd.IsChecked == true) AddValue("/P", TxtPwd.Password);

            if (!string.IsNullOrWhiteSpace(TxtCustom.Text))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(TxtCustom.Text.Trim());
            }

            return sb.ToString();
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = BuildParameters();
            DialogResult = true;
        }

        /// <summary>
        /// Токен параметра командной строки.
        /// </summary>
        private sealed class ParamToken
        {
            public ParamToken(string key, string value, string raw)
            {
                Key = key;
                Value = value;
                Raw = raw;
            }

            public string Key { get; }
            public string Value { get; }
            public string Raw { get; }
        }
    }
}