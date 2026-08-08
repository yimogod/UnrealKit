namespace UnrealKit.Core.Parsing;

public interface IUnrealMemReportParser
{
    Task<UnrealMemReportParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default);

    UnrealMemReportParseResult Parse(string inputPath, IReadOnlyList<string> lines);
}
