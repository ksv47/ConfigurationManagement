#if LINUX
using System;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Themes;

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
            Title = LocalizationManager.T("LaunchParams.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            Width = 800;
            Height = 640;
            MinWidth = 720;
            MinHeight = 480;

            // Высота и выравнивание из разметки (LaunchParametersWindow.xaml:28 и :48).
            _txtCustom = new TextBox
            {
                Text = currentParameters ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 110,
                Padding = new Thickness(6, 6),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _txtCustom.Styled(Themes.ControlThemes.ModernTextBox);
            ToolTip.SetTip(_txtCustom, LocalizationManager.T("LaunchParams.InputTooltip"));

            Content = BuildRoot();
        }

        /// <summary>
        /// Кружок справки тем же контролом, что и в остальных окнах
        /// (Controls/HelpLink.Avalonia.cs): в разметке это controls:HelpLink,
        /// с всплывающим окном по клику, а не только подсказкой.
        /// </summary>
        private static Control BuildHelpLink(string helpKey) => new Controls.HelpLink
        {
            HelpText = LocalizationManager.T(helpKey),
            VerticalAlignment = VerticalAlignment.Center
        };

        /// <summary>Итоговая строка параметров запуска.</summary>
        public string Result { get; private set; } = string.Empty;

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            // Три строки, как в разметке: рамка ввода, рамка справочника
            // на всё оставшееся место и кнопки (LaunchParametersWindow.xaml:31-37).
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Поле ввода лежит в рамке «Параметры» со справкой в заголовке,
            // как в разметке (LaunchParametersWindow.xaml:40-51). Своих заголовка
            // и подсказки у автора здесь нет.
            var inputBox = Controls.GroupBoxPanel.Build(
                "Main.Parameters", _txtCustom,
                margin: new Thickness(0, 0, 0, 12),
                padding: new Thickness(12),
                headerExtra: BuildHelpLink("LaunchParams.InputHelp"));
            Grid.SetRow(inputBox, 0);
            grid.Children.Add(inputBox);

            var list = new ListBox();
            // В WPF это ListView, до которого неявный стиль ListBox не доходит,
            // и горизонтальная прокрутка на нём выключена явно
            // (LaunchParametersWindow.xaml:62). Общий стиль порта ставит её Auto
            // всем спискам, поэтому здесь она возвращается в Disabled: ширину
            // и без того держит внешний ScrollViewer.
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.ItemsSource = BuildReferenceCatalog();
            list.ItemTemplate = new FuncDataTemplate<ParamRef>((item, _) =>
            {
                // Переработка контейнеров виртуализацией строит шаблон с null:
                // без этой проверки список из 52 строк роняет приложение
                // при первой же прокрутке. Тот же дефект был в списке колонок
                // окна настроек.
                if (item is null)
                    return new Control();

                var panel = new Grid { Margin = new Thickness(2, 3) };
                // Ширина колонки параметра из разметки (LaunchParametersWindow.xaml:69).
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
                panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

                var key = new TextBlock
                {
                    Text = item.Key,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(key, 0);
                panel.Children.Add(key);

                var desc = new TextBlock
                {
                    Text = item.Description,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
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

            var listHost = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            // Подписи колонок над списком: в WPF это шапка GridView, у нас список,
            // поэтому строка своя, но ширины те же, что у строк справочника.
            var columnsHeader = new Grid { Margin = new Thickness(2, 0, 2, 4) };
            columnsHeader.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(220)));
            columnsHeader.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            var paramHead = new TextBlock
            {
                Text = LocalizationManager.T("LaunchParams.Parameter"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            };
            var descHead = new TextBlock
            {
                Text = LocalizationManager.T("LaunchParams.Description"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            };
            Themes.ThemeBrushes.Bind(paramHead, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Themes.ThemeBrushes.Bind(descHead, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(paramHead, 0);
            Grid.SetColumn(descHead, 1);
            columnsHeader.Children.Add(paramHead);
            columnsHeader.Children.Add(descHead);
            // Справочник в такой же рамке со справкой в заголовке
            // (LaunchParametersWindow.xaml:54-74).
            var refContent = new Grid();
            refContent.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            refContent.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetRow(columnsHeader, 0);
            refContent.Children.Add(columnsHeader);
            Grid.SetRow(listHost, 1);
            refContent.Children.Add(listHost);

            var referenceBox = Controls.GroupBoxPanel.Build(
                "LaunchParams.Reference", refContent,
                margin: new Thickness(0, 0, 0, 12),
                padding: new Thickness(12),
                headerExtra: BuildHelpLink("LaunchParams.ReferenceHelp"));
            Grid.SetRow(referenceBox, 1);
            grid.Children.Add(referenceBox);

            // Кнопки
            // Оформление и порядок по разметке (LaunchParametersWindow.xaml:77).
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("Common.Ok", "IconCheck", 140, OnOk_Click),
                    BuildCancelActionButton(140)
                }
            };
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

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
        }

        /// <summary>Строит каталог ключей командной строки 1С для справочника.</summary>
        private static List<ParamRef> BuildReferenceCatalog()
        {
            var list = new List<ParamRef>();
            void Add(string key, string description) => list.Add(new ParamRef(key, description));

            // Параметры-флаги.
            Add("/DisableStartupMessages", LocalizationManager.T("LaunchParams.Ref.DisableStartupMessages"));
            Add("/DisableStartupDialogs", LocalizationManager.T("LaunchParams.Ref.DisableStartupDialogs"));
            Add("/DisableSplash", LocalizationManager.T("LaunchParams.Ref.DisableSplash"));
            Add("/WA-", LocalizationManager.T("LaunchParams.Ref.WA"));
            Add("/Debug", LocalizationManager.T("LaunchParams.Ref.Debug"));
            Add("/AllowExecuteScheduledJobs", LocalizationManager.T("LaunchParams.Ref.AllowExecuteScheduledJobs"));
            Add("/RunModeManagedApplication", LocalizationManager.T("LaunchParams.Ref.RunModeManagedApplication"));
            Add("/RunModeOrdinaryApplication", LocalizationManager.T("LaunchParams.Ref.RunModeOrdinaryApplication"));
            Add("/UpdateCfg", LocalizationManager.T("LaunchParams.Ref.UpdateCfg"));
            Add("/TestServer", LocalizationManager.T("LaunchParams.Ref.TestServer"));
            Add("/RestoreIB", LocalizationManager.T("LaunchParams.Ref.RestoreIB"));
            Add("/DumpIB", LocalizationManager.T("LaunchParams.Ref.DumpIB"));
            Add("/DumpCfg", LocalizationManager.T("LaunchParams.Ref.DumpCfg"));
            Add("/LoadCfg", LocalizationManager.T("LaunchParams.Ref.LoadCfg"));
            Add("/CheckConfig", LocalizationManager.T("LaunchParams.Ref.CheckConfig"));
            Add("/UpdateConfigDumpCfg", LocalizationManager.T("LaunchParams.Ref.UpdateConfigDumpCfg"));
            Add("/CreateInfobase", LocalizationManager.T("LaunchParams.Ref.CreateInfobase"));
            Add("/Command", LocalizationManager.T("LaunchParams.Ref.Command"));
            Add("/ManagedClient", LocalizationManager.T("LaunchParams.Ref.ManagedClient"));
            Add("/ThickClient", LocalizationManager.T("LaunchParams.Ref.ThickClient"));
            Add("/UpdateConfiguration", LocalizationManager.T("LaunchParams.Ref.UpdateConfiguration"));

            // Параметры с аргументами.
            Add("/UC", LocalizationManager.T("LaunchParams.Ref.UC"));
            Add("/L", LocalizationManager.T("LaunchParams.Ref.L"));
            Add("/Out", LocalizationManager.T("LaunchParams.Ref.Out"));
            Add("/C", LocalizationManager.T("LaunchParams.Ref.C"));
            Add("/Execute", LocalizationManager.T("LaunchParams.Ref.Execute"));
            Add("/DumpResult", LocalizationManager.T("LaunchParams.Ref.DumpResult"));
            Add("/N", LocalizationManager.T("LaunchParams.Ref.N"));
            Add("/P", LocalizationManager.T("LaunchParams.Ref.P"));
            Add("/S", LocalizationManager.T("LaunchParams.Ref.S"));
            Add("/F", LocalizationManager.T("LaunchParams.Ref.F"));
            Add("/Ref", LocalizationManager.T("LaunchParams.Ref.Ref"));
            Add("/Server", LocalizationManager.T("LaunchParams.Ref.Server"));
            Add("/Srvr", LocalizationManager.T("LaunchParams.Ref.Srvr"));
            Add("/IBName", LocalizationManager.T("LaunchParams.Ref.IBName"));
            Add("/DBMS", LocalizationManager.T("LaunchParams.Ref.DBMS"));
            Add("/DBSrvr", LocalizationManager.T("LaunchParams.Ref.DBSrvr"));
            Add("/DBUID", LocalizationManager.T("LaunchParams.Ref.DBUID"));
            Add("/DBPwd", LocalizationManager.T("LaunchParams.Ref.DBPwd"));
            Add("/App", LocalizationManager.T("LaunchParams.Ref.App"));
            Add("/ConfigurationRepository", LocalizationManager.T("LaunchParams.Ref.ConfigurationRepository"));
            Add("/ConfigurationRepositoryUser", LocalizationManager.T("LaunchParams.Ref.ConfigurationRepositoryUser"));
            Add("/ConfigurationRepositoryPwd", LocalizationManager.T("LaunchParams.Ref.ConfigurationRepositoryPwd"));
            Add("/DisplayAllFunctions", LocalizationManager.T("LaunchParams.Ref.DisplayAllFunctions"));
            Add("/WSNamespace", LocalizationManager.T("LaunchParams.Ref.WSNamespace"));
            Add("/IBSecurity", LocalizationManager.T("LaunchParams.Ref.IBSecurity"));
            Add("/CPUSecurity", LocalizationManager.T("LaunchParams.Ref.CPUSecurity"));
            Add("/SaveAgent", LocalizationManager.T("LaunchParams.Ref.SaveAgent"));
            Add("/ConfigurationName", LocalizationManager.T("LaunchParams.Ref.ConfigurationName"));
            Add("/RegisterExternalDataSource", LocalizationManager.T("LaunchParams.Ref.RegisterExternalDataSource"));
            Add("/UnregisterExternalDataSource", LocalizationManager.T("LaunchParams.Ref.UnregisterExternalDataSource"));
            Add("/SqlDump", LocalizationManager.T("LaunchParams.Ref.SqlDump"));

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