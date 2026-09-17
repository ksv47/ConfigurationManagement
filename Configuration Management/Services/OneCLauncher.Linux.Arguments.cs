#if LINUX
using Configuration_Management.Models;

namespace Configuration_Management.Services
{
    public static partial class OneCLauncher
    {
        // ====================================================================
        // Аргументы подключения
        // ====================================================================

        public static string BuildConnectionArgument(Infobase infobase)
        {
            var conn = infobase.Connection;
            // Значение в кавычках по грамматике ключа 1С (/F"…"). Это НЕ строка подключения:
            // кавычку внутри значения удвоением не экранируют — небезопасное значение (с «"»)
            // не подставляется, чтобы не допустить инъекцию /ключа (см. IsSafeCliValue).
            return conn.Type switch
            {
                ConnectionType.File => IsSafeCliValue(conn.FilePath)
                    ? $"/F\"{conn.FilePath.Trim().TrimEnd('\\', '/')}\""
                    : "",
                ConnectionType.WebServer => IsSafeCliValue(conn.WebUrl)
                    ? $"/WS\"{conn.WebUrl}\""
                    : "",
                _ => IsSafeCliValue(conn.GetServerWithPort()) && IsSafeCliValue(conn.DatabaseName)
                    ? $"/S\"{conn.GetServerWithPort()}\\{conn.DatabaseName}\""
                    : ""
            };
        }
    }
}
#endif