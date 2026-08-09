namespace UnrealKit.Core.Parsing;

public interface IStaticCameraPerfParser
{
    Task<StaticCameraPerfParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default);
    Task<StaticCameraPerfParseResult> ParseFileAsync(string inputFilePath, string screenshotsDirectory, CancellationToken cancellationToken = default);
    StaticCameraPerfParseResult Parse(string inputPath, IReadOnlyList<string> lines, string? screenshotsDirectory = null);
    StaticCameraPerfParseResult Parse(string inputPath, IReadOnlyList<string> lines, StaticCameraPerfConfig config, string? screenshotsDirectory = null);
}