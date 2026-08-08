using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public enum UnrealMemReportMetricStatus
{
    Parsed,
    Missing,
    Invalid
}

public sealed record UnrealMemReportMetric(
    string Group,
    string Name,
    long? ValueKb,
    string? RawValue,
    UnrealMemReportMetricStatus Status,
    int? LineNumber);

public sealed record UnrealMemReportSummary(IReadOnlyList<UnrealMemReportMetric> Metrics);

public sealed record UnrealMemReport(string Changelist, UnrealMemReportSummary Summary);

public sealed record UnrealMemReportParseResult(
    string InputPath,
    UnrealMemReport? Report,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}
