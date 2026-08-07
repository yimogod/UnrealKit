using UnrealKit.Core.Parsing;

namespace UnrealKit.Core.Capture;

public sealed record CaptureDirectoryInfo(
    string FullPath,
    string RelativePath,
    string CaptureId,
    string Platform,
    string Tag,
    DateTimeOffset CaptureDate,
    string ManifestPath,
    bool HasManifest);

public sealed record CaptureFileInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    string Category);

public sealed record CaptureAnalysisRequest(
    Projects.UkitProject Project,
    string CaptureDirectoryPath,
    string InputFilePath,
    string? AnalysisId = null);

public sealed record CaptureAnalysisResult(
    string AnalysisId,
    string AnalysisDirectory,
    string CaptureId,
    string InputFilePath,
    AndroidMemInfoParseResult ParseResult,
    string ResultJsonPath,
    IReadOnlyList<Diagnostics.Diagnostic> Diagnostics);

public sealed record CaptureAnalysisMetadata(
    string AnalysisId,
    string CaptureId,
    string InputFilePath,
    string InputFileName,
    DateTimeOffset ParsedAtUtc,
    string ToolVersion,
    string? ToolGitCommit,
    string? ProcessName,
    int? ProcessId,
    long? TotalPssKb,
    int DiagnosticCount,
    bool IsSuccess);
