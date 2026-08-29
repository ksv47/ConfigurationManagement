using System.Text.RegularExpressions;

namespace Configuration_Management.Services;

/// <summary>Маскирует секреты перед показом диагностического текста пользователю.</summary>
internal static class SensitiveDataMasker
{
    /// <summary>
    /// Значение DBPwd в строке подключения 1С. Внутренняя кавычка кодируется парой кавычек,
    /// поэтому пара должна поглощаться целиком, прежде чем одиночная кавычка закроет значение.
    /// </summary>
    private static readonly Regex DbPasswordRegex = new(
        @"(\bDBPwd\s*=\s*"")(?:(?:"""")|[^""])*""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Скрывает пароль СУБД во всём тексте — как в показанной команде CREATEINFOBASE,
    /// так и в диагностике платформы, если она повторила строку подключения.
    /// </summary>
    internal static string MaskDbPassword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return DbPasswordRegex.Replace(text, match => match.Groups[1].Value + "********\"");
    }
}
