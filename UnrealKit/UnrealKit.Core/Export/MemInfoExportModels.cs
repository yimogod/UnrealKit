using UnrealKit.Core.Parsing;

namespace UnrealKit.Core.Export;

public enum MemInfoExportFormat
{
    Csv,
    Tsv,
    Xlsx
}

public sealed record MemInfoExportRequest(
    AndroidMemInfoParseResult ParseResult,
    string OutputFilePath,
    DateTimeOffset ParsedAtUtc,
    bool IncludeDetails = false,
    string? CaptureId = null);

public sealed record MemInfoExportResult(string OutputFilePath, MemInfoExportFormat Format);
