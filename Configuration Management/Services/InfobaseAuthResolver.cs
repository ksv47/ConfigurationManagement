using Configuration_Management.Models;

namespace Configuration_Management.Services;

/// <summary>
/// Единый выбор учётных данных информационной базы по режиму (issue #236).
/// «1С:Предприятие» использует отдельную авторизацию (<see cref="Infobase.EnterpriseAuth"/>),
/// если она задана; «Конфигуратор» — отдельную авторизацию (<see cref="Infobase.ConfiguratorAuth"/>),
/// если она задана; иначе — авторизацию информационной базы (<see cref="Infobase.Connection"/>,
/// обратная совместимость).
/// Резолвинг применяется и при запуске 1С (<see cref="OneCLauncher"/>), и при чтении сведений
/// о конфигурации через COM-коннектор (<see cref="OneCComConnector"/>), чтобы учётные данные
/// выбирались одинаково: раздельная авторизация Конфигуратора/Предприятия не терялась при
/// построении строки подключения COM.
/// </summary>
public static class InfobaseAuthResolver
{
    /// <summary>
    /// Разрешает режим аутентификации, логин и пароль для заданного режима.
    /// </summary>
    public static void Resolve(Infobase infobase, OneCLaunchMode mode,
        out AuthenticationMode authMode, out string authUser, out string authPassword)
    {
        if (mode == OneCLaunchMode.Enterprise && infobase.EnterpriseAuth is { } entAuth)
        {
            authMode = entAuth.AuthenticationMode;
            authUser = entAuth.User;
            authPassword = entAuth.Password;
        }
        else if (mode == OneCLaunchMode.Configurator && infobase.ConfiguratorAuth is { } cfgAuth)
        {
            authMode = cfgAuth.AuthenticationMode;
            authUser = cfgAuth.User;
            authPassword = cfgAuth.Password;
        }
        else
        {
            var conn = infobase.Connection;
            authMode = conn?.AuthenticationMode ?? AuthenticationMode.Prompt;
            authUser = conn?.User ?? string.Empty;
            authPassword = conn?.Password ?? string.Empty;
        }
    }
}