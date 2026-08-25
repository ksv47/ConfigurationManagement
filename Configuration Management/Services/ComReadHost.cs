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
/// в Base64 — иначе табуляция или перевод строки внутри значения испортили бы протокол.
/// </para>
/// <para>
/// Пароль. Текст ошибки, который возвращает 1С, умеет цитировать строку подключения целиком.
/// Разбирать такой текст по грамматике бесполезно: <see cref="OneCComConnector"/> строит
/// строку без экранирования, поэтому и кавычка, и <c>;</c>, и перевод строки внутри пароля
/// делают её неоднозначной, и любое правило разбора ломается на очередном спецсимволе.
/// Поэтому пароль передаётся агенту отдельным полем и вырезается из ответа буквальным
/// вхождением, ещё до отправки — так секрет не попадает даже в канал.
/// </para>
/// </summary>
internal static class ComReadHost
{
    /// <summary>Ключ командной строки, включающий режим агента.</summary>
    public const string SwitchName = "--read-config-com";

    /// <summary>Префикс строки результата в стандартном выводе агента.</summary>
    private const string ResultPrefix = "CFGINFO\t";

    /// <summary>Чем заменяется вырезанный секрет.</summary>
    private const string Redacted = "***";

    /// <summary>Запас к таймауту запроса: агент должен успеть ответить сам, прежде чем его убьют.</summary>
    private const int AgentGraceMs = 2000;

    /// <summary>
    /// Сколько отказов агента подряд считать системными. Разовый сбой (антивирус, убийство
    /// из диспетчера) не должен глушить COM на всю сессию, а систематический — обязан.
    /// </summary>
    private const int FailuresBeforeLatch = 2;

    /// <summary>
    /// Сколько ждать, пока умирающий процесс завершится сам, прежде чем убивать его.
    /// Нужно, чтобы снять настоящий код возврата: Windows Error Reporting придерживает
    /// упавший процесс, а код убийства (0xFFFFFFFF) для диагностики бесполезен.
    /// </summary>
    private const int SelfExitGraceMs = 3000;

    private static readonly object Sync = new();

    /// <summary>
    /// Короткий монитор только для состояния защёлки. Отдельный от <see cref="Sync"/>:
    /// тот удерживается всё время запроса, а состояние меняется в том числе из потока
    /// интерфейса. Держится наносекунды и никогда — во время ввода-вывода, поэтому
    /// заморозить интерфейс не может. Раньше поля менялись по отдельности атомарными
    /// записями, и между проверкой поколения и записью защёлки оставалась щель, в которую
    /// проваливалась команда пользователя.
    /// </summary>
    private static readonly object StateLock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Переменные среды, которыми рантайм включает запись аварийного дампа. У агента
    /// их гасим: в его памяти в момент падения лежит строка подключения с паролем.
    /// </summary>
    private static readonly string[] DumpEnvironmentVariables =
    {
        "DOTNET_DbgEnableMiniDump",
        "DOTNET_DbgMiniDumpType",
        "DOTNET_DbgMiniDumpName",
        "DOTNET_CreateDumpDiagnostics",
        "DOTNET_EnableCrashReport",
        "COMPlus_DbgEnableMiniDump",
        "COMPlus_DbgMiniDumpType",
        "COMPlus_DbgMiniDumpName"
    };

    /// <summary>Строгий декодер: недопустимую последовательность нельзя молча превращать в U+FFFD.</summary>
    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Process? _agent;
    private static StreamWriter? _agentInput;
    private static StreamReader? _agentOutput;
    private static int _comUnavailable;
    private static int _consecutiveFailures;
    private static int _shuttingDown;
    private static int _sequence;

    /// <summary>
    /// Поколение сброса. Растёт при каждом <see cref="ResetAvailability"/>. Отказ запроса,
    /// начатого до сброса, не должен защёлкивать недоступность заново — иначе ручная
    /// команда пользователя молча съедалась бы уже летящим фоновым запросом.
    /// </summary>
    private static int _resetEpoch;

    /// <summary>
    /// COM признан неработоспособным в этой сессии. Дальнейшие попытки пропускаются
    /// без запуска процессов, пока не будет вызван <see cref="ResetAvailability"/>.
    /// </summary>
    public static bool ComUnavailable
    {
        get { lock (StateLock) return _comUnavailable != 0; }
    }

    /// <summary>
    /// Снимает признак недоступности: явные действия пользователя (регистрация коннектора,
    /// команда обновления сведений) должны давать возможность попробовать снова.
    /// Вызывать вместе с <see cref="OneCComConnector.ResetAvailabilityCache"/> — иначе
    /// останется второй, более ранний выключатель.
    /// </summary>
    public static void ResetAvailability()
    {
        lock (StateLock)
        {
            _resetEpoch++;
            _comUnavailable = 0;
            _consecutiveFailures = 0;
        }
    }

    /// <summary>Текущее поколение сброса — снимок на время запроса.</summary>
    private static int CurrentEpoch()
    {
        lock (StateLock) return _resetEpoch;
    }

    /// <summary>Успешное чтение обнуляет счётчик отказов.</summary>
    private static void NoteSuccess()
    {
        lock (StateLock) _consecutiveFailures = 0;
    }

    /// <summary>
    /// Глушит COM, если с начала запроса не было сброса. Отдельный метод нужен для
    /// разрядов, которые защёлкивают сразу, без накопления счётчика.
    /// </summary>
    private static void LatchIfSameEpoch(int epoch)
    {
        lock (StateLock)
        {
            if (_resetEpoch == epoch)
                _comUnavailable = 1;
        }
    }

    // ---------------------------------------------------------------- сторона родителя

    /// <summary>
    /// Читает наименование и версию конфигурации через агента.
    /// Основной процесс не пострадает, даже если COM-вызов оборвёт агента.
    /// </summary>
    /// <param name="connectString">Строка подключения (может содержать пароль).</param>
    /// <param name="secret">
    /// Пароль в чистом виде — чтобы агент вырезал его из текста ошибки 1С до отправки.
    /// Пустая строка означает, что вырезать нечего.
    /// </param>
    /// <param name="timeoutMs">Предельное время COM-вызова.</param>
    public static ComReadResult Read(string connectString, string? secret, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(connectString))
            return ComReadResult.Fail(ComFailureKind.Transport);

        if (ComUnavailable)
            return ComReadResult.Fail(ComFailureKind.Disabled);

        var epoch = CurrentEpoch();

        lock (Sync)
        {
            // Перепроверка под монитором: пока мы ждали очереди, соседний запрос мог
            // уже уронить агента и защёлкнуть недоступность.
            if (ComUnavailable)
                return ComReadResult.Fail(ComFailureKind.Disabled);

            if (!EnsureAgent(out var startDetail))
            {
                // Закрытие приложения — не отказ COM. Иначе на выходе во время фонового
                // дочитывания каждая следующая база писала бы в журнал ложную ошибку
                // запуска и приближала защёлку.
                if (Volatile.Read(ref _shuttingDown) != 0)
                    return ComReadResult.Fail(ComFailureKind.Transport);

                // Систематический отказ запуска — тоже отказ: иначе на списке из десятков баз
                // мы будем пытаться и писать в журнал по разу на каждую, и так каждый старт.
                return RegisterFailure(epoch, ComFailureKind.AgentStart, startDetail);
            }

            var input = _agentInput;
            var output = _agentOutput;
            if (input is null || output is null)
                return RegisterFailure(epoch, ComFailureKind.AgentStart, null);

            var seq = unchecked(++_sequence);

            try
            {
                input.WriteLine(string.Join("\t",
                    seq.ToString(CultureInfo.InvariantCulture),
                    timeoutMs.ToString(CultureInfo.InvariantCulture),
                    Encode(connectString),
                    Encode(secret ?? string.Empty)));

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
                    return RegisterFailure(epoch, ComFailureKind.AgentCrashed,
                        FormatExitCode(StopAgent(SelfExitGraceMs)));
                }

                var line = pending.Result;
                if (line is null)
                {
                    // Закрытие приложения тоже обрывает канал. Это не отказ COM: считать
                    // его крахом значило бы писать в журнал ложную запись и приближать
                    // защёлку на каждом выходе во время фонового дочитывания.
                    if (Volatile.Read(ref _shuttingDown) != 0)
                        return ComReadResult.Fail(ComFailureKind.Transport);

                    return RegisterFailure(epoch, ComFailureKind.AgentCrashed,
                        FormatExitCode(StopAgent(SelfExitGraceMs)));
                }

                var result = ParseResponse(line, seq, out var desynchronized);
                if (desynchronized)
                {
                    // Ответ не от нашего запроса. Дальше соответствие «запрос — ответ» уже
                    // не восстановить, а молча продолжать нельзя: сведения о конфигурации
                    // начали бы приписываться чужим базам без единого признака ошибки.
                    // Считаем отказом: систематический мусор в потоке агента иначе давал бы
                    // бесконечную череду перезапусков процесса без всякого ограничителя.
                    StopAgent(graceMs: 0);
                    return RegisterFailure(epoch, ComFailureKind.Transport, null);
                }

                switch (result.Failure)
                {
                    case ComFailureKind.NotRegistered:
                        // Отсутствие коннектора само не изменится — глушим и убираем агента.
                        LatchIfSameEpoch(epoch);
                        StopAgent(graceMs: 0);
                        break;

                    case ComFailureKind.Timeout:
                        // Агент ответил, но внутри него остался повисший STA-поток с незакрытым
                        // COM-объектом. Оставлять такого агента нельзя: потоки будут копиться,
                        // а поздний обрыв одного из них припишется чужому запросу. Грация здесь
                        // не нужна — мы точно знаем, что процесс жив.
                        StopAgent(graceMs: 0);
                        break;

                    case ComFailureKind.None:
                        // Счётчик сбрасывает только успех. Сбрасывать его на любой ответ
                        // означало бы, что на чередующемся списке двух отказов подряд
                        // не наберётся никогда.
                        NoteSuccess();
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                StopAgent(graceMs: 0);

                // Обрыв канала при закрытии приложения отказом не считаем.
                if (Volatile.Read(ref _shuttingDown) != 0)
                    return ComReadResult.Fail(ComFailureKind.Transport);

                return RegisterFailure(epoch, ComFailureKind.Transport, ex.Message);
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
        // что оборванный канал — это выход из приложения, а не отказ COM.
        Volatile.Write(ref _shuttingDown, 1);

        var process = Interlocked.Exchange(ref _agent, null);
        _agentInput = null;
        _agentOutput = null;
        KillAndDispose(process, graceMs: 0, waitAfterKillMs: 0);
    }

    /// <summary>Учитывает отказ и решает, пора ли глушить COM на сессию.</summary>
    private static ComReadResult RegisterFailure(int epoch, ComFailureKind kind, string? detail)
    {
        // Проверка поколения и изменение счётчика — одной неделимой операцией. Сброс,
        // случившийся после начала запроса, отменяет право этого запроса защёлкивать:
        // пользователь уже попросил попробовать снова.
        lock (StateLock)
        {
            if (_resetEpoch == epoch && ++_consecutiveFailures >= FailuresBeforeLatch)
                _comUnavailable = 1;
        }

        return ComReadResult.Fail(kind, detail);
    }

    /// <summary>Код возврата для показа. Null — код неизвестен, это отдельный случай.</summary>
    private static string? FormatExitCode(int? exitCode) =>
        exitCode is null ? null : "0x" + exitCode.Value.ToString("X8", CultureInfo.InvariantCulture);

    private static bool EnsureAgent(out string? detail)
    {
        detail = null;

        if (Volatile.Read(ref _shuttingDown) != 0)
        {
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

        StopAgent(graceMs: 0);

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

        // Агент наследует окружение родителя, а рантайм умеет писать дамп по переменным
        // среды. В памяти агента в момент падения лежит строка подключения с паролем,
        // поэтому диагностические дампы у него отключаем принудительно — независимо от
        // того, что настроено снаружи для основного приложения.
        foreach (var variable in DumpEnvironmentVariables)
            psi.Environment[variable] = string.Empty;

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

            // Пока мы запускали агента, приложение могло начать закрываться.
            if (Volatile.Read(ref _shuttingDown) != 0)
            {
                StopAgent(graceMs: 0);
                detail = "shutdown";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            StopAgent(graceMs: 0);
            return false;
        }
    }

    private static bool IsDotnetHost(string path) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Останавливает агента и возвращает его код возврата; null — если узнать не удалось.
    /// <paramref name="graceMs"/> — сколько ждать самостоятельного завершения: нужно только
    /// когда процесс предположительно умирает и код возврата нам важен.
    /// </summary>
    private static int? StopAgent(int graceMs)
    {
        var process = Interlocked.Exchange(ref _agent, null);
        _agentInput = null;
        _agentOutput = null;
        return KillAndDispose(process, graceMs, waitAfterKillMs: 3000);
    }

    private static int? KillAndDispose(Process? process, int graceMs, int waitAfterKillMs)
    {
        if (process is null)
            return null;

        int? exitCode = null;
        try
        {
            // Код возврата снимаем до убийства: иначе настоящий 0xC0000409 подменится
            // кодом принудительного завершения (Kill даёт 0xFFFFFFFF), и единственная
            // примета того самого дефекта, ради которого всё построено, пропадёт.
            if (!process.HasExited && graceMs > 0)
                process.WaitForExit(graceMs);

            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
            else
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже мёртв */ }
                // Код убитого процесса не берём: он всегда 0xFFFFFFFF и ничего не говорит.
                if (waitAfterKillMs > 0)
                    process.WaitForExit(waitAfterKillMs);
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

    private static ComReadResult ParseResponse(string line, int expectedSeq, out bool desynchronized)
    {
        desynchronized = false;

        if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
        {
            desynchronized = true;
            return ComReadResult.Fail(ComFailureKind.Transport);
        }

        var parts = line[ResultPrefix.Length..].Split('\t');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq)
            || seq != expectedSeq)
        {
            desynchronized = true;
            return ComReadResult.Fail(ComFailureKind.Transport);
        }

        // Ровно столько полей, сколько предусмотрено: лишние означают, что мы читаем
        // не то, что думаем.
        if (parts.Length == 4 && string.Equals(parts[1], "OK", StringComparison.Ordinal))
        {
            if (!TryDecode(parts[2], out var name) || !TryDecode(parts[3], out var version))
                return ComReadResult.Fail(ComFailureKind.Transport);

            return ComReadResult.Ok(new OneCConfigInfo(name, version));
        }

        if (parts.Length == 4 && string.Equals(parts[1], "ERR", StringComparison.Ordinal))
        {
            var kind = parts[2] switch
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
            return ComReadResult.Fail(kind, Decode(parts[3]));
        }

        return ComReadResult.Fail(ComFailureKind.Transport);
    }

    // Полезная нагрузка едет в Base64: строка подключения и сообщения 1С могут содержать
    // табуляцию и переводы строк, а протокол построчный и разделён табуляцией.
    private static string Encode(string? value) =>
        Convert.ToBase64String(Utf8NoBom.GetBytes(value ?? string.Empty));

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
            // Строгий декодер: недопустимую последовательность нельзя молча превращать
            // в U+FFFD и выдавать за успешно прочитанное имя конфигурации.
            decoded = Utf8Strict.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch
        {
            decoded = string.Empty;
            return false;
        }
    }

    // ---------------------------------------------------------------- сторона агента

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;

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

        // Падение агента — штатное событие этой схемы, а не повод показывать пользователю
        // системное окно «Unknown Hard Error» и писать на диск дамп, в памяти которого
        // лежит строка подключения вместе с паролем.
        try { SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX); } catch { }

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

            var parts = request.Split('\t');
            var seq = parts.Length > 0 ? parts[0] : "0";

            string response;
            try
            {
                response = HandleRequest(parts, seq);
            }
            catch (Exception ex)
            {
                // Секрет здесь по построению не встречается, но полагаться на это нельзя:
                // достаточно будущей правки, которая положит строку запроса в текст
                // исключения. Режем и тут — секрет уже раскодирован рядом.
                var secret = parts.Length > 3 ? Decode(parts[3]) : null;
                response = ResultPrefix + seq + "\tERR\tDBERR\t" + Encode(Redact(ex.Message, secret));
            }

            output.WriteLine(response);
        }
    }

    private static string HandleRequest(string[] parts, string seq)
    {
        // Формат: seq \t timeoutMs \t base64(строка подключения) \t base64(пароль)
        if (parts.Length < 4)
            return ResultPrefix + seq + "\tERR\tDBERR\t" + Encode("malformed request");

        var timeoutMs = int.TryParse(parts[1], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 8000;

        var connectString = Decode(parts[2]);
        var secret = Decode(parts[3]);

        if (connectString.Length == 0)
            return ResultPrefix + seq + "\tERR\tDBERR\t" + Encode("empty request");

        var info = ReadInProcess(connectString, timeoutMs, out var kind, out var detail);

        // Имя и версию конфигурации не режем: они приходят из Metadata и строку подключения
        // содержать не могут, а вырезание по совпадению портило их до неузнаваемости —
        // числовой пароль превращал версию «1.2.1» в «***.2.***», и это сохранялось в данные.
        // Резать нужно только текст ошибки, который приходит от 1С в свободной форме.
        return info is null
            ? ResultPrefix + seq + "\tERR\t" + KindToToken(kind) + "\t" + Encode(Redact(detail, secret))
            : ResultPrefix + seq + "\tOK\t" + Encode(info.Value.Name)
              + "\t" + Encode(info.Value.Version);
    }

    /// <summary>
    /// Вырезает секрет из текста буквальным вхождением. Работает независимо от того,
    /// как значение записано в строке подключения — в кавычках, без них, с разделителем
    /// или переводом строки внутри, — потому что не разбирает грамматику вовсе.
    /// Побочный эффект короткого пароля: из текста может исчезнуть лишнее. Это
    /// сознательный размен в пользу безопасности.
    /// </summary>
    private static string? Redact(string? text, string? secret)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret))
            return text;

        return text.Replace(secret, Redacted, StringComparison.Ordinal);
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
                        continue;

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
                    // разрядности, DLL занята. Причину запоминаем — иначе отказ выдаётся
                    // за «коннектор не зарегистрирован», а этот вердикт глушит COM сразу.
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
                    // Ошибка подключения к конкретной базе — перебор прекращаем.
                    // Продолжать нельзя: на машине с двумя коннекторами один неверный
                    // пароль давал бы две-три неудачные аутентификации на сервере 1С,
                    // втрое быстрее набивая счётчик блокировки учётной записи, а итоговый
                    // диагноз доставался бы от последнего ProgID — про базу 8.3 пользователь
                    // читал бы сообщение от V81. Случай «коннектор непригоден» уже покрыт
                    // веткой InstanceFailed, которая срабатывает до Connect.
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
