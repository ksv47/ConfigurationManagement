namespace Configuration_Management.Services;

/// <summary>Простой логгер приложения.</summary>
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
