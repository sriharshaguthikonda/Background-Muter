using Microsoft.Extensions.Logging;

namespace WinBGMuter.Logging;

internal sealed class LoggingEngineLoggerProvider : ILoggerProvider
{
    private readonly Func<LogLevel> _minimumLevelAccessor;

    public LoggingEngineLoggerProvider(Func<LogLevel> minimumLevelAccessor)
    {
        _minimumLevelAccessor = minimumLevelAccessor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new LoggingEngineLogger(categoryName, _minimumLevelAccessor);
    }

    public void Dispose()
    {
    }
}
