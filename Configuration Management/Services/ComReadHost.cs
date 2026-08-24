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
    /// <summary>COM отключён на эту сессию после предыдущего краха или отсутствия коннектора.</summary>
    Disabled,
    /// <summary>Не удалось запустить процесс-агент.</summary>
    AgentStart,
    /// <summary>Агент погиб на COM-вызове (нативный fast-fail). В детали кладётся код возврата.</summary>
    AgentCrashed,
    /// <summary>Ответ не пришёл за отведённое время.</summary>
    Timeout,
    /// <summary>Сбой обмена с агентом (протокол, каналы).</summary>
    Transport,
    /// <summary>Ни один из известных ProgID не зарегистрирован.</summary>
    NotRegistered,
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
/// процессам пользователя и попадает в журналы аудита запуска.
/// </para>
/// <para>
/// Гибель агента основной процесс не задевает — он видит закрытый канал и код возврата.
/// После настоящего краха или отсутствия коннектора COM глушится на сессию
/// (<see cref="ComUnavailable"/>), сбросить можно через <see cref="ResetAvailability"/>.
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

    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static Process? _agent;
    private static StreamWriter? _agentInput;
    private static StreamReader? _agentOutput;
    private static int _comUnavailable;

    /// <summary>
    /// COM признан неработоспособным в этой сессии: агент погиб на COM-вызове либо ни один
    /// известный ProgID не зарегистрирован. Дальнейшие попытки пропускаются без запуска процессов.
    /// </summary>
    public static bool ComUnavailable => Volatile.Read(ref _comUnavailable) != 0;

    /// <summary>
    /// Снимает признак недоступности: явные действия пользователя (регистрация коннектора,
    /// команда обновления сведений) должны давать возможность попробовать снова.
    /// </summary>
    public static void ResetAvailability() => Volatile.Write(ref _comUnavailable, 0);

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
            if (!EnsureAgent(out var startDetail))
                return ComReadResult.Fail(ComFailureKind.AgentStart, startDetail);

            try
            {
                // Запрос: таймаут и строка подключения. Строка уже очищена от переводов строк
                // в BuildComConnectString, но подстрахуемся — протокол построчный.
                _agentInput!.WriteLine(
                    timeoutMs.ToString(CultureInfo.InvariantCulture) + "\t" + Sanitize(connectString));

                var reader = _agentOutput!;
                var pending = Task.Run(() => reader.ReadLine());
                if (!pending.Wait(timeoutMs + AgentGraceMs))
                {
                    // Ответа нет: агент завис внутри COM. Убиваем — иначе повиснем сами.
                    StopAgent();
                    return ComReadResult.Fail(ComFailureKind.Timeout,
                        timeoutMs.ToString(CultureInfo.InvariantCulture));
                }

                var line = pending.Result;
                if (line is null)
                {
                    // Канал закрылся: агента оборвал нативный fast-fail на COM-вызове.
                    var exitCode = StopAgent();
                    Volatile.Write(ref _comUnavailable, 1);
                    return ComReadResult.Fail(ComFailureKind.AgentCrashed,
                        "0x" + exitCode.ToString("X8", CultureInfo.InvariantCulture));
                }

                var result = ParseResponse(line);
                if (result.Failure == ComFailureKind.NotRegistered)
                {
                    // Отсутствие коннектора не изменится до перезапуска — не гоняем агента впустую.
                    Volatile.Write(ref _comUnavailable, 1);
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

    /// <summary>Останавливает агента, если он запущен. Вызывается при выходе из приложения.</summary>
    public static void Shutdown()
    {
        lock (Sync)
        {
            StopAgent();
        }
    }

    private static bool EnsureAgent(out string? detail)
    {
        detail = null;

        if (_agent is { HasExited: false } && _agentInput is not null && _agentOutput is not null)
            return true;

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

    private static bool IsDotnetHost(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Останавливает агента и возвращает его код возврата (0, если узнать не удалось).</summary>
    private static int StopAgent()
    {
        var exitCode = 0;
        var process = _agent;

        _agentInput = null;
        _agentOutput = null;
        _agent = null;

        if (process is null)
            return exitCode;

        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
            else
            {
                try { process.Kill(entireProcessTree: true); } catch { /* уже мёртв */ }
                if (process.WaitForExit(3000) && process.HasExited)
                    exitCode = process.ExitCode;
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
            return ComReadResult.Ok(new OneCConfigInfo(parts[1], parts[2]));

        if (parts.Length >= 2 && string.Equals(parts[0], "ERR", StringComparison.Ordinal))
        {
            var kind = parts[1] switch
            {
                "NOTREG" => ComFailureKind.NotRegistered,
                "TIMEOUT" => ComFailureKind.Timeout,
                "NOCONN" => ComFailureKind.NoConnection,
                "METAPROP" => ComFailureKind.MetadataProperty,
                "METAREAD" => ComFailureKind.MetadataRead,
                _ => ComFailureKind.DatabaseError
            };
            return ComReadResult.Fail(kind, parts.Length >= 3 ? parts[2] : null);
        }

        return ComReadResult.Fail(ComFailureKind.Transport);
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
                response = ResultPrefix + "ERR\tDBERR\t" + Sanitize(ex.Message);
            }

            output.WriteLine(response);
        }

        return true;
    }

    private static string HandleRequest(string request)
    {
        var tab = request.IndexOf('\t');
        var timeoutMs = 8000;
        string connectString;

        if (tab > 0 && int.TryParse(request[..tab], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            timeoutMs = parsed;
            connectString = request[(tab + 1)..];
        }
        else
        {
            connectString = request;
        }

        var info = ReadInProcess(connectString, timeoutMs, out var kind, out var detail);
        return info is null
            ? ResultPrefix + "ERR\t" + KindToToken(kind) + "\t" + Sanitize(detail)
            : ResultPrefix + "OK\t" + Sanitize(info.Value.Name) + "\t" + Sanitize(info.Value.Version);
    }

    private static string KindToToken(ComFailureKind kind) => kind switch
    {
        ComFailureKind.NotRegistered => "NOTREG",
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
                object? connector = null;
                object? connection = null;
                try
                {
                    var type = Type.GetTypeFromProgID(progId);
                    if (type is null)
                        continue; // Эта версия платформы не зарегистрирована — пробуем следующую.

                    connector = Activator.CreateInstance(type);
                    if (connector is null)
                        continue;

                    connection = type.InvokeMember(
                        "Connect", BindingFlags.InvokeMethod, null, connector,
                        new object[] { connectString });
                    if (connection is null)
                    {
                        localKind = ComFailureKind.NoConnection;
                        localDetail = progId;
                        continue;
                    }

                    var metadata = InvokeGet(connection, "Metadata");
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
                    // Ошибка конкретной базы (нет прав, база занята, неверный путь) — не повод
                    // перебирать остальные ProgID: коннектор найден и ответил.
                    localKind = ComFailureKind.DatabaseError;
                    localDetail = (ex.InnerException ?? ex).Message;
                    return;
                }
                finally
                {
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
            // Поток фоновый, дожидаться его незачем: родитель всё равно убьёт агента,
            // если сочтёт нужным. Отвечаем честно и продолжаем обслуживать очередь.
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

    /// <summary>Убирает символы, ломающие построчный протокол обмена.</summary>
    private static string Sanitize(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
#endif
