#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Configuration_Management.Localization;
using Configuration_Management.Models;
using Configuration_Management.Services;
using Configuration_Management.Themes;
using Configuration_Management.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Configuration_Management
{
    public partial class MainWindow
    {

        /// <summary>
        /// Регистрирует настраиваемые горячие клавиши действий (запуск, правка, удаление и т.д.).
        /// </summary>
        private void RegisterLaunchHotkeys()
        {
            // Удаляем ранее зарегистрированные «пользовательские» биндинги (кроме Alt+1…9).
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Command is not null &&
                             kb.Modifiers != ModifierKeys.Alt)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            void Add(string? gesture, ICommand? command)
            {
                if (command is null) return;
                if (!TryParseKeyGesture(gesture, out var key, out var mods)) return;
                InputBindings.Add(new KeyBinding(command, key, mods));
            }

            Add(_viewModel.HotkeyEnterprise, _viewModel.LaunchEnterpriseCommand);
            Add(_viewModel.HotkeyConfigurator, _viewModel.LaunchConfiguratorCommand);
            Add(_viewModel.HotkeyFavorite, _viewModel.ToggleFavoriteCommand);
            Add(_viewModel.HotkeyEdit, _viewModel.EditInfobaseCommand);
            Add(_viewModel.HotkeyDelete, _viewModel.DeleteInfobaseCommand);
            Add(_viewModel.HotkeyClearCache, _viewModel.ClearCacheCommand);
            Add(_viewModel.HotkeyAdd, _viewModel.AddInfobaseCommand);
            Add(_viewModel.HotkeyPin, _viewModel.TogglePinCommand);
            // Переключение вкладок списка баз: Все / Избранное / Недавние.
            Add(_viewModel.HotkeyShowAll, _viewModel.ShowAllCommand);
            Add(_viewModel.HotkeyShowFavorites, _viewModel.ShowFavoritesCommand);
            Add(_viewModel.HotkeyShowRecent, _viewModel.ShowRecentCommand);

            // Жёстко заданные (без настройки) хоткеи ускорения работы (issue #160).
            // Очистка строки поиска и отключение всех включённых тегов; Ctrl+Shift+Plus /
            // Ctrl+Shift+Minus — развернуть/свернуть все узлы дерева. Регистрируются обе
            // раскладки (основная Oem* и цифровой блок Add/Subtract). Ctrl+Shift+C не
            // конфликтует с Ctrl+C (копирование) — в биндинг идёт только комбинация с Shift.
            InputBindings.Add(new KeyBinding(_viewModel.ClearSearchCommand, Key.C, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.ClearTagFiltersCommand, Key.T, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.ExpandAllGroupsCommand, Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.ExpandAllGroupsCommand, Key.Add, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.CollapseAllGroupsCommand, Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
            InputBindings.Add(new KeyBinding(_viewModel.CollapseAllGroupsCommand, Key.Subtract, ModifierKeys.Control | ModifierKeys.Shift));
        }

        /// <summary>
        /// Разбирает жест вида «F3», «Delete», «Ctrl+F2», «Shift+Insert».
        /// </summary>
        internal static bool TryParseKeyGesture(string? text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text.Trim(), "—", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "-", StringComparison.Ordinal) ||
                string.Equals(text.Trim(), "Нет", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
                else
                    return false;
            }

            var keyPart = parts[^1];
            // Синонимы
            if (keyPart.Equals("Del", StringComparison.OrdinalIgnoreCase))
                keyPart = "Delete";
            if (keyPart.Equals("Ins", StringComparison.OrdinalIgnoreCase))
                keyPart = "Insert";
            if (keyPart.Equals("Esc", StringComparison.OrdinalIgnoreCase))
                keyPart = "Escape";

            if (!Enum.TryParse<Key>(keyPart, true, out var parsed) || parsed == Key.None)
                return false;

            key = parsed;
            return true;
        }

        /// <summary>
        /// Регистрирует KeyBinding Alt+1…Alt+9 для быстрого запуска избранных баз.
        /// </summary>
        private void RegisterFavoriteHotkeys()
        {
            // Удаляем предыдущие биндинги Alt+1…9
            var toRemove = InputBindings
                .OfType<KeyBinding>()
                .Where(kb => kb.Modifiers == ModifierKeys.Alt &&
                             kb.Key >= Key.D1 && kb.Key <= Key.D9)
                .ToList();
            foreach (var kb in toRemove)
                InputBindings.Remove(kb);

            for (int i = 1; i <= 9; i++)
            {
                int index = i;
                var binding = new KeyBinding(
                    new ViewModels.RelayCommand(_ => _viewModel.LaunchFavoriteByHotkey(index)),
                    (Key)((int)Key.D0 + i),
                    ModifierKeys.Alt);
                InputBindings.Add(binding);
            }
        }

        /// <summary>
        /// Надёжный обработчик Alt+1…9 (KeyBinding с Alt иногда перехватывается системой).
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Стрелки ↑/↓/←/→ управляют выделением в списке баз, только если
            // фокус находится в пределах дерева и не в поле ввода текста.
            // Это гарантирует, что стрелки всегда перемещают выделение по дереву,
            // а не «прыгают» по кнопкам внутри строки (избранное, закрепление, теги).
            if (key is Key.Up or Key.Down or Key.Left or Key.Right &&
                Keyboard.Modifiers == ModifierKeys.None &&
                Keyboard.FocusedElement is not TextBox &&
                IsFocusInsideMainTree())
            {
                if (HandleArrowNavigation(key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Esc → в трей (если включено в настройках)
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Не перехватываем, если фокус в поле ввода тега — там свой обработчик
                if (Keyboard.FocusedElement is TextBox { Name: "InlineTagBox" })
                    return;

                if (_viewModel.EscapeToTray && _viewModel.ShowTrayIcon)
                {
                    MinimizeToTray();
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+F → фокус в поле поиска (в том числе когда фокус в другом поле ввода)
            if (key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (SearchTextBox is not null)
                {
                    SearchTextBox.Focus();
                    SearchTextBox.SelectAll();
                    e.Handled = true;
                    return;
                }
            }

            if (Keyboard.Modifiers != ModifierKeys.Alt)
                return;

            if (key >= Key.D1 && key <= Key.D9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.D0);
                e.Handled = true;
            }
            else if (key >= Key.NumPad1 && key <= Key.NumPad9)
            {
                _viewModel.LaunchFavoriteByHotkey(key - Key.NumPad0);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Определяет, находится ли клавиатурный фокус внутри дерева баз.
        /// Возвращает false, если фокус вне дерева (поле поиска, кнопка верхней панели и т.п.).
        /// </summary>
        private bool IsFocusInsideMainTree()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            return focused is not null && MainTree is not null &&
                   IsDescendantOf(focused, MainTree);
        }

        /// <summary>
        /// Проверяет, является ли <paramref name="candidate"/> потомком <paramref name="root"/> в визуальном дереве.
        /// </summary>
        private static bool IsDescendantOf(DependencyObject candidate, DependencyObject root)
        {
            for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, root))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Обрабатывает нажатие стрелки для навигации по дереву баз.
        /// ↑/↓ — перемещение по видимым строкам, ←/→ — раскрытие/сворачивание групп.
        /// Возвращает true, если событие обработано.
        /// </summary>
        private bool HandleArrowNavigation(Key key)
        {
            if (MainTree is null || _viewModel.GroupNodes.Count == 0)
                return false;

            // ↑/↓ — перемещение выделения по видимым строкам дерева. Навигация идёт
            // по контейнерам строк, а не по объектам данных: закреплённая база
            // присутствует в дереве дважды (узел «Закреплённые» и собственная
            // группа), и работа с данными всякий раз находила бы первое (верхнее)
            // вхождение, «перепрыгивая» выделение в начало списка.
            if (key is Key.Up or Key.Down)
            {
                var rows = GetVisibleTreeViewItems();
                if (rows.Count == 0)
                    return false;

                var currentIndex = FindCurrentRowIndex(rows);
                int targetIndex;

                if (currentIndex < 0)
                {
                    targetIndex = key == Key.Down ? 0 : rows.Count - 1;
                }
                else
                {
                    var last = rows.Count - 1;
                    targetIndex = key == Key.Down
                        ? (currentIndex >= last ? last : currentIndex + 1)
                        : (currentIndex <= 0 ? 0 : currentIndex - 1);
                }

                if (targetIndex == currentIndex && currentIndex >= 0)
                    return false;

                SelectRowItem(rows[targetIndex]);
                return true;
            }

            // → — раскрытие выбранной группы (или группы, где лежит база).
            var selectedGroup = _viewModel.SelectedGroupNode ?? (_viewModel.SelectedInfobase is null
                ? null
                : FindGroupNodeByInfobase(_viewModel.SelectedInfobase));

            if (key == Key.Right)
            {
                if (selectedGroup is not null && !selectedGroup.IsExpanded && selectedGroup.Items.Count > 0)
                {
                    _viewModel.ToggleGroupExpandedCommand.Execute(selectedGroup);
                    return true;
                }
                return false;
            }

            // ← — сворачивание выбранной группы; если курсор стоит на базе —
            // переводим выделение на группу, в которой она находится, не сворачивая группу.
            if (key == Key.Left)
            {
                if (_viewModel.SelectedGroupNode is { IsExpanded: true } grp)
                {
                    _viewModel.ToggleGroupExpandedCommand.Execute(grp);
                    return true;
                }

                if (_viewModel.SelectedInfobase is { } infobase &&
                    FindGroupNodeByInfobase(infobase) is { } container)
                {
                    SelectTreeNode(container);
                    return true;
                }

                return false;
            }

            return false;
        }

    }
}
#endif
