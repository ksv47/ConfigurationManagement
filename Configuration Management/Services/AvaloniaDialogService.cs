#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Configuration_Management.Localization;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Avalonia-версия диалогов (Linux). Реализует <see cref="IDialogService"/> через
    /// собственное модальное окно сообщения (Avalonia не имеет MessageBox из коробки).
    /// Методы интерфейса синхронные, поэтому модальный показ выполняется с помощью
    /// вложенного цикла сообщений <see cref="Dispatcher.PushFrame"/>.
    /// </summary>
    public sealed class AvaloniaDialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Information"), MessageWindowKind.Info);

        public void ShowWarning(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Warning"), MessageWindowKind.Warning);

        public void ShowError(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Error"), MessageWindowKind.Error);

        public bool Confirm(string message, string title = "")
        {
            var win = new MessageWindow(message, DefaultTitle(title, "Common.Confirm"), MessageWindowKind.Question);
            return ShowModalSync(win);
        }

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickFileAsync(DefaultTitle(title, "Dialog.OpenFile"), filter, initialDirectory));

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickSaveFileAsync(DefaultTitle(title, "Dialog.SaveFile"), defaultFileName, filter, initialDirectory));

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null)
            => RunSync(() => PickFolderAsync(DefaultTitle(title, "Dialog.SelectFolder"), initialDirectory));

        /// <summary>Подставляет локализованный заголовок по умолчанию, если переданный пуст.</summary>
        private static string DefaultTitle(string title, string fallbackKey) =>
            string.IsNullOrWhiteSpace(title) ? LocalizationManager.T(fallbackKey) : title;

        // ---- Файловые диалоги через Avalonia StorageProvider ----

        private async Task<string?> PickFileAsync(string title, string filter, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = BuildFileTypes(filter)
            };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var files = await provider.OpenFilePickerAsync(options);
            var file = files.FirstOrDefault();
            return file?.TryGetLocalPath();
        }

        private async Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filter, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "file" : defaultFileName,
                FileTypeChoices = BuildFileTypes(filter)
            };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var file = await provider.SaveFilePickerAsync(options);
            return file?.TryGetLocalPath();
        }

        private async Task<string?> PickFolderAsync(string title, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var folders = await provider.OpenFolderPickerAsync(options);
            var folder = folders.FirstOrDefault();
            return folder?.TryGetLocalPath();
        }

        private static IStorageProvider? CurrentStorageProvider()
        {
            if (CurrentOwner() is TopLevel topLevel)
                return topLevel.StorageProvider;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return TopLevel.GetTopLevel(desktop.MainWindow)?.StorageProvider;
            return null;
        }

        private static async Task<IStorageFolder?> TryGetFolder(IStorageProvider provider, string path)
        {
            try { return await provider.TryGetFolderFromPathAsync(path); }
            catch { return null; }
        }

        /// <summary>
        /// Разбирает фильтр вида "Конфигурация (*.cf)|*.cf|Все файлы (*.*)|*.*" в список
        /// типов для StorageProvider. Пустое значение → все файлы.
        /// </summary>
        private static IReadOnlyList<FilePickerFileType> BuildFileTypes(string filter)
        {
            var result = new List<FilePickerFileType>();
            if (string.IsNullOrWhiteSpace(filter))
            {
                result.Add(FilePickerFileTypes.All);
                return result;
            }

            var parts = filter.Split('|');
            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                var label = parts[i].Trim();
                var patterns = parts[i + 1]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                // Для фильтра «все файлы» (*.*) показываем локализованное название,
                // сам паттерн (расширения) не трогаем.
                if (patterns.Any(p => p.Trim() == "*.*"))
                    label = LocalizationManager.T("Common.AllFiles");
                result.Add(new FilePickerFileType(label) { Patterns = patterns });
            }
            if (result.Count == 0)
                result.Add(FilePickerFileTypes.All);
            return result;
        }

        /// <summary>
        /// Выполняет асинхронную операцию StorageProvider синхронно, попутно обрабатывая
        /// очередь задач Avalonia (аналогично <see cref="ShowModalSync"/>), чтобы не было
        /// взаимоблокировки на UI-потоке.
        /// </summary>
        private static T RunSync<T>(Func<Task<T>> taskFactory)
        {
            var task = taskFactory();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false,
                    TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.UIThread.PushFrame(frame);
            }
            return task.GetAwaiter().GetResult();
        }

        private static void ShowMessage(string message, string title, MessageWindowKind kind)
            => ShowModalSync(new MessageWindow(message, title, kind));

        /// <summary>
        /// Показывает окно модально и блокирует вызывающий поток до его закрытия,
        /// попутно обрабатывая очередь задач Avalonia (эмуляция синхронного ShowDialog).
        /// </summary>
        private static bool ShowModalSync(Window window)
        {
            var owner = CurrentOwner();
            bool result = true;
            var frame = new DispatcherFrame();
            window.Closed += (_, _) =>
            {
                result = window is MessageWindow mw ? mw.Result : true;
                frame.Continue = false;
            };

            if (owner is not null)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _ = window.ShowDialog(owner);
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.Show();
            }

            Dispatcher.UIThread.PushFrame(frame);

            return result;
        }

        private static Window? CurrentOwner()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;

            // Владельцем берём активное окно: диалог, вызванный из модального
            // окна настроек, иначе принадлежал бы уже заблокированному главному
            // окну, и модальность с фокусом на Linux вели бы себя странно.
            foreach (var window in desktop.Windows)
            {
                if (window.IsActive)
                    return window;
            }

            return desktop.MainWindow;
        }
    }

    internal enum MessageWindowKind { Info, Warning, Error, Question }

    /// <summary>Окно сообщения (MessageBox) для Linux.</summary>
    internal sealed class MessageWindow : Window
    {
        /// <summary>
        /// Ответ пользователя. У вопроса значение по умолчанию отрицательное:
        /// закрытие крестиком или Alt+F4 не должно означать согласие, а Confirm
        /// спрашивают перед удалением базы, группы и цветовой схемы.
        /// </summary>
        public bool Result { get; private set; }

        /// <summary>
        /// Подписка иконки на ресурс темы. Наблюдатель держит сильную ссылку
        /// на элемент, а ресурс живёт у приложения, поэтому без освобождения
        /// каждое показанное сообщение оставалось бы в памяти навсегда.
        /// </summary>
        private readonly System.Collections.Generic.List<IDisposable> _themeSubscriptions = new();

        /// <summary>Запоминает подписку на ресурс темы, если она была создана.</summary>
        private void TrackTheme(IDisposable? subscription)
        {
            if (subscription is not null)
                _themeSubscriptions.Add(subscription);
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (var subscription in _themeSubscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* освобождение подписки не должно мешать закрытию окна */ }
            }
            _themeSubscriptions.Clear();
            base.OnClosed(e);
        }

        public MessageWindow(string message, string title, MessageWindowKind kind)
        {
            // Сообщение без выбора подтверждать нечего: там ответ всегда
            // положительный, каким бы способом окно ни закрыли.
            Result = kind != MessageWindowKind.Question;

            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SystemDecorations = SystemDecorations.Full;

            // Фон красит само окно, а не содержимое: у содержимого есть отступ,
            // и полоса по периметру осталась бы фоном Window от Fluent.
            // Диалоги WPF-версии красят Window по той же причине.
            TrackTheme(Themes.ThemeBrushes.Bind(this, Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush"));

            var iconKey = kind switch
            {
                MessageWindowKind.Info => "IconInfo",
                MessageWindowKind.Warning => "IconWarning",
                MessageWindowKind.Error => "IconError",
                _ => "IconUnknown"
            };

            var messageBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            TrackTheme(Themes.ThemeBrushes.Bind(messageBlock, TextBlock.ForegroundProperty, "TextPrimaryColorBrush"));

            // Сетка, а не горизонтальный StackPanel: в стопке текст получает
            // бесконечную ширину и не переносится, длинное сообщение обрезается.
            var body = new Grid { Margin = new Thickness(4, 8, 4, 8) };
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var messageIcon = Configuration_Management.IconHelper.MakeIcon(iconKey, 28, subscriptions: _themeSubscriptions);
            messageIcon.Margin = new Thickness(0, 0, 12, 0);
            messageIcon.VerticalAlignment = VerticalAlignment.Top;
            body.Children.Add(messageIcon);
            Grid.SetColumn(messageBlock, 1);
            body.Children.Add(messageBlock);

            var okText = kind == MessageWindowKind.Question
                ? LocalizationManager.T("Common.Yes")
                : LocalizationManager.T("Common.Ok");
            Button okButton = new() { Content = okText, MinWidth = 90, IsDefault = true };
            TrackTheme(Themes.ThemeBrushes.Bind(okButton, Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, "AccentColorBrush"));
            TrackTheme(Themes.ThemeBrushes.Bind(okButton, Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, "TextOnAccentColorBrush"));
            okButton.Click += (_, _) => { Result = true; Close(); };

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };
            buttonsPanel.Children.Add(okButton);

            if (kind == MessageWindowKind.Question)
            {
                Button cancelButton = new() { Content = LocalizationManager.T("Common.No"), MinWidth = 90, IsCancel = true };
                TrackTheme(Themes.ThemeBrushes.Bind(cancelButton, Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, "SecondaryButtonBackgroundColorBrush"));
                TrackTheme(Themes.ThemeBrushes.Bind(cancelButton, Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, "ButtonTextColorBrush"));
                cancelButton.Click += (_, _) => { Result = false; Close(); };
                buttonsPanel.Children.Insert(0, cancelButton);
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
}
#endif