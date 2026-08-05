namespace UnrealKit.Core.Processes;

public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
}

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool Succeeded => ExitCode == 0;
}

public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string message, ProcessExecutionResult result)
        : base(message)
    {
        Result = result;
    }

    public ProcessExecutionResult Result { get; }
}
