#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
    /// Управление учётными записями (профилями): создание, переименование, удаление,
    /// установка и снятие пароля. Каждый профиль хранит собственные настройки и список баз.
    /// </summary>
    public class ProfilesWindow : ModalWindowBase
    {
        private readonly IProfileService _profileService;
        private readonly TextBlock _currentAccountLabel;
        private readonly ListBox _profilesList;
        private readonly TextBox _nameBox = new TextBox { Width = 260 }.Styled(Themes.ControlThemes.ModernTextBox);
        private readonly PasswordBox _passwordInput =
            new PasswordBox { Width = 260 }.Styled(Themes.ControlThemes.ModernPasswordBox);
        private readonly CheckBox _protectCheck = new() { Content = LocalizationManager.T("Profiles.ProtectWithPassword"), Margin = new Thickness(108, 0, 0, 0) };
        private readonly TextBlock _errorLabel;
        private bool _suppressSelection;

        public ProfilesWindow(IProfileService profileService)
        {
            _profileService = profileService;
            Title = LocalizationManager.T("Profiles.Title");
            Width = 560;
            Height = 520;
            MinWidth = 500;
            CanResize = false;
            SystemDecorations = SystemDecorations.Full;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            _currentAccountLabel = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(_currentAccountLabel, 0);

            var hint = new TextBlock { Text = LocalizationManager.T("Profiles.RestartHint"), TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(hint, 1);

            _profilesList = new ListBox { Margin = new Thickness(0, 0, 0, 12), DisplayMemberBinding = new Avalonia.Data.Binding("Name") };
            _profilesList.SelectionChanged += (_, _) => OnSelectionChanged();
            Grid.SetRow(_profilesList, 2);

            var editor = BuildEditor();
            Grid.SetRow(editor, 3);

            _errorLabel = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(_errorLabel, 4);

            var buttons = BuildButtons();
            Grid.SetRow(buttons, 5);

            grid.Children.Add(_currentAccountLabel);
            grid.Children.Add(hint);
            grid.Children.Add(_profilesList);
            grid.Children.Add(editor);
            grid.Children.Add(_errorLabel);
            grid.Children.Add(buttons);

            Content = grid;

            RefreshList();
        }

        private StackPanel BuildEditor()
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Profiles.Name"), VerticalAlignment = VerticalAlignment.Center, Width = 100 });
            nameRow.Children.Add(_nameBox);
            panel.Children.Add(nameRow);

            var passRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            passRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Profiles.Password"), VerticalAlignment = VerticalAlignment.Center, Width = 100 });
            passRow.Children.Add(_passwordInput);
            panel.Children.Add(passRow);

            panel.Children.Add(_protectCheck);

            return panel;
        }

        private Control BuildButtons()
        {
            // Раскладка по разметке (ProfilesWindow.xaml:161): отмена прижата
            // влево, остальные кнопки вправо. Удаление, как и отмена, идёт мягкой
            // заливкой, три остальные акцентом.
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

            var delete = SoftButton("Profiles.Delete", "IconDelete", 120);
            delete.Click += (_, _) => OnDelete();
            panel.Children.Add(delete);

            // Кнопка активации стоит между удалением и сохранением, как в разметке
            // (ProfilesWindow.xaml:183). Без неё сменить активный профиль
            // из интерфейса было нельзя вовсе: команда живёт в ProfilesViewModel,
            // а тот в Linux-сборку не входит.
            var activate = AccentButton("Profiles.Activate", "IconStar", 150);
            activate.Click += (_, _) => OnActivate();
            panel.Children.Add(activate);

            var save = AccentButton("Profiles.Save", "IconOk", 130);
            save.Click += (_, _) => OnSave();
            panel.Children.Add(save);

            var create = AccentButton("Profiles.Create", "IconAdd", 130);
            create.Click += (_, _) => OnCreate();
            panel.Children.Add(create);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var cancel = BuildCancelButton(110);
            Grid.SetColumn(cancel, 0);
            row.Children.Add(cancel);

            Grid.SetColumn(panel, 1);
            row.Children.Add(panel);
            return row;
        }

        /// <summary>Кнопка акцентной заливки со значком и подписью.</summary>
        private static Button AccentButton(string textKey, string iconKey, double width)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, LocalizationManager.T(textKey), 14, "ButtonTextBrush"),
                Width = width
            };
            button.Styled(Themes.ControlThemes.ModernButton);
            return button;
        }

        /// <summary>Кнопка мягкой заливки: в разметке ей задан фон ItemHoverBrush.</summary>
        private static Button SoftButton(string textKey, string iconKey, double width)
        {
            var button = new Button
            {
                Content = IconHelper.IconAndText(iconKey, LocalizationManager.T(textKey), 14, "TextPrimaryBrush"),
                Width = width
            };
            button.Styled(Themes.ControlThemes.ModernButton);
            Themes.ThemeBrushes.Bind(button, Button.BackgroundProperty, "ItemHoverBrush");
            Themes.ThemeBrushes.Bind(button, Button.ForegroundProperty, "TextPrimaryBrush");
            return button;
        }

        private UserProfile? SelectedProfile => _profilesList.SelectedItem as UserProfile;

        private void ApplyCurrentAccountLabel()
        {
            var name = _profileService.CurrentProfile?.Name ?? "-";
            _currentAccountLabel.Text = string.Format(LocalizationManager.T("Profiles.CurrentAccount"), name);
        }

        private void RefreshList(string? selectId = null)
        {
            _suppressSelection = true;
            _profilesList.ItemsSource = _profileService.Profiles.ToList();

            selectId ??= _profileService.CurrentProfile?.Id;
            var target = _profileService.Profiles.FirstOrDefault(p => p.Id == selectId);
            _profilesList.SelectedItem = target;
            _suppressSelection = false;

            if (target == null)
                ClearEditor();
            else
                LoadEditor(target);
            ApplyCurrentAccountLabel();
        }

        private void OnSelectionChanged()
        {
            if (_suppressSelection)
                return;
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
                ClearEditor();
            else
                LoadEditor(profile);
        }

        private void LoadEditor(UserProfile profile)
        {
            _nameBox.Text = profile.Name;
            _protectCheck.IsChecked = profile.HasPassword;
            _passwordInput.Clear();
        }

        private void ClearEditor()
        {
            _nameBox.Text = string.Empty;
            _protectCheck.IsChecked = false;
            _passwordInput.Clear();
        }

        private void ShowError(string message)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
        }

        private void OnCreate()
        {
            _errorLabel.IsVisible = false;
            var name = _nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                var password = _protectCheck.IsChecked == true ? _passwordInput.Password : null;
                var created = _profileService.CreateProfile(name, password);
                RefreshList(created.Id);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnSave()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Profiles.EmptySelection"));
                return;
            }

            var name = _nameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(LocalizationManager.T("Profiles.EmptyName"));
                return;
            }

            try
            {
                if (!string.Equals(profile.Name, name, StringComparison.Ordinal))
                    _profileService.RenameProfile(profile.Id, name);

                var wantPassword = _protectCheck.IsChecked == true;
                if (wantPassword && !string.IsNullOrEmpty(_passwordInput.Password))
                    _profileService.SetPassword(profile.Id, _passwordInput.Password);
                else if (!wantPassword && profile.HasPassword)
                    _profileService.SetPassword(profile.Id, null);

                RefreshList(profile.Id);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        /// <summary>Делает выбранный профиль активным и обновляет список с подписью.</summary>
        private void OnActivate()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
            {
                ShowError(LocalizationManager.T("Profiles.NoSelectionToActivate"));
                return;
            }

            try
            {
                _profileService.SetCurrentProfile(profile.Id);
                RefreshList(profile.Id);
                ApplyCurrentAccountLabel();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void OnDelete()
        {
            _errorLabel.IsVisible = false;
            var profile = SelectedProfile;
            if (profile == null)
                return;

            var confirm = string.Format(LocalizationManager.T("Profiles.DeleteConfirm"), profile.Name);
            var dialog = AppServices.GetRequiredService<IDialogService>();
            if (!dialog.Confirm(confirm, LocalizationManager.T("Profiles.Title")))
                return;

            if (!_profileService.DeleteProfile(profile.Id))
            {
                ShowError(LocalizationManager.T("Profiles.CantDeleteLast"));
                return;
            }

            RefreshList();
        }
    }
}
#endif