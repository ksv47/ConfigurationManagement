using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Configuration_Management.Models;
using Microsoft.Win32;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация подключения к информационным базам 1С через COM-коннектор.
/// Все COM-операции выполняются в фоновом STA-потоке (требование 1С) с ограничением
/// по времени, чтобы не блокировать UI и не «зависать» навсегда при недоступной базе.
/// </summary>
public sealed class OneCComConnector : IOneCComConnector
{
    private readonly IAppLogger _logger;

    /// <summary>ProgID COM-коннекторов 1С в порядке приоритета (от новых версий к старым).</summary>
    public static readonly string[] KnownProgIds =
    {
        "V83.COMConnector",
        "V82.COMConnector",
        "V81.COMConnector"
    };

    /// <summary>
    /// Текст последней ошибки COM-подключения (для вывода в UI при диагностике).
    /// Сбрасывается перед каждой новой успешной попыткой.
    /// </summary>
    public string? LastError { get; private set; }

    public OneCComConnector(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string BuildConnectString(Infobase infobase) => BuildComConnectString(infobase);

    /// <inheritdoc />
    public OneCComConnection? Connect(Infobase infobase, int timeoutMs = 8000)
    {
        if (infobase is null) return null;

        OneCComConnection? result = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = ConnectCore(infobase);
            }
            catch (Exception ex)
            {
                error = ex;
                LastError = ex.Message;
            }
        })
        {
            IsBackground = true,
            Name = "1C-COM-Connector"
        };

        // STA обязателен для COM-объектов 1С.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            LastError ??= $"Превышен таймаут подключения ({timeoutMs} мс).";
            _logger.Error($"Превышен таймаут COM-подключения к базе «{infobase.Name}».");
            return null;
        }
        if (error is not null)
            return null;

        return result;
    }

    /// <inheritdoc />
    public OneCConfigInfo? ReadConfigurationInfo(Infobase infobase, int timeoutMs = 8000)
    {
        if (infobase is null) return null;

        OneCConfigInfo? result = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var connection = ConnectCore(infobase);
                if (connection is null) return;

                var metadata = connection.GetConnectionProperty("Metadata");
                if (metadata is null)
                {
                    LastError ??= "Не удалось получить свойство Metadata из соединения.";
                    return;
                }

                var name = connection.GetString(metadata, "Name")
                           ?? connection.GetString(metadata, "Synonym")
                           ?? string.Empty;
                var version = connection.GetString(metadata, "Version") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
                {
                    LastError = "Не удалось прочитать наименование/версию конфигурации из Metadata.";
                    return;
                }

                LastError = null;
                result = new OneCConfigInfo(name, version);
            }
            catch (Exception ex)
            {
                error = ex;
                LastError = ex.Message;
                _logger.Error($"Ошибка чтения информации о конфигурации через COM для базы «{infobase.Name}».", ex);
            }
        })
        {
            IsBackground = true,
            Name = "1C-COM-ConfigRead"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            LastError ??= $"Превышен таймаут чтения ({timeoutMs} мс).";
            _logger.Error($"Превышен таймаут чтения конфигурации для базы «{infobase.Name}».");
            return null;
        }
        if (error is not null)
            return null;

        return result;
    }

    /// <summary>
    /// Устанавливает подключение в текущем (STA) потоке, перебирая известные ProgID.
    /// При успехе возвращает владеющее COM-объектами соединение.
    /// </summary>
    private OneCComConnection? ConnectCore(Infobase infobase)
    {
        var connectString = BuildComConnectString(infobase);
        if (string.IsNullOrWhiteSpace(connectString))
        {
            LastError = "Не удалось построить строку подключения COM (не заполнены сервер/база или путь к файловой базе).";
            return null;
        }

        // true, если хотя бы один COM-тип 1С удалось получить (зарегистрирован).
        var anyRegistered = false;

        foreach (var progId in KnownProgIds)
        {
            Type? type;
            try
            {
                type = Type.GetTypeFromProgID(progId);
            }
            catch (Exception ex)
            {
                LastError = $"Не удалось получить COM-тип {progId}: {ex.Message}";
                continue;
            }

            if (type is null)
            {
                LastError = $"COM-коннектор {progId} не зарегистрирован в системе.";
                continue;
            }

            anyRegistered = true;

            object? connector = null;
            object? connection = null;
            try
            {
                connector = Activator.CreateInstance(type);
                if (connector is null)
                {
                    LastError = $"Не удалось создать экземпляр COM-коннектора {progId}.";
                    continue;
                }

                connection = type.InvokeMember(
                    "Connect",
                    BindingFlags.InvokeMethod,
                    null,
                    connector,
                    new object[] { connectString });

                if (connection is null)
                {
                    LastError = $"COM-коннектор {progId} не вернул соединение для указанной строки подключения.";
                    TryComRelease(connector);
                    continue;
                }

                // Соединение установлено — оборачиваем и передаём владение вызывающему.
                LastError = null;
                return new OneCComConnection(connector, connection, progId, connectString);
            }
            catch (Exception ex)
            {
                LastError = $"COM-подключение через {progId} не удалось: {ex.Message}";
                _logger.Error($"COM-подключение через {progId} не удалось. Строка: {MaskCredentials(connectString)}", ex);
                // Пробуем следующий ProgID.
                TryComRelease(connection);
                TryComRelease(connector);
            }
        }

        // Ни один COM-коннектор 1С не зарегистрирован — понятное итоговое сообщение.
        if (!anyRegistered)
        {
            LastError = "COM-коннектор 1С не найден (не зарегистрированы V83/V82/V81.COMConnector). " +
                        DescribeProgIdStatus() + " " +
                        "Установите платформу 1С:Предприятие на этой машине (или зарегистрируйте COM-коннектор через меню).";
            _logger.Warn($"COM-коннектор 1С не зарегистрирован в системе для базы «{infobase.Name}». {DescribeProgIdStatus()}");
        }

        return null;
    }

    /// <summary>
    /// Возвращает описание наличия ProgID V83.COMConnector в реестре (64-битное и 32-битное представления),
    /// чтобы отличить «платформа не установлена» от «несоответствие разрядности».
    /// </summary>
    private static string DescribeProgIdStatus()
    {
        var in64 = IsProgIdRegistered(RegistryView.Registry64);
        var in32 = IsProgIdRegistered(RegistryView.Registry32);

        if (in64 && in32)
            return "(V83.COMConnector зарегистрирован и в 64-битном, и в 32-битном реестре, но не виден процессу приложения)";
        if (in64)
            return "(V83.COMConnector есть только в 64-битном реестре; приложение, вероятно, запущено как 32-битное)";
        if (in32)
            return "(V83.COMConnector есть только в 32-битном реестре; приложение, вероятно, запущено как 64-битное)";
        return "(V83.COMConnector отсутствует в реестре — платформа 1С не установлена или COM-коннектор не зарегистрирован)";
    }

    private static bool IsProgIdRegistered(RegistryView view)
    {
        try
        {
            using var classesRoot = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var progIdKey = classesRoot?.OpenSubKey("V83.COMConnector");
            if (progIdKey is null) return false;
            var clsid = progIdKey.GetValue("CLSID")?.ToString();
            if (string.IsNullOrWhiteSpace(clsid)) return false;
            using var clsidKey = classesRoot?.OpenSubKey($"CLSID\\{clsid}");
            return clsidKey is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void TryComRelease(object? com)
    {
        if (com is null) return;
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

    /// <summary>
    /// Скрывает пароль в строке подключения перед записью в лог.
    /// </summary>
    private static string MaskCredentials(string connectString)
    {
        if (string.IsNullOrEmpty(connectString)) return connectString;
        return Regex.Replace(connectString, @"Pwd=""[^""]*""", "Pwd=\"***\"", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Строка подключения для COM: File=...; или Srvr=...;Ref=...; (+ Usr/Pwd при наличии).
    /// Для веб-публикации COM-подключение обычно неприменимо.
    /// </summary>
    private static string BuildComConnectString(Infobase infobase)
    {
        var c = infobase.Connection;
        if (c is null) return string.Empty;

        var sb = new StringBuilder();
        switch (c.Type)
        {
            case ConnectionType.File:
                if (string.IsNullOrWhiteSpace(c.FilePath)) return string.Empty;
                sb.Append("File=\"").Append(c.FilePath.Trim().TrimEnd('\\', '/')).Append("\";");
                break;

            case ConnectionType.ClientServer:
                if (string.IsNullOrWhiteSpace(c.Server) || string.IsNullOrWhiteSpace(c.DatabaseName))
                    return string.Empty;
                sb.Append("Srvr=\"").Append(c.GetServerWithPort()).Append("\";");
                sb.Append("Ref=\"").Append(c.DatabaseName).Append("\";");
                break;

            default:
                // Веб — COM обычно не подходит.
                return string.Empty;
        }

        if (c.AuthenticationMode == AuthenticationMode.Credentials
            && !string.IsNullOrWhiteSpace(c.User))
        {
            sb.Append("Usr=\"").Append(c.User).Append("\";");
            if (!string.IsNullOrWhiteSpace(c.Password))
                sb.Append("Pwd=\"").Append(c.Password).Append("\";");
        }

        return sb.ToString();
    }
}