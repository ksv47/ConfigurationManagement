#if LINUX
using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Configuration_Management
{
    /// <summary>
    /// База модальных диалоговых окон (Avalonia/Linux). Предоставляет синхронный показ
    /// модального окна с эмуляцией <c>DialogResult</c>, как в <see cref="AvaloniaDialogService"/>:
    /// модальный цикл обрабатывает очередь задач <see cref="Dispatcher.UIThread.RunJobs"/>,
    /// пока окно открыто. Это позволяет вызывать диалоги синхронно из команд ViewModel,
    /// не блокируя UI-поток.
    /// </summary>
    public abstract class ModalWindowBase : Window
    {
        /// <summary>Результат диалога: true — подтверждён (ОК), false — отменён.</summary>
        public bool DialogResult { get; protected set; }

        /// <summary>
        /// Показывает окно модально (синхронно) относительно владельца и блокирует
        /// вызывающий поток до закрытия окна.
        /// </summary>
        /// <param name="owner">Окно-владелец (например, главное). Может быть null.</param>
        /// <returns>True, если пользователь подтвердил действие (DialogResult == true).</returns>
        protected bool ShowDialogSync(Window? owner = null)
        {
            if (owner is not null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Owner = owner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Show();

            while (IsVisible)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            return DialogResult;
        }

        /// <summary>
        /// Показывает окно модально (синхронно) без владельца.
        /// </summary>
        protected bool ShowDialogSync() => ShowDialogSync(null);

        /// <summary>
        /// Строит стандартный ряд кнопок «Отмена»/«ОК» с иконками и обработчиками.
        /// При нажатии «ОК» сначала выполняется <paramref name="onOk"/> (если задан),
        /// затем устанавливается <see cref="DialogResult"/> и окно закрывается.
        /// </summary>
        /// <param name="okText">Текст кнопки подтверждения.</param>
        /// <param name="okWidth">Ширина кнопки подтверждения.</param>
        /// <param name="onOk">Необязательный обратный вызов при подтверждении (например, сохранить результат).</param>
        protected StackPanel BuildButtons(string okText = "ОК", double okWidth = 130, Action? onOk = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            var cancel = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        IconHelper.MakeIcon("IconClose", 14),
                        new TextBlock { Text = "Отмена", VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                MinWidth = 110,
                IsCancel = true
            };
            cancel.Click += (_, _) => Close();
            panel.Children.Add(cancel);

            var ok = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        IconHelper.MakeIcon("IconOk", 14),
                        new TextBlock { Text = okText, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                MinWidth = okWidth,
                IsDefault = true
            };
            ok.Click += (_, _) =>
            {
                onOk?.Invoke();
                DialogResult = true;
                Close();
            };
            panel.Children.Add(ok);

            return panel;
        }
    }
}
#endif