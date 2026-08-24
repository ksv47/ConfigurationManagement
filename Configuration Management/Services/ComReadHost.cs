#if WINDOWS
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

/// <summary>Результат обращения к COM-коннектору: либо сведения, либо разряд отказа с подробностью.</summary>
internal readonly record struct ComReadResult(OneCConfigInfo? Info, ComFailureKind Failure, string? Detail)
{
    public static ComReadResult Ok(OneCConfigInfo info) => new(info, ComFailureKind.None, null);
    public static ComReadResult Fail(ComFailureKind kind, string? detail = null) => new(null, kind, detail);
}

/// <summary>
/// Обращается к COM-коннектору 1С (<c>V8*.COMConnector</c>) в отдельном процессе-агенте.
/// <para>
/// Зачем. <c>comcntr.dll</c> грузится прямо в процесс приложения, и под CoreCLR (.NET 5+)
/// вызов его метода <c>Connect</c> обрывает процесс нативным fast-fail
/// (<c>0xC0000409</c>, STATUS_STACK_BUFFER_OVERRUN) — без управляемого исключения, поэтому
/// его нельзя ни перехватить, ни записать в журнал. Под .NET Framework 4.8 тот же вызов
/// возвращает обычное исключение, то есть дело в рантайме, а не в данных базы: обрывается
/// даже подключение к заведомо рабочей базе. Из-за этого приложение молча умирало на старте,
/// как только в списке была хотя бы одна файловая или клиент-серверная база с незаполненными
/// ConfigurationName/ConfigurationVersion — для них запускается фоновое дочитывание сведений.
/// </para>
/// <para>
/// Как. COM живёт в дочернем экземпляре этого же приложения, запущенном с ключом
/// <see cref="SwitchName"/>. Агент один на сессию и обслуживает запросы построчно, поэтому
/// список из десятков баз не превращается в десятки запусков. Строка подключения содержит
/// пароль и передаётся через stdin, а не аргументом: командная строка процесса видна другим
/// процессам пользователя и попадает в журналы аудита запуска. Полезная нагрузка едет
/// в Base64 — иначе табуляция или перевод строки внутри пароля молча испортили бы и пароль,
/// и сам построчный протокол.
/// </para>
/// </summary>
internal static class ComReadHost
{
    /// <summary>Ключ командной строки, включающий режим агента.</summary>
    public const string SwitchName = "--read-config-com";

    /// <summary>Префикс строки результата в стандартном выводе агента.</summary>
    private const string ResultPrefix = "CFGINFO\t";

    /// <summary>Запас к таймауту запроса: агент должен успеть ответить сам, прежде чем его убьют.</summary>
    private const int AgentGraceMs = 2000;

    /// <summary>
    /// Сколько подряд идущих отказов агента считать системными. Разовый сбой (антивирус,
    /// убийство из диспетчера) не должен глушить COM на всю сессию, а систематический
    /// (нативный fast-fail на каждом вызове) обязан — иначе на списке из десятков баз
    /// мы уроним по процессу на каждую.
    /// </summary>
    private const int FailuresBeforeLatch = 2;

    /// <summary>
    /// Сколько ждать, пока умирающий процесс завершится сам, прежде чем убивать его.
    /// Нужно, чтобы снять настоящий код возврата: Windows Error Reporting придерживает
    /// упавший процесс, а код убийства (0xFFFFFFFF) для диагностики бесполезен.
    /// </summary>
    private const int SelfExitGraceMs = 500;

    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static Process? _agent;
    private static StreamWriter? _agentInput;
    private static StreamReader? _agentOutput;
    private static int _comUnavailable;
    private static int _consecutiveFailures;
    private static int _shuttingDown;

    /// <summary>
    /// COM признан неработоспособным в этой сессии. Дальнейшие попытки пропускаются
    /// без запуска процессов, пока не будет вызван <see cref="ResetAvailability"/>.
    /// </summary>
    public static bool ComUnavailable => Volatile.Read(ref _comUnavailable) != 0;

    /// <summary>
    /// Снимает признак недоступности: явные действия пользователя (регистрация коннектора,
    /// команда обновления сведений) должны давать возможность попробовать снова.
    /// Вызывать вместе с <see cref="OneCComConnector.ResetAvailabilityCache"/> — иначе
    /// останется второй, более ранний выключатель.
    /// </summary>
    public static void ResetAvailability()
    {
        // Монитор здесь брать нельзя: он удерживается всё время запроса, а метод
        // вызывается из потока интерфейса по команде пользователя — окно замирало бы
        // на время текущего чтения ровно тогда, когда пользователь пытается COM починить.
        // Оба поля атомарны сами по себе, взаимная согласованность не требуется.
        Volatile.Write(ref _comUnavailable, 0);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    // ---------------------------------------------------------------- сторона родителя

    /// <summary>
    /// Читает наименование и версию конфигурации через агента.
    /// Основной процесс не пострадает, даже если COM-вызов оборвёт агента.
    /// </summary>
    public static ComReadResult Read(string connectString, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(connectString))
            return ComReadResult.Fail(ComFailureKind.Transport);

        if (ComUnavailable)
            return ComReadResult.Fail(ComFailureKind.Disabled);

        lock (Sync)
        {
            // Перепроверка под монитором: пока мы ждали очереди, соседний запрос мог
            // уже уронить агента и защёлкнуть недоступность.
            if (ComUnavailable)
                return ComReadResult.Fail(ComFailureKind.Disabled);

            if (!EnsureAgent(out var startDetail))
                return ComReadResult.Fail(ComFailureKind.AgentStart, startDetail);

            var input = _agentInput;
            var output = _agentOutput;
            if (input is null || output is null)
                return ComReadResult.Fail(ComFailureKind.AgentStart);

            try
            {
                input.WriteLine(
                    timeoutMs.ToString(CultureInfo.InvariantCulture) + "\t" + Encode(connectString));

                var pending = Task.Run(() => output.ReadLine());
                // Задачу мы можем бросить, не дождавшись. Штатно она завершается сама
                // (закрытый канал даёт null), но наблюдателя вешаем на случай исключения:
                // иначе оно всплывёт в TaskScheduler.UnobservedTaskException, а тот
                // показывает пользователю диалог о фатальной ошибке.
                _ = pending.ContinueWith(static t => _ = t.Exception,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

                if (!pending.Wait(timeoutMs + AgentGraceMs))
                {
                    // Живой агент отвечает сам не позже собственного Join(timeoutMs), поэтому
                    // молчание дольше бюджета означает, что агента уже нет. Ждать закрытия
                    // канала нельзя: на машинах с включённым Windows Error Reporting упавший
                    // процесс удерживается, и EOF приходит через десятки секунд — крах
                    // выглядел бы обычным таймаутом, и защёлка не сработала бы никогда.
                    return AgentFailed(StopAgent());
                }

                var line = pending.Result;
                if (line is null)
                {
                    // Закрытие приложения тоже обрывает канал. Это не отказ COM: считать
                    // его крахом значило бы писать в журнал ложную запись и приближать
                    // защёлку на каждом выходе во время фонового дочитывания.
                    if (Volatile.Read(ref _shuttingDown) != 0)
                        return ComReadResult.Fail(ComFailureKind.Transport);

                    return AgentFailed(StopAgent());
                }

                var result = ParseResponse(line);

                if (result.Failure == ComFailureKind.NotRegistered)
                {
                    // Отсутствие коннектора само не изменится — не гоняем агента впустую.
                    Volatile.Write(ref _comUnavailable, 1);
                }
                else if (result.Failure == ComFailureKind.Timeout)
                {
                    // Агент ответил, но внутри него остался повисший STA-поток с незакрытым
                    // COM-объектом. Оставлять такого агента нельзя: потоки будут копиться,
                    // а поздний обрыв одного из них припишется чужому запросу.
                    StopAgent();
                }
                else if (result.Failure == ComFailureKind.None)
                {
                    // Счётчик сбрасывает только успех. Сбрасывать его на любой ответ
                    // означало бы, что на чередующемся списке («упала — ответила — упала»)
                    // двух отказов подряд не наберётся никогда, и агент будет падать
                    // на каждой проблемной базе до конца списка.
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                }

                return result;
            }
            catch (Exception ex)
            {
                StopAgent();
                return ComReadResult.Fail(ComFailureKind.Transport, ex.Message);
            }
        }
    }

    /// <summary>
    /// Останавливает агента. Намеренно не берёт <see cref="Sync"/>: метод вызывается из
    /// <c>App.OnExit</c> в потоке интерфейса, а монитор удерживается всё время запроса —
    /// иначе закрытие окна замирало бы до конца текущего чтения.
    /// </summary>
    public static void Shutdown()
    {
        // Признак ставим до того, как забирать процесс: активный Read должен понять,
        // что оборванный канал — это выход из приложения, а не отказ COM. И EnsureAgent
        // не должен в этот момент поднимать нового агента, который переживёт закрытие.
        Volatile.Write(ref _shuttingDown, 1);

        var process = Interlocked.Exchange(ref _agent, null);
        _agentInput = null;
        _agentOutput = null;
        KillAndDispose(process, waitForExitMs: 0);
    }

    /// <summary>Учитывает отказ агента и решает, пора ли глушить COM на сессию.</summary>
    private static ComReadResult AgentFailed(int exitCode)
    {
        if (Interlocked.Increment(ref _consecutiveFailures) >= FailuresBeforeLatch)
            Volatile.Write(ref _comUnavailable, 1);

        return ComReadResult.Fail(ComFailureKind.AgentCrashed, FormatExitCode(exitCode));
    }

    private static string? FormatExitCode(int exitCode) =>
        exitCode == 0 ? null : "0x" + exitCode.ToString("X8", CultureInfo.InvariantCulture);

    private static bool EnsureAgent(out string? detail)
    {
        detail = null;

        if (Volatile.Read(ref _shuttingDown) != 0)
        {
            // Приложение закрывается: поднимать агента незачем — он пережил бы родителя.
            detail = "shutdown";
            return false;
        }

        // HasExited бросает InvalidOperationException, если Shutdown из другого потока
        // успел освободить объект между чтением поля и обращением к свойству.
        try
        {
            if (_agent is { HasExited: false } && _agentInput is not null && _agentOutput is not null)
                return true;
        }
        catch (InvalidOperationException)
        {
        }

        StopAgent();

        var host = Environment.ProcessPath;
        if (string.IsNullOrEmpty(host))
        {
            detail = "Environment.ProcessPath";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        // Запуск через `dotnet App.dll`: ProcessPath указывает на dotnet.exe, и одного ключа мало.
        if (IsDotnetHost(host))
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(entry))
            {
                detail = "dotnet host";
                return false;
            }
            psi.ArgumentList.Add(entry);
        }

        psi.ArgumentList.Add(SwitchName);

        try
        {
            var process = Process.Start(psi);
            if (process is null)
            {
                detail = "Process.Start";
                return false;
            }

            // stderr обязательно вычитывать, иначе переполнение буфера подвесит агента
            // на записи, а нас — на чтении stdout. Содержимое нам не нужно.
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            _agent = process;
            _agentInput = new StreamWriter(process.StandardInput.BaseStream, Utf8NoBom) { AutoFlush = true };
            _agentOutput = process.StandardOutput;
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            StopAgent();
            return false;
        }
    }

    private static bool IsDotnetHost(string path) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Останавливает агента и возвращает его код возврата (0, если узнать не удалось).</summary>
    private static int StopAgent()
    {
        var process = Interlocked.Exchange(ref _agent, null);
        _agentInput = null;
        _agentOutput = null;
        return KillAndDispose(process, waitForExitMs: 3000);
    }

    private static int KillAndDispose(Process? process, int waitForExitMs)
    {
        if (process is null)
            return 0;

        var exitCode = 0;
        try
        {
            // Код возврата снимаем до убийства: иначе настоящий 0xC0000409 подменится
            // кодом принудительного завершения (Kill даёт 0xFFFFFFFF), и единственная
            // примета того самого дефекта, ради которого всё это построено, пропадёт.
            // Поэтому сначала даём процессу короткий шанс умереть самому: при нативном
            // fast-fail он уже умирает, просто его может придерживать Windows Error Reporting.
            if (!process.HasExited && waitForExitMs > 0)
                process.WaitForExit(Math.Min(waitForExitMs, SelfExitGraceMs));

            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
            else
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже мёртв */ }
                // Код убитого процесса не берём: он всегда 0xFFFFFFFF и ничего не говорит.
                if (waitForExitMs > 0)
                    process.WaitForExit(waitForExitMs);
            }
        }
        catch
        {
            // Код возврата — только для диагностики, ради него падать незачем.
        }
        finally
        {
            try { process.Dispose(); } catch { }
        }

        return exitCode;
    }

    private static ComReadResult ParseResponse(string line)
    {
        if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            return ComReadResult.Fail(ComFailureKind.Transport);

        var parts = line[ResultPrefix.Length..].Split('\t');
        if (parts.Length >= 3 && string.Equals(parts[0], "OK", StringComparison.Ordinal))
        {
            // Повреждённое поле — это сбой обмена, а не успешное чтение пустого имени:
            // иначе испорченный ответ выглядел бы удачей и сбрасывал счётчик отказов.
            if (!TryDecode(parts[1], out var name) || !TryDecode(parts[2], out var version))
                return ComReadResult.Fail(ComFailureKind.Transport);

            return ComReadResult.Ok(new OneCConfigInfo(name, version));
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "ERR", StringComparison.Ordinal))
        {
            var kind = parts[1] switch
            {
                "NOTREG" => ComFailureKind.NotRegistered,
                "NOINST" => ComFailureKind.InstanceFailed,
                "TIMEOUT" => ComFailureKind.Timeout,
                "NOCONN" => ComFailureKind.NoConnection,
                "METAPROP" => ComFailureKind.MetadataProperty,
                "METAREAD" => ComFailureKind.MetadataRead,
                "DBERR" => ComFailureKind.DatabaseError,
                _ => ComFailureKind.Transport
            };
            return ComReadResult.Fail(kind, parts.Length >= 3 ? Decode(parts[2]) : null);
        }

        return ComReadResult.Fail(ComFailureKind.Transport);
    }

    // Полезная нагрузка едет в Base64: строка подключения и сообщения 1С могут содержать
    // табуляцию и переводы строк, а протокол построчный и разделён табуляцией. Прежняя
    // «очистка» таких символов молча портила пароль и давала необъяснимый отказ входа.
    private static string Encode(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string? value) =>
        TryDecode(value, out var decoded) ? decoded : string.Empty;

    private static bool TryDecode(string? value, out string decoded)
    {
        if (string.IsNullOrEmpty(value))
        {
            decoded = string.Empty;
            return true;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch
        {
            decoded = string.Empty;
            return false;
        }
    }

    // ---------------------------------------------------------------- сторона агента

    /// <summary>
    /// Если приложение запущено как агент — обслуживает запросы до закрытия stdin и возвращает true.
    /// Вызывается первой строкой Main, до создания Application: агенту не нужны ни WPF, ни темы.
    /// </summary>
    public static bool TryHandleCommandLine(string[]? args)
    {
        if (args is null)
            return false;

        var isAgent = Array.Exists(args, a =>
            string.Equals(a, SwitchName, StringComparison.OrdinalIgnoreCase));
        if (!isAgent)
            return false;

        try
        {
            RunAgentLoop();
        }
        catch (IOException)
        {
            // Родитель закрылся посреди обмена — выходим тихо, а не аварийно.
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            // Агент не должен завершаться необработанным исключением: это даёт лишний
            // отчёт WER и выглядит как крах COM, которым не является.
        }

        return true;
    }

    private static void RunAgentLoop()
    {
        // Кодировку задаём своими потоками, а не через Console.OutputEncoding: у процесса
        // без консоли Console отдаёт кодовую страницу ANSI, и кириллица в имени конфигурации
        // приезжала бы родителю мусором.
        using var input = new StreamReader(Console.OpenStandardInput(), Utf8NoBom);
        using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = true };

        string? request;
        while ((request = input.ReadLine()) is not null)
        {
            if (request.Length == 0)
                continue;

            string response;
            try
            {
                response = HandleRequest(request);
            }
            catch (Exception ex)
            {
                response = ResultPrefix + "ERR\tDBERR\t" + Encode(ex.Message);
            }

            output.WriteLine(response);
        }
    }

    private static string HandleRequest(string request)
    {
        var tab = request.IndexOf('\t');
        var timeoutMs = 8000;
        string payload;

        if (tab > 0 && int.TryParse(request[..tab], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            timeoutMs = parsed;
            payload = request[(tab + 1)..];
        }
        else
        {
            payload = request;
        }

        var connectString = Decode(payload);
        if (connectString.Length == 0)
            return ResultPrefix + "ERR\tDBERR\t" + Encode("empty request");

        var info = ReadInProcess(connectString, timeoutMs, out var kind, out var detail);
        return info is null
            ? ResultPrefix + "ERR\t" + KindToToken(kind) + "\t" + Encode(detail)
            : ResultPrefix + "OK\t" + Encode(info.Value.Name) + "\t" + Encode(info.Value.Version);
    }

    private static string KindToToken(ComFailureKind kind) => kind switch
    {
        ComFailureKind.NotRegistered => "NOTREG",
        ComFailureKind.InstanceFailed => "NOINST",
        ComFailureKind.Timeout => "TIMEOUT",
        ComFailureKind.NoConnection => "NOCONN",
        ComFailureKind.MetadataProperty => "METAPROP",
        ComFailureKind.MetadataRead => "METAREAD",
        _ => "DBERR"
    };

    /// <summary>
    /// Собственно COM-обращение. Живёт только в агенте: именно здесь возможен нативный обрыв,
    /// который мы и изолируем. ProgID перебираются, как в OneCComConnector.KnownProgIds.
    /// </summary>
    private static OneCConfigInfo? ReadInProcess(
        string connectString, int timeoutMs, out ComFailureKind kind, out string? detail)
    {
        OneCConfigInfo? result = null;
        var localKind = ComFailureKind.NotRegistered;
        string? localDetail = null;

        var thread = new Thread(() =>
        {
            foreach (var progId in OneCComConnector.KnownProgIds)
            {
                object? connector;

                // Получение типа и создание объекта — отдельно от подключения. Их отказ
                // означает «эта версия платформы непригодна», и надо пробовать следующую:
                // V83 нередко остаётся в реестре после сноса платформы, а рабочим оказывается V82.
                try
                {
                    var type = Type.GetTypeFromProgID(progId);
                    if (type is null)
                        continue; // Эта версия платформы не зарегистрирована — следующая.

                    connector = Activator.CreateInstance(type);
                    if (connector is null)
                    {
                        localKind = ComFailureKind.InstanceFailed;
                        localDetail = progId;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // ProgID есть, но объект не создаётся: битая регистрация, несоответствие
                    // разрядности, DLL занята. Пробуем следующую версию, но причину
                    // запоминаем — иначе отказ выдаётся за «коннектор не зарегистрирован»,
                    // а этот вердикт глушит COM на всю сессию с первого раза.
                    localKind = ComFailureKind.InstanceFailed;
                    localDetail = progId + ": " + (ex.InnerException ?? ex).Message;
                    continue;
                }

                object? connection = null;
                object? metadata = null;
                try
                {
                    connection = connector.GetType().InvokeMember(
                        "Connect", BindingFlags.InvokeMethod, null, connector,
                        new object[] { connectString });
                    if (connection is null)
                    {
                        localKind = ComFailureKind.NoConnection;
                        localDetail = progId;
                        continue;
                    }

                    metadata = InvokeGet(connection, "Metadata");
                    if (metadata is null)
                    {
                        localKind = ComFailureKind.MetadataProperty;
                        return;
                    }

                    var name = AsString(metadata, "Name") ?? AsString(metadata, "Synonym") ?? string.Empty;
                    var version = AsString(metadata, "Version") ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
                    {
                        localKind = ComFailureKind.MetadataRead;
                        return;
                    }

                    result = new OneCConfigInfo(name, version);
                    localKind = ComFailureKind.None;
                    return;
                }
                catch (Exception ex)
                {
                    // Коннектор найден и ответил: это ошибка конкретной базы (нет прав,
                    // база занята, неверный путь). Перебирать остальные ProgID незачем.
                    localKind = ComFailureKind.DatabaseError;
                    localDetail = (ex.InnerException ?? ex).Message;
                    return;
                }
                finally
                {
                    Release(metadata);
                    Release(connection);
                    Release(connector);
                }
            }
        })
        {
            IsBackground = true,
            Name = "1C-COM-ConfigRead"
        };

        // STA обязателен для COM-объектов 1С.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            // Поток фоновый и завершению агента не мешает. Родитель на этот ответ агента
            // убьёт: внутри остался повисший COM-вызов, и переиспользовать процесс нельзя.
            kind = ComFailureKind.Timeout;
            detail = timeoutMs.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        kind = localKind;
        detail = localDetail;
        return result;
    }

    private static object? InvokeGet(object target, string property)
    {
        try
        {
            return target.GetType().InvokeMember(
                property, BindingFlags.GetProperty, null, target, null);
        }
        catch
        {
            return null;
        }
    }

    private static string? AsString(object target, string property) =>
        InvokeGet(target, property)?.ToString();

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Освобождение COM-объекта не должно ронять агента.
        }
    }
}
#endif
