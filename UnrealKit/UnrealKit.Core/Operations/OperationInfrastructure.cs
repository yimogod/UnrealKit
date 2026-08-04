namespace UnrealKit.Core.Operations;

public sealed record OperationProgress(
    string OperationId,
    string Stage,
    int? CurrentItem,
    int? TotalItems,
    string Message,
    Exception? Error = null);

public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error
}

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string OperationId,
    string Message,
    IReadOnlyDictionary<string, string>? Properties = null,
    Exception? Exception = null);

public interface IOperationLogger
{
    void Log(LogEvent logEvent);
}

public sealed class NullOperationLogger : IOperationLogger
{
    public static NullOperationLogger Instance { get; } = new();

    private NullOperationLogger()
    {
    }

    public void Log(LogEvent logEvent)
    {
    }
}
