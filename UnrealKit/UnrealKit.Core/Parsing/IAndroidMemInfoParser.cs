namespace UnrealKit.Core.Parsing;

public interface IAndroidMemInfoParser
{
    Task<AndroidMemInfoParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default);

    AndroidMemInfoParseResult Parse(string inputPath, IReadOnlyList<string> lines);
}