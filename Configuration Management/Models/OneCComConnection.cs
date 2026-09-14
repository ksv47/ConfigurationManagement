using System.Runtime.InteropServices;

namespace Configuration_Management.Models;

/// <summary>
/// Обёртка над открытым COM-подключением к информационной базе 1С.
/// Владеет COM-объектами коннектора и соединения; корректно освобождает их
/// при вызове <see cref="Dispose"/> (FinalReleaseComObject).
/// </summary>
/// <remarks>
/// ВАЖНО: COM-объекты 1С чувствительны к потоку (STA). Использование объектов
/// соединения допустимо только в том же потоке (STA), где они были созданы.
/// Внешние потребители обычно работают с коннектором через методы-обёртки
/// (например, <c>ReadConfigurationInfo</c>), которые выполняются внутри STA-потока.
/// </remarks>
public sealed class OneCComConnection : IDisposable
{
    /// <summary>COM-объект коннектора (например, V83.COMConnector).</summary>
    public object Connector { get; }

    /// <summary>COM-объект активного соединения с информационной базой.</summary>
    public object Connection { get; }

    /// <summary>ProgID использованного COM-коннектора (V83.COMConnector / V82 / V81).</summary>
    public string ProgId { get; }

    /// <summary>Строка подключения, с которой установлено соединение.</summary>
    public string ConnectString { get; }

    public OneCComConnection(object connector, object connection, string progId, string connectString)
    {
        Connector = connector ?? throw new ArgumentNullException(nameof(connector));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ProgId = progId ?? string.Empty;
        ConnectString = connectString ?? string.Empty;
    }

    /// <summary>Читает свойство COM-объекта как объект (например, Metadata). При ошибке — null.</summary>
    public object? GetProperty(object comObject, string propertyName)
    {
        try
        {
            return comObject.GetType().InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.GetProperty,
                null,
                comObject,
                null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Читает свойство COM-объекта как строку. При ошибке — null.</summary>
    public string? GetString(object comObject, string propertyName)
    {
        var value = GetProperty(comObject, propertyName);
        return value?.ToString();
    }

    /// <summary>Читает свойство объекта соединения как строку.</summary>
    public string? GetConnectionString(string propertyName) => GetString(Connection, propertyName);

    /// <summary>Читает свойство объекта соединения как объект.</summary>
    public object? GetConnectionProperty(string propertyName) => GetProperty(Connection, propertyName);

    /// <summary>
    /// Освобождает COM-объекты соединения и коннектора.
    /// Повторные вызовы безопасны.
    /// </summary>
    public void Dispose()
    {
        Release(Connection);
        Release(Connector);
    }

    private static void Release(object? com)
    {
        // COM-объекты 1С существуют только на Windows; на других ОС вызовы маршаллинга
        // бессмысленны (и их нет в целевой платформе — CA1416).
        if (com is null || !OperatingSystem.IsWindows()) return;
        try
        {
            if (Marshal.IsComObject(com))
                Marshal.FinalReleaseComObject(com);
        }
        catch
        {
            // Объект мог быть уже освобождён.
        }
    }
}