using System.Globalization;
using System.Text.RegularExpressions;
using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed partial class AndroidMemInfoParser : IAndroidMemInfoParser
{
    public async Task<AndroidMemInfoParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath)) throw new ArgumentException($"Android meminfo input must be a file, not a directory: {fullPath}", nameof(inputFilePath));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Android meminfo input file was not found.", fullPath);
        return Parse(fullPath, await File.ReadAllLinesAsync(fullPath, cancellationToken));
    }

    public AndroidMemInfoParseResult Parse(string inputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);
        var diagnostics = new List<Diagnostic>();
        var header = FindProcessHeader(inputPath, lines, diagnostics);
        var summaryStart = FindAppSummary(inputPath, lines, diagnostics);
        var summary = summaryStart is null ? null : ParseSummary(inputPath, lines, summaryStart.Value, diagnostics);
        var report = header is not null && summary is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? new AndroidMemInfoReport(header.Value.ProcessName, header.Value.ProcessId, summary)
            : null;
        return new AndroidMemInfoParseResult(inputPath, report, diagnostics);
    }

    private static (string ProcessName, int ProcessId)? FindProcessHeader(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].Contains("** MEMINFO in pid", StringComparison.Ordinal)) continue;
            var match = ProcessHeaderRegex().Match(lines[index]);
            if (!match.Success || !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
            {
                diagnostics.Add(Error("AMI102", "The Android meminfo process header is malformed.", inputPath, index + 1, "Expected '** MEMINFO in pid <number> [<package>] **'."));
                return null;
            }

            return (match.Groups["process"].Value, processId);
        }

        diagnostics.Add(Error("AMI101", "Missing Android meminfo process header.", inputPath, null, "Expected a line such as '** MEMINFO in pid 1234 [com.example.game] **'."));
        return null;
    }

    private static int? FindAppSummary(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var matches = Enumerable.Range(0, lines.Count).Where(index => string.Equals(lines[index].Trim(), "App Summary", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            diagnostics.Add(Error("AMI103", "Missing required 'App Summary' section.", inputPath, null, "Capture the complete output of 'adb shell dumpsys meminfo <package>'."));
            return null;
        }

        if (matches.Length > 1)
        {
            diagnostics.Add(Error("AMI104", "More than one 'App Summary' section was found; the input is ambiguous.", inputPath, matches[1] + 1, "Provide a single dumpsys meminfo output file for one process."));
            return null;
        }

        return matches[0];
    }

    private static AndroidMemInfoSummary ParseSummary(string inputPath, IReadOnlyList<string> lines, int summaryStart, List<Diagnostic> diagnostics)
    {
        long? javaHeapKb = null, nativeHeapKb = null, codeKb = null, stackKb = null, graphicsKb = null, privateOtherKb = null, systemKb = null, totalPssKb = null;
        var totalFound = false;
        for (var index = summaryStart + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) break;
            if (line.Contains("Pss(KB)", StringComparison.OrdinalIgnoreCase)) continue;
            var match = SummaryEntryRegex().Match(line);
            if (!match.Success)
            {
                diagnostics.Add(Error("AMI106", "The 'App Summary' section contains a malformed entry.", inputPath, index + 1, "Expected '<label>: <kilobytes>'."));
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            if (!long.TryParse(match.Groups["value"].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes))
            {
                diagnostics.Add(Error("AMI107", $"The App Summary value for '{label}' is not a valid kilobyte count.", inputPath, index + 1, "Use an integer value, optionally with thousands separators."));
                continue;
            }

            if (string.Equals(label, "Java Heap", StringComparison.OrdinalIgnoreCase)) javaHeapKb = kilobytes;
            else if (string.Equals(label, "Native Heap", StringComparison.OrdinalIgnoreCase)) nativeHeapKb = kilobytes;
            else if (string.Equals(label, "Code", StringComparison.OrdinalIgnoreCase)) codeKb = kilobytes;
            else if (string.Equals(label, "Stack", StringComparison.OrdinalIgnoreCase)) stackKb = kilobytes;
            else if (string.Equals(label, "Graphics", StringComparison.OrdinalIgnoreCase)) graphicsKb = kilobytes;
            else if (string.Equals(label, "Private Other", StringComparison.OrdinalIgnoreCase)) privateOtherKb = kilobytes;
            else if (string.Equals(label, "System", StringComparison.OrdinalIgnoreCase)) systemKb = kilobytes;
            else if (string.Equals(label, "TOTAL", StringComparison.OrdinalIgnoreCase)) { totalPssKb = kilobytes; totalFound = true; }
        }

        if (!totalFound) diagnostics.Add(Error("AMI105", "Missing required 'TOTAL' value in the 'App Summary' section.", inputPath, summaryStart + 1, "Include the complete App Summary section through its TOTAL line."));
        return new AndroidMemInfoSummary(javaHeapKb, nativeHeapKb, codeKb, stackKb, graphicsKb, privateOtherKb, systemKb, totalPssKb);
    }

    private static Diagnostic Error(string code, string message, string path, int? lineNumber, string suggestedFix) => new(DiagnosticSeverity.Error, code, message, path, suggestedFix, lineNumber);

    [GeneratedRegex(@"^\s*\*\*\s+MEMINFO\s+in\s+pid\s+(?<pid>\d+)\s+\[(?<process>[^\]]+)\]\s+\*\*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessHeaderRegex();

    [GeneratedRegex(@"^\s*(?<label>[^:]+):\s*(?<value>\S+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryEntryRegex();
}