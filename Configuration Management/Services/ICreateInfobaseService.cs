using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Исходы валидации и создания информационной базы.
/// Каждое значение, кроме <see cref="Success"/> и <see cref="VersionMismatch"/>,
/// соответствует отдельному предупреждению/ошибке, показываемому пользователю.
/// </summary>
public enum CreateInfobaseResultKind
{
    /// <summary>Валидация и создание прошли успешно.</summary>
    Success,

    /// <summary>Не введено наименование ИБ.</summary>
    EnterName,

    /// <summary>Не указан или не существует файл шаблона (создание из шаблона).</summary>
    EnterTemplateFile,

    /// <summary>Не выбрана версия платформы 1С.</summary>
    NoPlatform,

    /// <summary>Не указан путь к каталогу файловой базы.</summary>
    EnterFilePath,

    /// <summary>Не указаны сервер 1С и/или имя базы (клиент-серверный режим).</summary>
    EnterServerAndDb,

    /// <summary>
    /// Обнаружена несовместимая версия платформы на том же сервере (#91).
    /// Требуется подтверждение пользователя до продолжения создания.
    /// </summary>
    VersionMismatch,

    /// <summary>Ошибка при выполнении CREATEINFOBASE (поле <see cref="CreateInfobaseResult.ErrorMessage"/>).</summary>
    CreateFailed
}

/// <summary>
/// Результат попытки валидации и создания информационной базы.
/// Содержит исход операции и данные, необходимые для показа сообщения пользователю.
/// </summary>
public sealed class CreateInfobaseResult
{
    /// <summary>Исход операции.</summary>
    public CreateInfobaseResultKind Kind { get; set; } = CreateInfobaseResultKind.Success;

    /// <summary>Текст ошибки от лаунчера 1С при <see cref="CreateInfobaseResultKind.CreateFailed"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Версия платформы существующей несовместимой базы при
    /// <see cref="CreateInfobaseResultKind.VersionMismatch"/>.
    /// </summary>
    public string? IncompatibleExistingVersion { get; set; }

    /// <summary>Созданная ИБ при <see cref="CreateInfobaseResultKind.Success"/>.</summary>
    public Infobase? CreatedInfobase { get; set; }
}

/// <summary>
/// Сервис создания информационной базы: валидация ввода, проверка несовместимой версии
/// платформы на сервере (#91), вызов <c>OneCLauncher.CreateInfoBase</c> и сборка результата.
/// Не зависит от UI-фреймворка (WPF/Avalonia) — принимает примитивные параметры.
/// </summary>
public interface ICreateInfobaseService
{
    /// <summary>
    /// Выполняет валидацию и создание ИБ. При обнаружении несовместимой версии платформы на
    /// сервере (#91) и <paramref name="confirmVersionMismatch"/>=false возвращает
    /// <see cref="CreateInfobaseResultKind.VersionMismatch"/> без создания; вызывающий код должен
    /// запросить подтверждение и вызвать метод повторно с confirmVersionMismatch=true.
    /// </summary>
    /// <param name="request">Параметры создания ИБ.</param>
    /// <param name="confirmVersionMismatch">
    /// true — продолжать создание, даже если обнаружена несовместимая версия на сервере.
    /// </param>
    CreateInfobaseResult TryCreate(CreateInfobaseRequest request, bool confirmVersionMismatch);
}