using System.Globalization;
using System.Text;
using UnrealKit.Core.Analysis;
using UnrealKit.Core.Runtime;

namespace UnrealKit.Core.Export;

/// <summary>
/// Writes a trend to delimited text. Column names are a published contract; renaming or reordering
/// them is a breaking change.
/// </summary>
public sealed class TrendExportService : ITrendExportService
{
    private static readonly string[] SeriesColumnNames =
    [
        "ProjectFile", "ExportedAtUtc", "ToolVersion", "ToolGitCommit", "Source", "Platform", "Tag", "DeviceSerialNumber",
        "RangeFrom", "RangeTo", "Group", "Metric", "Unit", "Direction",
        "CaptureCount", "PresentCount", "MissingCount",
        "First", "Last", "Minimum", "Maximum", "Average", "TotalDelta", "TotalDeltaPercent", "Assessment"
    ];

    private static readonly string[] PointColumnNames =
    [
        "ProjectFile", "ExportedAtUtc", "ToolVersion", "ToolGitCommit", "Source", "Group", "Metric", "Unit", "Direction",
        "CaptureId", "CaptureDate", "DeviceSerialNumber", "DeviceModel", "Value", "DeltaFromPrevious", "Assessment"
    ];

    private readonly Func<AppVersionInfo> _versionProvider;

    public TrendExportService(Func<AppVersionInfo>? versionProvider = null)
    {
        _versionProvider = versionProvider ?? AppVersionInfoProvider.GetCurrent;
    }

    public Task<TrendExportResult> ExportAsync(TrendExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Result);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        var outputPath = Path.GetFullPath(request.OutputFilePath);
        var format = GetFormat(outputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return WriteAsync(request, outputPath, format, cancellationToken);
    }

    private static TrendExportFormat GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => TrendExportFormat.Csv,
        ".tsv" => TrendExportFormat.Tsv,
        _ => throw new ArgumentException("Trend export output must use a .csv or .tsv extension.", nameof(path))
    };

    private async Task<TrendExportResult> WriteAsync(
        TrendExportRequest request,
        string outputPath,
        TrendExportFormat format,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var delimiter = format == TrendExportFormat.Csv ? ',' : '\t';
        var version = _versionProvider();
        var result = request.Result;
        var deviceById = result.Captures.ToDictionary(capture => capture.CaptureId, StringComparer.Ordinal);

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(JoinDelimited(SeriesColumnNames, delimiter));
        foreach (var series in result.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JoinDelimited(CreateSeriesRow(request, version, series), delimiter));
        }

        if (request.IncludePoints)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(JoinDelimited(PointColumnNames, delimiter));
            foreach (var series in result.Series)
            {
                foreach (var point in series.Points)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    deviceById.TryGetValue(point.CaptureId, out var capture);
                    await writer.WriteLineAsync(JoinDelimited(CreatePointRow(request, version, series, point, capture), delimiter));
                }
            }
        }

        if (result.Diagnostics.Count > 0)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(JoinDelimited(["Severity", "Code", "Message", "Path", "SuggestedFix"], delimiter));
            foreach (var diagnostic in result.Diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JoinDelimited(
                    [diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message, diagnostic.Path, diagnostic.SuggestedFix],
                    delimiter));
            }
        }

        return new TrendExportResult(outputPath, format);
    }

    private static IReadOnlyList<string?> CreateSeriesRow(TrendExportRequest request, AppVersionInfo version, TrendSeries series)
    {
        var result = request.Result;
        return
        [
            result.ProjectFilePath,
            request.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            version.Version,
            version.GitCommit,
            result.Source.ToString(),
            result.Platform,
            result.Tag,
            result.DeviceSerialNumber,
            FormatDate(result.From),
            FormatDate(result.To),
            series.Group,
            series.Name,
            series.Unit,
            series.Direction.ToString(),
            series.PointCount.ToString(CultureInfo.InvariantCulture),
            series.PresentCount.ToString(CultureInfo.InvariantCulture),
            series.MissingCount.ToString(CultureInfo.InvariantCulture),
            FormatValue(series.First),
            FormatValue(series.Last),
            FormatValue(series.Minimum),
            FormatValue(series.Maximum),
            FormatValue(series.Average),
            FormatValue(series.TotalDelta),
            FormatValue(series.TotalDeltaPercent),
            series.OverallAssessment.ToString()
        ];
    }

    private static IReadOnlyList<string?> CreatePointRow(
        TrendExportRequest request,
        AppVersionInfo version,
        TrendSeries series,
        TrendPoint point,
        TrendCapture? capture)
    {
        return
        [
            request.Result.ProjectFilePath,
            request.ExportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            version.Version,
            version.GitCommit,
            request.Result.Source.ToString(),
            series.Group,
            series.Name,
            series.Unit,
            series.Direction.ToString(),
            point.CaptureId,
            FormatDate(point.CaptureDate),
            capture?.DeviceSerialNumber,
            capture?.DeviceModel,
            FormatValue(point.Value),
            FormatValue(point.DeltaFromPrevious),
            point.Assessment.ToString()
        ];
    }

    /// <summary>Missing values are written as the explicit token "missing", never as 0 or an empty cell.</summary>
    internal static string FormatValue(double? value) =>
        value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "missing";

    internal static string? FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string JoinDelimited(IEnumerable<string?> fields, char delimiter) =>
        string.Join(delimiter, fields.Select(field => Escape(field, delimiter)));

    private static string Escape(string? field, char delimiter)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        return field.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0
            ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : field;
    }
}
