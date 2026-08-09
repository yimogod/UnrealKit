using System.Globalization;
using System.Text.RegularExpressions;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed partial class Win64MemInfoParser : IWin64MemInfoParser
{
    public async Task<Win64MemInfoParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath))
            throw new ArgumentException($"Win64 meminfo input must be a file, not a directory: {fullPath}", nameof(inputFilePath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Win64 meminfo input file was not found.", fullPath);
        return Parse(fullPath, await File.ReadAllLinesAsync(fullPath, cancellationToken));
    }

    public Win64MemInfoParseResult Parse(string inputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);
        var diagnostics = new List<Diagnostic>();

        var header = FindProcessHeader(inputPath, lines, diagnostics);
        var counters = ParseCounters(inputPath, lines, diagnostics);

        var report = header is not null
            && !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            ? new Win64MemInfoReport(header.Value.ProcessName, header.Value.ProcessId, counters)
            : null;

        return new Win64MemInfoParseResult(inputPath, report, diagnostics);
    }

    private static (string ProcessName, int ProcessId)? FindProcessHeader(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Contains("** WIN64 MEMINFO for process", StringComparison.Ordinal))
                continue;
            var match = ProcessHeaderRegex().Match(lines[i]);
            if (!match.Success || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                diagnostics.Add(Error("WMI101", "The Win64 meminfo process header is malformed.", inputPath, i + 1,
                    "Expected '** WIN64 MEMINFO for process <name> (PID: <number>) **'."));
                return null;
            }

            return (match.Groups["name"].Value, pid);
        }

        diagnostics.Add(Error("WMI100", "Missing Win64 meminfo process header.", inputPath, null,
            "Expected a line such as '** WIN64 MEMINFO for process MyGame (PID: 12345) **'."));
        return null;
    }

    private static Win64MemInfoCounters ParseCounters(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        long? workingSet = null, privateMem = null, virtualMem = null, pagedMem = null, nonPagedMem = null;
        long? peakWorkingSet = null, peakVirtualMem = null;
        int threadCount = 0, handleCount = 0;
        string? totalProcessorTime = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var match = CounterRegex().Match(line);
            if (!match.Success) continue;

            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            var lineNum = i + 1;

            switch (key)
            {
                case "WorkingSetMB":
                    workingSet = ParseLongBytes(value, "WorkingSetMB", inputPath, lineNum, diagnostics);
                    break;
                case "PrivateMemoryMB":
                    privateMem = ParseLongBytes(value, "PrivateMemoryMB", inputPath, lineNum, diagnostics);
                    break;
                case "VirtualMemoryMB":
                    virtualMem = ParseLongBytes(value, "VirtualMemoryMB", inputPath, lineNum, diagnostics);
                    break;
                case "PagedMemoryMB":
                    pagedMem = ParseLongBytes(value, "PagedMemoryMB", inputPath, lineNum, diagnostics);
                    break;
                case "NonPagedMemoryMB":
                    nonPagedMem = ParseLongBytes(value, "NonPagedMemoryMB", inputPath, lineNum, diagnostics);
                    break;
                case "PeakWorkingSetMB":
                    peakWorkingSet = ParseLongBytes(value, "PeakWorkingSetMB", inputPath, lineNum, diagnostics);
                    break;
                case "PeakVirtualMemoryMB":
                    peakVirtualMem = ParseLongBytes(value, "PeakVirtualMemoryMB", inputPath, lineNum, diagnostics);
                    break;
                case "Threads":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out threadCount))
                        diagnostics.Add(Warning("WMI103", $"Could not parse Threads value: {value}", inputPath, lineNum, "Expected an integer."));
                    break;
                case "Handles":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out handleCount))
                        diagnostics.Add(Warning("WMI103", $"Could not parse Handles value: {value}", inputPath, lineNum, "Expected an integer."));
                    break;
                case "TotalProcessorTime":
                    totalProcessorTime = value;
                    break;
            }
        }

        return new Win64MemInfoCounters(workingSet, privateMem, virtualMem, pagedMem, nonPagedMem, peakWorkingSet, peakVirtualMem, threadCount, handleCount, totalProcessorTime);
    }

    private static long? ParseLongBytes(string value, string fieldName, string inputPath, int lineNum, List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb))
            return (long)(mb * 1024 * 1024);
        diagnostics.Add(Warning("WMI103", $"Could not parse {fieldName} value: {value}", inputPath, lineNum, "Expected a numeric value in MB."));
        return null;
    }

    private static Diagnostic Error(string code, string message, string path, int? lineNumber, string suggestedFix) =>
        new(DiagnosticSeverity.Error, code, message, path, suggestedFix, lineNumber);

    private static Diagnostic Warning(string code, string message, string path, int? lineNumber, string suggestedFix) =>
        new(DiagnosticSeverity.Warning, code, message, path, suggestedFix, lineNumber);

    [GeneratedRegex(@"^\*\*\s+WIN64\s+MEMINFO\s+for\s+process\s+(?<name>.+?)\s+\(PID:\s*(?<pid>\d+)\)\s+\*\*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessHeaderRegex();

    [GeneratedRegex(@"^\s*(?<key>\w+):\s*(?<value>.+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CounterRegex();
}