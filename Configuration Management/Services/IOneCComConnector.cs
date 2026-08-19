using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Сервис подключения к информационным базам 1С через COM-коннектор
/// (V83.COMConnector / V82 / V81).
/// Позволяет устанавливать соединение и получать информацию о базе.
/// </summary>
public interface IOneCComConnector
{
    /// <summary>
    /// Текст последней ошибки COM-операции (для диагностики в UI).
    /// null — если последняя операция завершилась успешно.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Устанавливает COM-подключение к информационной базе.
    /// Выполняется в фоновом STA-потоке с ограничением по времени.
    /// Возвращает null, если подключение не удалось или превышен таймаут.
    /// </summary>
    /// <param name="infobase">Информационная база для подключения.</param>
    /// <param name="timeoutMs">Максимальное время ожидания подключения, мс.</param>
    OneCComConnection? Connect(Infobase infobase, int timeoutMs = 8000);

    /// <summary>
    /// Строит строку подключения COM для информационной базы
    /// (File=...; или Srvr=...;Ref=...; + Usr/Pwd при наличии).
    /// </summary>
    string BuildConnectString(Infobase infobase);

    /// <summary>
    /// Считывает наименование и версию конфигурации базы через COM-коннектор.
    /// Возвращает null, если чтение не удалось или превышен таймаут.
    /// </summary>
    OneCConfigInfo? ReadConfigurationInfo(Infobase infobase, int timeoutMs = 8000);
}