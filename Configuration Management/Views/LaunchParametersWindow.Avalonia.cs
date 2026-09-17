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
using Configuration_Management.Models;
using Configuration_Management.Services;
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
            list.ItemTemplate = new FuncDataTemplate<OneCLaunchParameterReference>((item, _) =>
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
                if (list.SelectedItem is OneCLaunchParameterReference item)
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
            var updated = OneCLaunchArgumentParser.AppendParameter(_txtCustom.Text, text);
            if (updated == _txtCustom.Text)
                return;

            _txtCustom.Text = updated;
            _txtCustom.CaretIndex = _txtCustom.Text.Length;
            _txtCustom.Focus();
        }

        private void OnOk_Click()
        {
            Result = (_txtCustom.Text ?? string.Empty).Trim();
        }

        /// <summary>
        /// Строит каталог ключей командной строки 1С для справочника. Логику построения
        /// делегирует общему сервису <see cref="OneCLaunchArgumentParser"/> (ПЗ-5);
        /// пользовательских параметров на Linux в справочнике нет.
        /// </summary>
        private static IReadOnlyList<OneCLaunchParameterReference> BuildReferenceCatalog()
        {
            return OneCLaunchArgumentParser.BuildReferenceCatalog();
        }

    }
}
#endif