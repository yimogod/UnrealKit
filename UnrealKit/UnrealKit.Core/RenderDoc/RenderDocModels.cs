using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.RenderDoc;

// ── Models ──────────────────────────────────────────────────────────

/// <summary>
/// Describes a known RenderDoc Python script available for execution.
/// </summary>
public sealed record RenderDocScriptInfo(
    string Name,
    string ScriptPath,
    string Description);

/// <summary>
/// Input for executing a RenderDoc Python script.
/// </summary>
public sealed record RenderDocExecutionRequest(
    string PythonExecutable,
    string ScriptPath,
    IReadOnlyList<string> ScriptArguments,
    string? OutputDirectory = null,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Result of a RenderDoc Python script execution.
/// </summary>
public sealed record RenderDocExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? OutputDirectory,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool Succeeded => ExitCode == 0;
}

// ── Diagnostic codes ────────────────────────────────────────────────

public static class RenderDocDiagnosticCodes
{
    public const string PythonNotFound = "RDC001";
    public const string ScriptNotFound = "RDC002";
    public const string ExecutionFailed = "RDC003";
    public const string OutputDirectoryCreated = "RDC004";
    public const string OutputDirectoryFailed = "RDC005";
}