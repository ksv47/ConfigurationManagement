namespace Configuration_Management.Models
{
    /// <summary>
    /// Настройки шрифта для отдельной области интерфейса
    /// (список баз, заголовки, правая панель, строка состояния, вкладки, кнопки, поля ввода).
    /// </summary>
    public class ElementFontSettings
    {
        /// <summary>Семейство шрифта (например «Segoe UI»).</summary>
        public string FontFamily { get; set; } = "Segoe UI";

        /// <summary>Размер шрифта (в логических единицах WPF).</summary>
        public double FontSize { get; set; } = 13;

        /// <summary>Начертание шрифта: «Normal» или «Bold».</summary>
        public string FontWeight { get; set; } = "Normal";

        /// <summary>Стиль шрифта: «Normal» или «Italic».</summary>
        public string FontStyle { get; set; } = "Normal";

        /// <summary>Копия настроек.</summary>
        public ElementFontSettings Clone() => new()
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontStyle = FontStyle
        };
    }
}