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

namespace Configuration_Management.Services
{
    /// <summary>
    /// Avalonia-версия диалогов (Linux). Реализует <see cref="IDialogService"/> через
    /// собственное модальное окно сообщения (Avalonia не имеет MessageBox из коробки).
    /// Методы интерфейса синхронные, поэтому модальный показ выполняется с помощью
    /// вложенного цикла обработки <see cref="Dispatcher.UIThread.RunJobs"/>.
    /// </summary>
    public sealed class AvaloniaDialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "Информация")
            => ShowMessage(message, title, MessageWindowKind.Info);

        public void ShowWarning(string message, string title = "Внимание")
            => ShowMessage(message, title, MessageWindowKind.Warning);

        public void ShowError(string message, string title = "Ошибка")
            => ShowMessage(message, title, MessageWindowKind.Error);

        public bool Confirm(string message, string title = "Подтверждение")
        {
            var win = new MessageWindow(message, title, MessageWindowKind.Question);
            return ShowModalSync(win);
        }

        public string? OpenFileDialog(string title = "Открыть файл", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickFileAsync(title, filter, initialDirectory));

        public string? SaveFileDialog(string title = "Сохранить файл", string defaultFileName = "", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickSaveFileAsync(title, defaultFileName, filter, initialDirectory));

        public string? OpenFolderDialog(string title = "Выбор папки", string? initialDirectory = null)
            => RunSync(() => PickFolderAsync(title, initialDirectory));

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
            while (!task.IsCompleted)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
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
            if (owner is not null)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = owner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            bool result = true;
            window.Closed += (_, _) => result = window is MessageWindow mw ? mw.Result : true;

            window.Show();

            while (window.IsVisible)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            return result;
        }

        private static Window? CurrentOwner()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }

    internal enum MessageWindowKind { Info, Warning, Error, Question }

    /// <summary>Окно сообщения (MessageBox) для Linux.</summary>
    internal sealed class MessageWindow : Window
    {
        public bool Result { get; private set; } = true;

        public MessageWindow(string message, string title, MessageWindowKind kind)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SystemDecorations = SystemDecorations.Full;

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

            var body = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(4, 8, 4, 8),
                Children =
                {
                    Configuration_Management.IconHelper.MakeIcon(iconKey, 28),
                    messageBlock
                }
            };

            Button okButton = new() { Content = "OK", MinWidth = 90, IsDefault = true };
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
                Button cancelButton = new() { Content = "Отмена", MinWidth = 90, IsCancel = true };
                cancelButton.Click += (_, _) => { Result = false; Close(); };
                buttonsPanel.Children.Insert(0, cancelButton);
            }

            var content = new StackPanel
            {
                Spacing = 16,
                Padding = new Thickness(16),
                Children = { body, buttonsPanel }
            };
            Content = content;
        }
    }
}
#endif