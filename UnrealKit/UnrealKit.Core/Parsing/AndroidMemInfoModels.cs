using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed record AndroidMemInfoSummary(long? JavaHeapKb, long? NativeHeapKb, long? CodeKb, long? StackKb, long? GraphicsKb, long? PrivateOtherKb, long? SystemKb, long? TotalPssKb);

public sealed record AndroidMemInfoReport(string ProcessName, int ProcessId, AndroidMemInfoSummary Summary);

public sealed record AndroidMemInfoParseResult(string InputPath, AndroidMemInfoReport? Report, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}