#if WINDOWS
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Выполняет обращения к COM-коннектору 1С (<c>V83.COMConnector</c>) в отдельном процессе.
/// <para>
/// Причина: <c>comcntr.dll</c> грузится прямо в процесс приложения, и под CoreCLR (.NET 5+)
/// вызов его метода <c>Connect</c> обрывает процесс нативным fast-fail
/// (<c>0xC0000409</c>, STATUS_STACK_BUFFER_OVERRUN) — без управляемого исключения, поэтому
/// его нельзя ни перехватить, ни записать в журнал. Под .NET Framework 4.8 тот же вызов
/// возвращает обычное исключение, то есть дело в рантайме, а не в данных базы: обрывается
/// даже подключение к заведомо рабочей базе.
/// </para>
/// <para>
/// Из-за этого приложение молча умирало на старте, как только в списке была хотя бы одна
/// файловая или клиент-серверная база с незаполненными ConfigurationName/ConfigurationVersion:
/// для таких баз запускалось фоновое дочитывание сведений о конфигурации.
/// </para>
/// <para>
/// Здесь COM-вызов вынесен в дочерний экземпляр этого же приложения, запускаемый с ключом
/// <see cref="SwitchName"/>. Падение дочернего процесса больше не трогает основной — тот
/// лишь видит ненулевой код возврата. После первого такого падения COM помечается
/// недоступным на всю сессию (<see cref="ComUnavailable"/>): иначе на списке из десятков
/// баз мы бы плодили десятки заведомо гибнущих процессов.
/// </para>
/// </summary>
internal static class ComReadHost
{
    /// <summary>Ключ командной строки, включающий режим дочернего COM-агента.</summary>
    public const string SwitchName = "--read-config-com";

    /// <summary>Префикс строки результата в стандартном выводе дочернего процесса.</summary>
    private const string ResultPrefix = "CFGINFO\t";

    private static int _comUnavailable;

    /// <summary>
    /// COM признан неработоспособным в этой сессии (дочерний процесс погиб либо коннектор
    /// не зарегистрирован). Дальнейшие попытки пропускаются без запуска процессов.
    /// </summary>
    public static bool ComUnavailable => Volatile.Read(ref _comUnavailable) != 0;

    /// <summary>Текст последней ошибки для диагностики в интерфейсе.</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// Если приложение запущено как дочерний COM-агент — выполняет чтение и возвращает true.
    /// Вызывается в самом начале старта, до создания окон.
    /// </summary>
    public static bool TryHandleCommandLine(string[]? args)
    {
        if (args is null) return false;

        var index = Array.FindIndex(args, a =>
            string.Equals(a, SwitchName, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
            return false;

        var connectString = args[index + 1];
        string line;
        try
        {
            var info = ReadInProcess(connectString, out var error);
            line = info is null
                ? ResultPrefix + "ERR\t" + Sanitize(error)
                : ResultPrefix + "OK\t" + Sanitize(info.Value.Name) + "\t" + Sanitize(info.Value.Version);
        }
        catch (Exception ex)
        {
            line = ResultPrefix + "ERR\t" + Sanitize(ex.Message);
        }

        Console.Out.WriteLine(line);
        Console.Out.Flush();
        return true;
    }

    /// <summary>
    /// Читает наименование и версию конфигурации, выполняя COM в отдельном процессе.
    /// Возвращает null, если прочитать не удалось; основной процесс при этом не страдает.
    /// </summary>
    public static OneCConfigInfo? Read(string connectString, int timeoutMs)
    {
        LastError = null;

        if (string.IsNullOrWhiteSpace(connectString))
            return null;

        if (ComUnavailable)
        {
            LastError = "COM-коннектор недоступен (отключён после предыдущего сбоя).";
            return null;
        }

        var host = Environment.ProcessPath;
        if (string.IsNullOrEmpty(host))
        {
            LastError = "Не удалось определить путь к исполняемому файлу.";
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(SwitchName);
        psi.ArgumentList.Add(connectString);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                LastError = "Не удалось запустить дочерний процесс чтения конфигурации.";
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                TryKill(process);
                LastError = string.Format(
                    CultureInfo.CurrentCulture,
                    "Превышен таймаут чтения конфигурации ({0} мс).", timeoutMs);
                return null;
            }

            if (process.ExitCode != 0)
            {
                // Дочерний процесс убит нативным сбоем COM-коннектора — больше не пытаемся.
                Volatile.Write(ref _comUnavailable, 1);
                LastError = string.Format(
                    CultureInfo.CurrentCulture,
                    "COM-коннектор аварийно завершил дочерний процесс (код 0x{0:X8}). "
                    + "Чтение сведений о конфигурации через COM отключено до перезапуска.",
                    process.ExitCode);
                return null;
            }

            return ParseResult(stdout);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private static OneCConfigInfo? ParseResult(string? stdout)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            LastError = "Дочерний процесс не вернул результат.";
            return null;
        }

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith(ResultPrefix, StringComparison.Ordinal))
                continue;

            var parts = line[ResultPrefix.Length..].Split('\t');
            if (parts.Length >= 3 && string.Equals(parts[0], "OK", StringComparison.Ordinal))
                return new OneCConfigInfo(parts[1], parts[2]);

            LastError = parts.Length >= 2 ? parts[1] : "COM-чтение не удалось.";
            return null;
        }

        LastError = "Дочерний процесс не вернул результат.";
        return null;
    }

    /// <summary>
    /// Собственно COM-обращение. Выполняется только в дочернем процессе — именно здесь
    /// возможен нативный обрыв, который мы и изолируем.
    /// </summary>
    private static OneCConfigInfo? ReadInProcess(string connectString, out string? error)
    {
        OneCConfigInfo? result = null;
        string? localError = null;

        var thread = new Thread(() =>
        {
            object? connector = null;
            object? connection = null;
            try
            {
                var type = Type.GetTypeFromProgID("V83.COMConnector");
                if (type is null)
                {
                    localError = "COM-коннектор V83.COMConnector не зарегистрирован.";
                    return;
                }

                connector = Activator.CreateInstance(type);
                if (connector is null)
                {
                    localError = "Не удалось создать экземпляр V83.COMConnector.";
                    return;
                }

                connection = type.InvokeMember(
                    "Connect", BindingFlags.InvokeMethod, null, connector,
                    new object[] { connectString });
                if (connection is null)
                {
                    localError = "COM-коннектор не вернул соединение.";
                    return;
                }

                var metadata = connection.GetType().InvokeMember(
                    "Metadata", BindingFlags.GetProperty, null, connection, null);
                if (metadata is null)
                {
                    localError = "Не удалось получить свойство Metadata.";
                    return;
                }

                var name = GetString(metadata, "Name") ?? GetString(metadata, "Synonym") ?? string.Empty;
                var version = GetString(metadata, "Version") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
                {
                    localError = "Метаданные прочитать не удалось.";
                    return;
                }

                result = new OneCConfigInfo(name, version);
            }
            catch (Exception ex)
            {
                localError = ex.Message;
            }
            finally
            {
                Release(connection);
                Release(connector);
            }
        })
        {
            IsBackground = true,
            Name = "1C-COM-ConfigRead"
        };

        // STA обязателен для COM-объектов 1С.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        error = localError;
        return result;
    }

    private static string? GetString(object target, string property)
    {
        try
        {
            var value = target.GetType().InvokeMember(
                property, BindingFlags.GetProperty, null, target, null);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // Освобождение COM-объекта не должно ронять процесс.
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    /// <summary>Убирает символы, ломающие построчный протокол обмена с дочерним процессом.</summary>
    private static string Sanitize(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
#endif
