namespace Configuration_Management.Models
{
    /// <summary>
    /// Структурированное представление одного аргумента командной строки запуска 1С.
    /// Например, «/UC» без значения либо «/L "ru"» с значением. Платформенно-нейтральная
    /// модель разбора параметров запуска базы (ПЗ-5).
    /// </summary>
    public sealed class OneCLaunchArgument
    {
        /// <summary>
        /// Создаёт аргумент командной строки 1С.
        /// </summary>
        /// <param name="key">Ключ аргумента (например «/DisableStartupMessages» или «/UC»).</param>
        /// <param name="value">Значение аргумента без внешних кавычек (пусто для ключей-флагов).</param>
        public OneCLaunchArgument(string key, string value = "")
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        /// <summary>Ключ аргумента (например «/DisableStartupMessages» или «/UC»).</summary>
        public string Key { get; }

        /// <summary>Значение аргумента без внешних кавычек (пусто для ключей-флагов).</summary>
        public string Value { get; }

        /// <summary>Признак того, что аргумент несёт значение.</summary>
        public bool HasValue => !string.IsNullOrEmpty(Value);
    }
}