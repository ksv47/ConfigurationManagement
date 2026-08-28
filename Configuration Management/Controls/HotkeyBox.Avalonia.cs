#if LINUX
using System.Collections.Generic;
using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Configuration_Management.Localization;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Avalonia-версия текстового поля ввода горячей клавиши в стиле конфигуратора 1С:
    /// достаточно установить фокус и нажать нужную комбинацию (Ctrl+Shift+F и т.п.).
    /// Backspace/Delete — сбросить, Esc — отменить ввод. Свойство <see cref="Value"/> —
    /// каноническое представление жеста (например «Ctrl+Shift+F») или пустая строка.
    /// </summary>
    public class HotkeyBox : TextBox
    {
        /// <summary>
        /// Тема ищется по ключу стиля, а для наследника её в Fluent нет:
        /// без этого поле не отрисуется, как это было у дерева и PasswordBox.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(TextBox);

        /// <summary>Каноническое представление горячей клавиши (например «Ctrl+Shift+F») или пустая строка.</summary>
        public static readonly StyledProperty<string> ValueProperty =
            AvaloniaProperty.Register<HotkeyBox, string>(nameof(Value), string.Empty);

        public string Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public HotkeyBox()
        {
            IsReadOnly = true;
            Text = FormatValue(Value);
            ToolTip.SetTip(this, LocalizationManager.T("Hotkey.Tooltip"));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ValueProperty)
                Text = FormatValue((string?)change.NewValue);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            var key = e.Key;

            // Только модификатор — показываем «промежуточное» состояние, не фиксируем.
            if (IsModifierKey(key))
            {
                e.Handled = true;
                Text = BuildPendingText(key);
                return;
            }

            if (key == Key.Escape)
            {
                e.Handled = true;
                Text = FormatValue(Value); // отмена ввода
                return;
            }

            if (key == Key.Back || key == Key.Delete)
            {
                e.Handled = true;
                Value = string.Empty; // сброс назначения
                Text = LocalizationManager.T("Common.None");
                return;
            }

            // Клавиши навигации/ввода не должны записываться как горячая клавиша.
            if (key == Key.Tab || key == Key.Enter || IsNavigationKey(key))
                return;

            // Зафиксирована полноценная комбинация.
            e.Handled = true;
            Value = FormatCombo(e.KeyModifiers, key);
            Text = FormatValue(Value);
        }

        /// <summary>
        /// Разбирает сохранённое сочетание в жест Avalonia. Формат задаёт сам
        /// контрол, поэтому и разбор живёт здесь: сокращения Del, Ins и Esc
        /// раскрываются, а имена вроде «NumPad+» и «,» жестами не становятся,
        /// и такое назначение считается недопустимым.
        /// </summary>
        public static bool TryParse(string? text, out KeyGesture? gesture)
        {
            gesture = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return false;

            parts[^1] = parts[^1].ToLowerInvariant() switch
            {
                "del" => "Delete",
                "ins" => "Insert",
                "esc" => "Escape",
                _ => parts[^1]
            };

            // Цифра показывается как «1», а Enum.TryParse принимает такую строку
            // за числовое значение перечисления и отдаёт Key.Cancel вместо Key.D1.
            // Сочетание при этом сохраняется и регистрируется, но не срабатывает
            // никогда. Возвращаем цифре её имя до разбора.
            if (parts[^1].Length == 1 && parts[^1][0] >= '0' && parts[^1][0] <= '9')
                parts[^1] = "D" + parts[^1];

            // Последняя часть должна быть именно клавишей, а не модификатором:
            // «Ctrl» сам по себе Avalonia разбирает в жест с Key.None.
            if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None)
                return false;

            try
            {
                gesture = KeyGesture.Parse(string.Join("+", parts));
                return gesture.Key != Key.None;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Сочетание, которое нельзя отдавать окну: без модификаторов оно отберёт
        /// обычный ввод, а Ctrl+C, Ctrl+V и соседние отберут работу с буфером
        /// у полей ввода, потому что привязки окна проверяются раньше них.
        /// </summary>
        public static bool IsUnsafeForTextInput(KeyGesture gesture)
        {
            if (gesture.KeyModifiers == KeyModifiers.None)
            {
                var isFunctionKey = gesture.Key >= Key.F1 && gesture.Key <= Key.F24;
                var isEditingKey = gesture.Key is Key.Delete or Key.Insert or Key.Back;
                return !isFunctionKey && !isEditingKey;
            }

            if (gesture.KeyModifiers == KeyModifiers.Control)
                return gesture.Key is Key.C or Key.V or Key.X or Key.A or Key.Z or Key.Y;

            return false;
        }

        private static bool IsModifierKey(Key key) =>
            key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin;

        private static bool IsNavigationKey(Key key) =>
            key is Key.Left or Key.Right or Key.Up or Key.Down
                or Key.Home or Key.End or Key.PageUp or Key.PageDown
                or Key.CapsLock or Key.NumLock or Key.Scroll;

        private static string BuildPendingText(Key key) =>
            key switch
            {
                Key.LeftCtrl or Key.RightCtrl => "Ctrl+…",
                Key.LeftShift or Key.RightShift => "Shift+…",
                Key.LeftAlt or Key.RightAlt => "Alt+…",
                Key.LWin or Key.RWin => "Win+…",
                _ => "…"
            };

        private static string FormatCombo(KeyModifiers mods, Key key)
        {
            var parts = new List<string>();
            if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((mods & KeyModifiers.Meta) != 0) parts.Add("Win");
            parts.Add(KeyToDisplay(key));
            return string.Join("+", parts);
        }

        private static string KeyToDisplay(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return "NumPad" + (key - Key.NumPad0);
            if (key >= Key.F1 && key <= Key.F12)
                return "F" + (key - Key.F1 + 1);

            return key switch
            {
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "?",
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemQuotes => "\"",
                Key.OemSemicolon => ";",
                Key.OemBackslash => "\\",
                Key.OemPipe => "|",
                Key.OemTilde => "~",
                Key.Add => "NumPad+",
                Key.Subtract => "NumPad-",
                Key.Multiply => "NumPad*",
                Key.Divide => "NumPad/",
                Key.Decimal => "NumPad.",
                Key.Space => "Space",
                _ => key.ToString()
            };
        }

        private static string FormatValue(string? value)
            => string.IsNullOrWhiteSpace(value) ? LocalizationManager.T("Common.None") : value.Trim();
    }
}
#endif