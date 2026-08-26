#if WINDOWS
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>Чем закончилось обращение к COM-коннектору. Текст сообщения подбирает вызывающий.</summary>
internal enum ComFailureKind
{
    /// <summary>Успех.</summary>
    None,
    /// <summary>COM отключён на эту сессию после серии сбоев или отсутствия коннектора.</summary>
    Disabled,
    /// <summary>Не удалось запустить процесс-агент.</summary>
    AgentStart,
    /// <summary>Агент не пережил COM-вызов. В детали кладётся код возврата, если он известен.</summary>
    AgentCrashed,
    /// <summary>Агент жив, но COM-вызов не уложился в отведённое время.</summary>
    Timeout,
    /// <summary>Сбой обмена с агентом (протокол, каналы).</summary>
    Transport,
    /// <summary>
    /// Агент не смог разобрать запрос. Разряд отдельный, а не общий с <see cref="Transport"/>:
    /// у <see cref="Transport"/> подробность печатается пользователю дословно, поэтому он не
    /// должен приезжать по протоколу вовсе — иначе подделанный кадр вынес бы произвольный
    /// текст мимо решения о показе пароля.
    /// </summary>
    BadRequest,
    /// <summary>Ни один из известных ProgID не зарегистрирован.</summary>
    NotRegistered,
    /// <summary>ProgID зарегистрирован, но экземпляр коннектора создать не удалось.</summary>
    InstanceFailed,
    /// <summary>Коннектор не вернул соединение для этой строки подключения.</summary>
    NoConnection,
    /// <summary>Не удалось получить свойство Metadata.</summary>
    MetadataProperty,
    /// <summary>Metadata есть, но имя и версия пусты.</summary>
    MetadataRead,
    /// <summary>Ошибка на стороне 1С при подключении к конкретной базе.</summary>
    DatabaseError
}

/// <summary>
/// Результат обращения к COM-коннектору: либо сведения, либо разряд отказа с подробностью.
/// <para>
/// <paramref name="Detail"/> — текст для показа; пуст, если показывать нечего или текст
/// пришлось скрыть. <paramref name="Code"/> — код ошибки COM: он остаётся даже тогда, когда
/// текст скрыт, и позволяет сказать пользователю хоть что-то определённое.
/// </para>
/// </summary>
internal readonly record struct ComReadResult(
    OneCConfigInfo? Info, ComFailureKind Failure, string? Detail, string? Code = null)
{
    public static ComReadResult Ok(OneCConfigInfo info) => new(info, ComFailureKind.None, null);
    public static ComReadResult Fail(ComFailureKind kind, string? detail = null, string? code = null) =>
        new(null, kind, detail, code);
}
#endif