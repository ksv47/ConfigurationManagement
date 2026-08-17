using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Считывает наименование и версию конфигурации 1С (COM-коннектор или эвристика по файлу базы).
/// </summary>
public static class ConfigurationInfoService
{
    public readonly record struct ConfigInfo(string Name, string Version);

    /// <summary>
    /// Пытается прочитать имя и версию конфигурации для информационной базы.
    /// </summary>
    public static ConfigInfo? TryRead(Infobase ib, int timeoutMs = 8000)
    {
        if (ib is null) return null;

        try
        {
            var viaCom = TryReadViaCom(ib, timeoutMs);
            if (viaCom is not null)
                return viaCom;
        }
        catch
        {
            // COM недоступен / таймаут
        }

        try
        {
            if (ib.Connection?.Type == ConnectionType.File)
                return TryReadFromFileBase(ib.Connection.FilePath);
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Обновляет поля ConfigurationName / ConfigurationVersion, если удалось прочитать.
    /// Не затирает вручную заданные значения, если чтение не удалось.
    /// </summary>
    public static bool TryApply(Infobase ib, bool overwriteExisting = false)
    {
        if (ib is null) return false;
        if (!overwriteExisting
            && !string.IsNullOrWhiteSpace(ib.ConfigurationName)
            && !string.IsNullOrWhiteSpace(ib.ConfigurationVersion))
            return false;

        var info = TryRead(ib);
        if (info is null) return false;

        var changed = false;
        if (!string.IsNullOrWhiteSpace(info.Value.Name)
            && (overwriteExisting || string.IsNullOrWhiteSpace(ib.ConfigurationName)))
        {
            ib.ConfigurationName = info.Value.Name.Trim();
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(info.Value.Version)
            && (overwriteExisting || string.IsNullOrWhiteSpace(ib.ConfigurationVersion)))
        {
            ib.ConfigurationVersion = info.Value.Version.Trim();
            changed = true;
        }
        return changed;
    }

    private static ConfigInfo? TryReadViaCom(Infobase ib, int timeoutMs)
    {
        ConfigInfo? result = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = ReadViaComCore(ib);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "1C-COM-ConfigRead"
        };

        // STA нужен для COM 1С
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeoutMs))
            return null;

        if (error is not null)
            return null;
        return result;
    }

    private static ConfigInfo? ReadViaComCore(Infobase ib)
    {
        var connectString = BuildComConnectString(ib);
        if (string.IsNullOrWhiteSpace(connectString))
            return null;

        foreach (var progId in new[] { "V83.COMConnector", "V82.COMConnector", "V81.COMConnector" })
        {
            Type? type;
            try
            {
                type = Type.GetTypeFromProgID(progId);
            }
            catch
            {
                continue;
            }
            if (type is null) continue;

            object? connector = null;
            object? connection = null;
            try
            {
                connector = Activator.CreateInstance(type);
                if (connector is null) continue;

                connection = type.InvokeMember(
                    "Connect",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    connector,
                    new object[] { connectString });

                if (connection is null) continue;

                var metadata = connection.GetType().InvokeMember(
                    "Metadata",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    connection,
                    null);
                if (metadata is null) continue;

                var name = GetComString(metadata, "Name")
                           ?? GetComString(metadata, "Synonym")
                           ?? string.Empty;
                var version = GetComString(metadata, "Version") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version))
                    continue;

                return new ConfigInfo(name, version);
            }
            catch
            {
                // пробуем следующий ProgID
            }
            finally
            {
                TryComRelease(connection);
                TryComRelease(connector);
            }
        }

        return null;
    }

    private static string? GetComString(object comObject, string property)
    {
        try
        {
            var val = comObject.GetType().InvokeMember(
                property,
                System.Reflection.BindingFlags.GetProperty,
                null,
                comObject,
                null);
            return val?.ToString();
        }
        catch
        {
            return null;
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
        catch { /* ignore */ }
    }

    /// <summary>
    /// Строка подключения для COM: File=...; или Srvr=...;Ref=...; (+ Usr/Pwd при наличии).
    /// </summary>
    public static string BuildComConnectString(Infobase ib)
    {
        var c = ib.Connection;
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
                // Веб — COM обычно не подходит
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

    /// <summary>
    /// Эвристика: в 1Cv8.1CD иногда встречаются читаемые UTF-8/UTF-16 строки с версией вида 1.2.3.4.
    /// Имя конфигурации надёжно так не извлечь — возвращаем только версию, если найдена.
    /// </summary>
    private static ConfigInfo? TryReadFromFileBase(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        string? cdPath = null;
        if (File.Exists(filePath) && filePath.EndsWith(".1CD", StringComparison.OrdinalIgnoreCase))
            cdPath = filePath;
        else if (Directory.Exists(filePath))
        {
            cdPath = Path.Combine(filePath, "1Cv8.1CD");
            if (!File.Exists(cdPath))
                cdPath = Directory.EnumerateFiles(filePath, "1Cv8.1CD", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        if (string.IsNullOrEmpty(cdPath) || !File.Exists(cdPath))
            return null;

        // Читаем ограниченный объём (начало файла), ищем version-like строки
        const int maxBytes = 4 * 1024 * 1024;
        byte[] data;
        try
        {
            using var fs = new FileStream(cdPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = (int)Math.Min(fs.Length, maxBytes);
            data = new byte[len];
            _ = fs.Read(data, 0, len);
        }
        catch
        {
            return null;
        }

        var version = FindVersionString(data);
        if (string.IsNullOrEmpty(version))
            return null;

        return new ConfigInfo(string.Empty, version);
    }

    private static string? FindVersionString(byte[] data)
    {
        // Ищем ASCII-последовательности вида N.N.N.N
        var candidates = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if ((b >= '0' && b <= '9') || b == '.')
            {
                sb.Append((char)b);
                if (sb.Length > 24)
                    sb.Clear();
            }
            else
            {
                if (sb.Length >= 5)
                {
                    var s = sb.ToString();
                    if (IsLikelyConfigVersion(s))
                        candidates.Add(s);
                }
                sb.Clear();
            }
        }

        // Берём «самую похожую» на версию конфигурации (4+ компонента)
        return candidates
            .OrderByDescending(c => c.Count(ch => ch == '.'))
            .ThenByDescending(c => c.Length)
            .FirstOrDefault();
    }

    private static bool IsLikelyConfigVersion(string s)
    {
        var parts = s.Split('.');
        if (parts.Length < 3 || parts.Length > 5) return false;
        foreach (var p in parts)
        {
            if (p.Length == 0 || p.Length > 6) return false;
            if (!int.TryParse(p, out _)) return false;
        }
        // отсекаем слишком мелкие вроде 1.0
        return parts.Length >= 3;
    }
}
