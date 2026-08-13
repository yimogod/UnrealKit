using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Analysis;

/// <summary>Whether an increase in a metric is a regression, an improvement, or carries no quality meaning.</summary>
public enum MetricDirection
{
    LowerIsBetter,
    HigherIsBetter,
    Neutral
}

/// <summary>Availability of a metric on each side of the comparison.</summary>
public enum MetricDiffStatus
{
    Compared,
    MissingInBaseline,
    MissingInCurrent,
    MissingInBoth
}

/// <summary>Quality interpretation of a comparable delta.</summary>
public enum MetricDiffAssessment
{
    Unchanged,
    Improved,
    Regressed,

    /// <summary>The value changed but the metric has no better/worse direction.</summary>
    Changed,

    /// <summary>The delta could not be computed because one side is missing.</summary>
    Unknown
}

/// <summary>Report type both sides of a diff are read from. Never inferred; callers select it explicitly.</summary>
public enum BaselineDiffSource
{
    MemInfo,
    MemReport,
    StaticCamera,

    /// <summary>Win64 进程内存计数器（Win64DeviceService 采集，Win64MemInfoParser 解析）。</summary>
    Win64MemInfo
}

/// <summary>A single named measurement extracted from one report.</summary>
public sealed record MetricSample(
    string Group,
    string Name,
    string Unit,
    MetricDirection Direction,
    double? Value,
    int? LineNumber);

/// <summary>All measurements extracted from one report, with the diagnostics produced while reading it.</summary>
public sealed record MetricSnapshot(
    BaselineDiffSource Source,
    string InputPath,
    string? Label,
    IReadOnlyList<MetricSample> Samples,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool IsSuccess);

/// <summary>One metric compared between the baseline and the current report.</summary>
public sealed record MetricDiff(
    string Group,
    string Name,
    string Unit,
    MetricDirection Direction,
    double? BaselineValue,
    double? CurrentValue,
    double? Delta,
    double? DeltaPercent,
    MetricDiffStatus Status,
    MetricDiffAssessment Assessment,
    int? BaselineLineNumber,
    int? CurrentLineNumber);

/// <summary>
/// Baseline-versus-current comparison request. Both inputs are read-only; nothing is written to them.
/// </summary>
/// <param name="Source">Report type to read on both sides. Both inputs must be the same type.</param>
/// <param name="BaselineInputPath">Path to the report treated as the reference point.</param>
/// <param name="CurrentInputPath">Path to the report being evaluated against the baseline.</param>
/// <param name="MetricFilter">Optional metric names (<c>Name</c> or <c>Group/Name</c>) to restrict the comparison to.</param>
/// <param name="BaselineLabel">Optional display label for the baseline side, such as a capture ID.</param>
/// <param name="CurrentLabel">Optional display label for the current side, such as a capture ID.</param>
public sealed record BaselineDiffRequest(
    BaselineDiffSource Source,
    string BaselineInputPath,
    string CurrentInputPath,
    IReadOnlyList<string>? MetricFilter = null,
    string? BaselineLabel = null,
    string? CurrentLabel = null);

/// <summary>Result of a baseline-versus-current comparison.</summary>
public sealed record BaselineDiffResult(
    BaselineDiffSource Source,
    string BaselineInputPath,
    string CurrentInputPath,
    string? BaselineLabel,
    string? CurrentLabel,
    IReadOnlyList<MetricDiff> Metrics,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    public int RegressedCount => Metrics.Count(metric => metric.Assessment == MetricDiffAssessment.Regressed);

    public int ImprovedCount => Metrics.Count(metric => metric.Assessment == MetricDiffAssessment.Improved);

    public int UnchangedCount => Metrics.Count(metric => metric.Assessment == MetricDiffAssessment.Unchanged);

    public int MissingCount => Metrics.Count(metric => metric.Status != MetricDiffStatus.Compared);
}
