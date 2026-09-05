#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
using Configuration_Management.Themes;

namespace Configuration_Management.Services;

/// <summary>
/// Всплывающее окно сообщения в стиле Material Design (Linux/Avalonia).
/// Реализует <see cref="MaterialMessageKind"/> через программно построенное
/// окно с иконкой, текстом и кнопками (аналогично WPF-версии MaterialMessageWindow).
/// </summary>
internal sealed class MaterialMessageWindowAvalonia : Window
{
    public bool Confirmed { get; private set; } = true;

    /// <summary>Ответ дан кнопкой окна, а не закрытием через оконный менеджер.</summary>
    private bool _answered;

    public MaterialMessageWindowAvalonia(string message, string title, MaterialMessageKind kind)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;

        ThemeBrushes.Bind(this, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");

        // Иконка типа сообщения.
        var iconKey = kind switch
        {
            MaterialMessageKind.Warning => "IconWarning",
            MaterialMessageKind.Error => "IconError",
            MaterialMessageKind.Question => "IconUnknown",
            _ => "IconInfo"
        };
        var messageIcon = Configuration_Management.IconHelper.MakeIcon(iconKey, 28);
        messageIcon.Margin = new Thickness(0, 0, 12, 0);
        messageIcon.VerticalAlignment = VerticalAlignment.Top;

        // Текст.
        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeBrushes.Bind(messageBlock, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        // Тело: сетка, чтобы длинный текст переносился.
        var body = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        body.Children.Add(messageIcon);
        Grid.SetColumn(messageBlock, 1);
        body.Children.Add(messageBlock);

        // Кнопки. Подтверждение («Да»/«ОК») — явно зелёная с жирным белым текстом,
        // как в WPF-версии: не зависит от акцентной кисти темы и читается всегда.
        var okText = kind == MaterialMessageKind.Question
            ? LocalizationManager.T("Common.Yes")
            : LocalizationManager.T("Common.Ok");
        Button okButton = new()
        {
            Content = new TextBlock
            {
                Text = okText,
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            },
            MinWidth = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.Parse("#16A34A")),
            Foreground = Brushes.White
        };
        okButton.Click += (_, _) => { _answered = true; Confirmed = true; Close(); };

        // Порядок кнопок согласован с WPF-версией: «Да» (подтверждение) слева,
        // «Нет» (отмена) справа.
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonsPanel.Children.Add(okButton);

        if (kind == MaterialMessageKind.Question)
        {
            Button cancelButton = new()
            {
                Content = LocalizationManager.T("Common.No"),
                MinWidth = 90,
                IsCancel = true
            };
            ThemeBrushes.Bind(cancelButton, TemplatedControl.BackgroundProperty, "SecondaryButtonBackgroundColorBrush");
            ThemeBrushes.Bind(cancelButton, TemplatedControl.ForegroundProperty, "ButtonTextColorBrush");
            cancelButton.Click += (_, _) => { _answered = true; Confirmed = false; Close(); };

            // Закрытие вопроса крестиком оконного менеджера равнозначно ответу «Нет».
            // Иначе окно возвращает согласие, которого пользователь не давал.
            Closing += (_, _) =>
            {
                if (!_answered)
                    Confirmed = false;
            };
            buttonsPanel.Children.Add(cancelButton);
        }

        var content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16),
            Children = { body, buttonsPanel }
        };

        Content = content;
    }
}
#endif