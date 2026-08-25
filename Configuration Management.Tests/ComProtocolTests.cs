#if WINDOWS
using Configuration_Management.Services;
using Xunit;

namespace Configuration_Management.Tests;

/// <summary>
/// Разбор кадров протокола обмена с процессом-агентом.
/// <para>
/// Проверяется чистая функция <see cref="ComReadHost.ParseResponse"/>: она решает, доверять
/// ли ответу, и именно на ней держится правило «свободный текст 1С проходит через решение
/// о пароле». Повреждённый кадр обязан объявляться рассинхронизацией, а не превращаться
/// в отказ с сохранённой подробностью.
/// </para>
/// </summary>
public class ComProtocolTests
{
    private const string Prefix = "CFGINFO\t";

    private static string Frame(params string[] fields) => Prefix + string.Join("\t", fields);

    [Fact]
    public void Успех_разбирается_и_отдаёт_имя_и_версию()
    {
        var line = Frame("7", "OK", ComReadHost.Encode("Бухгалтерия"), ComReadHost.Encode("3.0.1"));

        var result = ComReadHost.ParseResponse(line, 7, out var desync, out var partial);

        Assert.False(desync);
        Assert.False(partial);
        Assert.Equal(ComFailureKind.None, result.Failure);
        Assert.Equal("Бухгалтерия", result.Info!.Value.Name);
        Assert.Equal("3.0.1", result.Info!.Value.Version);
    }

    [Fact]
    public void Ошибка_базы_доносит_текст_и_код()
    {
        var line = Frame("3", "ERR", "DBERR",
            ComReadHost.Encode("COMException 0x80004005"),
            ComReadHost.Encode("Неверный пароль"));

        var result = ComReadHost.ParseResponse(line, 3, out var desync, out var partial);

        Assert.False(desync);
        Assert.False(partial);
        Assert.Equal(ComFailureKind.DatabaseError, result.Failure);
        Assert.Equal("Неверный пароль", result.Detail);
        Assert.Equal("COMException 0x80004005", result.Code);
    }

    [Fact]
    public void Промежуточный_кадр_помечается_и_не_завершает_запрос()
    {
        var line = Frame("4", "PARTIAL", "DBERR",
            ComReadHost.Encode("COMException 0x1"),
            ComReadHost.Encode("V85.COMConnector: Неверный пароль"));

        var result = ComReadHost.ParseResponse(line, 4, out var desync, out var partial);

        Assert.False(desync);
        Assert.True(partial);
        Assert.Equal(ComFailureKind.DatabaseError, result.Failure);
        Assert.Equal("V85.COMConnector: Неверный пароль", result.Detail);
    }

    [Fact]
    public void Ответ_на_чужой_запрос_считается_рассинхронизацией()
    {
        var line = Frame("9", "OK", ComReadHost.Encode("Имя"), ComReadHost.Encode("1.0"));

        var result = ComReadHost.ParseResponse(line, 8, out var desync, out _);

        Assert.True(desync);
        Assert.Equal(ComFailureKind.Transport, result.Failure);
    }

    [Theory]
    // Лишнее поле: мы читаем не то, что думаем.
    [InlineData("1\tOK\tYWJj\tZGVm\tlишнее")]
    // Недостающее поле.
    [InlineData("1\tERR\tDBERR\tYWJj")]
    // Повреждённая нагрузка успеха.
    [InlineData("1\tOK\t!!!не-base64!!!\tZGVm")]
    // Повреждённая нагрузка ошибки.
    [InlineData("1\tERR\tDBERR\t!!!\tZGVm")]
    // Неизвестный разряд: отображать его в Transport с сохранением текста нельзя —
    // подробность Transport печатается пользователю мимо решения о пароле.
    [InlineData("1\tERR\tНЕИЗВЕСТНО\tYWJj\tZGVm")]
    public void Повреждённый_кадр_объявляется_рассинхронизацией_без_подробности(string tail)
    {
        var result = ComReadHost.ParseResponse(Prefix + tail, 1, out var desync, out _);

        Assert.True(desync);
        Assert.Equal(ComFailureKind.Transport, result.Failure);
        Assert.True(string.IsNullOrEmpty(result.Detail));
    }

    [Fact]
    public void Отказ_разбора_запроса_с_текстом_не_принимается()
    {
        // Агент отправляет BADREQ только без кода и текста. Требуем этого и на приёме:
        // иначе подделанный кадр вынес бы произвольный текст в разряд Transport,
        // подробность которого показывается пользователю дословно.
        var line = Frame("2", "ERR", "BADREQ",
            ComReadHost.Encode("код"), ComReadHost.Encode("Pwd=\"секрет\""));

        var result = ComReadHost.ParseResponse(line, 2, out var desync, out _);

        Assert.True(desync);
        Assert.Equal(ComFailureKind.Transport, result.Failure);
        Assert.True(string.IsNullOrEmpty(result.Detail));
    }

    [Fact]
    public void Отказ_разбора_запроса_без_нагрузки_принимается()
    {
        var line = Frame("2", "ERR", "BADREQ", string.Empty, string.Empty);

        var result = ComReadHost.ParseResponse(line, 2, out var desync, out _);

        Assert.False(desync);
        Assert.Equal(ComFailureKind.BadRequest, result.Failure);
    }

    [Fact]
    public void Мусор_вместо_кадра_считается_рассинхронизацией()
    {
        var result = ComReadHost.ParseResponse("что-то совсем не то", 1, out var desync, out _);

        Assert.True(desync);
        Assert.Equal(ComFailureKind.Transport, result.Failure);
    }

    [Theory]
    [InlineData(ComFailureKind.NotRegistered)]
    [InlineData(ComFailureKind.InstanceFailed)]
    [InlineData(ComFailureKind.Timeout)]
    [InlineData(ComFailureKind.NoConnection)]
    [InlineData(ComFailureKind.MetadataProperty)]
    [InlineData(ComFailureKind.MetadataRead)]
    [InlineData(ComFailureKind.DatabaseError)]
    [InlineData(ComFailureKind.BadRequest)]
    internal void Разряд_переживает_превращение_в_токен_и_обратно(ComFailureKind kind)
    {
        // Обе стороны протокола выводятся из одной таблицы. Тест сторожит именно это:
        // разойдясь, они однажды уже позволили свободному тексту 1С обойти решение о пароле.
        Assert.True(ComReadHost.TryMapToken(ComReadHost.KindToToken(kind), out var back));
        Assert.Equal(kind, back);
    }

    [Fact]
    public void Transport_не_приезжает_по_протоколу()
    {
        // Подробность Transport показывается пользователю дословно, поэтому отобразить
        // в него присланный агентом кадр нельзя ни одним токеном.
        Assert.False(ComReadHost.TryMapToken("TRANSPORT", out _));
    }

    [Fact]
    public void Более_осмысленный_диагноз_вытесняет_менее_осмысленный()
    {
        var rank = 0;

        Assert.True(ComReadHost.Promote(ref rank, ComFailureKind.InstanceFailed));
        Assert.True(ComReadHost.Promote(ref rank, ComFailureKind.DatabaseError));
        Assert.False(ComReadHost.Promote(ref rank, ComFailureKind.NoConnection));
        Assert.False(ComReadHost.Promote(ref rank, ComFailureKind.InstanceFailed));
    }

    [Fact]
    public void При_равном_весе_удерживается_уже_принятый()
    {
        var rank = 0;

        Assert.True(ComReadHost.Promote(ref rank, ComFailureKind.DatabaseError));
        Assert.False(ComReadHost.Promote(ref rank, ComFailureKind.DatabaseError));
    }
}
#endif
