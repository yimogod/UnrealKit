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

        var columns = ParseDetailedPssColumns(lines, headerIndex, dividerIndex);
        if (columns.Count == 0)
        {
            diagnostics.Add(Warning("AMI201", "The detailed PSS table header could not be mapped.", inputPath, headerIndex + 1, "Include a PSS table header with supported columns such as Pss, Private Dirty, SwapPss, Rss, or Heap Size."));
            return entries;
        }

        for (var index = dividerIndex + 1; index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]); index++)
        {
            var tokens = lines[index].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var firstValueIndex = Array.FindIndex(tokens, token => IsNumericOrPlaceholder(token));
            if (firstValueIndex <= 0)
            {
                diagnostics.Add(Warning("AMI202", "The detailed PSS table contains a malformed row.", inputPath, index + 1, "Expected a memory category followed by one or more numeric kilobyte columns."));
                continue;
            }

            var values = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            for (var valueIndex = 0; valueIndex < Math.Min(tokens.Length - firstValueIndex, columns.Count); valueIndex++)
            {
                var token = tokens[firstValueIndex + valueIndex];
                if (IsPlaceholder(token)) continue;
                if (!TryParseNumber(token, out var value))
                {
                    diagnostics.Add(Warning("AMI203", "The detailed PSS table contains an invalid kilobyte value.", inputPath, index + 1, "Use an integer value, a thousands separator, or a placeholder such as '----'."));
                    break;
                }

                if (columns[valueIndex] is { } column) values[column] = value;
            }

            entries.Add(new AndroidMemInfoPssEntry(
                string.Join(" ", tokens[..firstValueIndex]),
                GetColumnValue(values, "TotalPss"),
                GetColumnValue(values, "PrivateDirty"),
                GetColumnValue(values, "PrivateClean"),
                GetColumnValue(values, "SwapPss"),
                GetColumnValue(values, "Rss"),
                GetColumnValue(values, "HeapSize"),
                GetColumnValue(values, "HeapAlloc"),
                GetColumnValue(values, "HeapFree"),
                index + 1));
        }

        return entries;
    }

    private static IReadOnlyList<string?> ParseDetailedPssColumns(IReadOnlyList<string> lines, int headerIndex, int dividerIndex)
    {
        var columnCount = lines[dividerIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var headerRows = Enumerable.Range(headerIndex, dividerIndex - headerIndex)
            .Select(index => lines[index].Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(tokens => tokens.Length == columnCount)
            .ToArray();
        if (columnCount == 0 || headerRows.Length == 0) return [];

        var columns = new string?[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var label = string.Concat(headerRows.Select(row => row[columnIndex]));
            columns[columnIndex] = NormalizeDetailedPssColumn(label);
        }

        return columns;
    }

    private static string? NormalizeDetailedPssColumn(string label) => label.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
    {
        "PSS" or "PSSTOTAL" => "TotalPss",
        "PRIVATEDIRTY" => "PrivateDirty",
        "PRIVATECLEAN" => "PrivateClean",
        "SWAPPSS" or "SWAPPSSDIRTY" => "SwapPss",
        "RSS" or "RSSTOTAL" => "Rss",
        "HEAPSIZE" => "HeapSize",
        "HEAPALLOC" => "HeapAlloc",
        "HEAPFREE" => "HeapFree",
        _ => null
    };

    private static long? GetColumnValue(IReadOnlyDictionary<string, long?> values, string column) => values.TryGetValue(column, out var value) ? value : null;
    private static IReadOnlyList<AndroidMemInfoDalvikEntry> ParseDalvikEntries(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var entries = ParseNamedSection(inputPath, lines, diagnostics, "Dalvik Details", "AMI210", "AMI211", "AMI212", "AMI213", "Dalvik Details", "<category>: <kilobytes>");
        return entries.Select(entry => new AndroidMemInfoDalvikEntry(entry.Name, entry.Value, entry.LineNumber)).ToArray();
    }

    private static IReadOnlyList<AndroidMemInfoObjectEntry> ParseObjectEntries(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var entries = ParseNamedSection(inputPath, lines, diagnostics, "Objects", "AMI220", "AMI221", "AMI222", "AMI223", "Objects", "<object type>: <count>");
        return entries.Select(entry => new AndroidMemInfoObjectEntry(entry.Name, entry.Value, entry.LineNumber)).ToArray();
    }

    private static IReadOnlyList<(string Name, long Value, int LineNumber)> ParseNamedSection(
        string inputPath,
        IReadOnlyList<string> lines,
        List<Diagnostic> diagnostics,
        string sectionName,
        string duplicateSectionCode,
        string malformedEntryCode,
        string duplicateEntryCode,
        string truncationCode,
        string displayName,
        string entryFormat)
    {
        var entries = new List<(string Name, long Value, int LineNumber)>();
        var sectionIndices = FindSections(lines, sectionName);
        if (sectionIndices.Count == 0) return entries;

        if (sectionIndices.Count > 1)
        {
            foreach (var sectionIndex in sectionIndices.Skip(1))
            {
                diagnostics.Add(Warning(duplicateSectionCode, $"More than one '{displayName}' section was found; entries from every section are retained.", inputPath, sectionIndex + 1, "Capture one process dump when possible, then compare duplicate sections by their line numbers."));
            }
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sectionIndex in sectionIndices)
        {
            var sectionEntries = 0;
            for (var index = sectionIndex + 1; index < lines.Count && !string.IsNullOrWhiteSpace(lines[index]); index++)
            {
                if (IsKnownSectionHeader(lines[index])) break;
                if (!TryParseNamedNumber(lines[index], out var name, out var value))
                {
                    diagnostics.Add(Warning(malformedEntryCode, $"The {displayName} section contains a malformed entry.", inputPath, index + 1, $"Expected '{entryFormat}'."));
                    continue;
                }

                if (!seenNames.Add(name))
                {
                    diagnostics.Add(Warning(duplicateEntryCode, $"The {displayName} section contains a duplicate '{name}' entry; all values are retained.", inputPath, index + 1, "Use the line number to determine whether this is an OEM-specific subdivision or duplicate output."));
                }

                entries.Add((name, value, index + 1));
                sectionEntries++;
            }

            if (sectionEntries == 0)
            {
                diagnostics.Add(Warning(truncationCode, $"The {displayName} section ends without any entries and may be truncated.", inputPath, sectionIndex + 1, $"Capture the complete '{displayName}' section with at least one '{entryFormat}' entry."));
            }
        }

        return entries;
    }

    private static IReadOnlyList<int> FindSections(IReadOnlyList<string> lines, string sectionName) => Enumerable.Range(0, lines.Count)
        .Where(index => string.Equals(lines[index].Trim(), sectionName, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static bool IsKnownSectionHeader(string line) =>
        string.Equals(line.Trim(), "Dalvik Details", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(line.Trim(), "Objects", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(line.Trim(), "App Summary", StringComparison.OrdinalIgnoreCase);
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
