using System;
using System.Collections.Generic;
using System.IO;
using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Реализация <see cref="ICreateInfobaseService"/>. Содержит общую для WPF и Avalonia логику
/// создания ИБ: валидацию ввода, проверку несовместимой версии платформы на сервере (#91),
/// вызов <c>OneCLauncher.CreateInfoBase</c>, сборку результата и запоминание последней версии.
/// </summary>
public sealed class CreateInfobaseService : ICreateInfobaseService
{
    private readonly IInfobaseRepository _repository;

    public CreateInfobaseService(IInfobaseRepository repository)
    {
        _repository = repository;
    }

    public CreateInfobaseResult TryCreate(CreateInfobaseRequest request, bool confirmVersionMismatch)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterName };

        string? templatePath = null;
        if (request.FromTemplate)
        {
            templatePath = (request.TemplatePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterTemplateFile };
        }

        var platform = (request.PlatformVersion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(platform))
            return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.NoPlatform };

        ConnectionSettings connection;
        if (request.IsFile)
        {
            var filePath = (request.FilePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(filePath))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterFilePath };

            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: true,
                filePath: filePath,
                server: null,
                databaseName: null,
                templatePath: templatePath);
            if (!ok)
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.CreateFailed,
                    ErrorMessage = error
                };

            connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = filePath ?? ""
            };
        }
        else
        {
            var server = (request.Server ?? string.Empty).Trim();
            var refName = (request.DatabaseName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(refName))
                return new CreateInfobaseResult { Kind = CreateInfobaseResultKind.EnterServerAndDb };

            // Вариант 2 (#91): заранее предупреждаем, если выбранная версия платформы
            // отличается (по major.minor) от версий, которыми уже работают
            // клиент-серверные базы на этом же сервере. «Нет» — прерывает создание.
            var existingVersion = GetIncompatibleExistingVersion(platform, server);
            if (existingVersion != null && !confirmVersionMismatch)
            {
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.VersionMismatch,
                    IncompatibleExistingVersion = existingVersion
                };
            }

            var dbms = (request.Dbms ?? string.Empty).Trim();
            var dbServer = (request.DbServer ?? string.Empty).Trim();
            var dbName = (request.DbName ?? string.Empty).Trim();
            var dbUser = (request.DbUser ?? string.Empty).Trim();
            var dbPwd = request.DbPassword ?? "";
            var createSqlDatabase = request.CreateSqlDatabase;
            var blockScheduledJobs = request.BlockScheduledJobs;

            var (ok, error) = OneCLauncher.CreateInfoBase(
                platformVersion: platform,
                isFile: false,
                filePath: null,
                server: server,
                databaseName: refName,
                templatePath: templatePath,
                dbms: dbms,
                dbServer: dbServer,
                dbName: dbName,
                dbUser: dbUser,
                dbPassword: dbPwd,
                createSqlDatabase: createSqlDatabase,
                blockScheduledJobs: blockScheduledJobs);
            if (!ok)
                return new CreateInfobaseResult
                {
                    Kind = CreateInfobaseResultKind.CreateFailed,
                    ErrorMessage = error
                };

            connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = server,
                DatabaseName = refName,
                BlockScheduledJobs = blockScheduledJobs
            };
        }

        // Разрядность, выбранная суффиксом версии «(32)/(64)», сохраняется
        // в отдельное поле Architecture, а PlatformVersion — чистая версия
        // (без встроенной разрядности, чтобы она не попадала в ibases.v8i).
        PlatformVersionService.ParseVariant(platform, out var cleanPlatform, out var platformArch);
        var storedPlatform = string.IsNullOrWhiteSpace(cleanPlatform) ? platform : cleanPlatform;
        var storedArchitecture = platformArch == "32" || platformArch == "64"
            ? platformArch
            : "32-priority";

        var created = new Infobase
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Group = string.IsNullOrWhiteSpace(request.GroupPath) ? string.Empty : request.GroupPath,
            PlatformVersion = storedPlatform,
            Architecture = storedArchitecture,
            Connection = connection
        };

        // Создание прошло успешно — запоминаем версию для подстановки по умолчанию.
        SaveLastPlatformVersion(request.IsFile, platform);

        return new CreateInfobaseResult
        {
            Kind = CreateInfobaseResultKind.Success,
            CreatedInfobase = created
        };
    }

    /// <summary>
    /// Разбирает строку версии на числовые компоненты (major, minor).
    /// Суффиксы вроде « (64)» снимаются через <see cref="PlatformVersionService.ParseVariant"/>.
    /// </summary>
    private static (int Major, int Minor) GetMajorMinor(string version)
    {
        PlatformVersionService.ParseVariant(version, out var clean, out _);
        var v = string.IsNullOrWhiteSpace(clean) ? version : clean;
        var parts = (v ?? "").Split('.');
        int.TryParse(parts.Length >= 1 ? parts[0] : "", out var major);
        int.TryParse(parts.Length >= 2 ? parts[1] : "", out var minor);
        return (major, minor);
    }

    /// <summary>
    /// Эвристика Варианта 2 (#91): ищет среди уже существующих клиент-серверных баз на том же
    /// сервере базу, версия платформы которой отличается от выбранной по первым двум числам
    /// (major.minor). Возвращает версию такой базы или null, если расхождений нет.
    /// Ошибки чтения списка баз не блокируют создание — возвращаем null.
    /// </summary>
    private string? GetIncompatibleExistingVersion(string platform, string server)
    {
        var (selectedMajor, selectedMinor) = GetMajorMinor(platform);

        List<Infobase> infobases;
        try
        {
            infobases = _repository.Load();
        }
        catch
        {
            return null;
        }

        var targetServer = (server ?? "").Trim();
        foreach (var ib in infobases)
        {
            var conn = ib.Connection;
            if (conn == null || conn.Type != ConnectionType.ClientServer)
                continue;
            if (!string.Equals((conn.Server ?? "").Trim(), targetServer, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(ib.PlatformVersion))
                continue;

            var (major, minor) = GetMajorMinor(ib.PlatformVersion);
            if (major != selectedMajor || minor != selectedMinor)
                return ib.PlatformVersion;
        }

        return null;
    }

    /// <summary>
    /// Запоминает последнюю успешно использованную версию платформы отдельно для
    /// файловых и клиент-серверных баз. Ошибки сохранения не должны ломать создание ИБ.
    /// </summary>
    private void SaveLastPlatformVersion(bool isFile, string platform)
    {
        try
        {
            var settings = _repository.LoadSettings();
            PlatformVersionService.ParseVariant(platform, out var cleanPlatform, out _);
            var clean = string.IsNullOrWhiteSpace(cleanPlatform) ? platform : cleanPlatform;
            if (isFile)
                settings.LastFileCreatePlatformVersion = clean;
            else
                settings.LastClientServerCreatePlatformVersion = clean;
            _repository.SaveSettings(settings);
        }
        catch
        {
            // Несохранение последней версии не должно прерывать создание ИБ.
        }
    }
}