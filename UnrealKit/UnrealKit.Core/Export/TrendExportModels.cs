using UnrealKit.Core.Analysis;

namespace UnrealKit.Core.Export;

public enum TrendExportFormat
{
    Csv,
    Tsv,
    Xlsx
}

/// <param name="Result">Trend to export.</param>
/// <param name="OutputFilePath">Destination. The extension decides the format and never misrepresents it.</param>
/// <param name="ExportedAtUtc">Timestamp recorded in the export metadata.</param>
/// <param name="IncludePoints">
/// Include the per-capture point rows in addition to the per-series summary. The summary must stand
/// on its own without them.
/// </param>
public sealed record TrendExportRequest(
    TrendResult Result,
    string OutputFilePath,
    DateTimeOffset ExportedAtUtc,
    bool IncludePoints = false);

public sealed record TrendExportResult(string OutputFilePath, TrendExportFormat Format);
