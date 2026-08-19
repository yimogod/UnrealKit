using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Analysis;

/// <summary>
/// Selects the captures and metrics that make up a trend. Capture archives are read-only;
/// derived output goes to <c>Saved/</c> via the export services.
/// </summary>
/// <param name="Project">Project whose <c>Content/</c> archive is scanned.</param>
/// <param name="Source">Report type to read from each capture. Both the type and the file are explicit, never inferred.</param>
/// <param name="Platform">Optional platform directory filter. Null scans every platform directory.</param>
/// <param name="Tag">Optional capture tag filter.</param>
/// <param name="DeviceSerialNumber">Optional device filter, matched against the capture manifest.</param>
/// <param name="From">Optional inclusive lower bound on capture date.</param>
/// <param name="To">Optional inclusive upper bound on capture date.</param>
/// <param name="MetricFilter">Optional metric names (<c>Name</c> or <c>Group/Name</c>) to restrict the series to.</param>
/// <param name="FileName">
/// Optional file name to read inside each capture. Required when a capture holds more than one
/// candidate file, because picking one implicitly would silently compare different inputs across points.
/// </param>
public sealed record TrendRequest(
    Projects.UkitProject Project,
    BaselineDiffSource Source,
    string? Platform = null,
    string? Tag = null,
    string? DeviceSerialNumber = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<string>? MetricFilter = null,
    string? FileName = null);

/// <summary>Identifies one capture that contributes a point to every series.</summary>
public sealed record TrendCapture(
    string CaptureId,
    DateTimeOffset CaptureDate,
    string Platform,
    string Tag,
    string? DeviceSerialNumber,
    string? DeviceModel,
    string InputPath);

/// <summary>One metric value at one capture. A null value means the metric was absent from that capture.</summary>
public sealed record TrendPoint(
    string CaptureId,
    DateTimeOffset CaptureDate,
    double? Value,

    /// <summary>Change from the previous point that has a value. Null at the first such point.</summary>
    double? DeltaFromPrevious,

    MetricDiffAssessment Assessment);

/// <summary>One metric tracked across every capture in the trend, in chronological order.</summary>
public sealed record TrendSeries(
    string Group,
    string Name,
    string Unit,
    MetricDirection Direction,
    IReadOnlyList<TrendPoint> Points)
{
    private IReadOnlyList<double> PresentValues => Points.Where(point => point.Value is not null).Select(point => point.Value!.Value).ToArray();

    public int PointCount => Points.Count;

    /// <summary>Number of captures where this metric was present.</summary>
    public int PresentCount => Points.Count(point => point.Value is not null);

    /// <summary>Number of captures where this metric was absent. Absent is never treated as zero.</summary>
    public int MissingCount => Points.Count - PresentCount;

    public double? Minimum => PresentValues.Count == 0 ? null : PresentValues.Min();

    public double? Maximum => PresentValues.Count == 0 ? null : PresentValues.Max();

    public double? Average => PresentValues.Count == 0 ? null : PresentValues.Average();

    /// <summary>Value at the earliest capture that has one.</summary>
    public double? First => PresentValues.Count == 0 ? null : PresentValues[0];

    /// <summary>Value at the latest capture that has one.</summary>
    public double? Last => PresentValues.Count == 0 ? null : PresentValues[^1];

    /// <summary>Change from the first present value to the last. Null when fewer than two captures have a value.</summary>
    public double? TotalDelta => PresentValues.Count < 2 ? null : Last!.Value - First!.Value;

    /// <summary>
    /// <see cref="TotalDelta"/> relative to the first present value. Null when the first value is zero,
    /// so no infinite percentage is reported.
    /// </summary>
    public double? TotalDeltaPercent => TotalDelta is null || First is null || First.Value == 0
        ? null
        : TotalDelta.Value / Math.Abs(First.Value) * 100.0;

    /// <summary>Quality reading of <see cref="TotalDelta"/>, using the metric's own direction.</summary>
    public MetricDiffAssessment OverallAssessment => Points.Count == 0 || PresentCount < 2
        ? MetricDiffAssessment.Unknown
        : Points[^1].Assessment == MetricDiffAssessment.Unknown
            ? MetricDiffAssessment.Unknown
            : AssessDelta(TotalDelta, Direction);

    internal static MetricDiffAssessment AssessDelta(double? delta, MetricDirection direction)
    {
        if (delta is null)
        {
            return MetricDiffAssessment.Unknown;
        }

        if (Math.Abs(delta.Value) <= 1e-9)
        {
            return MetricDiffAssessment.Unchanged;
        }

        return direction switch
        {
            MetricDirection.LowerIsBetter => delta.Value > 0 ? MetricDiffAssessment.Regressed : MetricDiffAssessment.Improved,
            MetricDirection.HigherIsBetter => delta.Value > 0 ? MetricDiffAssessment.Improved : MetricDiffAssessment.Regressed,
            _ => MetricDiffAssessment.Changed
        };
    }
}

/// <summary>A metric trend across a filtered, chronologically ordered set of captures.</summary>
public sealed record TrendResult(
    BaselineDiffSource Source,
    string ProjectFilePath,
    string? Platform,
    string? Tag,
    string? DeviceSerialNumber,
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<TrendCapture> Captures,
    IReadOnlyList<TrendSeries> Series,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    public int RegressedCount => Series.Count(series => series.OverallAssessment == MetricDiffAssessment.Regressed);

    public int ImprovedCount => Series.Count(series => series.OverallAssessment == MetricDiffAssessment.Improved);

    public int UnchangedCount => Series.Count(series => series.OverallAssessment == MetricDiffAssessment.Unchanged);
}
