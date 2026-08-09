namespace UnrealKit.Core.Parsing;

using UnrealKit.Core.Diagnostics;

public sealed record Win64MemInfoCounters(
    long? WorkingSetBytes,
    long? PrivateMemoryBytes,
    long? VirtualMemoryBytes,
    long? PagedMemoryBytes,
    long? NonPagedMemoryBytes,
    long? PeakWorkingSetBytes,
    long? PeakVirtualMemoryBytes,
    int ThreadCount,
    int HandleCount,
    string? TotalProcessorTime);

public sealed record Win64MemInfoReport(string ProcessName, int ProcessId, Win64MemInfoCounters Counters);

public sealed record Win64MemInfoParseResult(string InputPath, Win64MemInfoReport? Report, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsSuccess => Report is not null && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}