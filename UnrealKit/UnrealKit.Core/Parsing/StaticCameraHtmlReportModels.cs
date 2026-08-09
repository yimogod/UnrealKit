namespace UnrealKit.Core.Parsing;

public sealed record StaticCameraHtmlReportRequest(
    StaticCameraPerfParseResult ParseResult,
    string OutputFilePath,
    StaticCameraPerfConfig? Config = null);

public sealed record StaticCameraHtmlReportResult(
    string OutputFilePath);
