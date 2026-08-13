using System.Text.Json;
using UnrealKit.Core.Analysis;

namespace UnrealKit.Cli;

/// <summary>差分与趋势结果的呈现。两者共用同一套列宽与数值格式，便于对照阅读。</summary>
internal static class AnalyzeResultWriters
{
    private const int MetricNameWidth = 46;
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static void WriteDiff(BaselineDiffResult result, bool onlyChanged, bool json)
    {
        // --only-changed 只影响呈现；汇总与退出码仍按全部指标计算。
        var metrics = onlyChanged
            ? result.Metrics.Where(metric => metric.Assessment != MetricDiffAssessment.Unchanged).ToArray()
            : result.Metrics.ToArray();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Source = result.Source.ToString(),
                result.BaselineInputPath,
                result.CurrentInputPath,
                result.BaselineLabel,
                result.CurrentLabel,
                result.IsSuccess,
                Summary = new
                {
                    Total = result.Metrics.Count,
                    result.RegressedCount,
                    result.ImprovedCount,
                    result.UnchangedCount,
                    result.MissingCount
                },
                Metrics = metrics.Select(metric => new
                {
                    metric.Group,
                    metric.Name,
                    metric.Unit,
                    Direction = metric.Direction.ToString(),
                    metric.BaselineValue,
                    metric.CurrentValue,
                    metric.Delta,
                    metric.DeltaPercent,
                    Status = metric.Status.ToString(),
                    Assessment = metric.Assessment.ToString(),
                    metric.BaselineLineNumber,
                    metric.CurrentLineNumber
                }),
                Diagnostics = result.Diagnostics.Select(diagnostic => new
                {
                    Severity = diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Path,
                    diagnostic.LineNumber,
                    diagnostic.SuggestedFix
                })
            }, IndentedJson));
            return;
        }

        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Baseline: {result.BaselineInputPath}{FormatLabel(result.BaselineLabel)}");
        Console.WriteLine($"Current:  {result.CurrentInputPath}{FormatLabel(result.CurrentLabel)}");

        if (metrics.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{"Metric",-46} {"Unit",-6} {"Baseline",14} {"Current",14} {"Delta",14} {"Delta%",9}  Assessment");
            foreach (var metric in metrics)
            {
                Console.WriteLine(string.Join(' ',
                    FormatMetricName(metric.Group, metric.Name),
                    metric.Unit.PadRight(6),
                    MetricFormatting.Value(metric.BaselineValue).PadLeft(14),
                    MetricFormatting.Value(metric.CurrentValue).PadLeft(14),
                    MetricFormatting.Delta(metric.Delta).PadLeft(14),
                    MetricFormatting.Percent(metric.DeltaPercent).PadLeft(9),
                    $" {DescribeDiff(metric)}"));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{result.Metrics.Count} metric(s): {result.RegressedCount} regressed, {result.ImprovedCount} improved, {result.UnchangedCount} unchanged, {result.MissingCount} missing.");
        if (onlyChanged && metrics.Length != result.Metrics.Count)
        {
            Console.WriteLine($"{result.Metrics.Count - metrics.Length} unchanged metric(s) hidden by --only-changed.");
        }

        CliOutput.WriteDiagnostics(result.Diagnostics);
    }

    internal static void WriteTrend(TrendResult result, bool includePoints, string? exportedPath, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Source = result.Source.ToString(),
                result.ProjectFilePath,
                result.Platform,
                result.Tag,
                result.DeviceSerialNumber,
                From = result.From is { } from ? MetricFormatting.DateExact(from) : null,
                To = result.To is { } to ? MetricFormatting.DateExact(to) : null,
                result.IsSuccess,
                ExportedFilePath = exportedPath,
                Summary = new
                {
                    CaptureCount = result.Captures.Count,
                    MetricCount = result.Series.Count,
                    result.RegressedCount,
                    result.ImprovedCount,
                    result.UnchangedCount
                },
                Captures = result.Captures.Select(capture => new
                {
                    capture.CaptureId,
                    CaptureDate = MetricFormatting.DateExact(capture.CaptureDate),
                    capture.Platform,
                    capture.Tag,
                    capture.DeviceSerialNumber,
                    capture.DeviceModel,
                    capture.InputPath
                }),
                Series = result.Series.Select(series => new
                {
                    series.Group,
                    series.Name,
                    series.Unit,
                    Direction = series.Direction.ToString(),
                    series.PointCount,
                    series.PresentCount,
                    series.MissingCount,
                    series.First,
                    series.Last,
                    series.Minimum,
                    series.Maximum,
                    series.Average,
                    series.TotalDelta,
                    series.TotalDeltaPercent,
                    Assessment = series.OverallAssessment.ToString(),
                    Points = includePoints
                        ? series.Points.Select(point => new
                        {
                            point.CaptureId,
                            CaptureDate = MetricFormatting.DateExact(point.CaptureDate),
                            point.Value,
                            point.DeltaFromPrevious,
                            Assessment = point.Assessment.ToString()
                        })
                        : null
                }),
                Diagnostics = result.Diagnostics.Select(diagnostic => new
                {
                    Severity = diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Path,
                    diagnostic.SuggestedFix
                })
            }, IndentedJson));
            return;
        }

        Console.WriteLine($"Source: {result.Source}");
        Console.WriteLine($"Project: {result.ProjectFilePath}");
        Console.WriteLine($"Filters: platform={result.Platform ?? "any"} tag={result.Tag ?? "any"} device={result.DeviceSerialNumber ?? "any"} from={MetricFormatting.Date(result.From)} to={MetricFormatting.Date(result.To)}");

        if (result.Captures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Captures (oldest to newest):");
            foreach (var capture in result.Captures)
            {
                Console.WriteLine($"  {capture.CaptureDate:yyyy-MM-dd}  {capture.CaptureId}  tag={capture.Tag}  device={capture.DeviceSerialNumber ?? "unknown"}");
            }
        }

        if (result.Series.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{"Metric",-46} {"Unit",-6} {"Points",7} {"First",14} {"Last",14} {"Delta",14} {"Delta%",9}  Assessment");
            foreach (var series in result.Series)
            {
                Console.WriteLine(string.Join(' ',
                    FormatMetricName(series.Group, series.Name),
                    series.Unit.PadRight(6),
                    $"{series.PresentCount}/{series.PointCount}".PadLeft(7),
                    MetricFormatting.Value(series.First).PadLeft(14),
                    MetricFormatting.Value(series.Last).PadLeft(14),
                    MetricFormatting.Delta(series.TotalDelta).PadLeft(14),
                    MetricFormatting.Percent(series.TotalDeltaPercent).PadLeft(9),
                    $" {MetricFormatting.DescribeAssessment(series.OverallAssessment)}"));

                if (!includePoints)
                {
                    continue;
                }

                foreach (var point in series.Points)
                {
                    Console.WriteLine($"      {point.CaptureDate:yyyy-MM-dd}  {MetricFormatting.Truncate(point.CaptureId, 34).PadRight(34)} {MetricFormatting.Value(point.Value).PadLeft(14)} {FormatPointDelta(point).PadLeft(14)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{result.Captures.Count} capture(s), {result.Series.Count} metric(s): {result.RegressedCount} regressed, {result.ImprovedCount} improved, {result.UnchangedCount} unchanged.");
        if (exportedPath is not null)
        {
            Console.WriteLine(exportedPath);
        }

        WriteTrendDiagnostics(result);
    }

    // 趋势诊断不带行号（数据来自多份文件），因此单列一个写法而不复用 CliOutput.WriteDiagnostics。
    private static void WriteTrendDiagnostics(TrendResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.SuggestedFix))
            {
                Console.Error.WriteLine($"  Fix: {diagnostic.SuggestedFix}");
            }
        }
    }

    // 有值但没有前值可比时没有增量，这与「本次测量缺失」不同，两者显示要区分。
    private static string FormatPointDelta(TrendPoint point) => point.DeltaFromPrevious is not null
        ? MetricFormatting.Delta(point.DeltaFromPrevious)
        : point.Value is null ? "missing" : "-";

    private static string FormatMetricName(string group, string name) =>
        MetricFormatting.Truncate($"{group}/{name}", MetricNameWidth).PadRight(MetricNameWidth);

    private static string FormatLabel(string? label) => label is null ? string.Empty : $" ({label})";

    private static string DescribeDiff(MetricDiff metric) => metric.Status switch
    {
        MetricDiffStatus.MissingInBaseline => "missing in baseline",
        MetricDiffStatus.MissingInCurrent => "missing in current",
        MetricDiffStatus.MissingInBoth => "missing in both",
        _ => MetricFormatting.DescribeAssessment(metric.Assessment)
    };
}
