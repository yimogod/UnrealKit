using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed record AndroidMemInfoSummary(long? JavaHeapKb, long? NativeHeapKb, long? CodeKb, long? StackKb, long? GraphicsKb, long? PrivateOtherKb, long? SystemKb, long? TotalPssKb);

public sealed record AndroidMemInfoPssEntry(
    string Name,
    long? TotalPssKb,
    long? PrivateDirtyKb,
    long? PrivateCleanKb,
    long? SwapPssKb,
    long? RssKb,
    long? HeapSizeKb,
    long? HeapAllocKb,
    long? HeapFreeKb,
    int LineNumber);

public sealed record AndroidMemInfoDalvikEntry(string Name, long PssKb, int LineNumber);

public sealed record AndroidMemInfoObjectEntry(string Name, long Count, int LineNumber);

public sealed record AndroidMemInfoReport(string ProcessName, int ProcessId, AndroidMemInfoSummary Summary)
{
    public IReadOnlyList<AndroidMemInfoPssEntry> DetailedPssEntries { get; init; } = [];

    public IReadOnlyList<AndroidMemInfoDalvikEntry> DalvikEntries { get; init; } = [];

    public IReadOnlyList<AndroidMemInfoObjectEntry> ObjectEntries { get; init; } = [];
}

public sealed record AndroidMemInfoParseResult(string InputPath, AndroidMemInfoReport? Report, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}
