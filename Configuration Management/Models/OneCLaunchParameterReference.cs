namespace Configuration_Management.Models
{
    /// <summary>
    /// Запись справочника параметров командной строки 1С: ключ и его локализованное
    /// описание. Используется окном «Параметры» для автодополнения ключей запуска
    /// (ПЗ-5). Платформенно-нейтральная модель.
    /// </summary>
    public sealed class OneCLaunchParameterReference
    {
        /// <summary>
        /// Создаёт запись справочника параметров запуска 1С.
        /// </summary>
        /// <param name="key">Ключ командной строки (например «/UC»).</param>
        /// <param name="description">Локализованное описание ключа.</param>
        /// <param name="isCustom">Признак пользовательского параметра (issue #141).</param>
        public OneCLaunchParameterReference(string key, string description, bool isCustom = false)
        {
            Key = key;
            Description = description;
            IsCustom = isCustom;
        }

        /// <summary>Ключ командной строки (например «/UC»).</summary>
        public string Key { get; }

        /// <summary>Локализованное описание ключа.</summary>
        public string Description { get; }

        /// <summary>Признак пользовательского параметра, добавленного в справочник (issue #141).</summary>
        public bool IsCustom { get; }
    }
}