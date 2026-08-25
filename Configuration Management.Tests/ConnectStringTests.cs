#if WINDOWS
using Configuration_Management.Models;
using Configuration_Management.Services;
using Xunit;

namespace Configuration_Management.Tests;

/// <summary>
/// Сборка строки подключения и маскировка учётных данных.
/// <para>
/// Здесь сторожится главное правило: свободный текст ошибки 1С показывается тогда и только
/// тогда, когда пароля в строке нет. Признак отдаёт сам сборщик, поэтому проверяется именно
/// он — и отдельно проверяется, что значение поля не может закрыть себя и дописать чужой
/// параметр. Ровно через это когда-то и пробивалась вся защита.
/// </para>
/// </summary>
public class ConnectStringTests
{
    private static Infobase FileBase(string path, string? user = null, string? password = null)
    {
        var infobase = new Infobase();
        infobase.Connection.Type = ConnectionType.File;
        infobase.Connection.FilePath = path;

        if (user is not null)
        {
            infobase.Connection.AuthenticationMode = AuthenticationMode.Credentials;
            infobase.Connection.User = user;
            infobase.Connection.Password = password ?? string.Empty;
        }

        return infobase;
    }

    [Fact]
    public void Без_учётных_данных_пароля_в_строке_нет()
    {
        var s = OneCComConnector.BuildComConnectString(FileBase(@"C:\Base"), out var hasSecret);

        Assert.False(hasSecret);
        Assert.Equal("File=\"C:\\Base\";", s);
    }

    [Fact]
    public void Логин_без_пароля_не_считается_секретом()
    {
        // Пароль добавляется только вместе с логином, но не наоборот: признак,
        // привязанный к наличию логина, однажды сломал чтение баз 8.2.
        var s = OneCComConnector.BuildComConnectString(FileBase(@"C:\Base", "Админ"), out var hasSecret);

        Assert.False(hasSecret);
        Assert.Contains("Usr=\"Админ\";", s);
        Assert.DoesNotContain("Pwd=", s);
    }

    [Fact]
    public void Пароль_отмечается_признаком()
    {
        var s = OneCComConnector.BuildComConnectString(FileBase(@"C:\Base", "Админ", "секрет"), out var hasSecret);

        Assert.True(hasSecret);
        Assert.Contains("Pwd=\"секрет\";", s);
    }

    [Theory]
    // Кавычка внутри значения удваивается — правило грамматики строки подключения 1С.
    [InlineData("па\"роль", "Pwd=\"па\"\"роль\";")]
    [InlineData("па\"\"роль", "Pwd=\"па\"\"\"\"роль\";")]
    // Разделитель и перевод строки кавычками уже закрыты и экранирования не требуют.
    [InlineData("па;роль", "Pwd=\"па;роль\";")]
    [InlineData("па\nроль", "Pwd=\"па\nроль\";")]
    public void Значение_пароля_экранируется(string password, string expected)
    {
        var s = OneCComConnector.BuildComConnectString(FileBase(@"C:\Base", "Админ", password), out var hasSecret);

        Assert.True(hasSecret);
        Assert.Contains(expected, s);
    }

    [Fact]
    public void Через_путь_нельзя_дописать_чужой_параметр()
    {
        // Без экранирования путь закрывал своё значение и добавлял настоящий Pwd, о котором
        // сборщик не знал: признак оставался ложным, и текст ошибки 1С выпускался как
        // безопасный «по построению». Проверено на живом коннекторе: 1С разбирала внедрённый
        // Pwd как отдельный параметр.
        var s = OneCComConnector.BuildComConnectString(
            FileBase("C:\\Base\";Pwd=ЧужойПароль;x=\""), out var hasSecret);

        Assert.False(hasSecret);
        // Единственная пара «кавычка + точка с запятой» — та, которой заканчивается File.
        Assert.Equal("File=\"C:\\Base\"\";Pwd=ЧужойПароль;x=\"\"\";", s);
        Assert.EndsWith("\";", s);
        Assert.Single(SplitParameters(s));
    }

    /// <summary>
    /// Делит строку на параметры по правилам грамматики: разделитель считается таковым
    /// только вне закавыченного значения, а удвоенная кавычка значение не закрывает.
    /// </summary>
    private static List<string> SplitParameters(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (text[i] == ';' && !inQuotes)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        if (start < text.Length)
            parts.Add(text[start..]);

        return parts;
    }

    [Fact]
    public void Маскировка_закрывает_закавыченный_пароль()
    {
        var masked = OneCComConnector.MaskCredentials("File=\"C:\\Base\";Usr=\"a\";Pwd=\"секрет\";");

        Assert.DoesNotContain("секрет", masked);
        Assert.Contains("Pwd=***", masked);
        // Диагностика вокруг сохраняется.
        Assert.Contains("File=\"C:\\Base\"", masked);
        Assert.Contains("Usr=\"a\"", masked);
    }

    [Fact]
    public void Маскировка_не_трогает_незакавыченную_форму()
    {
        // Путь вида D:\Базы\Pwd=1\buh — допустимое имя каталога. Прежнее правило «до пробела
        // или разделителя» уничтожало хвост пути вместе с диагностикой у базы, у которой
        // пароля нет вовсе. Пароль всегда кладётся в кавычках, поэтому здесь маскировать нечего.
        const string text = "Неправильный путь к файлу 'D:\\Базы\\Pwd=1\\buh'.";

        Assert.Equal(text, OneCComConnector.MaskCredentials(text));
    }

    [Fact]
    public void Маскировка_идемпотентна()
    {
        var once = OneCComConnector.MaskCredentials("Srvr=\"s\";Ref=\"b\";Usr=\"a\";Pwd=\"секрет\";");

        Assert.Equal(once, OneCComConnector.MaskCredentials(once));
    }

    [Fact]
    public void Маскировка_не_путает_похожие_имена()
    {
        const string text = "NotPwd=\"1\";PwdHint=\"подсказка\";";

        Assert.Equal(text, OneCComConnector.MaskCredentials(text));
    }
}
#endif
