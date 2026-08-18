using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Configuration_Management.Controls
{
    /// <summary>
    /// Текстовое поле ввода горячей клавиши в стиле конфигуратора 1С:
    /// достаточно установить фокус и нажать нужную комбинацию (Ctrl+Shift+F и т.п.).
    /// Поддерживаются модификаторы Ctrl, Shift, Alt и Win.
    /// Backspace/Delete — сбросить назначение, Esc — отменить текущий ввод.
    /// Свойство <see cref="Value"/> содержит каноническое представление жеста
    /// (например «Ctrl+Shift+F») или пустую строку, если клавиша не назначена.
    /// </summary>
    public class HotkeyBox : System.Windows.Controls.TextBox
    {
        static HotkeyBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(HotkeyBox),
                new FrameworkPropertyMetadata(typeof(HotkeyBox)));
        }

        /// <summary>Каноническое представление горячей клавиши (например «Ctrl+Shift+F») или пустая строка.</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(string),
                typeof(HotkeyBox),
                new FrameworkPropertyMetadata(string.Empty, OnValueChanged));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (HotkeyBox)d;
            box.Text = HotkeyBox.FormatValue((string)e.NewValue);
        }

        public HotkeyBox()
        {
            IsReadOnly = true;
            Text = FormatValue(Value);
            ToolTip = "Установите курсор и нажмите сочетание клавиш. Backspace/Delete — сбросить, Esc — отмена.";
        }

        private static string FormatValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Нет" : value.Trim();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

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
                Text = "Нет";
                return;
            }

            // Клавиши навигации/ввода не должны записываться как горячая клавиша.
            if (key == Key.Tab || key == Key.Enter || IsNavigationKey(key))
            {
                // Позволяем базовой обработке выполнить переход фокуса/ввод.
                return;
            }

            // Зафиксирована полноценная комбинация.
            e.Handled = true;
            Value = FormatCombo(Keyboard.Modifiers, key);
            Text = FormatValue(Value);
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

        private static string BuildPendingText(Key key)
        {
            return key switch
            {
                Key.LeftCtrl or Key.RightCtrl => "Ctrl+…",
                Key.LeftShift or Key.RightShift => "Shift+…",
                Key.LeftAlt or Key.RightAlt => "Alt+…",
                Key.LWin or Key.RWin => "Win+…",
                _ => "…"
            };
        }

        private static string FormatCombo(ModifierKeys mods, Key key)
        {
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(KeyToDisplay(key));
            return string.Join("+", parts);
        }

        private static string KeyToDisplay(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return "NumPad" + (key - Key.NumPad0).ToString();
            if (key >= Key.F1 && key <= Key.F24)
                return "F" + (key - Key.F1 + 1).ToString();

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
    }
}