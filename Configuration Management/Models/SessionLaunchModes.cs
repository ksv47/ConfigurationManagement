namespace Configuration_Management.Models;

/// <summary>
/// Режим клиента для «текущей сессии» (временное переопределение при запуске).
/// По аналогии с настройками подключения базы 1С толстый клиент разделён
/// по режиму форм на управляемые и обычные.
/// </summary>
public enum SessionClientMode
{
    /// <summary>Брать из настроек базы.</summary>
    Auto,

    /// <summary>
    /// Обычный режим (толстый клиент, обычные формы, /RunModeOrdinaryApplication).
    /// Объединяет прежние «Обычный режим» и «Толстый (обычные формы)» (issue #144).
    /// </summary>
    Ordinary,

    /// <summary>Толстый клиент (управляемые формы) (/RunModeManagedApplication).</summary>
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
