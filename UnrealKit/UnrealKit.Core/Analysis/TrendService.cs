using System.Text.Json;
using UnrealKit.Core.Capture;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Analysis;

/// <summary>
/// Builds per-metric series across multiple captures. Reads capture archives only; derived output is
/// written by the export services under <c>Saved/</c>.
/// </summary>
public sealed class TrendService : ITrendService
{
    private readonly IBaselineService _baselineService;
    private readonly ICaptureAnalysisService _captureAnalysisService;

    public TrendService(
        IBaselineService? baselineService = null,
        ICaptureAnalysisService? captureAnalysisService = null)
    {
        _baselineService = baselineService ?? new BaselineService();
        _captureAnalysisService = captureAnalysisService ?? new CaptureAnalysisService();
    }

    public async Task<TrendResult> BuildTrendAsync(
        TrendRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);

        if (request.From is not null && request.To is not null && request.From > request.To)
        {
            throw new ArgumentException($"Trend range start ({request.From:yyyy-MM-dd}) must not be later than its end ({request.To:yyyy-MM-dd}).", nameof(request));
        }

        var diagnostics = new List<Diagnostic>();
        var candidates = await _captureAnalysisService.ListCaptureDirectoriesAsync(
            request.Project, request.Platform, request.Tag, cancellationToken);

        // ListCaptureDirectoriesAsync returns newest first; a trend reads oldest to newest.
        var ordered = candidates
            .Where(capture => WithinRange(capture.CaptureDate, request.From, request.To))
            .OrderBy(capture => capture.CaptureDate)
            .ThenBy(capture => capture.CaptureId, StringComparer.Ordinal)
            .ToArray();

        var captures = new List<TrendCapture>();
        var snapshots = new List<MetricSnapshot>();

        foreach (var candidate in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await ReadManifestAsync(candidate, diagnostics, cancellationToken);

            if (request.DeviceSerialNumber is not null &&
                !string.Equals(manifest?.DeviceSerialNumber, request.DeviceSerialNumber, StringComparison.Ordinal))
            {
                // A capture without a manifest cannot be attributed to a device, so it is excluded
                // from a device-filtered trend rather than assumed to match.
                continue;
            }

            var inputPath = await ResolveInputPathAsync(candidate, request, diagnostics, cancellationToken);
            if (inputPath is null)
            {
                continue;
            }

            var snapshot = await _baselineService.LoadSnapshotAsync(
                request.Source, inputPath, candidate.CaptureId, cancellationToken);

            if (!snapshot.IsSuccess)
            {
                // One unreadable capture drops out of the trend as a warning rather than failing the
                // whole range, but it is never silently skipped.
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "TRD202",
                    $"Capture '{candidate.CaptureId}' was excluded because its report could not be parsed.",
                    inputPath,
                    "Run 'unrealkit parse' on this capture to see the parse errors, then re-run the trend."));
                // The underlying parse errors stay visible with their original codes, but are recorded
                // at Warning severity: the capture was excluded, and that did not fail the trend.
                foreach (var diagnostic in snapshot.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    diagnostics.Add(diagnostic with
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = $"[{candidate.CaptureId}] {diagnostic.Message}"
                    });
                }

                continue;
            }

            captures.Add(new TrendCapture(
                candidate.CaptureId,
                candidate.CaptureDate,
                candidate.Platform,
                candidate.Tag,
                manifest?.DeviceSerialNumber,
                manifest?.DeviceModel,
                inputPath));
            snapshots.Add(snapshot);
        }

        if (captures.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "TRD203",
                "No captures matched the trend filters, so no series were produced.",
                request.Project.ProjectFilePath,
                "Widen the platform, tag, device, or date filters, or run 'unrealkit parse capture-list' to see available captures."));
        }

        var filter = NormalizeFilter(request.MetricFilter);
        var series = BuildSeries(captures, snapshots, filter, diagnostics, request);

        return new TrendResult(
            request.Source,
            request.Project.ProjectFilePath,
            request.Platform,
            request.Tag,
            request.DeviceSerialNumber,
            request.From,
            request.To,
            captures,
            series,
            diagnostics);
    }

    private static List<TrendSeries> BuildSeries(
        IReadOnlyList<TrendCapture> captures,
        IReadOnlyList<MetricSnapshot> snapshots,
        IReadOnlySet<string>? filter,
        List<Diagnostic> diagnostics,
        TrendRequest request)
    {
        // Metric order follows first appearance across captures, so the oldest capture's layout leads
        // and metrics introduced later are appended rather than dropped.
        var definitions = new List<MetricSample>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexed = new List<Dictionary<string, MetricSample>>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var samples = new Dictionary<string, MetricSample>(StringComparer.OrdinalIgnoreCase);
            foreach (var sample in snapshot.Samples)
            {
                var key = $"{sample.Group}/{sample.Name}";
                if (samples.TryAdd(key, sample) && seen.Add(key))
                {
                    definitions.Add(sample);
                }
            }

            indexed.Add(samples);
        }

        var series = new List<TrendSeries>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            var key = $"{definition.Group}/{definition.Name}";
            if (filter is not null && !filter.Contains(definition.Name) && !filter.Contains(key))
            {
                continue;
            }

            matched.Add(definition.Name);
            matched.Add(key);

            var points = new List<TrendPoint>(captures.Count);
            double? previousValue = null;
            for (var index = 0; index < captures.Count; index++)
            {
                indexed[index].TryGetValue(key, out var sample);
                var value = sample?.Value;

                // Deltas step from the previous capture that actually had a value, so a gap in the
                // middle of the range does not read as a drop to zero and back.
                var delta = value is not null && previousValue is not null ? value.Value - previousValue.Value : (double?)null;
                points.Add(new TrendPoint(
                    captures[index].CaptureId,
                    captures[index].CaptureDate,
                    value,
                    delta,
                    TrendSeries.AssessDelta(delta, definition.Direction)));

                if (value is not null)
                {
                    previousValue = value;
                }
            }

            var trendSeries = new TrendSeries(definition.Group, definition.Name, definition.Unit, definition.Direction, points);
            if (trendSeries.MissingCount > 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "TRD204",
                    $"Metric '{key}' is missing in {trendSeries.MissingCount} of {trendSeries.PointCount} captures.",
                    request.Project.ProjectFilePath,
                    "Missing points are reported as missing rather than zero; capture the range with consistent settings to close the gaps."));
            }

            series.Add(trendSeries);
        }

        if (filter is not null)
        {
            foreach (var requested in filter.Where(name => !matched.Contains(name)))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "TRD201",
                    $"Requested metric '{requested}' was not found in any capture in the range.",
                    request.Project.ProjectFilePath,
                    "Run the trend without --metrics to list every available metric name."));
            }
        }

        return series;
    }

    private async Task<string?> ResolveInputPathAsync(
        CaptureDirectoryInfo capture,
        TrendRequest request,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var files = await _captureAnalysisService.ListCaptureFilesAsync(capture.FullPath, cancellationToken);

        if (request.FileName is not null)
        {
            var named = files.FirstOrDefault(file => string.Equals(file.FileName, request.FileName, StringComparison.Ordinal));
            if (named is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "TRD101",
                    $"Capture '{capture.CaptureId}' does not contain '{request.FileName}' and was excluded from the trend.",
                    capture.FullPath,
                    "Use a file name shared by every capture in the range, or narrow the range to captures that have it."));
                return null;
            }

            return named.FullPath;
        }

        var category = ResolveCategory(request.Source);
        var candidates = files.Where(file => string.Equals(file.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 1)
        {
            return candidates[0].FullPath;
        }

        // Neither "no candidate" nor "several candidates" is resolved implicitly: picking one would
        // silently plot different inputs at different points on the same series.
        diagnostics.Add(candidates.Length == 0
            ? new Diagnostic(
                DiagnosticSeverity.Warning,
                "TRD102",
                $"Capture '{capture.CaptureId}' contains no {category} files and was excluded from the trend.",
                capture.FullPath,
                "Confirm the capture completed, or restrict the trend to captures of the expected source type.")
            : new Diagnostic(
                DiagnosticSeverity.Warning,
                "TRD103",
                $"Capture '{capture.CaptureId}' contains {candidates.Length} {category} files and was excluded because the input is ambiguous.",
                capture.FullPath,
                $"Specify the file name to read from every capture: {string.Join(", ", candidates.Select(file => file.FileName))}"));
        return null;
    }

    private static string ResolveCategory(BaselineDiffSource source) => source switch
    {
        BaselineDiffSource.MemInfo => "MemInfo",
        BaselineDiffSource.MemReport => "Saved",
        BaselineDiffSource.StaticCamera => "Saved",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported trend source.")
    };

    private static async Task<CaptureManifest?> ReadManifestAsync(
        CaptureDirectoryInfo capture,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!capture.HasManifest)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "TRD104",
                $"Capture '{capture.CaptureId}' has no CaptureManifest.json, so its device and configuration are unknown.",
                capture.FullPath,
                "Device filtering cannot include this capture. Re-import it so a manifest is generated."));
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(capture.ManifestPath);
            return await JsonSerializer.DeserializeAsync<CaptureManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "TRD105",
                $"Capture '{capture.CaptureId}' has a CaptureManifest.json that could not be read: {exception.Message}",
                capture.ManifestPath,
                "Device filtering cannot include this capture. Repair or re-import the archive."));
            return null;
        }
    }

    private static bool WithinRange(DateTimeOffset captureDate, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is not null && captureDate.Date < from.Value.Date)
        {
            return false;
        }

        return to is null || captureDate.Date <= to.Value.Date;
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
}
