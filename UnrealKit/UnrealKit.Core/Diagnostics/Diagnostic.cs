namespace UnrealKit.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Path = null,
    string? SuggestedFix = null);
