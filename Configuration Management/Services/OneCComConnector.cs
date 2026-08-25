using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
    /// <remarks>
    /// ОПАСНО: выполняет COM-вызов в текущем процессе, а под CoreCLR это обрывает приложение
    /// нативным fast-fail 0xC0000409 без управляемого исключения. Вызывающих в приложении нет.
    /// Атрибут продублирован здесь намеренно: на интерфейсе он не предупреждает того, кто
    /// возьмёт конкретный тип.
    /// </remarks>
    [Obsolete("Выполняет COM-вызов в текущем процессе: под CoreCLR это обрывает приложение "
              + "нативным fast-fail 0xC0000409 без управляемого исключения. "
              + "Используйте путь через процесс-агент (ComReadHost).")]
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

        // Решение о тексте ошибки принимаем здесь, и принимаем его по строке подключения,
        // а не по тексту: если параметра Pwd в строке нет, то 1С пароля не видела и
        // процитировать не могла — текст безопасен по построению. Если есть — текст
        // не выпускаем вовсе. Никаких догадок о том, что именно 1С процитировала.
        var hasSecret = ContainsParameter(connectString, "Pwd");
        LastError = MaskCredentials(DescribeFailure(result, hasSecret));

        if (!alreadyDisabled)
            _logger.Error($"Не удалось прочитать сведения о конфигурации базы «{infobase.Name}»: {LastError}");

        return null;
    }

    /// <summary>
    /// Переводит разряд отказа в текст для пользователя. Тексты подбираются здесь, а не в агенте:
    /// в агенте локализация не поднята (он выходит до инициализации приложения), да и передавать
    /// по каналу код надёжнее, чем готовую строку.
    /// </summary>
    private static string DescribeFailure(ComReadResult result, bool hasSecret) => result.Failure switch
    {
        ComFailureKind.Disabled => LocalizationManager.T("Com.DisabledForSession"),
        ComFailureKind.AgentStart => string.Format(
            LocalizationManager.T("Com.AgentStartFailedFormat"), result.Detail ?? string.Empty),
        // Код возврата известен не всегда: если процесс придерживает Windows Error
        // Reporting, снять его не удаётся. Показывать «(код возврата )» нельзя.
        ComFailureKind.AgentCrashed => string.IsNullOrEmpty(result.Detail)
            ? LocalizationManager.T("Com.AgentCrashedUnknownCode")
            : string.Format(LocalizationManager.T("Com.AgentCrashedFormat"), result.Detail),
        ComFailureKind.Timeout => string.Format(
            LocalizationManager.T("Com.TimeoutReadFormat"), result.Detail ?? string.Empty),
        // Тот же текст, что и у быстрого отказа по кэшу (SetConnectorUnavailableError):
        // одно состояние — одна подсказка, включая совет установить платформу.
        ComFailureKind.NotRegistered => LocalizationManager.T("Com.NotFound")
                                        + " " + DescribeProgIdStatus()
                                        + " " + LocalizationManager.T("Com.NotFoundInstallHint"),
        // Коннектор в реестре есть, но не создаётся. Говорить «не найден» здесь нельзя:
        // текст противоречил бы и реестру, и быстрой проверке доступности.
        ComFailureKind.InstanceFailed => string.Format(
            LocalizationManager.T("Com.ProgIdInstanceFailedFormat"), result.Detail ?? string.Empty),
        ComFailureKind.NoConnection => string.Format(
            LocalizationManager.T("Com.ProgIdNoConnectionFormat"), result.Detail ?? string.Empty),
        ComFailureKind.MetadataProperty => LocalizationManager.T("Com.MetadataPropertyFailed"),
        ComFailureKind.MetadataRead => LocalizationManager.T("Com.MetadataReadFailed"),
        // Единственный разряд, где подробность — свободный текст от 1С. Показываем его,
        // только если в нём нет пароля; иначе остаётся опознавательный код.
        ComFailureKind.DatabaseError => DescribeDatabaseError(result, hasSecret),
        // Сбой обмена: подробность (текст исключения канала) полезнее общей фразы.
        ComFailureKind.Transport => string.IsNullOrWhiteSpace(result.Detail)
            ? LocalizationManager.T("Com.AgentNoResult")
            : result.Detail,
        _ => LocalizationManager.T("Com.AgentNoResult")
    };

    /// <summary>
    /// Решает, показывать ли свободный текст ошибки 1С.
    /// <para>
    /// Решение принимается по строке подключения, а не по содержимому текста. Искать в
    /// тексте пароль бесполезно: строка не экранирована, сообщение может прийти усечённым
    /// посреди значения, и проверка на начало пароля пропускала почти любой процитированный
    /// фрагмент, а проверка на короткий пароль глушила диагностику без всякого повода —
    /// обе ошибки измерены аудитом. Здесь работает простое и доказуемое правило: нет
    /// параметра Pwd в строке — 1С пароля не видела и процитировать не могла.
    /// </para>
    /// </summary>
    private static string DescribeDatabaseError(ComReadResult result, bool hasSecret)
    {
        var code = string.IsNullOrWhiteSpace(result.Code)
            ? LocalizationManager.T("Com.UnknownCode")
            : result.Code;

        if (hasSecret)
            return string.Format(LocalizationManager.T("Com.DbErrorHiddenFormat"), code);

        return string.IsNullOrWhiteSpace(result.Detail)
            ? string.Format(LocalizationManager.T("Com.DbErrorCodeOnlyFormat"), code)
            : result.Detail;
    }

    /// <summary>Есть ли в строке подключения параметр с указанным именем.</summary>
    private static bool ContainsParameter(string text, string name)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0)
            {
                var prev = text[i - 1];
                if (char.IsLetterOrDigit(prev) || prev == '_')
                    continue;
            }

            if (i + name.Length > text.Length)
                break;
            if (string.Compare(text, i, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            var k = SkipSpaces(text, i + name.Length);
            if (k < text.Length && text[k] == '=')
                return true;
        }

        return false;
    }

    /// <summary>
    /// Сбрасывает оба вердикта о недоступности COM: кэш реестра этого класса и сессионную
    /// защёлку процесса-агента. Их два, и снимать надо оба — иначе после установки платформы
    /// команда обновления по-прежнему молча откажет по устаревшему кэшу.
    /// </summary>
    public static void ResetComVerdicts()
    {
        ResetAvailabilityCache();
        ComReadHost.ResetAvailability();
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
                    // Строку подключения в журнал не пишем: она не экранирована, и маскировка
                // на ней принципиально ненадёжна — пароль с кавычкой, точкой с запятой или
                // переводом строки пробивает любое правило разбора. Имени ProgID для разбора
                // достаточно.
                _logger.Error($"COM-подключение через {progId} не удалось.", ex);
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
    /// Имена параметров, значение которых нельзя показывать. Имя пользователя (Usr)
    /// намеренно не маскируется: это логин, а не секрет, и он нужен для разбора отказов
    /// аутентификации. Так же вела себя и прежняя реализация.
    /// </summary>
    private static readonly string[] SecretParameters = { "Pwd", "Password" };

    /// <summary>
    /// Скрывает пароль перед записью в журнал или показом пользователю.
    /// <para>
    /// Применяется не только к самой строке подключения, но и к произвольному тексту:
    /// сообщения об ошибках, приходящие от 1С, умеют цитировать строку подключения целиком.
    /// </para>
    /// <para>
    /// Значение считается идущим до ближайшего <c>;</c> или конца строки — это грамматика
    /// строки подключения. Разбирать баланс кавычек нельзя: <see cref="BuildComConnectString"/>
    /// кавычку внутри пароля не экранирует, поэтому маскировка по кавычкам пропускала хвост
    /// пароля наружу. Правило по разделителю устойчиво ко всем формам записи — в кавычках,
    /// без кавычек, с удвоенными кавычками, с кавычкой внутри значения и с незакрытой кавычкой —
    /// и при этом сохраняет диагностику, идущую после разделителя.
    /// </para>
    /// </summary>
    private static string MaskCredentials(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var name = MatchSecretParameter(text, i);
            if (name is null)
            {
                sb.Append(text[i]);
                i++;
                continue;
            }

            var afterEq = SkipSpaces(text, i + name.Length) + 1; // за знаком равенства
            var quoteAt = SkipSpaces(text, afterEq);
            int end;

            if (quoteAt < text.Length && text[quoteAt] == '"')
            {
                // Закавыченное значение. Берём первую кавычку, за которой идёт разделитель
                // или конец строки, и за пределы строки не выходим.
                // Правило намеренно узкое. Оно не покрывает пароль, внутри которого стоит
                // «";», — но такой текст сюда и не попадает: при наличии пароля агент
                // свободный текст ошибки не отдаёт вовсе. Прежнее широкое правило «до
                // последней кавычки во всём остатке» закрывало этот случай, зато съедало
                // диагностику: из многострочного сообщения 1С исчезали строки с настоящей
                // причиной сбоя, а измерения показали потерю текста примерно в 7-8 % входов.
                var close = FindClosingQuote(text, quoteAt + 1);
                end = close >= 0 ? close + 1 : FindLineEnd(text, quoteAt);
            }
            else
            {
                // Незакавыченное значение встречается только в свободном тексте от 1С.
                // Начинается сразу за знаком равенства и обрывается пробелом: пропускать
                // пробелы после «=» нельзя, иначе в прозе «Pwd= is required» маска
                // съедала бы следующее слово.
                end = afterEq;
                while (end < text.Length && text[end] != ';'
                       && text[end] != '\r' && text[end] != '\n'
                       && !char.IsWhiteSpace(text[end]))
                    end++;
            }

            sb.Append(name).Append("=***");
            i = end;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Возвращает имя секретного параметра, если в позиции <paramref name="i"/> начинается
    /// именно он и за ним следует присваивание. Проверяются обе границы слова, иначе
    /// под маскировку попадали бы посторонние параметры вроде <c>NotPwd</c> и <c>PwdHint</c>.
    /// </summary>
    private static string? MatchSecretParameter(string text, int i)
    {
        if (i > 0)
        {
            var prev = text[i - 1];
            if (char.IsLetterOrDigit(prev) || prev == '_')
                return null;
        }

        foreach (var name in SecretParameters)
        {
            if (i + name.Length > text.Length)
                continue;
            if (string.Compare(text, i, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            var k = SkipSpaces(text, i + name.Length);
            if (k < text.Length && text[k] == '=')
                return text.Substring(i, name.Length);
        }

        return null;
    }

    private static int SkipSpaces(string text, int i)
    {
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            i++;
        return i;
    }

    /// <summary>
    /// Первая кавычка в пределах строки, за которой значение заканчивается.
    /// −1, если такой кавычки в строке нет (значение не закрыто).
    /// </summary>
    private static int FindClosingQuote(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '\r' || text[i] == '\n')
                return -1;
            if (text[i] != '"')
                continue;

            // Закрывающей считаем кавычку перед любым несловообразующим символом, а не только
            // перед «;». Прежнее узкое условие теряло остаток строки, если после значения шла
            // запятая или пробел: «Pwd="p", причина: …» превращалось в «Pwd=***».
            if (i + 1 >= text.Length || !char.IsLetterOrDigit(text[i + 1]))
                return i;
        }
        return -1;
    }

    /// <summary>Конец текущей строки или конец текста.</summary>
    private static int FindLineEnd(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
            if (text[i] == '\r' || text[i] == '\n')
                return i;
        return text.Length;
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