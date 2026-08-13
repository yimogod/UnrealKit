using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Parsing;

namespace UnrealKit.Core.Analysis;

/// <summary>
/// Compares a current report against a baseline report of the same type.
/// Inputs are read-only: nothing is written back to either side.
/// </summary>
public sealed class BaselineService : IBaselineService
{
    /// <summary>
    /// Absolute tolerance below which two values are treated as unchanged. Static camera timings are
    /// logged with limited precision, so an exact double comparison would report noise as change.
    /// </summary>
    private const double ValueEpsilon = 1e-9;

    private const string KilobyteUnit = "KB";
    private const string ByteUnit = "B";
    private const string MillisecondUnit = "ms";
    private const string CountUnit = "count";

    public async Task<MetricSnapshot> LoadSnapshotAsync(
        BaselineDiffSource source,
        string inputFilePath,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath))
        {
            throw new ArgumentException($"Baseline diff input must be a file, not a directory: {fullPath}", nameof(inputFilePath));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Baseline diff input file was not found.", fullPath);
        }

        return source switch
        {
            BaselineDiffSource.MemInfo => CreateMemInfoSnapshot(
                await new AndroidMemInfoParser().ParseFileAsync(fullPath, cancellationToken), label),
            BaselineDiffSource.MemReport => CreateMemReportSnapshot(
                await new UnrealMemReportParser().ParseFileAsync(fullPath, cancellationToken), label),
            BaselineDiffSource.StaticCamera => CreateStaticCameraSnapshot(
                await new StaticCameraPerfParser().ParseFileAsync(fullPath, cancellationToken), label),
            BaselineDiffSource.Win64MemInfo => CreateWin64MemInfoSnapshot(
                await new Win64MemInfoParser().ParseFileAsync(fullPath, cancellationToken), label),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported baseline diff source.")
        };
    }

    public async Task<BaselineDiffResult> DiffAsync(
        BaselineDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaselineInputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CurrentInputPath);

        var baseline = await LoadSnapshotAsync(request.Source, request.BaselineInputPath, request.BaselineLabel, cancellationToken);
        var current = await LoadSnapshotAsync(request.Source, request.CurrentInputPath, request.CurrentLabel, cancellationToken);
        return Diff(baseline, current, request.MetricFilter);
    }

    public BaselineDiffResult Diff(
        MetricSnapshot baseline,
        MetricSnapshot current,
        IReadOnlyList<string>? metricFilter = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var diagnostics = new List<Diagnostic>();
        if (baseline.Source != current.Source)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "BDF101",
                $"Baseline source '{baseline.Source}' does not match current source '{current.Source}'.",
                current.InputPath,
                "Compare two reports of the same type."));
            return new BaselineDiffResult(
                baseline.Source, baseline.InputPath, current.InputPath,
                baseline.Label, current.Label, [], diagnostics);
        }

        // Parse problems on either side are carried through, tagged by side so the caller can tell
        // which report to re-capture. Warnings do not block the comparison; errors do.
        AppendSideDiagnostics(diagnostics, baseline, "baseline");
        AppendSideDiagnostics(diagnostics, current, "current");

        if (!baseline.IsSuccess)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "BDF102",
                "The baseline report could not be parsed, so no comparison was performed.",
                baseline.InputPath,
                "Resolve the baseline parse errors listed above, then re-run the comparison."));
        }

        if (!current.IsSuccess)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "BDF103",
                "The current report could not be parsed, so no comparison was performed.",
                current.InputPath,
                "Resolve the current parse errors listed above, then re-run the comparison."));
        }

        if (!baseline.IsSuccess || !current.IsSuccess)
        {
            return new BaselineDiffResult(
                baseline.Source, baseline.InputPath, current.InputPath,
                baseline.Label, current.Label, [], diagnostics);
        }

        var filter = NormalizeFilter(metricFilter);
        var metrics = BuildDiffs(baseline, current, filter, diagnostics);

        if (filter is not null)
        {
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var metric in metrics)
            {
                matched.Add(metric.Name);
                matched.Add($"{metric.Group}/{metric.Name}");
            }

            foreach (var requested in filter.Where(name => !matched.Contains(name)))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "BDF201",
                    $"Requested metric '{requested}' was not found in either report.",
                    current.InputPath,
                    "Run the comparison without --metrics to list every available metric name."));
            }
        }

        if (metrics.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "BDF203",
                "No metrics were available to compare.",
                current.InputPath,
                "Confirm that both reports contain the expected sections, and widen or remove the metric filter."));
        }

        return new BaselineDiffResult(
            baseline.Source, baseline.InputPath, current.InputPath,
            baseline.Label, current.Label, metrics, diagnostics);
    }

    private static List<MetricDiff> BuildDiffs(
        MetricSnapshot baseline,
        MetricSnapshot current,
        IReadOnlySet<string>? filter,
        List<Diagnostic> diagnostics)
    {
        var baselineSamples = IndexSamples(baseline.Samples);
        var currentSamples = IndexSamples(current.Samples);

        // Baseline order first so the output reads as "what the reference measured"; metrics that
        // only exist in the current report are appended rather than dropped.
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in baselineSamples.Keys.Concat(currentSamples.Keys))
        {
            if (seen.Add(key))
            {
                keys.Add(key);
            }
        }

        var metrics = new List<MetricDiff>(keys.Count);
        foreach (var key in keys)
        {
            baselineSamples.TryGetValue(key, out var baselineSample);
            currentSamples.TryGetValue(key, out var currentSample);
            var reference = baselineSample ?? currentSample!;

            if (filter is not null && !filter.Contains(reference.Name) && !filter.Contains(key))
            {
                continue;
            }

            var status = (baselineSample?.Value, currentSample?.Value) switch
            {
                (null, null) => MetricDiffStatus.MissingInBoth,
                (null, not null) => MetricDiffStatus.MissingInBaseline,
                (not null, null) => MetricDiffStatus.MissingInCurrent,
                _ => MetricDiffStatus.Compared
            };

            double? delta = null;
            double? deltaPercent = null;
            var assessment = MetricDiffAssessment.Unknown;

            if (status == MetricDiffStatus.Compared)
            {
                var baselineValue = baselineSample!.Value!.Value;
                var currentValue = currentSample!.Value!.Value;
                delta = currentValue - baselineValue;
                if (baselineValue != 0)
                {
                    deltaPercent = delta.Value / Math.Abs(baselineValue) * 100.0;
                }

                assessment = Assess(delta.Value, reference.Direction);
            }
            else
            {
                // A one-sided metric is reported as missing rather than compared against zero: an
                // absent section is not the same measurement as a section that reported zero.
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "BDF202",
                    status switch
                    {
                        MetricDiffStatus.MissingInBaseline => $"Metric '{key}' is present in the current report but missing in the baseline.",
                        MetricDiffStatus.MissingInCurrent => $"Metric '{key}' is present in the baseline but missing in the current report.",
                        _ => $"Metric '{key}' is missing in both reports."
                    },
                    current.InputPath,
                    "Treat the metric as missing rather than zero; capture both reports with the same settings to compare it."));
            }

            metrics.Add(new MetricDiff(
                reference.Group,
                reference.Name,
                reference.Unit,
                reference.Direction,
                baselineSample?.Value,
                currentSample?.Value,
                delta,
                deltaPercent,
                status,
                assessment,
                baselineSample?.LineNumber,
                currentSample?.LineNumber));
        }

        return metrics;
    }

    private static MetricDiffAssessment Assess(double delta, MetricDirection direction)
    {
        if (Math.Abs(delta) <= ValueEpsilon)
        {
            return MetricDiffAssessment.Unchanged;
        }

        return direction switch
        {
            MetricDirection.LowerIsBetter => delta > 0 ? MetricDiffAssessment.Regressed : MetricDiffAssessment.Improved,
            MetricDirection.HigherIsBetter => delta > 0 ? MetricDiffAssessment.Improved : MetricDiffAssessment.Regressed,
            _ => MetricDiffAssessment.Changed
        };
    }

    private static Dictionary<string, MetricSample> IndexSamples(IReadOnlyList<MetricSample> samples)
    {
        var indexed = new Dictionary<string, MetricSample>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            // First occurrence wins; the underlying parsers already emit duplicate diagnostics.
            indexed.TryAdd($"{sample.Group}/{sample.Name}", sample);
        }

        return indexed;
    }

    private static IReadOnlySet<string>? NormalizeFilter(IReadOnlyList<string>? metricFilter)
    {
        if (metricFilter is null)
        {
            return null;
        }

        var names = metricFilter
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Count == 0 ? null : names;
    }

    private static void AppendSideDiagnostics(List<Diagnostic> diagnostics, MetricSnapshot snapshot, string side)
    {
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            diagnostics.Add(diagnostic with { Message = $"[{side}] {diagnostic.Message}" });
        }
    }

    private static MetricSnapshot CreateMemInfoSnapshot(AndroidMemInfoParseResult result, string? label)
    {
        var samples = new List<MetricSample>();
        if (result.Report is { } report)
        {
            var summary = report.Summary;
            foreach (var (name, value) in new (string, long?)[]
            {
                ("JavaHeapKb", summary.JavaHeapKb),
                ("NativeHeapKb", summary.NativeHeapKb),
                ("CodeKb", summary.CodeKb),
                ("StackKb", summary.StackKb),
                ("GraphicsKb", summary.GraphicsKb),
                ("PrivateOtherKb", summary.PrivateOtherKb),
                ("SystemKb", summary.SystemKb),
                ("TotalPssKb", summary.TotalPssKb)
            })
            {
                samples.Add(new MetricSample("AppSummary", name, KilobyteUnit, MetricDirection.LowerIsBetter, value, null));
            }

            foreach (var entry in report.DetailedPssEntries)
            {
                samples.Add(new MetricSample("DetailedPss", entry.Name, KilobyteUnit, MetricDirection.LowerIsBetter, entry.TotalPssKb, entry.LineNumber));
            }

            foreach (var entry in report.DalvikEntries)
            {
                samples.Add(new MetricSample("Dalvik", entry.Name, KilobyteUnit, MetricDirection.LowerIsBetter, entry.PssKb, entry.LineNumber));
            }

            foreach (var entry in report.ObjectEntries)
            {
                samples.Add(new MetricSample("Objects", entry.Name, CountUnit, MetricDirection.LowerIsBetter, entry.Count, entry.LineNumber));
            }
        }

        return new MetricSnapshot(
            BaselineDiffSource.MemInfo,
            result.InputPath,
            label,
            samples,
            result.Diagnostics,
            result.IsSuccess);
    }

    private static MetricSnapshot CreateWin64MemInfoSnapshot(Win64MemInfoParseResult result, string? label)
    {
        var samples = new List<MetricSample>();
        if (result.Report is { } report)
        {
            var counters = report.Counters;
            foreach (var (name, value) in new (string, long?)[]
            {
                ("WorkingSetBytes", counters.WorkingSetBytes),
                ("PrivateMemoryBytes", counters.PrivateMemoryBytes),
                ("VirtualMemoryBytes", counters.VirtualMemoryBytes),
                ("PagedMemoryBytes", counters.PagedMemoryBytes),
                ("NonPagedMemoryBytes", counters.NonPagedMemoryBytes),
                ("PeakWorkingSetBytes", counters.PeakWorkingSetBytes),
                ("PeakVirtualMemoryBytes", counters.PeakVirtualMemoryBytes)
            })
            {
                samples.Add(new MetricSample("ProcessMemory", name, ByteUnit, MetricDirection.LowerIsBetter, value, null));
            }

            samples.Add(new MetricSample("ProcessMemory", "ThreadCount", CountUnit, MetricDirection.Neutral, counters.ThreadCount, null));
            samples.Add(new MetricSample("ProcessMemory", "HandleCount", CountUnit, MetricDirection.Neutral, counters.HandleCount, null));
        }

        return new MetricSnapshot(
            BaselineDiffSource.Win64MemInfo,
            result.InputPath,
            label,
            samples,
            result.Diagnostics,
            result.IsSuccess);
    }

    private static MetricSnapshot CreateMemReportSnapshot(UnrealMemReportParseResult result, string? label)
    {
        var samples = new List<MetricSample>();
        if (result.Report is { } report)
        {
            foreach (var metric in report.Summary.Metrics)
            {
                // Missing and Invalid both surface as a null value, keeping the metric row visible
                // rather than silently absent from the comparison.
                samples.Add(new MetricSample(
                    metric.Group,
                    metric.Name,
                    KilobyteUnit,
                    MetricDirection.LowerIsBetter,
                    metric.Status == UnrealMemReportMetricStatus.Parsed ? metric.ValueKb : null,
                    metric.LineNumber));
            }

            samples.Add(new MetricSample("Details", "TextureCount", CountUnit, MetricDirection.Neutral, report.Textures.Count, null));
            samples.Add(new MetricSample("Details", "RenderTargetCount", CountUnit, MetricDirection.Neutral, report.RenderTargets.Count, null));
            samples.Add(new MetricSample("Details", "ObjectCount", CountUnit, MetricDirection.Neutral, report.Objects.Count, null));

            foreach (var texture in report.Textures.Where(texture => texture.MemoryKb is not null))
            {
                samples.Add(new MetricSample("Textures", texture.Name, KilobyteUnit, MetricDirection.LowerIsBetter, texture.MemoryKb, texture.LineNumber));
            }

            foreach (var renderTarget in report.RenderTargets.Where(renderTarget => renderTarget.MemoryKb is not null))
            {
                samples.Add(new MetricSample("RenderTargets", renderTarget.Name, KilobyteUnit, MetricDirection.LowerIsBetter, renderTarget.MemoryKb, renderTarget.LineNumber));
            }
        }

        return new MetricSnapshot(
            BaselineDiffSource.MemReport,
            result.InputPath,
            label,
            samples,
            result.Diagnostics,
            result.IsSuccess);
    }

    private static MetricSnapshot CreateStaticCameraSnapshot(StaticCameraPerfParseResult result, string? label)
    {
        var samples = new List<MetricSample>();
        if (result.Report is { } report)
        {
            var average = report.Average;
            samples.Add(new MetricSample("Average", "FrameTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, average.FrameTimeMs, null));
            samples.Add(new MetricSample("Average", "GameTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, average.GameTimeMs, null));
            samples.Add(new MetricSample("Average", "DrawTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, average.DrawTimeMs, null));
            samples.Add(new MetricSample("Average", "RhiTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, average.RhiTimeMs, null));
            samples.Add(new MetricSample("Average", "GpuTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, average.GpuTimeMs, null));
            samples.Add(new MetricSample("Average", "MemoryBytes", ByteUnit, MetricDirection.LowerIsBetter, average.MemoryBytes, null));
            samples.Add(new MetricSample("Average", "DrawCalls", CountUnit, MetricDirection.LowerIsBetter, average.DrawCalls, null));
            samples.Add(new MetricSample("Average", "Triangles", CountUnit, MetricDirection.LowerIsBetter, average.Triangles, null));
            samples.Add(new MetricSample("Coverage", "CameraCount", CountUnit, MetricDirection.Neutral, report.CameraCount, null));
            samples.Add(new MetricSample("Coverage", "ParsedCameraCount", CountUnit, MetricDirection.Neutral, report.ParseCameraCount, null));

            // Cameras are keyed by name so the same viewpoint lines up across runs even when the
            // camera order or count changed between captures.
            foreach (var frame in report.Frames)
            {
                var group = $"Camera:{frame.CameraName}";
                samples.Add(new MetricSample(group, "FrameTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, frame.FrameTimeMs, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "GameTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, frame.GameTimeMs, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "DrawTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, frame.DrawTimeMs, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "RhiTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, frame.RhiTimeMs, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "GpuTimeMs", MillisecondUnit, MetricDirection.LowerIsBetter, frame.GpuTimeMs, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "MemoryBytes", ByteUnit, MetricDirection.LowerIsBetter, frame.MemoryBytes, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "DrawCalls", CountUnit, MetricDirection.LowerIsBetter, frame.DrawCalls, frame.FirstLineNumber));
                samples.Add(new MetricSample(group, "Triangles", CountUnit, MetricDirection.LowerIsBetter, frame.Triangles, frame.FirstLineNumber));
            }
        }

        return new MetricSnapshot(
            BaselineDiffSource.StaticCamera,
            result.InputPath,
            label,
            samples,
            result.Diagnostics,
            result.IsSuccess);
    }
}
