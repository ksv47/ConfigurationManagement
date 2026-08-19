using System.IO;
using System.Text;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Считывает наименование и версию конфигурации 1С (COM-коннектор или эвристика по файлу базы).
/// </summary>
public static class ConfigurationInfoService
{
    /// <summary>
    /// Текст последней ошибки чтения через COM-коннектор (для вывода в UI).
    /// null — если COM-чтение не выполнялось или завершилось успешно.
    /// </summary>
    public static string? LastComError { get; private set; }

    /// <summary>
    /// Пытается прочитать имя и версию конфигурации для информационной базы.
    /// Сначала используется COM-коннектор, затем эвристика по файловой базе.
    /// </summary>
    public static OneCConfigInfo? TryRead(Infobase ib, int timeoutMs = 8000)
    {
        if (ib is null) return null;

        LastComError = null;
        try
        {
            var connector = AppServices.GetRequiredService<IOneCComConnector>();
            var viaCom = connector.ReadConfigurationInfo(ib, timeoutMs);
            if (viaCom is not null)
                return viaCom;
            LastComError = connector.LastError;
        }
        catch (Exception ex)
        {
            // COM недоступен / таймаут
            LastComError = ex.Message;
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

    /// <summary>
    /// Читает наименование и версию конфигурации и сразу применяет их к базе
    /// (по умолчанию перезаписывая уже заполненные значения). Возвращает прочитанные
    /// данные, либо null, если чтение не удалось.
    /// </summary>
    public static OneCConfigInfo? ReadAndApply(Infobase ib, bool overwriteExisting = true, int timeoutMs = 8000)
    {
        if (ib is null) return null;
        var info = TryRead(ib, timeoutMs);
        if (info is null) return null;
        TryApply(ib, overwriteExisting);
        return info;
    }

    /// <summary>
    /// Эвристика: в 1Cv8.1CD иногда встречаются читаемые UTF-8/UTF-16 строки с версией вида 1.2.3.4.
    /// Имя конфигурации надёжно так не извлечь — возвращаем только версию, если найдена.
    /// </summary>
    private static OneCConfigInfo? TryReadFromFileBase(string? filePath)
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

        return new OneCConfigInfo(string.Empty, version);
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
