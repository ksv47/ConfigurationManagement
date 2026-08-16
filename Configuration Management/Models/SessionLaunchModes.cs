namespace Configuration_Management.Models;

/// <summary>
/// Режим клиента для «текущей сессии» (временное переопределение при запуске).
/// </summary>
public enum SessionClientMode
{
    /// <summary>Брать из настроек базы.</summary>
    Auto,

    /// <summary>Обычное приложение (/RunModeOrdinaryApplication).</summary>
    Ordinary,

    /// <summary>Толстый клиент.</summary>
    Thick,

    /// <summary>Тонкий клиент (/RunModeManagedApplication).</summary>
    Thin
}

/// <summary>
/// Разрядность для «текущей сессии».
/// </summary>
public enum SessionArchitectureMode
{
    /// <summary>Брать из настроек базы.</summary>
    Auto,

    /// <summary>Принудительно 32-бит.</summary>
    X86,

    /// <summary>Принудительно 64-бит.</summary>
    X64
}
