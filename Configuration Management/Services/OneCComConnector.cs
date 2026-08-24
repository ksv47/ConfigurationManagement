using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Configuration_Management.Localization;
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

    // -- Кэш доступности COM-коннекторов 1С ----------------------------------
    // Реестр COM не меняется в течение сеанса (кроме ручной регистрации),
    // поэтому результат проверки кэшируется, чтобы не повторять её для каждой базы.
    private static readonly object AvailabilityLock = new();
    private static bool? _connectorsAvailable;
    private static string? _cachedAvailabilityStatus;

    /// <summary>
    /// Проверяет (с кэшированием), зарегистрирован ли хотя бы один COM-коннектор 1С,
    /// фактически доступный из текущего процесса (учитывает разрядность через GetTypeFromProgID).
    /// Если коннектор недоступен, вызывающие методы быстро прерываются, не плодя потоки и исключения.
    /// </summary>
    public static bool IsComConnectorAvailable()
    {
        lock (AvailabilityLock)
        {
            if (_connectorsAvailable is { } cached)
                return cached;

            var available = false;
            foreach (var progId in KnownProgIds)
            {
                try
                {
                    if (Type.GetTypeFromProgID(progId) is not null)
                    {
                        available = true;
                        break;
                    }
                }
                catch
                {
                    // Не зарегистрирован / несоответствие разрядности — пробуем следующий.
                }
            }

            _connectorsAvailable = available;
            _cachedAvailabilityStatus = DescribeProgIdStatus();
            return available;
        }
    }

    /// <summary>
    /// Сбрасывает кэш доступности (вызывается после ручной регистрации COM-коннектора).
    /// </summary>
    public static void ResetAvailabilityCache()
    {
        lock (AvailabilityLock)
        {
            _connectorsAvailable = null;
            _cachedAvailabilityStatus = null;
        }
    }

    /// <summary>Кэшированное описание состояния ProgID (реестр).</summary>
    private static string AvailabilityStatusText
    {
        get
        {
            lock (AvailabilityLock)
                return _cachedAvailabilityStatus ?? DescribeProgIdStatus();
        }
    }

    /// <summary>
    /// Заполняет LastError и пишет в лог понятное сообщение о том, что COM-коннектор
    /// не зарегистрирован (быстрый отказ без создания потока).
    /// </summary>
    private void SetConnectorUnavailableError(string baseName)
    {
        var status = AvailabilityStatusText;
        LastError = LocalizationManager.T("Com.NotFound") + " " +
                    status + " " +
                    LocalizationManager.T("Com.NotFoundInstallHint");
        _logger.Warn($"COM-коннектор 1С не зарегистрирован в системе для базы «{baseName}». {status}");
    }

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

        // Быстрый отказ: коннектор не зарегистрирован — не создаём поток и не ловим COMException.
        if (!IsComConnectorAvailable())
        {
            SetConnectorUnavailableError(infobase.Name);
            return null;
        }

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
            LastError ??= string.Format(LocalizationManager.T("Com.TimeoutConnectFormat"), timeoutMs);
            _logger.Error($"Превышен таймаут COM-подключения к базе «{infobase.Name}».");
            return null;
        }
        if (error is not null)
            return null;

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Чтение выполняется не здесь, а в дочернем процессе (<see cref="ComReadHost"/>).
    /// Под CoreCLR вызов <c>Connect</c> у comcntr.dll обрывает процесс нативным fast-fail
    /// (0xC0000409) без управляемого исключения — перехватить его в этом процессе нельзя,
    /// поэтому COM изолирован. Подробности и история — в комментарии к ComReadHost.
    /// </remarks>
    public OneCConfigInfo? ReadConfigurationInfo(Infobase infobase, int timeoutMs = 8000)
    {
        if (infobase is null) return null;

        // Быстрый отказ: коннектор не зарегистрирован — не запускаем процесс-агент.
        if (!IsComConnectorAvailable())
        {
            SetConnectorUnavailableError(infobase.Name);
            return null;
        }

        var connectString = BuildComConnectString(infobase);
        if (string.IsNullOrWhiteSpace(connectString))
        {
            LastError = LocalizationManager.T("Com.ConnStringBuildFailed");
            return null;
        }

        // Запоминаем состояние до вызова: если COM был отключён ещё раньше, повторно
        // писать об этом в журнал незачем — на списке из десятков баз это дало бы
        // десятки одинаковых строк подряд на каждом старте.
        var alreadyDisabled = ComReadHost.ComUnavailable;

        var result = ComReadHost.Read(connectString, timeoutMs);
        if (result.Failure == ComFailureKind.None && result.Info is not null)
        {
            LastError = null;
            return result.Info;
        }

        LastError = DescribeFailure(result);

        if (!alreadyDisabled)
            _logger.Error($"Не удалось прочитать сведения о конфигурации базы «{infobase.Name}»: {LastError}");

        return null;
    }

    /// <summary>
    /// Переводит разряд отказа в текст для пользователя. Тексты подбираются здесь, а не в агенте:
    /// в агенте локализация не поднята (он выходит до инициализации приложения), да и передавать
    /// по каналу код надёжнее, чем готовую строку.
    /// </summary>
    private static string DescribeFailure(ComReadResult result) => result.Failure switch
    {
        ComFailureKind.Disabled => LocalizationManager.T("Com.DisabledForSession"),
        ComFailureKind.AgentStart => string.Format(
            LocalizationManager.T("Com.AgentStartFailedFormat"), result.Detail ?? string.Empty),
        ComFailureKind.AgentCrashed => string.Format(
            LocalizationManager.T("Com.AgentCrashedFormat"), result.Detail ?? string.Empty),
        ComFailureKind.Timeout => string.Format(
            LocalizationManager.T("Com.TimeoutReadFormat"), result.Detail ?? string.Empty),
        ComFailureKind.NotRegistered => LocalizationManager.T("Com.NotFound")
                                        + " " + DescribeProgIdStatus(),
        ComFailureKind.NoConnection => string.Format(
            LocalizationManager.T("Com.ProgIdNoConnectionFormat"), result.Detail ?? string.Empty),
        ComFailureKind.MetadataProperty => LocalizationManager.T("Com.MetadataPropertyFailed"),
        ComFailureKind.MetadataRead => LocalizationManager.T("Com.MetadataReadFailed"),
        ComFailureKind.DatabaseError => result.Detail ?? LocalizationManager.T("Com.MetadataReadFailed"),
        _ => LocalizationManager.T("Com.AgentNoResult")
    };

    /// <summary>
    /// Устанавливает подключение в текущем (STA) потоке, перебирая известные ProgID.
    /// При успехе возвращает владеющее COM-объектами соединение.
    /// </summary>
    private OneCComConnection? ConnectCore(Infobase infobase)
    {
        var connectString = BuildComConnectString(infobase);
        if (string.IsNullOrWhiteSpace(connectString))
        {
            LastError = LocalizationManager.T("Com.ConnStringBuildFailed");
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
                LastError = string.Format(LocalizationManager.T("Com.ProgIdTypeFailedFormat"), progId, ex.Message);
                continue;
            }

            if (type is null)
            {
                LastError = string.Format(LocalizationManager.T("Com.ProgIdNotRegisteredFormat"), progId);
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
                    LastError = string.Format(LocalizationManager.T("Com.ProgIdInstanceFailedFormat"), progId);
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
                    LastError = string.Format(LocalizationManager.T("Com.ProgIdNoConnectionFormat"), progId);
                    TryComRelease(connector);
                    continue;
                }

                // Соединение установлено — оборачиваем и передаём владение вызывающему.
                LastError = null;
                return new OneCComConnection(connector, connection, progId, connectString);
            }
            catch (Exception ex)
            {
                LastError = string.Format(LocalizationManager.T("Com.ConnectFailedFormat"), progId, ex.Message);
                _logger.Error($"COM-подключение через {progId} не удалось. Строка: {MaskCredentials(connectString)}", ex);
                // Пробуем следующий ProgID.
                TryComRelease(connection);
                TryComRelease(connector);
            }
        }

        // Ни один COM-коннектор 1С не зарегистрирован — понятное итоговое сообщение.
        if (!anyRegistered)
        {
            LastError = LocalizationManager.T("Com.NotFound") + " " +
                        DescribeProgIdStatus() + " " +
                        LocalizationManager.T("Com.NotFoundInstallHint");
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
            return LocalizationManager.T("Com.ProgIdStatusBothVisible");
        if (in64)
            return LocalizationManager.T("Com.ProgIdStatusOnly64");
        if (in32)
            return LocalizationManager.T("Com.ProgIdStatusOnly32");
        return LocalizationManager.T("Com.ProgIdStatusAbsent");
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