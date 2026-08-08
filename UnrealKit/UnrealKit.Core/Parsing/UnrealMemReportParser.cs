using UnrealKit.Core.Diagnostics;

namespace UnrealKit.Core.Parsing;

public sealed class UnrealMemReportParser : IUnrealMemReportParser
{
    public async Task<UnrealMemReportParseResult> ParseFileAsync(string inputFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        var fullPath = Path.GetFullPath(inputFilePath);
        if (Directory.Exists(fullPath)) throw new ArgumentException("Input must be a file, not a directory.", nameof(inputFilePath));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("MemReport input file was not found.", fullPath);
        return Parse(fullPath, await File.ReadAllLinesAsync(fullPath, cancellationToken));
    }

    public UnrealMemReportParseResult Parse(string inputPath, IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(lines);
        var changelistLine = lines.FirstOrDefault(line => line.StartsWith("Changelist:", StringComparison.OrdinalIgnoreCase));
        if (changelistLine is null)
        {
            return new UnrealMemReportParseResult(inputPath, null,
                [new Diagnostic(DiagnosticSeverity.Error, "UMR101", "Missing required Changelist metadata.", inputPath, "Select a complete UE memreport file.")]);
        }

        var changelist = changelistLine.Split(':', 2)[1].Trim();
        var diagnostics = new List<Diagnostic>();
        var summary = new UnrealMemReportSummary(ParseMetrics(inputPath, lines, diagnostics));
        return new UnrealMemReportParseResult(inputPath, new UnrealMemReport(changelist, summary), diagnostics);
    }

    private static IReadOnlyList<UnrealMemReportMetric> ParseMetrics(string inputPath, IReadOnlyList<string> lines, List<Diagnostic> diagnostics)
    {
        var definitions = new[]
        {
            ("Wwise", "SoundEngine Reserved"), ("Wwise", "SoundBank"), ("Lua", "Lua Memory"),
            ("Texture Streaming", "Average Required PoolSize"), ("Texture Streaming", "Wanted Mips"), ("Texture Streaming", "NonStreaming Mips"),
            ("Shader", "Shader"), ("RHI", "RHI Buffer"), ("RHI", "RHI Texture"), ("RHI", "RHI Render Target"),
            ("LLM Platform", "FMalloc"), ("LLM Platform", "Total"), ("LLM Full", "Total")
        };
        var values = new Dictionary<string, UnrealMemReportMetric>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var parts = lines[index].Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            var definition = definitions.FirstOrDefault(item => string.Equals(item.Item2, parts[0], StringComparison.OrdinalIgnoreCase));
            if (definition.Item2 is null) continue;
            if (values.ContainsKey(definition.Item2))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR201", $"Duplicate summary metric: {definition.Item2}.", inputPath, "The first value is retained.", index + 1));
                continue;
            }

            var tokens = parts[1].Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!TryParseKb(tokens, out var valueKb))
            {
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "UMR202", $"Invalid memory value for {definition.Item2}.", inputPath, "Use a numeric value and memory unit.", index + 1));
                values.Add(definition.Item2, new UnrealMemReportMetric(definition.Item1, definition.Item2, null, parts[1], UnrealMemReportMetricStatus.Invalid, index + 1));
                continue;
            }

            values.Add(definition.Item2, new UnrealMemReportMetric(definition.Item1, definition.Item2, valueKb, parts[1], UnrealMemReportMetricStatus.Parsed, index + 1));
        }

        return definitions.Select(item => values.TryGetValue(item.Item2, out var value) ? value : new UnrealMemReportMetric(item.Item1, item.Item2, null, null, UnrealMemReportMetricStatus.Missing, null)).ToArray();
    }

    private static bool TryParseKb(IReadOnlyList<string> tokens, out long valueKb)
    {
        valueKb = 0;
        if (tokens.Count == 0 || !decimal.TryParse(tokens[0].Replace(",", string.Empty, StringComparison.Ordinal), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)) return false;
        var unit = tokens.Count > 1 ? tokens[1].ToUpperInvariant() : "KB";
        var multiplier = unit switch
        {
            "B" => 1m / 1024m,
            "KB" or "KIB" or "K" => 1m,
            "MB" or "MIB" or "M" => 1024m,
            "GB" or "GIB" or "G" => 1024m * 1024m,
            _ => 0m
        };
        if (multiplier == 0m) return false;
        valueKb = decimal.ToInt64(decimal.Round(value * multiplier, 0, MidpointRounding.AwayFromZero));
        return true;
    }
}
