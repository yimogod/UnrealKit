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
        var detailedPssEntries = ParseDetailedPssEntries(inputPath, lines, diagnostics);
        var dalvikEntries = ParseDalvikEntries(inputPath, lines, diagnostics);
        var objectEntries = ParseObjectEntries(inputPath, lines, diagnostics);
        var report = header is not null && summary is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? new AndroidMemInfoReport(header.Value.ProcessName, header.Value.ProcessId, summary)
            {
                DetailedPssEntries = detailedPssEntries,
                DalvikEntries = dalvikEntries,
                ObjectEntries = objectEntries
            }
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
            if (line.Contains("Pss", StringComparison.OrdinalIgnoreCase) && line.Contains("KB", StringComparison.OrdinalIgnoreCase)) continue;
            var match = SummaryEntryRegex().Match(line);
            if (!match.Success)
            {
                diagnostics.Add(Error("AMI106", "The 'App Summary' section contains a malformed entry.", inputPath, index + 1, "Expected '<label>: <kilobytes>'."));
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            if (!TryParseNumber(match.Groups["value"].Value, out var kilobytes))
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

    private static IReadOnlyList<AndroidMemInfoPssEntry> ParseDetailedPssEntries(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var entries = new List<AndroidMemInfoPssEntry>();
        var headerIndex = Enumerable.Range(0, lines.Count).FirstOrDefault(index =>
            lines[index].Contains("Pss", StringComparison.OrdinalIgnoreCase) &&
            lines[index].Contains("Private", StringComparison.OrdinalIgnoreCase));
        if (headerIndex == 0 && (lines.Count == 0 || !lines[0].Contains("Pss", StringComparison.OrdinalIgnoreCase))) return entries;

        var dividerIndex = headerIndex + 1;
        while (dividerIndex < lines.Count && !lines[dividerIndex].Contains("---", StringComparison.Ordinal)) dividerIndex++;
        if (dividerIndex == lines.Count) return entries;

        for (var index = dividerIndex + 1; index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]); index++)
        {
            var tokens = lines[index].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var firstValueIndex = Array.FindIndex(tokens, token => IsNumericOrPlaceholder(token));
            if (firstValueIndex <= 0)
            {
                diagnostics.Add(Warning("AMI202", "The detailed PSS table contains a malformed row.", inputPath, index + 1, "Expected a memory category followed by one or more numeric kilobyte columns."));
                continue;
            }

            var values = new long?[8];
            for (var valueIndex = 0; valueIndex < Math.Min(tokens.Length - firstValueIndex, values.Length); valueIndex++)
            {
                var token = tokens[firstValueIndex + valueIndex];
                if (IsPlaceholder(token)) continue;
                if (!TryParseNumber(token, out var value))
                {
                    diagnostics.Add(Warning("AMI203", "The detailed PSS table contains an invalid kilobyte value.", inputPath, index + 1, "Use an integer value, a thousands separator, or a placeholder such as '----'."));
                    break;
                }

                values[valueIndex] = value;
            }

            entries.Add(new AndroidMemInfoPssEntry(string.Join(" ", tokens[..firstValueIndex]), values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], index + 1));
        }

        return entries;
    }

    private static IReadOnlyList<AndroidMemInfoDalvikEntry> ParseDalvikEntries(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var entries = new List<AndroidMemInfoDalvikEntry>();
        var sectionIndex = FindSection(lines, "Dalvik Details");
        if (sectionIndex is null) return entries;

        for (var index = sectionIndex.Value + 1; index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]); index++)
        {
            if (!TryParseNamedNumber(lines[index], out var name, out var value))
            {
                diagnostics.Add(Warning("AMI211", "The Dalvik Details section contains a malformed entry.", inputPath, index + 1, "Expected '<category>: <kilobytes>'."));
                continue;
            }

            entries.Add(new AndroidMemInfoDalvikEntry(name, value, index + 1));
        }

        return entries;
    }

    private static IReadOnlyList<AndroidMemInfoObjectEntry> ParseObjectEntries(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var entries = new List<AndroidMemInfoObjectEntry>();
        var sectionIndex = FindSection(lines, "Objects");
        if (sectionIndex is null) return entries;

        for (var index = sectionIndex.Value + 1; index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]); index++)
        {
            if (!TryParseNamedNumber(lines[index], out var name, out var value))
            {
                diagnostics.Add(Warning("AMI221", "The Objects section contains a malformed entry.", inputPath, index + 1, "Expected '<object type>: <count>'."));
                continue;
            }

            entries.Add(new AndroidMemInfoObjectEntry(name, value, index + 1));
        }

        return entries;
    }

    private static int? FindSection(IReadOnlyList<string> lines, string sectionName)
    {
        var matches = Enumerable.Range(0, lines.Count)
            .Where(index => string.Equals(lines[index].Trim(), sectionName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool TryParseNamedNumber(string line, out string name, out long value)
    {
        var match = SummaryEntryRegex().Match(line);
        name = match.Success ? match.Groups["label"].Value.Trim() : string.Empty;
        value = 0;
        return match.Success && TryParseNumber(match.Groups["value"].Value, out value);
    }

    private static bool TryParseNumber(string value, out long number) => long.TryParse(value.Trim().TrimEnd('K', 'B', 'k', 'b').Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static bool IsNumericOrPlaceholder(string value) => IsPlaceholder(value) || TryParseNumber(value, out _);

    private static bool IsPlaceholder(string value) => value is "----" or "N/A" or "n/a";

    private static Diagnostic Error(string code, string message, string path, int? lineNumber, string suggestedFix) => new(DiagnosticSeverity.Error, code, message, path, suggestedFix, lineNumber);

    private static Diagnostic Warning(string code, string message, string path, int? lineNumber, string suggestedFix) => new(DiagnosticSeverity.Warning, code, message, path, suggestedFix, lineNumber);

    [GeneratedRegex(@"^\s*\*\*\s+MEMINFO\s+in\s+pid\s+(?<pid>\d+)\s+\[(?<process>[^\]]+)\]\s+\*\*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessHeaderRegex();

    [GeneratedRegex(@"^\s*(?<label>[^:]+):\s*(?<value>\S+)\s*(?:KB)?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SummaryEntryRegex();
}
