using Microsoft.Extensions.Logging;

namespace WinBGMuter.Logging;

internal static class AppLogging
{
    private static LogLevel _minimumLevel = LogLevel.Information;

    private static readonly ILoggerFactory LoggerFactoryInstance = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders();
        builder.AddProvider(new LoggingEngineLoggerProvider(() => _minimumLevel));
    });

    public static ILoggerFactory LoggerFactory => LoggerFactoryInstance;

    public static ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);

    public static ILogger<T> CreateLogger<T>() => LoggerFactory.CreateLogger<T>();

    public static void SetMinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
    }

    public static void SetMinimumLevelFromLegacy(LoggingEngine.LOG_LEVEL_TYPE legacyLevel)
    {
        _minimumLevel = ConvertLegacyLevel(legacyLevel);
    }

    public static LogLevel ConvertLegacyLevel(LoggingEngine.LOG_LEVEL_TYPE legacyLevel) => legacyLevel switch
    {
        LoggingEngine.LOG_LEVEL_TYPE.LOG_NONE => LogLevel.None,
        LoggingEngine.LOG_LEVEL_TYPE.LOG_ERROR => LogLevel.Error,
        LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING => LogLevel.Warning,
        LoggingEngine.LOG_LEVEL_TYPE.LOG_INFO => LogLevel.Information,
        _ => LogLevel.Debug
    };

    public static LoggingEngine.LOG_LEVEL_TYPE ConvertToLegacyLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Critical => LoggingEngine.LOG_LEVEL_TYPE.LOG_ERROR,
        LogLevel.Error => LoggingEngine.LOG_LEVEL_TYPE.LOG_ERROR,
        LogLevel.Warning => LoggingEngine.LOG_LEVEL_TYPE.LOG_WARNING,
        LogLevel.Information => LoggingEngine.LOG_LEVEL_TYPE.LOG_INFO,
        LogLevel.Debug => LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG,
        LogLevel.Trace => LoggingEngine.LOG_LEVEL_TYPE.LOG_DEBUG,
        _ => LoggingEngine.LOG_LEVEL_TYPE.LOG_NONE
    };
}
