namespace UnrealKit.Core.Parsing;

public interface IWin64MemInfoParser
{
    Task<Win64MemInfoParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default);

    Win64MemInfoParseResult Parse(string inputPath, IReadOnlyList<string> lines);
}