using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WinBGMuter.Logging;

internal sealed class LoggingEngineLogger : ILogger
{
    private readonly string _categoryName;
    private readonly Func<LogLevel> _minimumLevelAccessor;

    public LoggingEngineLogger(string categoryName, Func<LogLevel> minimumLevelAccessor)
    {
        _categoryName = categoryName;
        _minimumLevelAccessor = minimumLevelAccessor;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _minimumLevelAccessor();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) where TState : notnull
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        var formattedMessage = $"[{_categoryName}] {logLevel}: {message}";

        if (exception != null)
        {
            formattedMessage += $" Exception: {exception}";
        }

        Debug.WriteLine(formattedMessage);

        if (LoggingEngine.Enabled)
        {
            LoggingEngine.LogLine(formattedMessage, loglevel: AppLogging.ConvertToLegacyLevel(logLevel));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        public void Dispose()
        {
        }
    }
}
