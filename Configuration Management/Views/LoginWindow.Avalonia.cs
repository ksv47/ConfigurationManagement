#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Controls;
using Configuration_Management.Localization;
using Configuration_Management.Themes;
using Configuration_Management.Models;
using Configuration_Management.Services;

namespace Configuration_Management
{
    /// <summary>
    /// Окно выбора учётной записи (профиля) по аналогии со списком пользователей 1С.
    /// Показывается при запуске, если в приложении создано несколько профилей.
    /// Для профиля с паролем запрашивается пароль; для незащищённого — вход сразу.
    /// </summary>
    public class LoginWindow : ModalWindowBase
    {
        private readonly IProfileService _profileService;
        private readonly ListBox _profilesList;
        private readonly StackPanel _passwordPanel;
        private readonly PasswordBox _passwordInput;
        private readonly TextBlock _errorLabel;

        /// <summary>Идентификатор выбранного профиля (null, если вход не выполнен).</summary>
        public string? SelectedProfileId { get; private set; }

        /// <summary>
        /// Показывает окно авторизации и, если пользователь вошёл, возвращает идентификатор
        /// выбранного профиля; иначе — null (приложение завершает работу).
        /// </summary>
        public static string? ShowLogin(IProfileService profileService)
        {
            var window = new LoginWindow(profileService);
            window.ShowDialogSync();
            return window.SelectedProfileId;
        }

        public LoginWindow(IProfileService profileService)
        {
            _profileService = profileService;
            Title = LocalizationManager.T("Auth.Title");
            Width = 460;
            Height = 360;
            MinWidth = 400;
            CanResize = false;
            SystemDecorations = SystemDecorations.Full;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var hint = new TextBlock
            {
                Text = LocalizationManager.T("Auth.SelectAccountHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 0.85
            };
            Grid.SetRow(hint, 0);

            _profilesList = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                ItemsSource = profileService.Profiles,
                DisplayMemberBinding = new Avalonia.Data.Binding("Name")
            };
            _profilesList.SelectionChanged += (_, _) => OnSelectionChanged();
            Grid.SetRow(_profilesList, 1);

            _passwordPanel = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 0, 0, 12),
                IsVisible = false
            };
            _passwordPanel.Children.Add(new TextBlock { Text = LocalizationManager.T("Auth.Password") });
            _passwordInput = new PasswordBox();
            _passwordInput.Styled(ControlThemes.ModernPasswordBox);
            _passwordInput.KeyDown += OnPasswordInput_KeyDown;
            _passwordPanel.Children.Add(_passwordInput);
            Grid.SetRow(_passwordPanel, 2);

            _errorLabel = new TextBlock
            {
                Foreground = Brushes.IndianRed,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                IsVisible = false
            };
            Grid.SetRow(_errorLabel, 3);

            var buttons = BuildLoginButtons();
            Grid.SetRow(buttons, 4);

            grid.Children.Add(hint);
            grid.Children.Add(_profilesList);
            grid.Children.Add(_passwordPanel);
            grid.Children.Add(_errorLabel);
            grid.Children.Add(buttons);

            Content = grid;

            Opened += (_, _) =>
            {
                if (_profilesList.Items.Count > 0)
                    _profilesList.SelectedIndex = 0;
                ApplySelectionState();
            };
        }

        private UserProfile? SelectedProfile =>
            _profilesList.SelectedItem as UserProfile;

        private void OnSelectionChanged()
        {
            _errorLabel.IsVisible = false;
            ApplySelectionState();
        }

        private void ApplySelectionState()
        {
            var hasPassword = SelectedProfile?.HasPassword == true;
            _passwordPanel.IsVisible = hasPassword;
            if (hasPassword)
            {
                _passwordInput.Focus();
                _passwordInput.Clear();
            }
        }

        private void OnPasswordInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryLogin();
                e.Handled = true;
            }
        }

        private StackPanel BuildLoginButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            // Оформление по разметке (LoginWindow.xaml:62): обе кнопки в стиле
            // основной, у отмены заливка мягкая, у входа акцентная.
            var cancel = BuildCancelButton(110);
            panel.Children.Add(cancel);

            var loginCaption = new TextBlock
            {
                Text = LocalizationManager.T("Auth.Login"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var login = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { IconHelper.MakeIcon("IconOk", 14, "ButtonTextBrush"), loginCaption }
                },
                Width = 130,
                IsDefault = true
            };
            login.Styled(ControlThemes.ModernButton);
            RegisterConfirmCaption(loginCaption, "Auth.Login");
            login.Click += (_, _) => TryLogin();
            panel.Children.Add(login);

            return panel;
        }

        private void TryLogin()
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Auth.EmptySelection"));
                return;
            }

            if (profile.HasPassword && !_profileService.VerifyPassword(profile.Id, _passwordInput.Password))
            {
                ShowError(LocalizationManager.T("Auth.WrongPassword"));
                _passwordInput.Clear();
                _passwordInput.Focus();
                return;
            }

            SelectedProfileId = profile.Id;
            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
        }
    }
}
#endif