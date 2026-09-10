using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Разворачивает шаблон имени COM-коннектора 1С по версии платформы (issue #175).
/// Общий для обеих сборок (WPF и Avalonia): в отличие от <c>OneCComConnector</c>,
/// который входит только в Windows-сборку, этот класс используется и окном настроек
/// Linux для интерактивного предпросмотра, чтобы поведение при подключении
/// совпадало с тем, что видит пользователь.
/// </summary>
public static class ComConnectorTemplate
{
    /// <summary>
    /// Разворачивает шаблон по версии. Пустая строка версии/шаблона или
    /// невозможность разобрать версию → null.
    /// </summary>
    public static string? Expand(string? template, string? platformVersion)
    {
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(platformVersion))
            return null;

        var seg = platformVersion.Split('.');
        if (seg.Length == 0)
            return null;

        // %V12% — первые две цифры версии (для 8.3.x это «83»), %V3%/%V4% — третья/четвёртая.
        // Из каждого сегмента берутся только цифры, чтобы чужие символы из строки версии
        // не попадали в ProgID.
        var v12 = Digits(seg.Length > 1 ? seg[0] + seg[1] : seg[0]);
        var v3 = Digits(seg.Length > 2 ? seg[2] : "");
        var v4 = Digits(seg.Length > 3 ? seg[3] : "");

        // Нет первой части — расшифровать нечего.
        if (v12.Length == 0)
            return null;

        return Apply(template, v12, v3, v4);
    }

    /// <summary>
    /// Применяет значения плейсхолдеров с обрезкой разделителей перед пустыми
    /// сегментами (issue #175). Пример: "V%V12%_%V3%_%V4%.ComConnector" + 8.3.27
    /// → "V83_27.ComConnector" (а не "V83_27_.ComConnector").
    /// </summary>
    private static string Apply(string template, string v12, string v3, string v4)
    {
        // Токенизация: литералы между плейсхолдерами + значения плейсхолдеров.
        // lit[i] — литерал ПЕРЕД плейсхолдером ph[i]; tail — литерал ПОСЛЕ последнего.
        var ph = new List<string>();
        var lit = new List<string>();
        ParseTokens(template, lit, ph, out var tail);

        // Шаблон без плейсхолдеров — вернуть как есть (эквивалент прежнего поведения).
        if (ph.Count == 0)
            return template;

        var sb = new StringBuilder();
        for (int i = 0; i < ph.Count; i++)
        {
            var value = ph[i] switch { "%V12%" => v12, "%V3%" => v3, _ => v4 };
            if (value.Length > 0)
            {
                sb.Append(lit[i]);  // разделитель перед текущим плейсхолдером
                sb.Append(value);
            }
            // пустой сегмент: его значение и разделитель lit[i] опускаются
        }

        sb.Append(tail); // суффикс ProgID (.ComConnector) сохраняется всегда
        return sb.ToString();
    }

    /// <summary>
    /// Разбивает шаблон по плейсхолдерам %V12%/%V3%/%V4% в порядке появления:
    /// lit[0] — текст до первого плейсхолдера, lit[i] — текст между ph[i-1] и ph[i],
    /// tail — текст после последнего плейсхолдера.
    /// </summary>
    private static void ParseTokens(string template, List<string> lit, List<string> ph, out string tail)
    {
        var pos = 0;
        while (pos < template.Length)
        {
            var next = FindNextPlaceholder(template, pos, out var name);
            if (next < 0)
                break;

            lit.Add(template.Substring(pos, next - pos));
            ph.Add(name);
            pos = next + name.Length;
        }

        tail = template.Substring(pos);
    }

    /// <summary>
    /// Ищет ближайший плейсхолдер начиная с <paramref name="start"/>.
    /// Возвращает индекс начала плейсхолдера или -1, если плейсхолдеров больше нет.
    /// </summary>
    private static int FindNextPlaceholder(string template, int start, out string name)
    {
        const string v12 = "%V12%";
        const string v3 = "%V3%";
        const string v4 = "%V4%";

        var i12 = template.IndexOf(v12, start, StringComparison.Ordinal);
        var i3 = template.IndexOf(v3, start, StringComparison.Ordinal);
        var i4 = template.IndexOf(v4, start, StringComparison.Ordinal);

        // Выбираем самый ранний из найденных.
        var best = -1;
        name = "";
        if (i12 >= 0 && (best < 0 || i12 < best)) { best = i12; name = v12; }
        if (i3 >= 0 && (best < 0 || i3 < best)) { best = i3; name = v3; }
        if (i4 >= 0 && (best < 0 || i4 < best)) { best = i4; name = v4; }

        return best;
    }

    /// <summary>Оставляет в строке только десятичные цифры.</summary>
    private static string Digits(string s)
    {
        if (s.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsAsciiDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }
}