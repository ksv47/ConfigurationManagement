#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Configuration_Management.Controls;
using Configuration_Management.ViewModels;

namespace Configuration_Management
{
    /// <summary>
    /// Горячие клавиши главного окна (Avalonia/Linux): регистрация привязок,
    /// обработка нажатий на клавиатуре и вспомогательные проверки открытых диалогов.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Горячие клавиши действий. Сочетания берутся из вьюмодели, оттуда же
        /// их показывают подсказки и контекстное меню, поэтому список и подписи
        /// не расходятся.
        /// Важно про порядок: в Avalonia привязки окна проверяются раньше, чем
        /// клавишу получит элемент с фокусом, в отличие от WPF. Ни одно из этих
        /// сочетаний не совпадает с правкой текста, поэтому ввод в поле поиска
        /// они не задевают, но добавлять сюда Ctrl+C, Ctrl+V и подобное нельзя:
        /// они отберут клавишу у поля ввода. Delete по этой же причине живёт
        /// в отдельном обработчике с проверкой фокуса, а не здесь.
        /// </summary>
        private void RegisterHotkeys()
        {
            if (_vm is null)
                return;

            KeyBindings.Clear();

            // Alt+1…Alt+9 запускают избранные базы по порядку слотов и ставятся
            // ПЕРЕД пользовательскими: Avalonia перебирает привязки по порядку
            // списка и останавливается на первой подошедшей, поэтому иначе
            // назначенный пользователем Alt+1 перебивал бы избранное. Версия
            // для Windows добивается того же с другого конца: там
            // RegisterFavoriteHotkeys сперва удаляет из InputBindings все
            // Alt+1…9, включая пользовательские (MainWindow.Hotkeys.cs:118).
            // Незанятый слот привязку всё равно имеет и клавишу поглощает,
            // но действия не выполняет: так же ведёт себя и версия для Windows.
            for (var number = 1; number <= 9; number++)
            {
                var slot = number;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = new KeyGesture((Key)((int)Key.D0 + slot), KeyModifiers.Alt),
                    Command = new ViewModels.RelayCommand(_ => _vm.LaunchFavoriteByHotkey(slot))
                });
            }

            // Delete в привязки не идёт: он правит текст, и в поле ввода
            // не должен удалять базу. Ему отдельный обработчик ниже.
            AddHotkey(_vm.HotkeyEnterprise, _vm.LaunchEnterpriseCommand);
            AddHotkey(_vm.HotkeyConfigurator, _vm.LaunchConfiguratorCommand);
            AddHotkey(_vm.HotkeyEdit, _vm.EditInfobaseCommand);
            AddHotkey(_vm.HotkeyAdd, _vm.AddInfobaseCommand);
            AddHotkey(_vm.HotkeyFavorite, _vm.ToggleFavoriteCommand);
            AddHotkey(_vm.HotkeyPin, _vm.TogglePinCommand);
            AddHotkey(_vm.HotkeyClearCache, _vm.ClearCacheCommand);
            // Переключение режимов списка баз: Все, Избранное, Недавние.
            AddHotkey(_vm.HotkeyShowAll, _vm.ShowAllCommand);
            AddHotkey(_vm.HotkeyShowFavorites, _vm.ShowFavoritesCommand);
            AddHotkey(_vm.HotkeyShowRecent, _vm.ShowRecentCommand);

            // Очистка строки поиска и сброс фильтра тегов — настраиваемые хоткеи (issue #160),
            // значения по умолчанию Ctrl+Shift+C / Ctrl+Shift+T задаются в настройках.
            // Добавляются ПОСЛЕ пользовательских, чтобы назначенные пользователем
            // сочетания имели приоритет.
            AddHotkey(_vm.HotkeyClearSearch, _vm.ClearSearchCommand);
            AddHotkey(_vm.HotkeyClearTags, _vm.ClearTagFiltersCommand);
            // Переключение подробностей правой панели информации — настраиваемый хоткей (issue #172);
            // значение по умолчанию Ctrl+D задаётся в настройках.
            AddHotkey(_vm.HotkeyRightPanelDetails, _vm.ToggleRightPanelDetailsCommand);
            // Смена пользователя — настраиваемый хоткей (issue #200).
            AddHotkey(_vm.HotkeySwitchUser, _vm.SwitchUserCommand);
            // Ctrl+Shift+Plus / Ctrl+Shift+Minus — развернуть/свернуть все узлы дерева.
            // Регистрируются обе раскладки (основная клавиатура Oem* и цифровой блок Add/Subtract).
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift),
                Command = _vm.ExpandAllGroupsCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Add, KeyModifiers.Control | KeyModifiers.Shift),
                Command = _vm.ExpandAllGroupsCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.OemMinus, KeyModifiers.Control | KeyModifiers.Shift),
                Command = _vm.CollapseAllGroupsCommand
            });
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Subtract, KeyModifiers.Control | KeyModifiers.Shift),
                Command = _vm.CollapseAllGroupsCommand
            });
        }

        /// <summary>
        /// Удаление базы по назначенному сочетанию. В общие привязки оно
        /// не идёт, потому что по умолчанию это Delete: клавиша текстовая,
        /// и в поле ввода она должна править текст, а не удалять базу.
        /// </summary>
        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || _vm is null)
                return;

            // Ctrl+Shift++ / Ctrl+Shift+- — «развернуть все» / «свернуть все» (issue #160).
            // Дублируем назначенные в RegisterHotkeys KeyBindings надёжным явным разбором:
            // KeyBinding/KeyGesture на части раскладок и при разном состоянии фокуса
            // срабатывают только со второго нажатия. Прямой вызов тех же команд, что и у
            // кнопок верхней панели, делает хоткей детерминированным с первого нажатия.
            // Если привязка уже обработала жест (e.Handled == true), сюда не доходим —
            // повторного срабатывания нет.
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 &&
                (e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                if (e.Key is Key.OemPlus or Key.Add)
                {
                    _vm.ExpandAllGroupsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (e.Key is Key.OemMinus or Key.Subtract)
                {
                    _vm.CollapseAllGroupsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // Esc при открытом диалоге закрывает сам диалог. Пока пользователь не
            // кликнул внутри диалога, событие приходит именно сюда: сфокусированной
            // остаётся кнопка главного окна, которой диалог и открыли, а клавиатурное
            // событие Avalonia ведёт вверх по дереву от сфокусированного элемента и
            // маршрута диалога не задевает вовсе (issue #226). После клика внутри
            // маршрут идёт через диалог, и Esc обрабатывает его собственный OnKeyDown.
            // Фокус в диалог не переносим: первым элементом обхода в безрамочном окне
            // оказывается кнопка «Свернуть» собственной полосы заголовка, и рамка
            // фокуса вставала бы на неё.
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None
                && TopmostModalDialog() is { } dialog)
            {
                dialog.CloseAsCancel();
                e.Handled = true;
                return;
            }

            // Esc уводит окно в трей, если так задано настройкой. В поле ввода
            // клавиша остаётся своей: там ей отменяют правку.
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None
                && _vm.EscapeToTray && _vm.ShowTrayIcon && CanRestoreHiddenWindow
                && FocusManager?.GetFocusedElement() is not TextBox
                // При открытом модальном диалоге (свойства базы, настройки) Esc
                // должен закрывать только сам диалог, а не уводить главное окно
                // в трей (issue #226).
                && !HasOpenModalDialog())
            {
                SaveWindowLayout();
                _vm.PersistSettings();
                ApplyTrayVisibility();
                Hide();
                e.Handled = true;
                return;
            }
            if (!Controls.HotkeyBox.TryParse(_vm.HotkeyDelete, out var gesture) || gesture is null)
                return;
            if (e.Key != gesture.Key || e.KeyModifiers != gesture.KeyModifiers)
                return;

            // Только для текстовых клавиш без модификаторов: назначенное F8
            // должно работать и в поле ввода, как любая другая горячая клавиша.
            var isTextEditingKey = gesture.KeyModifiers == KeyModifiers.None
                && gesture.Key is Key.Delete or Key.Back or Key.Insert;
            if (isTextEditingKey && FocusManager?.GetFocusedElement() is TextBox)
                return;

            if (_vm.DeleteInfobaseCommand.CanExecute(null))
                _vm.DeleteInfobaseCommand.Execute(null);
            e.Handled = true;
        }

        /// <summary>
        /// Верхнее по Z-порядку открытое окно, если это диалог, унаследованный от
        /// <see cref="ModalWindowBase"/>, иначе <c>null</c>. Порядок берём у платформы
        /// (<see cref="Window.SortWindowsByZOrder"/>), а не порядок открытия: у окон
        /// без отношения владения он последнему открытому не равен. Если сверху лежит
        /// окно другого рода (сообщение, ход обновления), метод возвращает <c>null</c>:
        /// закрывать вместо него диалог под ним нельзя.
        /// </summary>
        private ModalWindowBase? TopmostModalDialog()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return null;

            var visible = new List<Window>();
            foreach (var window in desktop.Windows)
            {
                if (!ReferenceEquals(window, this) && window.IsVisible)
                    visible.Add(window);
            }

            if (visible.Count == 0)
                return null;

            var ordered = visible.ToArray();
            Window.SortWindowsByZOrder(ordered);
            return ordered[ordered.Length - 1] as ModalWindowBase;
        }

        /// <summary>
        /// Есть ли открытый модальный дочерний диалог (свойства базы, настройки и т.п.).
        /// Все дополнительные окна в приложении показываются модально (ShowDialog/
        /// ShowDialogSync). Проверяем флаг IsVisible, а не IsActive: на Linux/X11 окно
        /// после открытия не всегда сразу получает активацию (issue #226), и по одному
        /// лишь IsActive мы бы не распознали открытый диалог — тогда Esc уводил бы главное
        /// окно в трей, не закрыв диалог. Если видимо любое окно, кроме главного, Esc
        /// должен обработать сам диалог (см. ModalWindowBase.OnKeyDown), а не главное окно.
        /// Закрытые окна в списке имеют IsVisible == false и на результат не влияют.
        /// </summary>
        private bool HasOpenModalDialog()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                    is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return false;

            foreach (var window in desktop.Windows)
            {
                if (!ReferenceEquals(window, this) && window.IsVisible)
                    return true;
            }

            return false;
        }

        private void AddHotkey(string? gesture, System.Windows.Input.ICommand? command)
        {
            if (command is null || !Controls.HotkeyBox.TryParse(gesture, out var parsed) || parsed is null)
                return;
            KeyBindings.Add(new KeyBinding { Gesture = parsed, Command = command });
        }
    }
}
#endif