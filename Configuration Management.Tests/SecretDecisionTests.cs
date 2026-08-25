#if WINDOWS
using Configuration_Management.Services;
using Xunit;

namespace Configuration_Management.Tests;

/// <summary>
/// Само решение о показе свободного текста ошибки 1С.
/// <para>
/// Предыдущие тесты проверяли только входы этого решения — что признак пароля выставляется
/// верно и что кадры разбираются строго. Здесь проверяется то, ради чего всё строилось:
/// что при наличии пароля свободный текст не выпускается вовсе, а при отсутствии
/// не теряется. Пять редакций подряд браковали именно за ошибки в этой развилке.
/// </para>
/// </summary>
public class SecretDecisionTests
{
    private static ComReadResult DbError(string text, string code = "COMException 0x80004005") =>
        ComReadResult.Fail(ComFailureKind.DatabaseError, text, code);

    [Fact]
    public void При_наличии_пароля_текст_ошибки_не_выпускается()
    {
        const string quoted = "Ошибка базы Srvr=\"srv\";Ref=\"buh\";Usr=\"a\";Pwd=\"секрет\";";

        var message = OneCComConnector.DescribeDatabaseError(DbError(quoted), hasSecret: true);

        // Ни пароля, ни цитаты 1С: при наличии пароля свободный текст не выпускается вовсе.
        // Проверка намеренно не смотрит на готовую фразу — в тестовой среде словарь
        // локализации не поднят, и T() возвращает сам ключ.
        Assert.DoesNotContain("секрет", message);
        Assert.DoesNotContain("Ошибка базы", message);
        Assert.DoesNotContain("Srvr=", message);
    }

    [Fact]
    public void При_наличии_пароля_код_остаётся_даже_без_кода_от_агента()
    {
        var message = OneCComConnector.DescribeDatabaseError(
            ComReadResult.Fail(ComFailureKind.DatabaseError, "текст", code: null), hasSecret: true);

        Assert.DoesNotContain("текст", message);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Без_пароля_текст_ошибки_доходит_целиком()
    {
        const string text = "Неверно указан пользователь или пароль\nНеправильное имя пользователя";

        var message = OneCComConnector.DescribeDatabaseError(DbError(text), hasSecret: false);

        Assert.Equal(text, message);
    }

    [Fact]
    public void Без_пароля_и_без_текста_остаётся_код()
    {
        var message = OneCComConnector.DescribeDatabaseError(
            DbError(string.Empty), hasSecret: false);

        // Пустой текст не должен давать пустое сообщение: пользователю остаётся код.
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void Склейка_диагнозов_помечает_каждый_своим_ProgID()
    {
        var items = new List<(string ProgId, string Text, string Code)>
        {
            ("V85.COMConnector", "Несовместимая версия", "COMException 0x1"),
            ("V83.COMConnector", "Неверный пароль", "COMException 0x2")
        };

        var text = ComReadHost.JoinDiagnoses(items, static e => e.Text);
        var codes = ComReadHost.JoinDiagnoses(items, static e => e.Code);

        Assert.Equal("V85.COMConnector: Несовместимая версия; V83.COMConnector: Неверный пароль", text);
        Assert.Equal("V85.COMConnector: COMException 0x1; V83.COMConnector: COMException 0x2", codes);
    }

    [Fact]
    public void Склеенный_текст_проходит_то_же_решение_что_и_одиночный()
    {
        var items = new List<(string ProgId, string Text, string Code)>
        {
            ("V85.COMConnector", "Pwd=\"секрет\" в цитате", "COMException 0x1"),
            ("V83.COMConnector", "и ещё раз Pwd=\"секрет\"", "COMException 0x2")
        };

        var joined = ComReadHost.JoinDiagnoses(items, static e => e.Text);
        var message = OneCComConnector.DescribeDatabaseError(DbError(joined), hasSecret: true);

        Assert.DoesNotContain("секрет", message);
    }
}
#endif
