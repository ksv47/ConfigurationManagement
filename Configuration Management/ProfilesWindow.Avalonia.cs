#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Configuration_Management.Localization;
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
        private readonly TextBox _nameBox;
        private readonly PasswordBox _passwordInput;
        private readonly CheckBox _protectCheck;
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
            _nameBox = new TextBox { Padding = new Thickness(8, 6), MinWidth = 280 };
            nameRow.Children.Add(_nameBox);
            panel.Children.Add(nameRow);

            var passRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            passRow.Children.Add(new TextBlock { Text = LocalizationManager.T("Profiles.Password"), VerticalAlignment = VerticalAlignment.Center, Width = 100 });
            _passwordInput = new PasswordBox { Padding = new Thickness(8, 6), MinWidth = 280 };
            passRow.Children.Add(_passwordInput);
            panel.Children.Add(passRow);

            _protectCheck = new CheckBox { Content = LocalizationManager.T("Profiles.ProtectWithPassword"), Margin = new Thickness(108, 0, 0, 0) };
            panel.Children.Add(_protectCheck);

            return panel;
        }

        private StackPanel BuildButtons()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

            var delete = new Button { Content = LocalizationManager.T("Profiles.Delete"), MinWidth = 110 };
            delete.Click += (_, _) => OnDelete();
            panel.Children.Add(delete);

            var save = new Button { Content = LocalizationManager.T("Profiles.Save"), MinWidth = 130 };
            save.Click += (_, _) => OnSave();
            panel.Children.Add(save);

            var create = new Button { Content = LocalizationManager.T("Profiles.Create"), MinWidth = 130 };
            create.Click += (_, _) => OnCreate();
            panel.Children.Add(create);

            return panel;
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