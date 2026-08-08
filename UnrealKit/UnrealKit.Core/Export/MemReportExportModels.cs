using UnrealKit.Core.Parsing;

namespace UnrealKit.Core.Export;

public sealed record MemReportExportRequest(
    UnrealMemReportParseResult ParseResult,
    string OutputFilePath,
    DateTimeOffset ParsedAtUtc,
    bool IncludeDetails = false,
    string? CaptureId = null);

public sealed record MemReportExportResult(string OutputFilePath, MemInfoExportFormat Format);