#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Configuration_Management.Themes;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия контейнера элемента дерева (TreeViewItem). Единый тип контейнеров нужен,
    /// чтобы на них действовал стиль в MainWindow, отключающий стандартную подсветку (фон рисует
    /// карточка строки). Уровень вложенности TreeView вычисляет сам, поэтому ручное свойство Level
    /// из WPF-версии не требуется и удалено.
    /// </summary>
    public class LeveledTreeViewItem : TreeViewItem
    {
        /// <summary>
        /// Тема оформления ищется по типу контрола, а для наследника её в Fluent нет:
        /// без этого шаблон не находится и контрол не отрисовывается вовсе.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TreeViewItem);

        protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new LeveledTreeViewItem();

        private Control? _chevron;

        /// <summary>
        /// Подсказка стрелки раскрытия. Сама стрелка приходит из шаблона Fluent,
        /// поэтому ищется по имени части после его применения. В разметке WPF
        /// подсказка тоже своя на каждое состояние (MainWindow.xaml:1436 и 1439).
        /// </summary>
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _chevron = e.NameScope.Find<Control>("PART_ExpandCollapseChevron");
            if (_chevron is ToggleButton chevron)
                chevron.Theme = ExpanderTheme();
            UpdateChevronTooltip();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsExpandedProperty)
                UpdateChevronTooltip();
        }

        /// <summary>
        /// Оформление кнопки разворота по разметке WPF (MainWindow.xaml:1468-1497):
        /// рамка 22 на 22 со скруглением 5 акцентной кистью, внутри минус у
        /// развёрнутой группы и плюс у свёрнутой, при наведении заливка акцентом
        /// и белый знак. Штатная галка Fluent на её месте была расхождением.
        /// </summary>
        private static ControlTheme ExpanderTheme()
        {
            var theme = new ControlTheme(typeof(ToggleButton))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(Layoutable.WidthProperty, 26d),
                    new Setter(Layoutable.HeightProperty, 26d),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToggleButton>((_, scope) =>
                    {
                        var minus = new Path
                        {
                            Name = "ЗнакРазвёрнуто",
                            Width = 12,
                            Height = 12,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Data = IconHelper.Geometry("IconMinus")
                        };
                        ThemeBrushes.Bind(minus, Path.FillProperty, "AccentBrush");
                        var plus = new Path
                        {
                            Name = "ЗнакСвёрнуто",
                            Width = 12,
                            Height = 12,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            IsVisible = false,
                            Data = IconHelper.Geometry("IconPlus")
                        };
                        ThemeBrushes.Bind(plus, Path.FillProperty, "AccentBrush");

                        var glyphs = new Panel();
                        glyphs.Children.Add(minus);
                        glyphs.Children.Add(plus);

                        var border = new Border
                        {
                            Name = "РамкаРазворота",
                            Width = 22,
                            Height = 22,
                            CornerRadius = new CornerRadius(5),
                            BorderThickness = new Thickness(1.5),
                            Child = glyphs
                        };
                        ThemeBrushes.Bind(border, Border.BackgroundProperty, "CardBackgroundBrush");
                        ThemeBrushes.Bind(border, Border.BorderBrushProperty, "AccentBrush");
                        return border.RegisterInNameScope(scope);
                    }))
                }
            };

            // Свёрнутой группе плюс, развёрнутой минус.
            theme.Add(new Style(x => x.Nesting().Not(y => y.Class(":checked")).Template().Name("ЗнакРазвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) }
            });
            theme.Add(new Style(x => x.Nesting().Not(y => y.Class(":checked")).Template().Name("ЗнакСвёрнуто"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, true) }
            });
            // При наведении заливка акцентом и белый знак, как в разметке.
            theme.Add(new Style(x => x.Nesting().Class(":pointerover").Template().Name("РамкаРазворота"))
            {
                Setters = { new Setter(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentBrush")) }
            });
            foreach (var part in new[] { "ЗнакРазвёрнуто", "ЗнакСвёрнуто" })
                theme.Add(new Style(x => x.Nesting().Class(":pointerover").Template().Name(part))
                {
                    Setters = { new Setter(Path.FillProperty, Brushes.White) }
                });
            return theme;
        }

        private void UpdateChevronTooltip()
        {
            if (_chevron is null)
                return;
            // Подсказка одна и статичная, как в разметке WPF. Раньше здесь были
            // две меняющиеся по состоянию, это расхождение с версией для Windows.
            ToolTip.SetTip(_chevron, Localization.LocalizationManager.T("Main.ExpandCollapseGroup"));
        }
    }
}
#endif